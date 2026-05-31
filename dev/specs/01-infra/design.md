# 01-infra 設計

Status: 未着手

## 上流参照
plan.md の「アーキテクチャ」「タイムボックス Day1 0-2h」「リスクと早期決定」。
関連: [[02-sql-erp-tool]] [[03-ai-search-tool]] [[04-orchestrator-agents]]

## 目的
OpsContext が動作するための Azure リソース一式を作成し、後続の全コンポーネントが接続できる状態にする。
ローカル開発時は接続文字列/APIキーを直書きで進め、Container Apps へのデプロイは 09-deploy で扱う。

## スコープ

やること:
- リソースグループ作成
- Azure SQL Database (Serverless) — 擬似ERP + コンテキストストア用
- Azure AI Search (Basic) — opscontext-context / opscontext-knowledge の2インデックス
- Azure Blob Storage — 議事録原本置き場（MVP では実質使わないが作っておく）
- Azure OpenAI — gpt-4o デプロイ / text-embedding-3-small デプロイ
- Log Analytics ワークスペース（Container Apps 環境用）
- 接続文字列・エンドポイント・APIキーをローカルの `appsettings.Development.json` に書き出す

やらないこと（plan.md「やらないこと」より）:
- Bicep / Terraform による IaC 化
- Entra ID 本物連携（Managed Identity の配線は 09-deploy で行う）
- Container Apps 環境の作成（09-deploy で扱う）

## 設計詳細

### リソース構成

| リソース | SKU/Tier | 用途 |
|---|---|---|
| Resource Group | - | rg-opscontext |
| Azure SQL Database | Serverless(GP_S_Gen5_1) | 擬似ERP + ContextStore |
| Azure AI Search | Basic | ナレッジ + 決定ログのベクトル検索 |
| Azure Blob Storage | LRS Standard | 議事録原本（MVP外） |
| Azure OpenAI | S0 | gpt-4o + text-embedding-3-small |
| Log Analytics | PerGB2018 | Container Apps 連携用 |

### Azure OpenAI デプロイ名

- モデル: `gpt-4o` → デプロイ名: `gpt-4o`
- モデル: `text-embedding-3-small` → デプロイ名: `embedding-small`

### AI Search インデックス（空の状態で作成）

- `opscontext-knowledge`: ナレッジベース（手動 seed は 03-ai-search-tool で実施）
- `opscontext-context`: 決定ログ / 観察ログ（05-context-store で upsert）

### ローカル設定ファイルの出力先

```
OpsContext.Web/appsettings.Development.json
```

```json
{
  "OpsContext": {
    "AzureOpenAi": {
      "Endpoint": "https://<name>.openai.azure.com/",
      "ApiKey": "<key>",
      "ChatDeployment": "gpt-4o",
      "EmbeddingDeployment": "embedding-small"
    },
    "SqlConnectionString": "Server=<server>.database.windows.net;Database=opscontext;User Id=<user>;Password=<pw>;Encrypt=True;",
    "AiSearch": {
      "Endpoint": "https://<name>.search.windows.net",
      "ApiKey": "<key>",
      "KnowledgeIndex": "opscontext-knowledge",
      "ContextIndex": "opscontext-context"
    },
    "BlobConnectionString": "DefaultEndpointsProtocol=https;AccountName=<account>;..."
  }
}
```

`appsettings.Development.json` は `.gitignore` に追加してシークレットをコミットしない。

## 依存

なし（01-infra が全体の起点）

## 受け入れ条件

plan.md「検証方法」ステップ1の前提となるインフラ確認:
- `az sql db show` で SQL Database が Running 状態
- `az cognitiveservices account show` で OpenAI リソースが確認できる
- `az search service show` で AI Search が確認できる
- `OpsContext.Web` をローカルで `dotnet run` し、起動エラーが出ないこと（DB 接続は 02 で確認）
- `appsettings.Development.json` に全接続情報が揃っていること
- `appsettings.Development.json` が `.gitignore` で除外されていること

## タスク

- [ ] `az group create` で rg-opscontext 作成
- [ ] Azure SQL Database (Serverless GP_S_Gen5_1) 作成、ファイアウォール規則でローカルIP許可
- [ ] Azure OpenAI リソース作成 + gpt-4o / text-embedding-3-small デプロイ
- [ ] Azure AI Search (Basic) 作成、2インデックスを空の状態で定義
- [ ] Azure Blob Storage (LRS) 作成
- [ ] Log Analytics ワークスペース作成（Container Apps 用。今は空で可）
- [ ] `appsettings.Development.json` に全接続情報を記入
- [ ] `.gitignore` に `appsettings.Development.json` を追加
- [ ] `dotnet run` で OpsContext.Web が起動エラーなく立ち上がることを確認
