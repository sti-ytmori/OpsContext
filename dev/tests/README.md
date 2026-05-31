# OpsContext E2E テスト

OpsContext.Web の Playwright E2E テストスイートです。

## 前提条件

- Node.js 18 以上
- アプリが起動済みであること

```powershell
dotnet watch --project OpsContext.Web
```

## セットアップ

```powershell
cd dev\tests
npm install
npx playwright install chromium
```

## 実行

```powershell
# ヘッドレス実行（CI 向け）
npm test

# UI モード（デバッグ・ステップ実行）
npm run test:ui

# HTML レポート表示
npm run test:report
```

## テストアカウント

MockUserStore の共通パスワードは `Demo@2026`。

| ユーザー名        | 表示名         | ロール      |
|------------------|----------------|-------------|
| user_system      | システム管理者  | Admin       |
| user_sales       | 田中 一郎       | Sales       |
| user_purchasing  | 鈴木 花子       | Purchasing  |
| user_production  | 佐藤 次郎       | Production  |
| user_accounting  | 山田 三郎       | Accounting  |

## テストケース一覧

| ファイル               | 対象画面         | ケース数 |
|-----------------------|-----------------|---------|
| 01_login.spec.ts      | ログイン         | 8       |
| 02_home.spec.ts       | ホーム           | 7       |
| 03_chat.spec.ts       | チャット         | 12      |
| 04_data.spec.ts       | 業務データ        | 18      |
| 05_context.spec.ts    | コンテキスト管理  | 9       |
| 06_accounts.spec.ts   | アカウント管理    | 10      |
| 07_roles.spec.ts      | ロール設定        | 6       |
| 08_dataset.spec.ts    | 基幹データセット   | 6       |
| 09_admin.spec.ts      | 管理・デバッグ    | 4       |
| 合計                   |                 | 80      |

詳細は [CHECKLIST.md](./CHECKLIST.md) を参照。
