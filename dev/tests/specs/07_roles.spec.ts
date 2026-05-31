/**
 * TC-07: ロール設定ページ
 *
 * チェックリスト:
 * [ ] TC-07-01  /roles でロール設定ページが表示される
 * [ ] TC-07-02  ロール名（Sales / Purchasing / Production / Accounting）の一覧が表示される
 * [ ] TC-07-03  各ロールのロールプロンプト編集エリアが表示される（Admin のみ編集可）
 * [ ] TC-07-04  Admin で各ロールに「保存」ボタンが 4 件表示される
 *
 * 更新系:
 * [ ] TC-07-05  Admin で Sales のプロンプトを編集して「保存」→ Snackbar 確認・元値に復元
 * [ ] TC-07-06  非 Admin（Sales）でアクセスすると編集エリアが disabled、保存ボタンが非表示
 */

import { test, expect } from "@playwright/test";
import { login, USERS } from "../helpers/auth";

test.describe("TC-07: ロール設定", () => {

  test.beforeEach(async ({ page }) => {
    await login(page, USERS.admin);
  });

  test("TC-07-01 ロール設定ページ表示", async ({ page }) => {
    await page.goto("/roles");
    await expect(page.getByRole("heading", { name: "ロール設定" })).toBeVisible();
  });

  test("TC-07-02 ロール一覧表示", async ({ page }) => {
    await page.goto("/roles");
    await expect(page.getByRole("paragraph").filter({ hasText: /^Sales$/ })).toBeVisible();
    await expect(page.getByRole("paragraph").filter({ hasText: /^Purchasing$/ })).toBeVisible();
    await expect(page.getByRole("paragraph").filter({ hasText: /^Production$/ })).toBeVisible();
    await expect(page.getByRole("paragraph").filter({ hasText: /^Accounting$/ })).toBeVisible();
  });

  test("TC-07-03 各ロールのプロンプト編集エリアが表示される", async ({ page }) => {
    await page.goto("/roles");
    await page.waitForLoadState("networkidle");
    // Roles.razor: MudTextField Label="{Role} ロール共通の追加指示" で各ロールに表示
    for (const role of ["Sales", "Purchasing", "Production", "Accounting"]) {
      await expect(
        page.getByLabel(`${role} ロール共通の追加指示`)
      ).toBeVisible({ timeout: 10000 });
    }
  });

  test("TC-07-04 Admin で各ロールに保存ボタンが表示される", async ({ page }) => {
    await page.goto("/roles");
    await page.waitForLoadState("networkidle");
    // Roles.razor: Admin のみ「保存」ボタンを表示（_isAdmin チェック）
    const saveBtns = page.getByRole("button", { name: "保存" });
    await expect(saveBtns.first()).toBeVisible({ timeout: 10000 });
    const count = await saveBtns.count();
    // Sales / Purchasing / Production / Accounting の 4 ロール分あること
    expect(count).toBeGreaterThanOrEqual(4);
  });

});

// ─── TC-07 更新系 ───────────────────────────────────────────────────────────

test.describe("TC-07 更新系: ロールプロンプト保存", () => {

  test("TC-07-05 Admin で Sales のロールプロンプトを編集・保存できる（後始末で元値に戻す）", async ({ page }) => {
    await login(page, USERS.admin);
    await page.goto("/roles");
    await page.waitForLoadState("networkidle");

    // Sales のプロンプト入力欄を取得
    const salesField = page.getByLabel("Sales ロール共通の追加指示");
    await expect(salesField).toBeVisible({ timeout: 5000 });
    const originalValue = await salesField.inputValue();

    // 一意マーカーを追加して保存
    const marker = `__test_${Date.now()}`;
    await salesField.fill(originalValue + marker);

    // Sales の「保存」ボタン（Roles.razor: Sales が最初のロール、4 件中の 1 番目）
    const saveBtns = page.getByRole("button", { name: "保存" });
    await saveBtns.first().click();

    // Snackbar 確認
    await expect(page.getByText("Sales のロールプロンプトを保存しました。")).toBeVisible({ timeout: 5000 });

    // 後始末: 元の値に戻す
    await page.waitForTimeout(500); // ReloadAsync 完了を待つ
    await salesField.fill(originalValue);
    await saveBtns.first().click();
    await expect(page.getByText("Sales のロールプロンプトを保存しました。")).toBeVisible({ timeout: 5000 });
  });

  test("TC-07-06 非 Admin（Sales）で /roles にアクセスすると編集エリアが disabled・保存ボタンが非表示", async ({ page }) => {
    await login(page, USERS.sales);
    await page.goto("/roles");
    await page.waitForLoadState("networkidle");

    // Roles.razor: _isAdmin == false → MudTextField Disabled かつ 保存ボタン非表示
    const salesField = page.getByLabel("Sales ロール共通の追加指示");
    await expect(salesField).toBeVisible({ timeout: 5000 });
    await expect(salesField).toBeDisabled();

    // 保存ボタンが表示されない
    await expect(page.getByRole("button", { name: "保存" })).not.toBeVisible();
  });

});
