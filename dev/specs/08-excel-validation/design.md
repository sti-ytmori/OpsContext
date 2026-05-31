# 08-excel-validation 設計

Status: 完了

## 上流参照

plan.md の「Tool層シグネチャ」「エージェント設計（Salesエージェント Excel Tool 欄）」「タイムボックス Day2 16-19h」「デモ動画 0:45-2:05」「検証方法 ステップ3-8」「Critical Files」「やらないこと」。
関連: [[02-sql-erp-tool]] [[05-context-store]] [[07-blazor-ui]]

## 目的

営業担当者が受け取った見積依頼明細（.xlsx）を OpsContext に取り込み、擬似 ERP の在庫・与信・生産データと突合して各行に OK / Warning / NG の判定を付与する。判定結果を着色済み .xlsx でエクスポートする導線を提供し、デモシナリオ 0:45-2:05 の Excel 突合フローを実現する。

## スコープ

やること:
- `IExcelTool` の定義: `ParseQuoteLines(Stream xlsx)` / `ExportWithVerdicts(IReadOnlyList<QuoteLine> lines)`
- `ExcelService` の実装（ClosedXML 採用）
- `QuoteLine` モデルの定義（入力フィールド + 出力フィールド）
- `ICalcTool` の定義: `CreditAvailable` / `GrossMarginRatio` の2メソッドの計算ロジック
- `QuoteValidationService.ValidateAsync` の実装（SqlErpTool 並列呼び出し + ICalcTool 判定 + LLM 推奨文生成）
- デモ用サンプルファイル `sample/見積依頼明細.xlsx` の作成（5-8行、NG/Warning/OK 混在）
- `QuoteReview.razor` に対する UI 要件の定義（実装は [[07-blazor-ui]] が担う）
- 取込事実を `ContextEntries` に observation として記録する方針の定義

やらないこと（plan.md「やらないこと」より）:
- Excel の自由編集（自然言語での任意セル変更）
- 見積明細の DB 永続化（明細は in-memory 保持、取込 observation のみ ContextEntries に記録）
- コア E2E（ステップ1-2 のチャット基幹突合フロー）が未完の場合、本機能全体をカットする（plan.md「タイムボックス Day2 16-19h」より、コア E2E 優先）
- ClosedXML 以外の Excel ライブラリの採用（EPPlus は商用ライセンスのため不採用）
- ストリーミング応答
- Bicep / Terraform / 単体テスト / CI

## 設計詳細

### QuoteLine モデル

`OpsContext.Agents/Models/QuoteLine.cs` に定義する。

入力フィールド（Excel 取込時に読み込む列）:
- `ProductCode` (string) — 製品コード（例: 弁P-101）
- `ProductName` (string) — 製品名
- `Qty` (int) — 数量
- `RequestedDate` (DateOnly) — 希望納期
- `UnitPrice` (decimal) — 提示単価

出力フィールド（突合後に書き戻す列）:
- `Verdict` (enum: OK / Warning / NG) — 総合判定
- `RiskLevel` (string) — リスク分類（与信超過 / 在庫不足 / 納期不可 / 粗利不足 など）
- `Recommendation` (string) — 推奨アクション（LLM が生成）
- `RefNote` (string) — 根拠数値の参照（例: 在庫80個、与信残12M）

### IExcelTool インターフェース

```csharp
public interface IExcelTool
{
    IReadOnlyList<QuoteLine> ParseQuoteLines(Stream xlsx);
    byte[] ExportWithVerdicts(IReadOnlyList<QuoteLine> lines);
}
```

実装クラス: `OpsContext.Agents/Tools/ExcelService.cs`

ライブラリ選定:
- ClosedXML (MIT ライセンス) を採用
- EPPlus は商用ライセンス（非 LGPL）のため不採用（plan.md 明記事項）

`ParseQuoteLines`:
- A列から順に ProductCode / ProductName / Qty / RequestedDate / UnitPrice を読み取る
- 1行目はヘッダー行としてスキップ
- 空行に当たったら読み取り終了

`ExportWithVerdicts`:
- 入力 xlsx のレイアウトをベースに Verdict / RiskLevel / Recommendation / RefNote 列を右端に追記
- Verdict=NG の行は背景色を赤 (#FFCCCC)、Warning は黄 (#FFFACD)、OK は緑 (#CCFFCC) で着色
- byte[] として返し、Blazor の `IJSRuntime` 経由でブラウザダウンロードさせる

### ICalcTool インターフェース

```csharp
public interface ICalcTool
{
    decimal CreditAvailable(decimal limit, decimal used, decimal pendingOrderAmount);
    decimal GrossMarginRatio(decimal price, decimal cost);
}
```

実装クラス: `OpsContext.Agents/Tools/CalcTool.cs`

設計思想:
- 与信残高計算・粗利率計算は決定的な数値判断であり、LLM に委ねない
- `CreditAvailable` = limit - used - pendingOrderAmount（負値 = 与信超過）
- `GrossMarginRatio` = (price - cost) / price（0以下 = 逆ザヤ）
- 判定閾値: CreditAvailable < 0 → NG、0 以上かつ与信枠の 20% 未満 → Warning / GrossMarginRatio < 0.15 → Warning

### QuoteValidationService.ValidateAsync

`OpsContext.Agents/QuoteValidationService.cs` に実装する。

処理フロー:
1. `IExcelTool.ParseQuoteLines` で明細リストを取得
2. distinct 製品コードと顧客コードで `ISqlErpTool` の以下を `Task.WhenAll` で並列実行:
   - `GetCustomerCreditAsync(customerCode)` — 与信枠・使用額取得
   - `GetProductInventoryAsync(productCode)` — 在庫数取得（製品コードごと）
   - `GetProductionCapacityAsync(productCode, from, to)` — 生産枠取得（製品コードごと）
3. 並列実行結果が揃ったら `ICalcTool` で各行の判定を確定:
   - 数量 > 在庫数 + 生産枠 → NG（在庫不足）
   - `CreditAvailable` < 0 → NG（与信超過）
   - `CreditAvailable` が与信枠の 20% 未満 → Warning（与信逼迫）
   - `GrossMarginRatio` < 0.15 → Warning（粗利不足）
   - 上記いずれも該当しない → OK
4. Verdict が確定した各行の `Recommendation` のみ LLM（Azure OpenAI）に生成させる（数値判断は渡さない）
5. `IContextStoreTool.AppendObservationAsync` で取込事実を記録:
   - テキスト例: 「Excel明細 6 行取込・うち 2 行リスク(NG:1 Warning:1)」
   - Kind = observation

数値判断を LLM に委ねない理由: 計算結果の再現性・監査可能性を確保するため、閾値評価はコードで行い LLM は文章生成のみ担当する。

### sample/見積依頼明細.xlsx データ設計

デモ用サンプルファイルを `sample/見積依頼明細.xlsx` に配置する。

| ProductCode | ProductName | Qty | RequestedDate | UnitPrice | 期待 Verdict |
|---|---|---|---|---|---|
| 弁P-101 | 高圧弁 弁P-101型 | 200 | 2026-06-13 | 80000 | NG (与信超過 + 在庫不足) |
| 弁P-101 | 高圧弁 弁P-101型 | 150 | 2026-06-13 | 80000 | Warning (与信逼迫) |
| 弁Q-202 | 制御弁 Q-202型 | 30 | 2026-07-01 | 45000 | OK |
| 弁Q-202 | 制御弁 Q-202型 | 10 | 2026-07-15 | 45000 | OK |
| ポンプR-310 | 循環ポンプ R-310 | 5 | 2026-06-20 | 120000 | OK |
| フランジS-50 | 配管フランジ S-50 | 100 | 2026-06-30 | 12000 | Warning (粗利不足) |

合計 6 行（5-8 行範囲内）。A商事(A001) の与信枠残 12M に対して弁P-101 200個（1600万円）は超過するため NG 判定が確実に出る構成とする。

### UI 要件（QuoteReview.razor 向け定義）

実装は [[07-blazor-ui]] で行うが、本 design で要件を定義する。

コンポーネント: `OpsContext.Web/Components/Pages/QuoteReview.razor`

必要な UI 要素:
- `InputFile` — .xlsx ファイルの選択・アップロード（ファイル選択後に自動で `ParseQuoteLines` 呼び出し）
- MudBlazor `MudDataGrid<QuoteLine>` — 取込行を表示。Verdict 列は色付きバッジ（NG=赤、Warning=黄、OK=緑）
- 「基幹と突合」ボタン — `QuoteValidationService.ValidateAsync` を呼び出してグリッドを更新
- 数量の inline 編集 — Qty を変更すると即時に `ValidateAsync` を再実行して判定を更新
- 「ダウンロード」ボタン — `ExportWithVerdicts` を呼び出し、`IJSRuntime` で .xlsx をブラウザダウンロード
- 「この線で進めます」ボタン — `AppendDecisionAsync` で decision を記録し、CuratorAgent に ConversationEvent を送出、「Curator: 決定を記録しました」トーストを表示

明細は Blazor ページのメモリ（`List<QuoteLine>` フィールド）で保持する。ページ遷移でリセットされる。

### コンテキスト記録方針

- 明細データ自体は DB に永続化しない（in-memory 保持）
- 取込完了時に `IContextStoreTool.AppendObservationAsync` で以下を記録:
  - CaseId: 現在の営業案件 ID
  - Role: Sales
  - Kind: observation
  - Text: 「Excel明細 N 行取込・うち M 行リスク(NG:X Warning:Y)」
- 「この線で進めます」時に `IContextStoreTool.AppendDecisionAsync` で decision を記録

## 依存

- [[02-sql-erp-tool]]: `SqlErpTool` の `GetCustomerCreditAsync` / `GetProductInventoryAsync` / `GetProductionCapacityAsync` を `QuoteValidationService` 内で並列実行
- [[05-context-store]]: 取込事実（observation）と担当者決定（decision）を `ContextEntries` に記録

後続:
- [[07-blazor-ui]]: `QuoteReview.razor` の UI 実装がこの design の UI 要件を参照する

## 受け入れ条件

plan.md「検証方法」ステップ3-8 の通り:

- ステップ3: `sample/見積依頼明細.xlsx` をアップロード → DataGrid に 5-8 行が表示されること
- ステップ4: 「基幹と突合」実行 → 右ペインに `GetCustomerCredit / GetProductInventory / GetProductionCapacity` の SQL カードが並列で現れること
- ステップ5: 弁P-101 200個の行が NG (赤) で表示され、推奨に「150+80 分割」相当が入ること
- ステップ6: 在庫/与信に余裕のある行が OK (緑) で表示されること
- ステップ7: 数量を inline で 150 に修正 → 判定が Warning/OK に変わること
- ステップ8: 「ダウンロード」→ 判定/リスク/推奨列が追記され、リスク行が着色された .xlsx が取得できること

## タスク

- [x] `OpsContext.Agents/Models/QuoteLine.cs` を作成（入力5フィールド + 出力4フィールド + Verdict enum）
- [x] NuGet に `ClosedXML` を追加、`OpsContext.Agents/Tools/ExcelService.cs` で `IExcelTool` を実装（parse + export + 着色）
- [x] `OpsContext.Agents/Tools/CalcTool.cs` で `ICalcTool` を実装（CreditAvailable / GrossMarginRatio、閾値判定ロジック）
- [x] `OpsContext.Agents/QuoteValidationService.cs` を実装（Task.WhenAll 並列 SqlErp 呼び出し → CalcTool 判定 → LLM 推奨文生成 → observation 記録）
- [x] `sample/見積依頼明細.xlsx` を作成（6行、NG/Warning/OK 混在、弁P-101 NG/Warning 確定構成）
- [x] DI 登録: `IExcelTool` / `ICalcTool` / `QuoteValidationService` を `Program.cs` にスコープ登録
- [x] `QuoteReview.razor` を `QuoteValidationService.ValidateAsync` に配線（モックロジックを削除）
- [ ] E2E 手動確認: ステップ3-8 の受け入れ条件を全通しして `- [x]` に更新（Azure接続後）
