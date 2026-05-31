/**
 * TC-09: 管理・デバッグページ (/admin)
 *
 * チェックリスト:
 * [ ] TC-09-01  ページタイトルが "OpsContext - 管理・デバッグ" である
 * [ ] TC-09-02  未解錠時にパスワードゲート（「ロック解除」ボタン）が表示される
 * [ ] TC-09-03  誤パスワードで「パスワードが違います」エラーが表示される
 * [ ] TC-09-04  正しいパスワードで解錠 → デモデータ管理・サンプルデータを投入・データをリセットが表示される
 *
 * 注意: 解錠パスワードの既定値は "opsadmin"（Admin.razor の既定値）。
 *       appsettings.json / 環境変数で OpsContext:AdminPassword を変更している場合は要調整。
 */

import { test, expect } from "@playwright/test";
import { login, USERS } from "../helpers/auth";

const ADMIN_PASSWORD = "opsadmin";

test.describe("TC-09: 管理・デバッグ", () => {

  test.beforeEach(async ({ page }) => {
    // /admin は認証さえ通ればロール不問でアクセスできる（Authorize 属性のみ）
    await login(page, USERS.admin);
  });

  test("TC-09-01 ページタイトル", async ({ page }) => {
    await page.goto("/admin");
    await expect(page).toHaveTitle("OpsContext - 管理・デバッグ");
  });

  test("TC-09-02 未解錠時にパスワードゲートが表示される", async ({ page }) => {
    await page.goto("/admin");
    await page.waitForLoadState("networkidle");
    // Admin.razor: _unlocked=false のとき「ロック解除」ボタンとパスワード入力を表示
    await expect(page.getByRole("button", { name: "ロック解除" })).toBeVisible();
    await expect(page.getByLabel("パスワード")).toBeVisible();
  });

  test("TC-09-03 誤パスワードで「パスワードが違います」エラー表示", async ({ page }) => {
    await page.goto("/admin");
    await page.waitForLoadState("networkidle");
    await page.getByLabel("パスワード").fill("wrongpassword");
    await page.getByRole("button", { name: "ロック解除" }).click();
    await expect(page.getByText("パスワードが違います")).toBeVisible();
  });

  test("TC-09-04 正しいパスワードで解錠 → デモデータ管理が表示される", async ({ page }) => {
    await page.goto("/admin");
    await page.waitForLoadState("networkidle");
    await page.getByLabel("パスワード").fill(ADMIN_PASSWORD);
    await page.getByRole("button", { name: "ロック解除" }).click();
    // Admin.razor: 解錠後に「デモデータ管理」「サンプルデータを投入」「データをリセット」を表示
    await expect(page.getByText("デモデータ管理")).toBeVisible({ timeout: 5000 });
    await expect(page.getByRole("button", { name: "サンプルデータを投入" })).toBeVisible();
    await expect(page.getByRole("button", { name: "データをリセット" })).toBeVisible();
    // 「動作モード」カードも表示
    await expect(page.getByText("動作モード")).toBeVisible();
  });

});
