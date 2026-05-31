# E2E テストスイート (dev/tests)

OpsContext.Web の Playwright E2E テスト。

## 構成

```
dev/tests/
├── CLAUDE.md              # このファイル
├── CHECKLIST.md           # テスト一覧チェックリスト（53 ケース）
├── package.json           # @playwright/test 依存
├── playwright.config.ts   # ベースURL・タイムアウト設定
├── helpers/
│   └── auth.ts            # login() / logout() ユーティリティ・ユーザー定数
└── specs/
    ├── 01_login.spec.ts   # ログイン・ログアウト・認証リダイレクト
    ├── 02_home.spec.ts    # ホームページ・クイックアクション・ナビ
    ├── 03_chat.spec.ts    # チャット送受信・ローディング・ボタン状態・caseId ヘッダー
    ├── 04_data.spec.ts    # 業務データ一覧・グリッド・ボタン群
    ├── 05_context.spec.ts # コンテキスト管理・クエリパラメータ
    ├── 06_accounts.spec.ts# アカウント管理（Admin/非Admin アクセス制御）
    ├── 07_roles.spec.ts   # ロール設定・編集エリア・保存ボタン
    ├── 08_dataset.spec.ts # 基幹データセット（/dataset）・タブ・同期設定
    └── 09_admin.spec.ts   # 管理・デバッグ（/admin）・パスワードゲート・デモデータ
```

## 前提

- アプリが `http://localhost:5179` で起動していること
- 起動コマンド: `dotnet watch --project OpsContext.Web`
- テスト用アカウント（MockUserStore / 共通パスワード `Demo@2026`）:
  - `user_system` — Admin
  - `user_sales` — Sales
  - `user_purchasing` — Purchasing
  - `user_production` — Production
  - `user_accounting` — Accounting

## テスト実行

```powershell
cd dev\tests
npm install
npm test               # ヘッドレス実行
npm run test:ui        # UI モード（デバッグ用）
npm run test:report    # HTML レポート表示
```

## 方針

- 各 spec ファイルの先頭コメントブロックがそのファイルのチェックリストを兼ねる
- CHECKLIST.md が全ケースの一覧（手動確認でも使える形式）。現在 71 ケース（うち更新系 18 件）
- AI 応答が絡む TC-03-08 等はタイムアウトを長めに設定して対応
- テスト間で状態を持ち越さないよう、各テストで login() からフレッシュに開始する
- 不安定になりやすいセレクターは aria-label / role ベースを優先する

## 破壊的操作（削除・更新）のテスト方針

削除・上書きなど DB に影響する操作のテストは、テスト自身がデータを作成してから操作する。
既存のシードデータや本番データには触らない。

```typescript
test("削除できる", async ({ page }) => {
  // 1. テスト専用データを作成
  await page.getByRole("button", { name: "新規業務データ" }).click();
  await page.getByLabel("業務データ名").fill("削除テスト用_" + Date.now());
  await page.getByRole("button", { name: "作成して開く" }).click();
  await page.goto("/data");

  // 2. 作ったデータだけを削除
  await page.locator('[title="削除"]').last().click();

  // 3. 消えたことを確認
  await expect(page.getByText("業務データを削除しました")).toBeVisible();
});
```

- 名前に `Date.now()` 等を含めてテストデータと実データを区別できるようにする
- SQL Server 接続（本番モード）でも、テスト前後で DB の状態が変わらないことを保証する
- テストが途中で失敗してゴミが残る場合は、次回実行時の影響を受けないよう名前や ID で識別できる設計にする
