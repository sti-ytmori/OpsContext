# 04-orchestrator-agents 設計

Status: 進行中

## 上流参照

plan.md の「エージェント設計」「アーキテクチャ / 協調パターン」「アーキテクチャ / エージェント協調シーケンス」「タイムボックス Day1 10-13h / Day2 0-4h」「やらないこと」「検証方法」。
関連: [[02-sql-erp-tool]] [[03-ai-search-tool]] [[05-context-store]] [[06-curator-agent]] [[07-blazor-ui]]

## 目的

Microsoft Agent Framework を使い、Orchestrator がユーザーのロール claim に応じて各ロールエージェント（Sales / Purchasing / Production / Accounting）へ Handoff し、各エージェントが Tool 群を並列・直列に呼び出して業務視点の回答を返す多層エージェント基盤を構築する。
Curia の AgentHubService パターン（`/agents/{role}.json` でエージェント定義をロード）を流用し、プロンプトを JSON 外部化することで審査デモ中の微調整を容易にする。

## スコープ

やること:
- `OpsContext.Agents` クラスライブラリへ `Microsoft.Agents.AI` NuGet 追加と Hello Agent 動作確認
- Orchestrator エージェントの定義（Handoff 先ルーティング + ロール翻訳プロンプト）
- SalesAgent（SQL 3本並列 + AI Search + ContextStore + ExcelTool 呼び出し）の実装
- AccountingAgent（SQL + AI Search(DecisionLog) + Calc）の実装
- Purchasing / Production Agent（型定義 + 最小 Tool 接続のみ、中身の作り込みは MVP 外）
- `/agents/{role}.json` によるエージェント定義のファイル外部化（AgentHubService パターン）
- `IChatClient`（Microsoft.Extensions.AI）の DI 登録と Azure OpenAI 接続
- Handoff（主）/ Sequential（Tool 直列）/ Parallel（SQL 3本同時）の協調パターン実装

やらないこと（plan.md「やらないこと」より）:
- GroupChat パターン（preview 不安定のため不採用）
- ストリーミング応答
- Entra ID 本物連携（認証は [[07-blazor-ui]] の FakeRoleAuthenticationHandler で処理）
- Purchasing / Production Agent の中身作り込み（型と最小ツール接続のみ）
- Azure AI Agent Service（コードベース深掘り訴求の方針で不採用）

## 設計詳細

### エージェント一覧と担当 Tool

| Agent | 使う Tool | フォーカス |
|---|---|---|
| Orchestrator | HandoffToRoleAgent | ロール claim でルーティング、翻訳プロンプト付加 |
| SalesAgent | ISqlErpTool(Credit, SimilarOrders), IAiSearchTool(Knowledge), IContextStoreTool, IExcelTool(ValidateQuoteLines) | 受注獲得・粗利・顧客関係・見積突合 |
| AccountingAgent | ISqlErpTool(Credit, RunReadOnlyQuery), IAiSearchTool(DecisionLog), ICalcTool | 与信・回収・売上計上影響 |
| PurchasingAgent | ISqlErpTool(Inventory, SimilarOrders), IAiSearchTool(Knowledge) | 仕入リスク・代替調達（スタブ） |
| ProductionAgent | ISqlErpTool(Capacity, Inventory), IAiSearchTool(Knowledge) | 生産枠・スケジュール（スタブ） |
| CuratorAgent | IContextStoreTool(Append*), IAiSearchTool(upsert) | 裏方抽出・蓄積（[[06-curator-agent]] で設計） |

### 協調パターン

Handoff（主）: Orchestrator がユーザーのロール cookie を読み、対応するロールエージェントへ Handoff する。Handoff の際に `contextSummary`（ReadCaseContextAsync の結果を Markdown 化したもの）を引き渡す。

Sequential（Tool 直列）: 各ロールエージェントが Tool を呼ぶ順序は、後続の Tool に前の結果が必要な場合（例: Calc に SQL 結果を渡す）のみ直列にする。

Parallel（SQL 3本同時）: SalesAgent は `GetCustomerCreditAsync` / `GetProductInventoryAsync` / `GetProductionCapacityAsync` を `Task.WhenAll` で同時実行する。これが plan.md のメインデモシナリオの肝。

### Orchestrator 翻訳プロンプト骨子（plan.md 転記）

```
ユーザーロール: {role}
ロール別フォーカス: Sales=受注獲得 / Purchasing=仕入リスク / Production=生産枠 / Accounting=与信回収
他ロール案件を見るときは {role} 観点で翻訳し、翻訳理由を1文添える。
```

フル版プロンプトは `/agents/orchestrator.json` の `systemPrompt` フィールドに外部化する。

### エージェント定義ファイル構成（AgentHubService パターン）

Curia の `AgentHubService` パターンを流用し、エージェント定義を JSON ファイルでロードする。

```
OpsContext.Agents/
  agents/
    orchestrator.json
    sales.json
    accounting.json
    purchasing.json
    production.json
```

各 JSON の構造:

```json
{
  "id": "sales",
  "name": "SalesAgent",
  "description": "営業視点で受注獲得・粗利・顧客リスクを分析するエージェント",
  "systemPrompt": "あなたは営業担当エージェントです。...",
  "tools": ["SqlErp", "AiSearch", "ContextStore", "Excel"]
}
```

ロード実装: `AgentDefinitionLoader`（static クラス）が `agents/*.json` を列挙し `JsonSerializer.Deserialize<AgentDefinition>` する（try/catch 個別耐性、名前順ソート）。Curia の `AgentHubService.GetAgentDefinitions()` をほぼそのまま移植。

### IChatClient DI 登録

```csharp
// Program.cs (OpsContext.Web)
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<OpsContextOptions>>().Value;
    return new AzureOpenAIClient(
            new Uri(opts.AzureOpenAi.Endpoint),
            new AzureKeyCredential(opts.AzureOpenAi.ApiKey))
        .GetChatClient(opts.AzureOpenAi.ChatDeployment);
});
```

Managed Identity を使う場合は `new DefaultAzureCredential()` に差し替え可能（[[09-deploy]] 参照）。

### OpsContextOptions 設定クラス

Curia の `AppSettings` Llm* フラットプロパティを整理して専用クラスに切り出す。

```csharp
// OpsContext.Agents/Options/OpsContextOptions.cs
public sealed class OpsContextOptions
{
    public AzureOpenAiOptions AzureOpenAi { get; set; } = new();
    public string SqlConnectionString { get; set; } = "";
    public AiSearchOptions AiSearch { get; set; } = new();
    public string BlobConnectionString { get; set; } = "";
}

public sealed class AzureOpenAiOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ChatDeployment { get; set; } = "gpt-4o";
    public string EmbeddingDeployment { get; set; } = "embedding-small";
}

public sealed class AiSearchOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string KnowledgeIndex { get; set; } = "opscontext-knowledge";
    public string ContextIndex { get; set; } = "opscontext-context";
}
```

DI 登録: `builder.Services.Configure<OpsContextOptions>(config.GetSection("OpsContext"));`

### SalesAgent の並列 Tool 呼び出し骨子

```csharp
// SalesAgent 内での並列実行イメージ
var (creditTask, inventoryTask, capacityTask, searchTask) = (
    _sqlErp.GetCustomerCreditAsync(customerCode, ct),
    _sqlErp.GetProductInventoryAsync(productCode, ct),
    _sqlErp.GetProductionCapacityAsync(productCode, DateOnly.FromDateTime(DateTime.Today),
        DateOnly.FromDateTime(DateTime.Today.AddDays(14)), ct),
    _aiSearch.SearchDecisionLogsAsync(query, customerCode, topK: 5, ct)
);
await Task.WhenAll(creditTask, inventoryTask, capacityTask, searchTask);
```

結果を `SqlQueryResult.ToMarkdownTable()` で文字列化し、IChatClient に渡して営業視点の分析テキストを生成する。

### メインデモシナリオのシーケンス（plan.md 転記・参照）

1. User（Sales ロール）: 「A商事から弁P-101を200個 納期2週で見積依頼」
2. Web → Orchestrator: Chat(userRole=Sales, message, caseId)
3. Orchestrator → SalesAgent: Handoff(contextSummary)
4. SalesAgent: SQL 3本並列 + AI Search（上記コード）
5. SalesAgent → Orchestrator: 営業視点の分析結果
6. Orchestrator → Web: 与信枠残12M 分割提案150個+80個
7. User: 「この線で進めます」
8. Web → ContextStoreTool: AppendDecisionAsync(caseId, "Sales", user, text)
9. Web → CuratorAgent: ConversationEvent（fire-and-forget。[[06-curator-agent]] が処理）

### ファイル配置

```
OpsContext.Agents/
  Agents/
    OrchestratorAgent.cs
    SalesAgent.cs
    AccountingAgent.cs
    PurchasingAgent.cs        -- スタブ
    ProductionAgent.cs        -- スタブ
  Options/
    OpsContextOptions.cs
  Agents/Loaders/
    AgentDefinitionLoader.cs  -- AgentHubService パターン移植
  agents/
    orchestrator.json
    sales.json
    accounting.json
    purchasing.json
    production.json
```

## 依存

- [[02-sql-erp-tool]]: ISqlErpTool が実装済みであること
- [[03-ai-search-tool]]: IAiSearchTool が実装済みであること
- [[05-context-store]]: IContextStoreTool が実装済みであること（ReadCaseContextAsync で文脈を引き渡す）

## 受け入れ条件

plan.md「検証方法」より:

- ステップ2: ドロップダウンで Sales 選択後、チャット入力が Sales ロールとして送信されること
- ステップ4: 「基幹と突合」実行時に右ペインで GetCustomerCredit / GetProductInventory / GetProductionCapacity の SQL カードが並列で現れること
- ステップ5: 弁P-101 200個の行が NG (赤) で表示され、推奨に「150+80 分割」相当が含まれること
- ステップ11: Accounting ロールに切替後、同 CaseId の案件を開いたとき経理視点へ翻訳されること（Orchestrator の翻訳プロンプトが効いていること）

## タスク

- [x] `Microsoft.Extensions.AI` + `Azure.AI.OpenAI` を採用（Microsoft.Agents.AI は beta 不安定のため見送り、plan.md 逸脱メモ: 「Agent Framework C# SDK は IChatClient + カスタム Orchestrator/Handoff パターンで代替」）
- [x] `OpsContextOptions` / `AzureOpenAiOptions` / `AiSearchOptions` クラスを定義し、`Program.cs` の DI 登録と `appsettings.Development.json` のバインドを設定する
- [x] `IChatClient` を DI に登録し（AzureOpenAIClient ベース + モック切替）、単発チャットが返ることを確認する
- [x] `AgentDefinitionLoader` を実装し（Curia AgentHubService 移植）、`agents/*.json` を読み込めることを確認する
- [x] `agents/` フォルダに 5ファイルの JSON を作成し、各エージェントの systemPrompt 骨子を記述する
- [x] `OrchestratorAgent` を実装し、ロール claim に応じた Handoff ルーティングを動作させる
- [x] `SalesAgent` を実装し、SQL 3本並列 + AI Search の `Task.WhenAll` 呼び出しが動作することを確認する
- [x] `AccountingAgent` を実装し（SQL + AI Search + Calc）、経理視点の回答が返ることを確認する
- [x] `PurchasingAgent` / `ProductionAgent` をスタブとして定義する（Tool 接続は最小限）
- [ ] Orchestrator 翻訳プロンプトの動作を検証する（Sales ロールで登録した決定を Accounting で参照し、経理視点への翻訳が確認できること）（Azure接続後）
