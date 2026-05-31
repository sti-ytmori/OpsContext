# 開発環境セットアップガイド

OpsContext をローカルで動かすための環境構築手順。  
Windows と macOS それぞれの手順を記載する。

---

## 1. .NET SDK 10.0

### Windows

```powershell
# winget (Windows 11 標準パッケージマネージャ)
winget install Microsoft.DotNet.SDK.10

# または公式インストーラーをダウンロード
# https://dotnet.microsoft.com/download/dotnet/10.0
```

インストール後、ターミナルを再起動して確認:

```powershell
dotnet --version
dotnet --list-sdks
```

### macOS

```bash
# Homebrew を使う場合
brew install dotnet@9

# または公式インストーラー (.pkg) をダウンロード
# https://dotnet.microsoft.com/download/dotnet/10.0

# Homebrew でインストールした場合はパスを通す
echo 'export PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH"' >> ~/.zshrc
source ~/.zshrc
```

確認:

```bash
dotnet --version   # 10.0.x
```

> このリポジトリのルートに `global.json` があり SDK 10.0 に固定済み。  
> SDK 10.0 preview がインストールされていても自動的に 10.0 が使われる。

---

## 2. Git

### Windows

```powershell
winget install Git.Git
# インストール後ターミナル再起動
git --version
```

### macOS

```bash
# Xcode Command Line Tools に含まれているため通常は不要
git --version
# 未インストールの場合はダイアログが出てインストールを促される

# または Homebrew でインストール
brew install git
```

---

## 3. コードエディタ

どちらか使いやすい方を選択。

### VS Code（推奨・軽量）

```powershell
# Windows
winget install Microsoft.VisualStudioCode

# macOS
brew install --cask visual-studio-code
```

VS Code を開いたら以下の拡張機能を入れる:

- C# Dev Kit（必須）
- .NET Install Tool（C# Dev Kit が自動推奨）

### Visual Studio 2022（Windows のみ）

- https://visualstudio.microsoft.com/ja/ からインストール
- ワークロード「ASP.NET と Web 開発」を選択

---

## 4. Azure CLI（Azure リソース作成時に必要）

### Windows

```powershell
winget install Microsoft.AzureCLI
# または
# https://learn.microsoft.com/ja-jp/cli/azure/install-azure-cli-windows

az --version
az login
# ブラウザが開いてサインイン
```

### macOS

```bash
brew install azure-cli
az --version
az login
```

---

## 5. Azure Data Studio（SQL スクリプト実行に必要）

Azure SQL Database にスキーマと seed データを流し込むために使う。  
SSMS（Windows のみ）でも代替可。

### Windows / macOS 共通

公式サイトからダウンロード:  
https://learn.microsoft.com/ja-jp/azure-data-studio/download-azure-data-studio

---

## 6. Docker Desktop（Container Apps デプロイ時に必要）

ローカル動作確認だけであれば不要。Container Apps にデプロイする際に使う。

### Windows

```powershell
winget install Docker.DockerDesktop
```

インストール後 Docker Desktop を起動し、Settings > General の「Use WSL 2 based engine」が有効になっていることを確認。

### macOS

```bash
brew install --cask docker
# Docker Desktop を起動して初期設定を完了
docker --version
```

---

## 7. ngrok（ローカル公開フォールバック時に必要）

Container Apps デプロイが間に合わない場合のフォールバック用。  
5/31 19:00 判断ラインで必要になった場合だけ使う。

### Windows

```powershell
winget install Ngrok.Ngrok
# または https://ngrok.com/download から zip をダウンロードして PATH に追加
ngrok --version
```

### macOS

```bash
brew install ngrok
ngrok --version
```

ngrok のアカウント登録（無料）と認証が必要:

```bash
# ngrok ダッシュボード (https://dashboard.ngrok.com) で Authtoken を取得
ngrok config add-authtoken <your-token>
```

---

## 8. リポジトリのクローンとビルド確認

```bash
# Windows (PowerShell) / macOS (Terminal) 共通
git clone <repo-url>
cd 20260526_hackason

dotnet build OpsContext.sln
# ビルドに成功しました。0 個の警告、0 エラー が出ればOK
```

---

## 9. モックモードで起動

Azure リソースが無くても UI を確認できる。

```powershell
# Windows PowerShell
$env:OpsContext__MockMode = "true"
dotnet run --project OpsContext.Web
```

```bash
# macOS / bash
export OpsContext__MockMode=true
dotnet run --project OpsContext.Web
```

ブラウザで http://localhost:5179 を開く。

---

## 10. 接続情報の設定（Azure 作成後）

`OpsContext.Web/appsettings.Development.json` を編集する（`.gitignore` で除外済み）:

```json
{
  "OpsContext": {
    "AzureOpenAi": {
      "Endpoint": "https://<your-resource>.openai.azure.com/",
      "ApiKey": "<your-api-key>",
      "ChatDeployment": "gpt-4o",
      "EmbeddingDeployment": "embedding-small"
    },
    "SqlConnectionString": "Server=<server>.database.windows.net;Database=opscontext;User Id=<user>;Password=<pw>;Encrypt=True;",
    "AiSearch": {
      "Endpoint": "https://<your-search>.search.windows.net",
      "ApiKey": "<your-api-key>",
      "KnowledgeIndex": "opscontext-knowledge",
      "ContextIndex": "opscontext-context"
    }
  }
}
```

接続情報を入れたら SqlConnectionString が空でなくなるため、自動的に本番モードで起動する:

```bash
dotnet run --project OpsContext.Web
```

---

## まとめ（最小構成）

デモ動作確認だけなら以下の 3 つをインストールするだけでよい:

| # | ツール | 用途 |
|---|---|---|
| 1 | .NET SDK 10.0 | ビルド・実行 |
| 2 | Git | リポジトリ取得 |
| 3 | VS Code + C# Dev Kit | コード編集 |

残りは Azure リソース作成や Container Apps デプロイのタイミングで入れれば間に合う。
