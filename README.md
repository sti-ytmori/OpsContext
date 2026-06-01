# OpsContext — Microsoft Agent Hackathon 2026

業務担当者のロール（営業・購買・生産・経理）ごとに AI エージェントの語り口とフォーカスが切り替わり、
基幹データ × 判断ログ × ナレッジを横断して翻訳・継承する業務コンテキスト基盤。

---

## 必要な環境（事前インストール）

| ツール | バージョン目安 | 用途 |
|---|---|---|
| .NET SDK | 10.0.x (preview) | ビルド・実行（必須） |
| Git | any | リポジトリ管理（必須） |
| VS Code + C# Dev Kit | any | コード編集（必須） |
| Azure CLI (`az`) | 2.x | Azure リソース作成・デプロイ（任意） |
| sqlcmd | 15.x | SQL スクリプト実行（任意） |
| Docker Desktop | any | コンテナビルド（任意） |
| ngrok | any | ローカル公開フォールバック（任意） |

```powershell
dotnet --version   # 10.0.x が返れば OK
```

---

## クイックスタート（モックモード — Azure 接続不要）

Azure リソースを作る前でも UI の動作イメージを確認できます。

```powershell
# 1. ビルド
dotnet build OpsContext.sln

# 2. モックモードで起動
dotnet run --project OpsContext.Web -- --mock
```

ブラウザで `http://localhost:5179` を開く。

モックモードでは:
- SQL / Azure OpenAI / AI Search に接続せず固定データを返す
- フェイクロール認証は本番と同じ動作（右上ドロップダウンでロール切替可能）

---

## ローカル本番モードで起動（接続情報設定済み前提）

```powershell
dotnet run --project OpsContext.Web

## もしくは
dotnet watch --project OpsContext.Web
```

起動後にターミナルに表示される URL をブラウザで開く。
デフォルトは `https://localhost:7202` または `http://localhost:5179`。

HTTPS を使う場合は初回のみ dev 証明書を信頼する:

```powershell
dotnet dev-certs https --trust
```

---

## 接続情報の設定

`OpsContext.Web/appsettings.Development.json` に Azure の接続情報を記入する（.gitignore 対象）:

```json
{
  "OpsContext": {
    "AzureOpenAi": {
      "Endpoint": "https://<resource>.services.ai.azure.com",
      "ApiKey": "<api-key>",
      "ChatDeployment": "gpt-5.4-mini",
      "EmbeddingDeployment": "text-embedding-3-small"
    },
    "SqlConnectionString": "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<db>;...",
    "AiSearch": {
      "Endpoint": "https://<resource>.search.windows.net",
      "ApiKey": "<api-key>",
      "KnowledgeIndex": "opscontext-knowledge",
      "ContextIndex": "opscontext-context"
    },
    "BlobConnectionString": "DefaultEndpointsProtocol=https;AccountName=<account>;AccountKey=<key>;EndpointSuffix=core.windows.net"
  }
}
```

`OpsContext.Seed/appsettings.json` にも同じ値を記入する。

---

## ビルド・起動コマンド一覧

```powershell
# ビルド
dotnet build OpsContext.sln

# Web 起動（モックモード）
dotnet run --project OpsContext.Web -- --mock

# Web 起動（本番モード）
dotnet run --project OpsContext.Web

# ナレッジ seed 投入
dotnet run --project OpsContext.Seed

# Docker ビルド（リポジトリルートから実行）
docker build -t opscontext .
docker run -p 8080:8080 opscontext
```

---

## プロジェクト構成

```
OpsContext.sln
├── OpsContext.Web/              # Blazor Server アプリ（提出物の本体）
│   ├── Components/
│   │   ├── Pages/
│   │   │   ├── Chat.razor           # メインチャット
│   │   │   ├── ContextViewer.razor  # コンテキストストア閲覧
│   │   │   ├── Dataset.razor        # ERPデータセット表示
│   │   │   ├── DataList.razor       # データ一覧
│   │   │   ├── Data.razor           # データ詳細
│   │   │   ├── Accounts.razor       # アカウント管理
│   │   │   ├── Roles.razor          # ロール設定
│   │   │   ├── Admin.razor          # 管理画面
│   │   │   ├── Login.razor          # ログイン
│   │   │   └── LoginHistory.razor   # ログイン履歴
│   │   └── Layout/MainLayout.razor  # ロール別ヘッダー色
│   ├── Services/                # SqlUserStore / SqlRolePromptStore / SqlLoginHistoryStore / RoleStateService 等
│   └── appsettings.Development.json # 接続情報（.gitignore 対象）
├── OpsContext.Agents/           # エージェント・Tool 層クラスライブラリ
│   ├── Agents/                  # OrchestratorAgent / SalesAgent / AccountingAgent / PurchasingAgent / ProductionAgent
│   │                            # AgentDefinition / AgentDefinitionLoader / AgentPromptComposer
│   ├── Tools/                   # SqlErpTool / AiSearchTool / CsvService / CalcTool / ContextStoreTool
│   │                            # SqlErpDatasetStore / SqlDataGridStore 等（Mock 実装含む）
│   ├── Models/                  # QuoteLine / ContextEntry / ConversationEvent / ErpDatasetModels 等
│   ├── Services/                # CuratorHostedService / CuratorQueue（バックグラウンド Curator）
│   ├── Options/                 # OpsContextOptions（設定バインド）
│   ├── GridAgentService.cs      # グリッド向けエージェントサービス
│   └── QuoteValidationService.cs
├── OpsContext.Seed/             # ナレッジ投入コンソール
├── agents/                      # エージェント定義 JSON（orchestrator / sales / accounting / purchasing / production）
├── sql/
│   ├── schema.sql               # 擬似ERP DDL
│   ├── schema_context_store.sql # ContextEntries / FocusSnapshots 等
│   ├── schema_data_grid.sql
│   ├── schema_erp_snapshot.sql
│   ├── schema_login_history.sql
│   ├── schema_role_prompts.sql
│   ├── schema_users.sql
│   ├── seed.sql                 # A商事・弁P-101 サンプルデータ
│   ├── seed_users.sql
│   └── update_personal_prompts.sql
├── _asset/                      # アーキテクチャ図・コンセプト画像
├── Dockerfile                   # リポジトリルートに配置済み
├── dev/
│   ├── plan.md                  # 実装計画（source of truth）
│   └── specs/                   # モジュール別設計書（design.md）
└── README.md                    # このファイル
```

---

## Container Apps デプロイ

Azure CLI インストール後（`winget install Microsoft.AzureCLI`）:

```powershell
az login
az containerapp up `
  --name ca-opscontext `
  --resource-group rg-opscontext `
  --location japaneast `
  --source . `
  --ingress external `
  --target-port 8080
```

デプロイ後に環境変数として接続情報を設定する（`az containerapp secret set` または Azure ポータル）。

---

## フォールバック（Container Apps が間に合わない場合）

```powershell
# ローカル起動
dotnet run --project OpsContext.Web

# 別ターミナルで ngrok 公開
ngrok http 5179
# 発行された URL を提出 URL として使う
# 審査期間（6/2〜6/18）はローカルマシンをスリープしない設定にする
```

---

## 技術スタック

| レイヤー | 技術 |
|---|---|
| Web フレームワーク | ASP.NET Core Blazor Server (.NET 10) |
| UI ライブラリ | MudBlazor |
| AI エージェント基盤 | Microsoft Agent Framework (Microsoft.Agents.AI) |
| LLM | Azure AI Foundry gpt-5.4-mini |
| Embedding | text-embedding-3-small |
| ベクトル検索 | Azure AI Search (opscontext-knowledge / opscontext-context) |
| RDB | Azure SQL Database (Serverless, 無料オファー) |
| Markdown レンダリング | Markdig |
| コンテナ実行基盤 | Azure Container Apps |

---

## 関連リンク

- コンペ概要: https://zenn.dev/hackathons/microsoft-agent-hackathon-2026
- Microsoft Agent Framework: https://learn.microsoft.com/ja-jp/agent-framework/overview/
- 設計書: [dev/plan.md](dev/plan.md)
- 人手タスク一覧: [dev/specs/human-task.md](dev/specs/human-task.md)
