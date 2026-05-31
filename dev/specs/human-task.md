# Human Task — 人手が必要な作業一覧

Claude では実行できない、ポータル操作・認証情報記入・撮影・提出など人が手動で行う必要があるタスクをここに集約する。
design.md のタスクと重複するものは、human-task.md 側を「いつやるか」の目安として使い、完了したら `- [x]` にする。

---

## 1. Azure リソース作成（[[01-infra]]）

- [x] `az group create --name rg-opscontext --location japaneast` でリソースグループ作成
- [x] Azure SQL Database (Serverless GP_S_Gen5_1) を作成し
  - [ ] ローカル IP のファイアウォール規則を追加する
- [x] Azure Blob Storage (LRS Standard) を作成する
- [x] Log Analytics ワークスペースを作成する（Container Apps 環境用）← `az containerapp up` が自動作成するため手動作業不要

## 2. Azure OpenAI / Microsoft AI Foundry モデルデプロイ（[[01-infra]]）

- [x] Azure AI Foundry ポータル（または Azure OpenAI Studio）で Azure OpenAI リソースを作成する
- [x] `gpt-5.4-mini` モデルをデプロイする
- [x] `text-embedding-3-small` モデルをデプロイする（デプロイ名: `embedding-small`）
- [x] デプロイが `Succeeded` 状態になることをポータルで確認する

注意: `Microsoft.Agents.AI` NuGet が依存する Azure AI Foundry のエンドポイントは、上記 Azure OpenAI リソースのエンドポイントと同一。追加でポータル作業は不要だが、モデルのリージョン可用性（japaneast で gpt-4o が使えること）を事前に確認する。

## 3. Azure AI Search インデックス設定（[[01-infra]]・[[03-ai-search-tool]]）

- [x] Azure AI Search (Basic) リソースを作成する
- [x] `opscontext-knowledge` インデックスを作成する（ポータル or REST。ベクトル検索フィールド `contentVector` 1536次元 / HNSW / コサイン類似度を設定する）
- [x] `opscontext-context` インデックスを作成する（同上）
- [x] インデックスの作成後、セマンティック検索が有効なプランであることを確認する（Basic は標準で有効）

## 4. 接続情報の記入（[[01-infra]]）

- [x] `OpsContext.Web/appsettings.Development.json` に以下をすべて記入する:
  - `OpsContext:AzureOpenAi:Endpoint`
  - `OpsContext:AzureOpenAi:ApiKey`
  - `OpsContext:SqlConnectionString`
  - `OpsContext:AiSearch:Endpoint`
  - `OpsContext:AiSearch:ApiKey`
  - `OpsContext:BlobConnectionString`
- [x] `OpsContext.Seed/appsettings.json` にも同じ接続情報を記入する（Endpoint / ApiKey / SqlConnectionString / AiSearch Endpoint・ApiKey）
- [x] `.gitignore` に `appsettings.Development.json` が追加されていることを確認する（`**/appsettings.Development.json` が `.gitignore` に記載済み）

## 5. SQL スキーマ・seed データ投入（[[02-sql-erp-tool]]）

- [x] Azure SQL Database に `sql/schema.sql` を実行してテーブルを作成する（sqlcmd -f 65001 でUTF-8指定が必要）
- [x] `sql/schema_context_store.sql` を実行してコンテキストストアテーブルを作成する（[[05-context-store]]）
- [x] `sql/seed.sql` を実行してサンプルデータを投入する（A商事 A001 / 弁P-101 等）
- [x] `SELECT COUNT(*) FROM Customers` で10件、`SELECT * FROM Inventory WHERE ProductCode='弁P-101'` で1行確認済み

## 6. AI Search ナレッジ seed 投入（[[03-ai-search-tool]]）

- [x] `OpsContext.Seed` コンソールプロジェクトをビルドして実行し、擬似ナレッジ17件を `opscontext-knowledge` インデックスに投入する（EmbeddingDeployment名は `text-embedding-3-small`）
- [x] AI Search インデックスは Seed が自動作成（`category` フィールド含む正しいスキーマで作成済み）
- [ ] Azure ポータルの AI Search「検索エクスプローラー」で `*` 検索を実行し、17件のドキュメントが確認できることを確認する（任意）
- [ ] `SearchKnowledgeAsync(query="与信", roleFilter="Accounting")` の単体動作を確認する（Claude が実装後に人が実行）

## 7. Container Apps デプロイ（[[09-deploy]]）

注意: `Dockerfile` はリポジトリルートに実装済み。`az containerapp up --source .` はそれを自動的に使用する。

- [ ] `az containerapp up --source . --name ca-opscontext ...` を実行してコンテナビルド + デプロイを完了させる
- [ ] システム割り当てマネージドID を有効化する: `az containerapp identity assign --system-assigned`
- [ ] Azure SQL Database の `CREATE USER [ca-opscontext] FROM EXTERNAL PROVIDER` を実行してマネージドID ユーザーを作成する
- [ ] AI Search / OpenAI への RBAC ロール付与を Azure ポータルまたは az コマンドで行う
- [ ] Blazor Server の SignalR セッションアフィニティを有効化する: `az containerapp ingress sticky-sessions set --affinity sticky`
- [ ] (マネージドID が詰まった場合のフォールバック) `az containerapp secret set` で接続文字列・APIキーを直接登録する

判断ライン: 5/31 19h 時点で Container Apps が動作していない場合は ngrok でローカル公開して提出 URL にする（[[09-deploy]] 参照）。

## 8. ngrok フォールバック（[[09-deploy]]）

- [ ] ngrok をインストールしてアカウント登録しておく（事前準備）
- [ ] 必要に応じて `ngrok http 5000` を実行し、発行された URL を提出 URL に使う
- [ ] 審査期間（6/2〜6/18）中、ローカルマシンをスリープしない設定にする

## 9. デモ動画撮影・編集・公開（Day 3）

- [ ] OBS Studio をインストールし、1080p / 30fps / マイク直の設定を確認する
- [ ] デモ台本（`video_script.md`）を読み込み、事前リハーサルを1〜2回行う
- [ ] OBS でデモ動画を撮影する（5テイク前提）
- [ ] 動画編集ツールで3分以内に編集する（章立てに沿ったカット・テロップ）
- [ ] YouTube に限定公開でアップロードし、公開 URL を取得する
- [ ] Azure AI Speech（合成音声）でナレーションを生成してオーバーレイする（CLAUDE.md 必須要件）

## 10. Zenn 記事執筆と提出（Day 3）

- [ ] `zenn_blog_draft.md` をベースに Zenn 記事を執筆する（アーキテクチャ図 Mermaid + デモ動画埋め込み + プロンプト設計説明を必須で含める）
- [ ] 記事内で GitHub リポジトリ URL と成果物 URL（Container Apps or ngrok）を記載する
- [ ] GitHub リポジトリを整理し、提出時点を `tag` で示す（branch ではなく tag を使う）
- [ ] 提出フォームに以下を入力して送信する:
  - 動作確認可能な成果物 URL
  - Zenn ブログ記事 URL
  - GitHub リポジトリ URL（任意）
- [ ] 提出後、審査期間（6/2〜6/18）を通じて成果物 URL が応答し続けることを確認する

---

## 追記ルール

Claude が design.md や実装作業中に「ポータル操作・認証情報記入・外部サービス登録・撮影・人による判断」が必要と判断した場合は、このファイルの適切なカテゴリに `- [ ]` を追記する。
