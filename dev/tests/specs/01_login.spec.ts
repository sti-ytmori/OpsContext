/**
 * TC-01: ログイン画面
 *
 * チェックリスト:
 * [ ] TC-01-01  ページタイトルが "ログイン - OpsContext" である
 * [ ] TC-01-02  "OpsContext" ロゴと "ERP Agent" チップが表示される
 * [ ] TC-01-03  誤パスワードでエラーアラートが表示される
 * [ ] TC-01-04  正常ログイン (user_sales) → "/" へリダイレクト
 * [ ] TC-01-05  Microsoft アカウントボタンが disabled 状態（未実装バッジ付き）
 * [ ] TC-01-06  ログイン後、ヘッダーにユーザー名とロールが表示される
 * [ ] TC-01-07  ログアウトで "/login" へ戻る
 * [ ] TC-01-08  未認証で "/" にアクセスすると "/login" へリダイレクト
 */

import { test, expect } from "@playwright/test";
import { login, logout, USERS } from "../helpers/auth";

test.describe("TC-01: ログイン画面", () => {

  test("TC-01-01 ページタイトル", async ({ page }) => {
    await page.goto("/login");
    await expect(page).toHaveTitle("ログイン - OpsContext");
  });

  test("TC-01-02 ロゴ・チップ表示", async ({ page }) => {
    await page.goto("/login");
    await expect(page.getByText("OpsContext").first()).toBeVisible();
    await expect(page.getByText("ERP Agent")).toBeVisible();
    await expect(page.getByText("アカウントにサインイン")).toBeVisible();
  });

  test("TC-01-03 誤パスワードでエラーアラート", async ({ page }) => {
    await page.goto("/login");
    await page.fill('input[name="userName"]', "user_sales");
    await page.fill('input[name="password"]', "wrongpassword");
    await page.click('button[type="submit"]');
    await expect(page.getByText("ユーザー名またはパスワードが正しくありません。")).toBeVisible();
  });

  test("TC-01-04 正常ログイン → ホームへ遷移", async ({ page }) => {
    await login(page, USERS.sales);
    await expect(page).toHaveURL("/");
  });

  test("TC-01-05 Microsoft ボタンが disabled", async ({ page }) => {
    await page.goto("/login");
    const msBtn = page.getByText("Microsoft アカウントでサインイン");
    await expect(msBtn).toBeVisible();
    const btn = page.locator('button[disabled]', { hasText: "Microsoft アカウントでサインイン" });
    await expect(btn).toBeDisabled();
    await expect(page.getByText("未実装")).toBeVisible();
  });

  test("TC-01-06 ログイン後ヘッダーにユーザー名・ロール表示", async ({ page }) => {
    await login(page, USERS.sales);
    await expect(page.getByText("田中 一郎")).toBeVisible();
    await expect(page.getByText("Sales").first()).toBeVisible();
  });

  test("TC-01-07 ログアウトで /login へ戻る", async ({ page }) => {
    await login(page, USERS.sales);
    await logout(page);
    await expect(page).toHaveURL(/\/login/);
  });

  test("TC-01-08 未認証で保護ページへアクセス → /login へリダイレクト", async ({ page }) => {
    await page.goto("/accounts");
    await expect(page).toHaveURL(/\/login/);
  });

});
