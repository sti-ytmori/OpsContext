# 10-grid-agentic 設計

Status: 完了

## 上流参照
plan.md の「Grid × AI 協働シーケンス（業務データ画面）」「エージェント設計（GridChatService）」。関連: [[08-excel-validation]]。

逸脱メモ: plan.md は GridChatService が「LLM がアクション JSON を1つ返し、コードが適用する」設計だったが、Agentic 性（審査基準2）強化のため、自律ツールループ（Function Calling）型の GridAgentService に置換した。validate_erp という固定 op は廃止し、AI が read 系ツールを自己判断で多段呼び出しして突合する形に変更。

## 目的
業務データ画面 /data の AI を「意図分類器」から「自律ツール選択エージェント」に作り替える。AI が自分でデータ構造（列の意味）と基幹データを読み解き、複数ツールを多段で呼び、編集を提案する。実行過程を可視化し、編集は人間承認を必須にする。

## スコープ
やること:
- 生 IChatClient + ChatOptions.Tools + 自前ツールループ（FunctionInvokingChatClient は不使用）
- 読み取り系ツール（自動実行）: describe_table / read_rows / get_customer_credit / get_product_inventory / get_production_capacity / search_similar_orders / search_knowledge / evaluate_quote_against_erp
- 書き込み系ツール（提案のみ）: propose_set_cell / add_row / add_column / delete_row / delete_column
- IProgress<AgentActivity> による実行ステップのリアルタイム可視化
- PendingEdit による人間承認ゲート（承認時のみ IDataGridStore へ適用）
- MockChatClient をツール呼び出しプロトコルの状態機械として再現

やらないこと（plan.md「やらないこと」より）:
- ストリーミング応答
- 編集系 SQL（読み取りは SELECT-only 維持、Grid 変更は IDataGridStore のみ）
- /chat 側の Agentic 化（今回は /data に集中）

## 設計詳細
- OpsContext.Agents/Models/AgentActivityModels.cs — AgentActivity / PendingEdit / GridAgentResult
- OpsContext.Agents/GridAgentService.cs — ツール定義 + 自律ループ + ApplyEditAsync（承認適用）
- OpsContext.Agents/Services/MockChatClient.cs — GRID_AGENT_MODE + Tools 時に FunctionCallContent を状態機械で返す
- OpsContext.Web/Components/Pages/Data.razor — 右ペインを「編集提案（差分カード＋一括承認）」+「エージェント実行ステップ（タイムライン）」に刷新
- OpsContext.Web/Program.cs — DI を GridAgentService に差し替え

ループ: GetResponseAsync(messages, {Tools}) → FunctionCallContent を抽出 → AIFunction.InvokeAsync → FunctionResultContent を messages に追加 → ツール呼び出しが無くなるまで反復（最大10）。書き込み系は Grid を変更せず PendingEdit を積むのみ。

## 依存
- 依存先: 02-sql-erp-tool（ISqlErpTool）, 08-excel-validation（QuoteValidationService）, 05-context-store（IContextStoreTool）, 07-blazor-ui（Data.razor）

## 受け入れ条件
plan.md「検証方法」5〜8 を Agentic 版に更新:
- /data で「基幹と突合して直して」→ 実行ステップが describe→read→ERP照会→evaluate→propose と順次表示
- 承認前は Grid 不変、差分カード承認時のみセル反映、一括承認も可
- /context に適用が observation 記録される
- 実 Azure OpenAI 接続でも同等動作

## タスク
- [x] AgentActivityModels.cs 作成
- [x] GridAgentService.cs 作成（ツール定義 + ループ + ApplyEditAsync）
- [x] MockChatClient ツールモード再現
- [x] Data.razor 右ペイン刷新（タイムライン + 提案承認）
- [x] Program.cs DI 差し替え・旧 GridChatService/GridAction 削除
- [x] ビルド確認（OpsContext.Web 成功）
- [ ] 実 Azure OpenAI 接続での E2E 確認（Container Apps デプロイ前）
