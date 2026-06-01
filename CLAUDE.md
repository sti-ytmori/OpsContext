# OpsContext — Microsoft Agent Hackathon 2026

業務担当者のロール（営業/購買/生産/経理）ごとに AI エージェントの語り口とフォーカスが切り替わり、基幹データ x 判断ログ x ナレッジを横断して翻訳・継承する業務コンテキスト基盤。

Claude は実装・保守の作業者として動く。コードの追加・修正・調査・デバッグが主な役割。

## プロジェクト状態

実装完了。2026/6/2〜6/18 が審査期間で、その間は Azure Container Apps 上での稼働維持が必要。

## 技術スタック

- Web フレームワーク: ASP.NET Core Blazor Server (.NET 10) + MudBlazor
- AI エージェント基盤: Microsoft Agent Framework (Microsoft.Agents.AI)
- LLM: Azure AI Foundry gpt-5.4-mini / Embedding: text-embedding-3-small
- ベクトル検索: Azure AI Search (opscontext-knowledge / opscontext-context インデックス)
- RDB: Azure SQL Database (Serverless)
- コンテナ実行基盤: Azure Container Apps
- 認証 (デモ用): FakeRoleAuthenticationHandler + Cookie

## エージェント構成

- Orchestrator: ルーティング・統合
- ロール別エージェント: SalesAgent / AccountingAgent / PurchasingAgent / ProductionAgent
- Curator: バックグラウンドで判断ログをコンテキストストアに書き込む (CuratorHostedService)
- Tool 層: SqlErpTool / AiSearchTool / CsvService / CalcTool / ContextStoreTool

エージェント定義 JSON は `agents/` に置く（orchestrator.json / sales.json / accounting.json / purchasing.json / production.json）。

## プロジェクト構成

```
OpsContext.sln
- OpsContext.Web/          # Blazor Server アプリ本体
  - Components/Pages/      # Chat / ContextViewer / Dataset / DataList / Accounts / Roles / Admin 等
  - Services/              # SqlUserStore / SqlRolePromptStore / SqlLoginHistoryStore 等
- OpsContext.Agents/       # エージェント・Tool 層クラスライブラリ
  - Agents/                # OrchestratorAgent / SalesAgent / AccountingAgent / PurchasingAgent / ProductionAgent
  - Services/              # CuratorHostedService / CuratorQueue
  - Tools/                 # SqlErpTool / AiSearchTool / CsvService / CalcTool / ContextStoreTool 等
  - Models/                # ContextEntry / ConversationEvent / ErpDatasetModels 等
  - Options/               # OpsContextOptions
- OpsContext.Seed/         # ナレッジ投入コンソール
- agents/                  # エージェント定義 JSON (5ファイル)
- sql/                     # スキーマ DDL + seed データ
- dev/                     # SDD ドキュメント・Playwright E2E テスト
- _asset/                  # アーキテクチャ図・コンセプト画像
```

## ビルド・起動・テスト

```powershell
# ビルド
dotnet build OpsContext.sln

# モックモード起動（Azure 接続不要 — UI 確認用）
dotnet run --project OpsContext.Web -- --mock

# 本番モード起動（appsettings.Development.json に接続情報が必要）
dotnet run --project OpsContext.Web

# ホットリロード
dotnet watch --project OpsContext.Web

# ナレッジ seed 投入（Azure AI Search / Blob 接続情報が必要）
dotnet run --project OpsContext.Seed

# E2E テスト（Playwright） — 詳細は dev/tests/README.md を参照
cd dev/tests && npx playwright test
```

SDK バージョンは `global.json` で 10.0.100-preview に固定済み。

## 接続情報

`OpsContext.Web/appsettings.Development.json`（.gitignore 対象）に記入:

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
    "BlobConnectionString": "DefaultEndpointsProtocol=https;AccountName=<account>;AccountKey=<key>;..."
  }
}
```

`OpsContext.Seed/appsettings.json` にも同じ値を記入する。

## 開発ワークフロー

`dev/plan.md` が設計の source of truth。`dev/specs/NN-slug/design.md` が機能単位の HOW と進捗チェックリスト。詳細手順は `dev/CLAUDE.md` を参照。

人手タスク（Azure ポータル操作・認証情報記入・動画撮影・提出フォーム入力等）は `dev/specs/human-task.md` に集約する。

## 作業規約

- 箇条書きは `- `（ハイフン + 半角スペース）を使う。
- 太字 (****) やイタリクス (**) の Markdown 記法は使わない。
- 括弧は半角 () を使う（全角不可）。
- 一度に確認する質問は3つまでに絞る。
- 発散と収束を意識する（結論を急がず選択肢を広げてから絞る）。
- 審査基準（ビジネスインパクト / Agentic 性 / 完成度・実現性）で常に評価する。

## コンペ提出要件（要点）

- 提出物1: 成果物 URL — 6/2〜6/18 の審査期間中、稼働状態を維持すること
- 提出物2: Zenn ブログ記事 — アーキテクチャ図・3分以内のデモ動画（YouTube）・プロンプト設計の説明が必須
- 提出物3: GitHub リポジトリ URL（任意）— 締切後も開発を続ける場合は branch ではなく tag で提出時点を示すこと
- 必須技術要件: Azure 実行基盤（Container Apps 等）+ Microsoft AI 技術（Azure OpenAI / Agent Framework 等）をいずれか1つ以上使用

## 関連リソース

- コンペ概要: https://zenn.dev/hackathons/microsoft-agent-hackathon-2026
- ルール詳細: https://zenn.dev/hackathons/microsoft-agent-hackathon-2026?tab=rule
- Microsoft Agent Framework: https://learn.microsoft.com/ja-jp/agent-framework/overview/
- Azure AI Agent Service 学習パス: https://learn.microsoft.com/ja-jp/training/paths/develop-ai-agents-azure/
- セットアップ手順: SETUP.md
- 実装計画: dev/plan.md
- 人手タスク一覧: dev/specs/human-task.md
