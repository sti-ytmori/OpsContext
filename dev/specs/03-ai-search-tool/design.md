# 03-ai-search-tool 設計

Status: 進行中

## 上流参照

plan.md の「Tool層シグネチャ」「コンテキスト永続化方針」「アーキテクチャ」「タイムボックス Day1 7-10h」「やらないこと」「検証方法」。
関連: [[01-infra]] [[04-orchestrator-agents]] [[05-context-store]] [[06-curator-agent]]

## 目的

Azure AI Search の2インデックスに対するベクトル検索を提供する Tool 層を実装する。
ナレッジ（受注判断基準・与信規程・過去類似案件）と決定/観察ログの双方を統一インターフェースで検索可能にし、エージェントが根拠ある回答を生成できる基盤を整える。
あわせて、デモシナリオが成立するための擬似ナレッジ20件の seed 投入もこの slug で完結させる。

## スコープ

やること:
- `IAiSearchTool` インターフェースの定義（2メソッド）
- `AiSearchHit` 型の定義
- `opscontext-knowledge` インデックスへの擬似ナレッジ20件 seed（C# コンソール or REST）
- `opscontext-context` インデックスのフィールド定義（upsert は [[05-context-store]] / [[06-curator-agent]] が担う）
- `AiSearchTool` クラスの実装（Azure.Search.Documents SDK）
- embedding 生成: text-embedding-3-small（デプロイ名 `embedding-small`）
- `roleFilter` の $filter 変換（`role eq 'Sales'` 等）
- 接続情報は [[01-infra]] が出力した `appsettings.Development.json` の `AiSearch` セクションを利用

やらないこと（plan.md「やらないこと」より）:
- AI Search インデックスの作成（[[01-infra]] で実施済み）
- `opscontext-context` への upsert 実装（[[05-context-store]] / [[06-curator-agent]] が担う）
- ストリーミング応答
- Cosmos DB / Power Platform との連携
- 単体テスト / CI

## 設計詳細

### インターフェースとモデル

```csharp
public interface IAiSearchTool
{
    Task<IReadOnlyList<AiSearchHit>> SearchKnowledgeAsync(
        string query,
        int topK,
        string? roleFilter,
        CancellationToken ct);

    Task<IReadOnlyList<AiSearchHit>> SearchDecisionLogsAsync(
        string query,
        string? customerCode,
        int topK,
        CancellationToken ct);

    // [[05-context-store]] の Append* メソッドが呼び出す。
    // ContextEntries 1件を opscontext-context インデックスに upsert して ドキュメント ID を返す。
    Task<string> UpsertContextEntryAsync(
        string entryId,
        string caseId,
        string role,
        string kind,
        string customerCode,
        string text,
        CancellationToken ct);
}

public record AiSearchHit
{
    public double Score { get; init; }
    public string Content { get; init; } = "";
    public string Id { get; init; } = "";
    public Dictionary<string, string> Metadata { get; init; } = new();
}
```

### インデックス定義

#### opscontext-knowledge（擬似ナレッジ）

| フィールド名 | 型 | 属性 | 説明 |
|---|---|---|---|
| id | Edm.String | key, retrievable | ドキュメントID |
| content | Edm.String | retrievable, searchable | ナレッジ本文 |
| contentVector | Collection(Edm.Single) 1536次元 | retrievable, vector | embedding-3-small ベクトル |
| role | Edm.String | retrievable, filterable | 対象ロール (Sales / Purchasing / Production / Accounting / All) |
| category | Edm.String | retrievable, filterable | ナレッジ種別 (credit_policy / past_order / production_rule 等) |
| createdAt | Edm.DateTimeOffset | retrievable, sortable | 登録日時 |

ベクトル構成: HNSW アルゴリズム / コサイン類似度 / m=4 / efConstruction=400

#### opscontext-context（決定ログ/観察ログ ミラー）

| フィールド名 | 型 | 属性 | 説明 |
|---|---|---|---|
| id | Edm.String | key, retrievable | EntryId（SQL の ContextEntries.EntryId と同値） |
| content | Edm.String | retrievable, searchable | エントリ本文 |
| contentVector | Collection(Edm.Single) 1536次元 | retrievable, vector | embedding-3-small ベクトル |
| caseId | Edm.String | retrievable, filterable | 案件ID |
| role | Edm.String | retrievable, filterable | 記録したロール |
| kind | Edm.String | retrievable, filterable | decision / observation / question / answer / handoff |
| customerCode | Edm.String | retrievable, filterable | 顧客コード（A001 等） |
| createdAt | Edm.DateTimeOffset | retrievable, sortable | 記録日時 |

### SearchKnowledgeAsync の実装方針

1. `query` を embedding-small で embedding ベクトル化する。
2. ベクトル検索（VectorSearch）を実行する（topK 件取得）。
3. `roleFilter` が非 null の場合、`$filter=role eq '{roleFilter}' or role eq 'All'` を付与する。
4. 結果を `AiSearchHit` にマッピングして返す。Metadata には `role` / `category` を格納する。

### SearchDecisionLogsAsync の実装方針

1. `query` を embedding-small で embedding ベクトル化する。
2. ベクトル検索（VectorSearch）を実行する（topK 件取得）。
3. `customerCode` が非 null の場合、`$filter=customerCode eq '{customerCode}'` を付与する。
4. 結果を `AiSearchHit` にマッピングして返す。Metadata には `caseId` / `role` / `kind` / `customerCode` を格納する。

### 擬似ナレッジ seed 方針（20件）

seed 方法: `OpsContext.Seed` C# コンソールプロジェクト（または curl/REST 直叩き）で一括投入する。

seed 内容の分類:

- 与信規程 (credit_policy, role=Accounting): 5件
  - 与信枠使用率80%超の場合の分割出荷ルール
  - 3か月以内に支払遅延歴がある顧客への加算条件
  - 新規顧客の初回与信枠設定基準
  - 大口受注（1,000万円超）の経営承認フロー
  - 売上計上が月を跨ぐ場合の請求タイミング基準
- 過去類似案件 (past_order, role=Sales): 5件
  - A商事 弁P-101 150個 分割受注 成立事例（3年前）
  - A商事 生産遅延による納期延長交渉成功事例
  - 大口受注を分割提案に切り替えて与信枠内に収めた事例
  - 競合切替時の特例値引き承認事例
  - 納期2週間以内の緊急受注対応フロー
- 生産ルール (production_rule, role=Production): 4件
  - 弁P-101 標準リードタイム30日・緊急枠での最短2週対応条件
  - 安全在庫50個を下回る場合の追加発注トリガー
  - 同製品複数受注が重なった場合の優先順位付けルール
  - 部材手配を含む生産能力の計算方法
- 購買調達 (procurement_rule, role=Purchasing): 3件
  - 弁P-101 主要サプライヤーとの緊急手配条件
  - 代替調達可能部材のリストと切替判断基準
  - 部材リードタイムが生産スケジュールに与える影響評価

各ドキュメントは content 300〜500字程度の日本語テキストとし、embedding-small でベクトル化して投入する。

### 接続設定

```json
"OpsContext": {
  "AiSearch": {
    "Endpoint": "https://<name>.search.windows.net",
    "ApiKey": "<key>",
    "KnowledgeIndex": "opscontext-knowledge",
    "ContextIndex": "opscontext-context"
  },
  "AzureOpenAi": {
    "EmbeddingDeployment": "embedding-small"
  }
}
```

`AiSearchTool` は `OpsContextOptions` を DI で受け取り、`SearchClient` と `EmbeddingsClient`（または `IChatClient`）を生成する。

### NuGet パッケージ

- `Azure.Search.Documents` — SearchClient / ベクトル検索
- `Azure.AI.OpenAI` — embedding 生成（`Microsoft.Extensions.AI` 経由でも可）

## 依存

- [[01-infra]]: AI Search エンドポイント・APIキー・インデックス（空）、OpenAI エンドポイント・embedding-small デプロイ

## 受け入れ条件

plan.md「検証方法」より該当ステップ:

- ステップ4: 「基幹と突合」実行時、右ペインに `GetCustomerCredit / GetProductInventory / GetProductionCapacity` の SQL カードとあわせて AI Search 呼び出し結果カードが表示されること（`SearchDecisionLogsAsync` が呼ばれることで確認）
- ステップ12: `Accounting` ロールで同案件を開いたとき、経理視点要約に「A商事 3か月前与信遅延 / 与信枠80%消費」が含まれること。これは `opscontext-knowledge` の与信規程 seed と `opscontext-context` の決定ログが `SearchKnowledgeAsync` / `SearchDecisionLogsAsync` で引けることで成立する

追加確認:

- `SearchKnowledgeAsync(query, topK=3, roleFilter="Accounting", ct)` を単体呼び出しして、与信規程カテゴリのドキュメントが上位に返ること
- `SearchDecisionLogsAsync(query="A商事 大口受注", customerCode="A001", topK=3, ct)` を単体呼び出しして、A商事関連の過去事例が返ること

## タスク

- [x] `IAiSearchTool` / `AiSearchHit` をインターフェース定義ファイルに追加
- [ ] `opscontext-knowledge` インデックスのフィールド定義を確認し、必要に応じてポータルまたは REST で更新（vectorSearch 設定含む）— Azure接続後
- [ ] `opscontext-context` インデックスのフィールド定義を確認し、同様に更新 — Azure接続後
- [x] `OpsContext.Seed` コンソールプロジェクトを作成し、擬似ナレッジ17件を定義（与信規程5/過去案件5/生産4/購買3）
- [ ] seed プロジェクトで embedding-small を呼び出してベクトル化し、`opscontext-knowledge` に一括投入 — Azure接続後
- [x] `AiSearchTool` クラスを実装（`SearchKnowledgeAsync` / `SearchDecisionLogsAsync` / `UpsertContextEntryAsync` の全メソッド）
- [x] `roleFilter` / `customerCode` の `$filter` 変換ロジックを実装し、SQL インジェクション相当の不正文字をエスケープ
- [x] DI 登録（`services.AddSingleton<IAiSearchTool, AiSearchTool>()`）と `OpsContextOptions` へのバインド（Program.cs 実装済み）
- [ ] ローカルで `SearchKnowledgeAsync` / `SearchDecisionLogsAsync` を手動呼び出しして期待結果が返ることを確認 — Azure接続後
