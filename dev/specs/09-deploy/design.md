# 09-deploy 設計

Status: 未着手

## 上流参照

plan.md の「タイムボックス Day2 17-19h」「タイムボックス Day3 14-16h」「リスクと早期決定」「アーキテクチャ」「認証」「やらないこと」「検証方法 ステップ13」。
関連: [[01-infra]] [[07-blazor-ui]]

## 目的

`OpsContext.Web` を Azure Container Apps (ACA) 上に公開し、審査員が提出 URL に直接アクセスして E2E フローを操作できる状態にする。
審査期間 (6/2〜6/18) を通じて稼働を維持することも本コンポーネントの責務とする。
Container Apps デプロイが間に合わない場合の ngrok フォールバックも設計に含む。

## スコープ

やること:
- Dockerfile の設計と `OpsContext.Web` のコンテナ化 (`dotnet publish` → `mcr.microsoft.com/dotnet/aspnet:8.0` ベース)
- Azure Container Apps 環境 + コンテナアプリの作成と公開
- システム割り当てマネージドID (Managed Identity) によるプライマリ接続パスの設定
- `az containerapp secret set` による接続文字列・API キー注入 (フォールバックパス)
- `secretRef` を使った環境変数へのシークレット参照の配線
- 最小レプリカ数 = 1 設定による審査期間中の稼働維持
- ngrok フォールバック手順の明記 (5/31 19h 判断ライン)
- コスト概算メモ

やらないこと (plan.md「やらないこと」より):
- Bicep / Terraform による IaC 化
- GitHub Actions CI/CD パイプラインの構築
- Entra ID 本物連携 (認証は FakeRoleAuthenticationHandler のまま)
- ストリーミング応答
- Production / Purchasing Agent の中身作り込み

## 設計詳細

### Dockerfile

`OpsContext.Web/Dockerfile` をマルチステージビルドで作成する。

```dockerfile
# ビルドステージ
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish OpsContext.Web/OpsContext.Web.csproj -c Release -o /app/publish

# ランタイムステージ
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "OpsContext.Web.dll"]
```

- ベースイメージは `mcr.microsoft.com/dotnet/aspnet:8.0` (軽量 runtime のみ)
- `dotnet publish -c Release` で最適化ビルド
- ポートは `8080` (ACA デフォルト)
- `.dockerignore` で `appsettings.Development.json` / `bin/` / `obj/` を除外する

### Container Apps 環境構成

| 設定項目 | 値 |
|---|---|
| リソースグループ | rg-opscontext (01-infra で作成済み) |
| Container Apps 環境 | cae-opscontext |
| Log Analytics ワークスペース | 01-infra で作成済みのものを指定 |
| コンテナアプリ名 | ca-opscontext |
| CPU / メモリ | 0.5 vCPU / 1.0 Gi |
| 最小レプリカ数 | 1 (審査期間中の稼働維持) |
| 最大レプリカ数 | 1 (コスト抑制) |
| Ingress | external / ターゲットポート 8080 |

最小レプリカ = 1 にすることでコールドスタート遅延と審査期間中の停止を防ぐ。
コスト目安: 0.5 vCPU / 1 Gi / 常時稼働 ≒ 約 $8〜12 / 月 (24h 稼働 20日間で $6〜8 程度)。

### 認証設計: DefaultAzureCredential DI 抽象化

plan.md「認証」節の方針に従い、以下の切り替え可能な構造を取る。

- ローカル開発時: `appsettings.Development.json` の接続文字列 / API キーを直接読み込む
- 本番 (Container Apps): `DefaultAzureCredential` を優先し、システム割り当てマネージドID で Azure サービスに接続

DI 登録の骨子:

```csharp
// Program.cs
var credential = new DefaultAzureCredential();
builder.Services.AddSingleton<TokenCredential>(credential);

// SqlErpTool / ContextStoreTool の接続
// - ローカル: SqlConnectionString を直接使用
// - 本番: Managed Identity トークンを SqlConnection に付与
//   (Microsoft.Data.SqlClient の Active Directory Default 認証を利用)
```

`TokenCredential` を DI に登録しておくことで、SQL / AI Search / OpenAI すべてのクライアントを
ローカルでは接続文字列、本番ではマネージドID に切り替えられる設計とする。

### プライマリパス: Managed Identity による接続

システム割り当てマネージドID を Container App に付与し、以下のロールを Azure RBAC で割り当てる。

| サービス | 付与ロール |
|---|---|
| Azure SQL Database | `db_datareader` / `db_datawriter` (SQL 側で `CREATE USER ... FROM EXTERNAL PROVIDER` で作成) |
| Azure AI Search | `Search Index Data Contributor` |
| Azure OpenAI | `Cognitive Services OpenAI User` |

SQL 接続文字列の Authentication 設定:

```
Server=<server>.database.windows.net;Database=opscontext;Authentication=Active Directory Default;Encrypt=True;
```

`Active Directory Default` を指定すると `DefaultAzureCredential` がトークンを取得して接続する。
API キーの指定は不要になる。

### フォールバックパス: 接続文字列 + API キーをシークレット直入れ

Managed Identity の設定で詰まった場合は、接続文字列と API キーを Container Apps シークレットに直接登録して運用する。
記事 (Zenn ブログ) では「時間都合でフォールバックを使用した」と正直に明記する方針とする。

シークレット登録コマンド例:

```bash
az containerapp secret set \
  --name ca-opscontext \
  --resource-group rg-opscontext \
  --secrets \
    sql-conn="Server=<server>.database.windows.net;Database=opscontext;User Id=<user>;Password=<pw>;Encrypt=True;" \
    aoai-key="<azure-openai-api-key>" \
    search-key="<ai-search-api-key>"
```

環境変数への `secretRef` 参照:

```yaml
env:
  - name: OpsContext__SqlConnectionString
    secretRef: sql-conn
  - name: OpsContext__AzureOpenAi__ApiKey
    secretRef: aoai-key
  - name: OpsContext__AiSearch__ApiKey
    secretRef: search-key
```

アプリ側は `IConfiguration` から読み込む設計のため、シークレット参照経由でも動作が変わらない。

### ngrok フォールバック

plan.md「リスクと早期決定」より:

5/31 19h 時点で Container Apps へのデプロイが動作していない場合は、ローカル ngrok 公開で提出 URL とする。

手順:

```bash
ngrok http 5000  # dotnet run の Listen ポートに合わせる
```

- Zenn 記事には Container Apps デプロイのコードと手順を併記し、動作確認済みであることを示す
- 審査員向け README に「ngrok URL は審査期間中 (6/2〜6/18) 維持する」と明記する
- ローカルマシンをスリープしないよう設定する（電源オプション調整）

### appsettings.Production.json / 環境変数優先ルール

Container Apps では環境変数が `appsettings.json` を上書きする ASP.NET Core の規約を利用する。

```json
// appsettings.json (コミット済み / プレースホルダのみ)
{
  "OpsContext": {
    "AzureOpenAi": {
      "Endpoint": "",
      "ApiKey": "",
      "ChatDeployment": "gpt-4o",
      "EmbeddingDeployment": "embedding-small"
    },
    "SqlConnectionString": "",
    "AiSearch": {
      "Endpoint": "",
      "ApiKey": "",
      "KnowledgeIndex": "opscontext-knowledge",
      "ContextIndex": "opscontext-context"
    }
  }
}
```

環境変数キー変換規則: `OpsContext__AzureOpenAi__Endpoint` → `OpsContext:AzureOpenAi:Endpoint`

`appsettings.Development.json` は `.gitignore` で除外済み (01-infra で設定)。

### Blazor Server の SignalR 設定

Blazor Server は SignalR を使うため、Container Apps のセッションアフィニティを有効にする。

```bash
az containerapp ingress sticky-sessions set \
  --name ca-opscontext \
  --resource-group rg-opscontext \
  --affinity sticky
```

または `--sticky-sessions true` オプションを `az containerapp create` 時に指定する。

### デプロイコマンド概要

```bash
# 1. コンテナレジストリなしで直接デプロイ (--source オプション使用)
az containerapp up \
  --name ca-opscontext \
  --resource-group rg-opscontext \
  --environment cae-opscontext \
  --source . \
  --ingress external \
  --target-port 8080 \
  --min-replicas 1 \
  --max-replicas 1

# 2. システム割り当てマネージドID 有効化
az containerapp identity assign \
  --name ca-opscontext \
  --resource-group rg-opscontext \
  --system-assigned

# 3. (フォールバック) シークレット登録
az containerapp secret set ...
az containerapp update --set-env-vars ...
```

`az containerapp up --source` は Dockerfile を自動検出してビルド + デプロイまで行う。
ACR (Azure Container Registry) の自動作成も含まれるため、事前の ACR 作成は不要。

## 依存

- [[01-infra]]: リソースグループ / Log Analytics ワークスペースが事前に存在すること
- [[07-blazor-ui]]: デプロイ対象の `OpsContext.Web` プロジェクトがビルド可能な状態であること

## 受け入れ条件

plan.md「検証方法」ステップ13より:

- Container Apps の公開 URL で `https://ca-opscontext.<hash>.<region>.azurecontainerapps.io` にアクセスできること
- ロール切替ドロップダウンが機能し、Sales / Accounting ロールの切り替えが動作すること
- 見積依頼明細.xlsx のアップロード → 基幹突合 → 判定表示 → ダウンロードの E2E フローが公開 URL 上で動作すること
- 「この線で進めます」→ Curator 記録 → Accounting 視点翻訳の一連フローが動作すること
- 審査期間 (6/2〜6/18) を通じて URL が応答し続けること (最小レプリカ = 1 で確保)

## タスク

- [x] `Dockerfile` をリポジトリルートに作成（`az containerapp up --source .` が自動検出できる位置。`OpsContext.Web/Dockerfile` と同内容）。ローカル docker build 確認は Azure 接続後（human-task）
- [x] `.dockerignore` を作成・更新 (`appsettings.Development.json` / `bin/` / `obj/` / `dev/` / `_memo/` 等を除外)
- [ ] `az containerapp up --source` で Container Apps 環境 + アプリを作成しデプロイ
- [ ] システム割り当てマネージドID を有効化し、SQL / AI Search / OpenAI の RBAC ロールを付与
- [ ] マネージドID 接続確認 → 詰まった場合は `az containerapp secret set` でフォールバック接続文字列を登録
- [ ] Blazor Server の SignalR セッションアフィニティを有効化
- [ ] 公開 URL で E2E フロー (検証方法ステップ13) を通し確認
- [ ] 最小レプリカ = 1 が設定されていることを `az containerapp show` で確認
- [ ] 提出 README に公開 URL とテスト用ロール切替手順を記載
