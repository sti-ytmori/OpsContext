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

## SQL スキーマ・seed データ投入

Azure SQL Database 作成後、以下の順で実行する。
`-f 65001`（UTF-8 指定）は必須（省略すると日本語コメント周辺の列定義が欠落するバグが発生する）。

```powershell
$S = "tcp:<server>.database.windows.net,1433"
$d = "<database>"
$U = "<user>"
$P = "<password>"

sqlcmd -S $S -d $d -U $U -P $P -i sql/schema.sql               -b -f 65001
sqlcmd -S $S -d $d -U $U -P $P -i sql/schema_context_store.sql -b -f 65001
sqlcmd -S $S -d $d -U $U -P $P -i sql/seed.sql                 -b -f 65001
```

---

## AI Search ナレッジ投入

接続情報記入後に実行する。インデックスが存在しない場合は自動作成される。

```powershell
dotnet run --project OpsContext.Seed
```

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
│   │   │   ├── Cases.razor          # 案件一覧
│   │   │   └── QuoteReview.razor    # Excel 突合画面
│   │   └── Layout/MainLayout.razor  # ロール別ヘッダー色
│   ├── Services/FakeRoleAuthenticationHandler.cs
│   └── appsettings.Development.json # 接続情報（.gitignore 対象）
├── OpsContext.Agents/           # エージェント・Tool 層クラスライブラリ
│   ├── Agents/                  # Orchestrator / Sales / Accounting / Purchasing / Production / Curator
│   ├── Tools/                   # SqlErpTool / AiSearchTool / ExcelService / CalcTool / ContextStoreTool
│   ├── Models/                  # QuoteLine / ContextEntry / ConversationEvent
│   ├── Services/                # CuratorHostedService（バックグラウンド Curator）
│   └── Options/                 # OpsContextOptions（設定バインド）
├── OpsContext.Seed/             # ナレッジ投入コンソール
├── sql/
│   ├── schema.sql               # 擬似ERP 7テーブル DDL
│   ├── schema_context_store.sql # Cases / ContextEntries / FocusSnapshots
│   └── seed.sql                 # A商事・弁P-101 サンプルデータ
├── sample/
│   └── 見積依頼明細.xlsx         # デモ用サンプル Excel（6行、NG/Warning/OK 混在）
├── Dockerfile                   # リポジトリルートに配置済み
├── dev/
│   ├── plan.md                  # 実装計画（source of truth）
│   └── specs/                   # モジュール別設計書（design.md）
└── README.md                    # このファイル
```

---

## デモシナリオ（E2E 1本）

1. ブラウザで起動 URL を開く
2. 右上ドロップダウンで「Sales（営業）」を選択 → ヘッダーが緑に
3. チャットに「A商事から弁P-101を200個、納期2週間で見積依頼が来た」と入力
4. エージェントが SQL（与信・在庫・生産能力）と AI Search（過去案件・規程）を横断して回答
5. 「この線で進めます」と入力 → Curator が判断をコンテキストストアに記録
6. ロール切替「Accounting（経理）」→ 同案件を案件一覧から開く
7. 経理視点の要約に「A商事 与信遅延歴 / 与信枠消費率」が含まれることを確認

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
| Excel 処理 | ClosedXML (MIT) |
| コンテナ実行基盤 | Azure Container Apps |
| 認証（デモ用） | FakeRoleAuthenticationHandler + Cookie |

---

## 関連リンク

- コンペ概要: https://zenn.dev/hackathons/microsoft-agent-hackathon-2026
- Microsoft Agent Framework: https://learn.microsoft.com/ja-jp/agent-framework/overview/
- 設計書: [dev/plan.md](dev/plan.md)
- 人手タスク一覧: [dev/specs/human-task.md](dev/specs/human-task.md)
