# dev/ — 軽量SDD（仕様駆動開発）ワークフロー

## 役割と3層構造

このフォルダは OpsContext（Microsoft Agent Hackathon 2026 応募作品）の実装作業場。

```
plan.md                     # 上流。WHY/WHAT の全体計画。変更不可（原則）
specs/<NN-slug>/design.md   # 機能単位の HOW + 進捗チェックリスト
コード（OpsContext.*）       # design.md を根拠に実装
```

`plan.md` が設計の唯一の出典（source of truth）。`design.md` は plan.md の該当節を引用・具体化するもの。矛盾が生じた場合は plan.md を修正する（または design.md の「上流参照」直下に逸脱理由を明記する）。

## design.md の命名と配置

- 1機能/コンポーネント = 1フォルダ = `specs/NN-slug/design.md`
- NN は実装依存順の採番（01 が完成してから 02 着手、という意味）

| No. | slug | 対応する plan.md 構成要素 |
|---|---|---|
| 01 | infra | Azureリソース構成（Day1 0-2h） |
| 02 | sql-erp-tool | Tool層 / SQLスキーマ / SqlErpTool |
| 03 | ai-search-tool | Tool層 / AiSearchTool |
| 04 | orchestrator-agents | エージェント設計 / Orchestrator + ロール別Agent |
| 05 | context-store | SQLスキーマ(コンテキストストア) / ContextStoreTool |
| 06 | curator-agent | エージェント設計 / CuratorAgent |
| 07 | blazor-ui | アーキテクチャ / Blazor Server UI |
| 08 | excel-validation | Tool層 / ExcelTool / QuoteValidationService |
| 09 | deploy | Container Apps デプロイ / タイムボックス Day2 後半 |

新しい機能が増えたら末尾に追番して追加する。

## design.md のテンプレート

新規作成時はこの7項目をそのままコピーして埋める。

```markdown
# NN-slug 設計

Status: 未着手

## 上流参照
plan.md の「<節名>」。関連: [[他のslug]]

## 目的
この単位が何を担うか（2-3行）

## スコープ
やること:
- xxx

やらないこと（plan.md「やらないこと」より該当を転記）:
- xxx

## 設計詳細
インターフェース・クラス・データフロー・プロンプト骨子など

## 依存
- 依存先: NN-slug（理由）

## 受け入れ条件
plan.md「検証方法」の該当ステップ番号と内容を転記

## タスク
- [ ] タスク1
- [ ] タスク2
```

## 進捗管理ルール

- `- [ ]` = 未着手 / `- [x]` = 完了
- 着手中のタスク行末に `(WIP)` を付す（任意）
- 各 design.md 先頭の `Status:` を `未着手 / 進行中 / 完了` で書き換える
- 全体の進み具合は plan.md「タイムボックス」節で確認（別途インデックスファイルは作らない）

## Claude への作業手順

1. 新機能の着手時
   - 該当 `specs/NN-slug/design.md` がなければテンプレートで作成
   - plan.md の該当節から設計詳細を具体化して埋める
   - タスクを5-10行程度に分解する

2. 実装時
   - design.md のタスクを上から順に消化し、完了したら `- [x]` に更新
   - plan.md とズレる実装判断をした場合は「上流参照」直下に逸脱メモを1行残す
   - 実装中に「ポータル操作・認証情報記入・外部サービス登録・撮影・人による判断が必要」と気づいたら `specs/human-task.md` の該当カテゴリに `- [ ]` を追記する

3. 完了時
   - Status を `完了` に書き換える
   - 未消化タスクがあれば残したまま完了にして良い（ハッカソン向け）

## human-task.md

`specs/human-task.md` は Claude が実行できない人手タスク（Azure ポータル操作・接続情報記入・動画撮影・提出フォーム入力等）を集約したファイル。

- 実装作業中に人手タスクが生じたら、その都度 `specs/human-task.md` の適切なカテゴリに追記する
- 追記するのは「ポータル操作」「CLI コマンドの実行判断」「認証情報の記入」「外部サービスへの登録・公開」「動画・記事・提出物の作成」に該当するもの
- コードの変更で完結するタスクは design.md のタスク欄に残し、human-task.md には書かない

## 継承する上位規約

親フォルダ `CLAUDE.md`（20260526_hackason/CLAUDE.md）の方針を継承する。主要なもの:
- 質問は3つまで（一度に確認したい論点は3つを上限に）
- 発散と収束を意識（結論を急がず選択肢を広げてから絞る）
- 審査基準（ビジネスインパクト / Agentic性 / 完成度）で常に評価
- 箇条書きは `- `（半角スペース）、太字・イタリクス記法は使わない
