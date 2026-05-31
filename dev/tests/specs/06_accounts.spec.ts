/**
 * TC-06: アカウント管理ページ
 *
 * チェックリスト:
 * [ ] TC-06-01  Admin でログインすると /accounts でアカウント一覧が表示される
 * [ ] TC-06-02  5 件のデフォルトユーザーが表示される（system / sales / purchasing / production / accounting）
 * [ ] TC-06-03  「新規ユーザー作成」ボタンが表示される（Admin のみ）
 * [ ] TC-06-04  非 Admin (Sales) で /accounts にアクセスすると「新規作成」ボタンが非表示
 * [ ] TC-06-05  Admin でログイン履歴リンクが表示される
 * [ ] TC-06-06  Admin で /login-history が表示される
 *
 * 更新系:
 * [ ] TC-06-07  Admin で新規アカウントを作成 → Snackbar「作成しました」・一覧に表示
 * [ ] TC-06-08  作成したアカウントのプロフィール編集（表示名変更）→ Snackbar「更新しました」・反映
 * [ ] TC-06-09  作成したアカウントを削除 → Snackbar「削除しました」・一覧から消える
 * [ ] TC-06-10  Admin 自身（user_system）の行に削除ボタンがない（自己削除不可）
 */

import { test, expect } from "@playwright/test";
import { login, USERS } from "../helpers/auth";

test.describe("TC-06: アカウント管理 (Admin)", () => {

  test("TC-06-01 Admin でアカウント一覧表示", async ({ page }) => {
    await login(page, USERS.admin);
    await page.goto("/accounts");
    await expect(page.getByRole("heading", { name: "アカウント管理" })).toBeVisible();
  });

  test("TC-06-02 デフォルトユーザー表示", async ({ page }) => {
    await login(page, USERS.admin);
    await page.goto("/accounts");
    await expect(page.getByText("user_system")).toBeVisible();
    await expect(page.getByText("user_sales")).toBeVisible();
    await expect(page.getByText("user_purchasing")).toBeVisible();
  });

  test("TC-06-04 非 Admin で /accounts にアクセスすると「新規作成」ボタンが表示されない", async ({ page }) => {
    // Accounts.razor: [Authorize] のみ（ロール制限なし）なので非Admin もページ自体にはアクセスできる。
    // ただし _isAdmin == false のため「新規作成」ボタンは表示されない。
    await login(page, USERS.sales);
    await page.goto("/accounts");
    await page.waitForLoadState("networkidle");
    await expect(page.getByRole("button", { name: "新規作成" })).not.toBeVisible();
  });

  test("TC-06-03 Admin で新規作成ボタンが表示される", async ({ page }) => {
    await login(page, USERS.admin);
    await page.goto("/accounts");
    await page.waitForLoadState("networkidle");
    // Accounts.razor: Admin のみ「新規作成」ボタンを表示（StartIcon=PersonAdd）
    await expect(page.getByRole("button", { name: "新規作成" })).toBeVisible();
  });

  test("TC-06-05 Admin でログイン履歴メニュー表示", async ({ page }) => {
    await login(page, USERS.admin);
    await expect(page.getByRole("link", { name: "ログイン履歴" })).toBeVisible();
  });

  test("TC-06-06 Admin で /login-history 表示", async ({ page }) => {
    await login(page, USERS.admin);
    await page.goto("/login-history");
    await expect(page.getByRole("heading", { name: "ログイン履歴" })).toBeVisible();
  });

});

// ─── TC-06 更新系 ───────────────────────────────────────────────────────────
// 作成・編集・削除は一意ユーザー名を使い、シードユーザーには触れない。

test.describe("TC-06 更新系: アカウントの作成・編集・削除", () => {

  test("TC-06-07 Admin で新規アカウントを作成すると一覧に表示される", async ({ page }) => {
    await login(page, USERS.admin);
    await page.goto("/accounts");
    await page.waitForLoadState("networkidle");

    const testUserName = `u_test_${Date.now()}`;

    // 新規作成ダイアログを開く
    await page.getByRole("button", { name: "新規作成" }).click();
    await expect(page.getByText("新規アカウント作成")).toBeVisible({ timeout: 3000 });

    // ダイアログ内のフォームを入力
    // Accounts.razor MudDialog: MudTextField Label="ユーザー名（英数字・記号）"
    const dialog = page.locator(".mud-dialog");
    await dialog.getByLabel("ユーザー名（英数字・記号）").fill(testUserName);
    await dialog.getByLabel("表示名").fill("テストユーザー");
    await dialog.getByLabel("パスワード").fill("Test@2026!");

    // 作成ボタン
    await dialog.getByRole("button", { name: "作成" }).click();

    // Snackbar 確認
    await expect(page.getByText(`アカウント '${testUserName}' を作成しました。`)).toBeVisible({ timeout: 5000 });
    // 一覧に表示される
    await expect(page.getByText(testUserName)).toBeVisible({ timeout: 5000 });

    // 後始末: 作成ユーザーを削除
    const userRow = page.locator("tr").filter({ hasText: testUserName });
    const deleteBtn = userRow.locator("button[class*='mud-icon-button-color-error']").first();
    if (await deleteBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await deleteBtn.click();
    }
  });

  test("TC-06-08 作成したアカウントのプロフィール編集（表示名変更）が反映される", async ({ page }) => {
    await login(page, USERS.admin);
    await page.goto("/accounts");
    await page.waitForLoadState("networkidle");

    const testUserName = `u_edit_${Date.now()}`;

    // アカウント作成
    await page.getByRole("button", { name: "新規作成" }).click();
    const createDialog = page.locator(".mud-dialog");
    await createDialog.getByLabel("ユーザー名（英数字・記号）").fill(testUserName);
    await createDialog.getByLabel("表示名").fill("変更前の名前");
    await createDialog.getByLabel("パスワード").fill("Test@2026!");
    await createDialog.getByRole("button", { name: "作成" }).click();
    await expect(page.getByText(`アカウント '${testUserName}' を作成しました。`)).toBeVisible({ timeout: 5000 });

    // プロフィール編集ダイアログを開く（Admin のみ表示の「プロフィール編集」アイコン）
    // Accounts.razor: MudTooltip Text="プロフィール編集" > MudIconButton (Edit アイコン)
    const userRow = page.locator("tr").filter({ hasText: testUserName });
    // Color.Default の MudIconButton (Edit アイコン) - Error 色以外の最初のボタン
    const editBtns = userRow.locator("button[class*='mud-icon-button']").filter({ hasNot: page.locator("[class*='mud-icon-button-color-error']") });
    await editBtns.first().click();

    const editDialog = page.locator(".mud-dialog");
    await expect(editDialog.getByText("プロフィール編集")).toBeVisible({ timeout: 3000 });
    await editDialog.getByLabel("表示名").fill("変更後の名前");
    await editDialog.getByRole("button", { name: "保存" }).click();

    // Snackbar 確認
    await expect(page.getByText(`アカウント '${testUserName}' を更新しました。`)).toBeVisible({ timeout: 5000 });
    // 一覧に新しい表示名が表示される
    await expect(page.getByText("変更後の名前")).toBeVisible({ timeout: 5000 });

    // 後始末
    const updatedRow = page.locator("tr").filter({ hasText: testUserName });
    const deleteBtn = updatedRow.locator("button[class*='mud-icon-button-color-error']").first();
    if (await deleteBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await deleteBtn.click();
    }
  });

  test("TC-06-09 作成したアカウントを削除するとSnackbarが出て一覧から消える", async ({ page }) => {
    await login(page, USERS.admin);
    await page.goto("/accounts");
    await page.waitForLoadState("networkidle");

    const testUserName = `u_del_${Date.now()}`;

    // アカウント作成
    await page.getByRole("button", { name: "新規作成" }).click();
    const createDialog = page.locator(".mud-dialog");
    await createDialog.getByLabel("ユーザー名（英数字・記号）").fill(testUserName);
    await createDialog.getByLabel("表示名").fill("削除テストユーザー");
    await createDialog.getByLabel("パスワード").fill("Test@2026!");
    await createDialog.getByRole("button", { name: "作成" }).click();
    await expect(page.getByText(`アカウント '${testUserName}' を作成しました。`)).toBeVisible({ timeout: 5000 });
    await expect(page.getByText(testUserName)).toBeVisible({ timeout: 5000 });

    // 削除ボタンクリック（Color.Error の MudIconButton）
    const userRow = page.locator("tr").filter({ hasText: testUserName });
    const deleteBtn = userRow.locator("button[class*='mud-icon-button-color-error']").first();
    await expect(deleteBtn).toBeVisible({ timeout: 5000 });
    await deleteBtn.click();

    // Snackbar 確認
    await expect(page.getByText(`アカウント '${testUserName}' を削除しました。`)).toBeVisible({ timeout: 5000 });
    // 一覧から消えたことを確認
    await expect(page.getByText(testUserName)).not.toBeVisible({ timeout: 5000 });
  });

  test("TC-06-10 Admin 自身（user_system）の行に削除ボタンがない（自己削除不可）", async ({ page }) => {
    await login(page, USERS.admin);
    await page.goto("/accounts");
    await page.waitForLoadState("networkidle");

    // user_system 行（「あなた」チップが付く）
    // Accounts.razor: context.UserName == _currentUserName → 削除ボタンを非表示
    const myRow = page.locator("tr").filter({ hasText: "user_system" });
    await expect(myRow).toBeVisible({ timeout: 5000 });

    // Color.Error の削除ボタンが存在しないこと
    await expect(myRow.locator("button[class*='mud-icon-button-color-error']")).not.toBeVisible();
  });

});
