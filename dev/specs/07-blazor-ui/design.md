# 07-blazor-ui 設計

Status: 進行中

## 上流参照

plan.md の「アーキテクチャ / Blazor Server: OpsContext.Web」「認証」「タイムボックス Day1 13-18h / Day2 11-16h」「デモ動画（0:00〜3:00）」「検証方法」「やらないこと」。
関連: [[04-orchestrator-agents]] [[05-context-store]] [[06-curator-agent]] [[08-excel-validation]] [[09-deploy]]

## 目的

Blazor Server アプリ `OpsContext.Web` として、チャット + ツール呼出カード（左右 2 ペイン）・案件一覧・ロール切替ドロップダウン・Excel 突合画面（QuoteReview）を提供する。
フェイクロール認証（FakeRoleAuthenticationHandler）で4ロールのログイン切替を実現し、Entra ID への差し替えが DI の変更のみで可能な抽象化を維持する。
UI ライブラリは MudBlazor を採用する。

## スコープ

やること:
- `dotnet new blazor -int Server` で `OpsContext.Web` プロジェクト作成
- MudBlazor パッケージ追加と MudThemeProvider 設定
- フェイクロール認証（FakeRoleAuthenticationHandler + 右上ドロップダウン + Cookie 保存）
- チャットページ（`/chat`）: 左ペイン（会話）+ 右ペイン（ツール呼出カード / SQL 結果）
- 案件一覧ページ（`/cases`）: Cases テーブルの一覧 + 案件クリックで `/chat?caseId=<id>` に遷移
- QuoteReview ページ（`/quote`）: MudDataGrid + InputFile + ダウンロードボタン（[[08-excel-validation]] 要件実装）
- ロール別のヘッダー色変化（Sales=緑 / Purchasing=橙 / Production=青 / Accounting=赤）で視覚的にロールを識別
- 「この線で進めます」ボタン押下 → AppendDecisionAsync + ICuratorQueue.Enqueue の配線
- Curator 完了トースト（「Curator: 決定を記録しました」MudSnackbar）
- CaseId の NewGuid() 生成とセッション保持（CascadingValue）

やらないこと（plan.md「やらないこと」より）:
- Entra ID 本物連携（FakeRoleAuthenticationHandler で代替）
- ストリーミング応答（応答完了後に一括表示）
- マルチターン会話の永続化（チャット履歴はコンポーネントステートのみ）
- Cosmos DB / Power Platform / Speech / Vision
- Excel の自由編集（自然言語での任意セル変更）

## 設計詳細

### プロジェクト構成

```
OpsContext.Web/
  Program.cs
  appsettings.json
  appsettings.Development.json   -- .gitignore 除外（[[01-infra]] 参照）
  Components/
    App.razor
    Layout/
      MainLayout.razor           -- 2カラムレイアウト + ロール切替ドロップダウン
      NavMenu.razor
    Pages/
      Chat.razor                 -- メインチャットページ (/chat)
      Cases.razor                -- 案件一覧 (/cases)
      QuoteReview.razor          -- Excel突合ページ (/quote)
  Services/
    FakeRoleAuthenticationHandler.cs
    RoleStateService.cs          -- 現在ロールをカスケードする Scoped サービス
```

### フェイクロール認証

Curia の認証は WPF 固有。OpsContext では以下の独自実装を使う。

`FakeRoleAuthenticationHandler`: `AuthenticationHandler<AuthenticationSchemeOptions>` を継承。Cookie `opscontext_role` の値（Sales / Purchasing / Production / Accounting）を読んで `ClaimsIdentity` を生成する。値がなければ `Sales` をデフォルトとする。

右上ドロップダウン（MudSelect）でロールを選択すると Cookie をセットしてページをリロードする。

```csharp
// Program.cs 抜粋
builder.Services.AddAuthentication("FakeRole")
    .AddScheme<AuthenticationSchemeOptions, FakeRoleAuthenticationHandler>("FakeRole", null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AnyRole", p => p.RequireAuthenticatedUser());
```

Entra ID への差し替え時は `AddScheme` 部分を `AddMicrosoftIdentityWebApp` に変えるだけ（DI 抽象化方針）。

### チャットページ（Chat.razor）レイアウト

```
┌─────────────────────────────────────────────────────────┐
│ ヘッダー: OpsContext [ロール色] [ロール切替▼] [案件一覧]  │
├────────────────────────┬────────────────────────────────┤
│ 左ペイン (チャット)      │ 右ペイン (ツール呼出カード)      │
│                        │ ┌──────────────────────────┐ │
│ [エージェント] ...       │ │ GetCustomerCredit (SQL)   │ │
│ [ユーザー] ...           │ │ > SELECT ...              │ │
│                        │ │ 結果: 与信残12M ...        │ │
│                        │ └──────────────────────────┘ │
│ ─────────────────────  │ ┌──────────────────────────┐ │
│ [テキストボックス] [送信] │ │ SearchDecisionLogs        │ │
│ [この線で進めます]        │ │ ...                      │ │
│                        │ └──────────────────────────┘ │
└────────────────────────┴────────────────────────────────┘
```

ツール呼出カードは `ToolCallResult` リスト（ToolName / SqlOrQuery / ResultMarkdown）をコンポーネントステートに保持し、エージェント応答後にレンダリングする。

### 案件一覧ページ（Cases.razor）

- ページロード時に `SELECT CaseId, Title, CustomerCode, Status, UpdatedAt FROM Cases ORDER BY UpdatedAt DESC` を実行して MudTable に表示。
- 行クリックで `NavigationManager.NavigateTo($"/chat?caseId={caseId}")` して既存案件のコンテキストをロード。
- コンテキストロード: `/chat` ページの OnParametersSetAsync で `ReadCaseContextAsync(caseId, currentRole)` を呼び、チャット履歴を再構成する。

### QuoteReview ページ（/quote）

[[08-excel-validation]] の UI 要件実装。

- InputFile（MudFileUpload）: .xlsx ファイルをアップロードし `ExcelTool.ParseQuoteLines()` を呼び出す
- MudDataGrid: QuoteLine リストを表示。Verdict 列は OK=緑/Warning=黄/NG=赤のセル背景色
- 「基幹と突合」ボタン: `QuoteValidationService.ValidateAsync()` を呼び出し（SQL 並列実行）、DataGrid を更新
- Qty 列インライン編集: MudDataGrid の `EditMode=DataGridEditMode.Cell` で有効化
- 編集後に再判定ボタン or 自動再判定（任意）
- 「ダウンロード」ボタン: `ExcelTool.ExportWithVerdicts(lines)` → `byte[]` → `IJSRuntime` で `saveAs` ダウンロード
- 「この線で進めます」ボタン: AppendDecisionAsync + ICuratorQueue.Enqueue を呼ぶ（Chat ページと同じ配線）

### CaseId のセッション保持

```csharp
// Chat.razor の OnInitializedAsync
if (string.IsNullOrEmpty(CaseIdFromQuery))
    _caseId = Guid.NewGuid().ToString();
else
    _caseId = CaseIdFromQuery;
```

CaseId は `CascadingValue` または URL クエリパラメータで子コンポーネントに渡す。

### ロール別ヘッダー色

```csharp
private static string RoleColor(string role) => role switch
{
    "Sales"      => "#2e7d32",  // 緑
    "Purchasing" => "#e65100",  // 橙
    "Production" => "#1565c0",  // 青
    "Accounting" => "#b71c1c",  // 赤
    _            => "#424242"
};
```

### Curator トースト

Curator の完了通知は `IHubContext<CuratorHub>` + SignalR または 3秒 polling（どちらでも可）で Blazor に伝達し、`MudSnackbar` で「Curator: 決定を記録しました」を表示する。

### ファイル配置（Critical Files 反映）

plan.md「Critical Files」より:
- `OpsContext.Web/Components/Pages/QuoteReview.razor` — Excel突合 DataGrid
- `OpsContext.Web/Components/Pages/Chat.razor` — メインチャット
- `OpsContext.Web/Components/Pages/Cases.razor` — 案件一覧
- `OpsContext.Web/Services/FakeRoleAuthenticationHandler.cs` — フェイク認証

## 依存

- [[04-orchestrator-agents]]: エージェント呼び出し（Orchestrator への Chat メッセージ送信）が実装済みであること
- [[05-context-store]]: IContextStoreTool が DI に登録済みであること（AppendDecisionAsync / ReadCaseContextAsync）
- [[06-curator-agent]]: ICuratorQueue が DI に登録済みであること
- [[08-excel-validation]]: IExcelTool / QuoteValidationService が実装済みであること

## 受け入れ条件

plan.md「検証方法」より:

- ステップ1: `dotnet run --project OpsContext.Web` でローカル起動し、エラーが出ないこと
- ステップ2: 右上ドロップダウンで Sales を選択し、ヘッダーが緑になること
- ステップ3: `sample/見積依頼明細.xlsx` をアップロードし DataGrid に 5〜8 行が表示されること
- ステップ4: 「基幹と突合」実行後、右ペインに SQL カードが並列で現れること
- ステップ5: 弁P-101 200個の行が NG (赤) で表示され推奨文が含まれること
- ステップ6: 在庫/与信に余裕ある行が OK (緑) で表示されること
- ステップ7: Qty を 150 に編集後、判定が Warning/OK に変わること
- ステップ8: ダウンロードで判定列着色 .xlsx が取得できること
- ステップ9: 「この線で進めます」ボタン後に「Curator: 決定を記録しました」トーストが出ること
- ステップ11: Accounting ドロップダウン切替後、案件一覧から同案件を選択できること
- ステップ12: 経理視点要約に「A商事 3か月前与信遅延 / 与信枠80%消費」が含まれること

## タスク

- [x] `dotnet new blazor -int Server` で OpsContext.Web を作成し、MudBlazor パッケージを追加する
- [x] `FakeRoleAuthenticationHandler` を実装し、Cookie ベースのロール切替ドロップダウンを動作させる
- [x] `MainLayout.razor` に2ペインレイアウトとロール別ヘッダー色を実装する
- [x] `Chat.razor` を実装する（チャット入力・送信・エージェント応答表示・ツール呼出カード）
- [x] 「この線で進めます」ボタンの配線（AppendDecisionAsync + ICuratorQueue.Enqueue）と Curator トーストを実装する
- [x] `Cases.razor` を実装する（Cases テーブル一覧 + 案件クリックで /chat?caseId= 遷移）
- [x] `QuoteReview.razor` を実装する（InputFile + MudDataGrid + 突合ボタン + inline 編集 + ダウンロード）
- [x] CaseId の NewGuid() 生成とセッション保持（URL クエリパラメータ）を実装する
- [ ] ローカルで E2E 1 本（ステップ1〜12）を通して動作確認する（Azure接続後）
