/**
 * TC-02: ホームページ
 *
 * チェックリスト:
 * [ ] TC-02-01  ページタイトルが "OpsContext - ホーム" である
 * [ ] TC-02-02  3 つのクイックアクションカードが表示される
 * [ ] TC-02-03  「新規チャット」カードクリック → /chat へ遷移
 * [ ] TC-02-04  「業務データ」カードクリック → /data へ遷移
 * [ ] TC-02-05  「コンテキスト管理」カードクリック → /context へ遷移
 * [ ] TC-02-06  ナビバーに全メニュー項目が表示される
 * [ ] TC-02-07  ヘッダーの "OpsContext" ロゴクリックで / に留まる（リロード）
 */

import { test, expect } from "@playwright/test";
import { login, USERS } from "../helpers/auth";

test.describe("TC-02: ホームページ", () => {

  test.beforeEach(async ({ page }) => {
    await login(page, USERS.sales);
  });

  test("TC-02-01 ページタイトル", async ({ page }) => {
    await expect(page).toHaveTitle("OpsContext - ホーム");
  });

  test("TC-02-02 クイックアクション 3 枚表示", async ({ page }) => {
    await expect(page.getByText("新規チャット").first()).toBeVisible();
    await expect(page.getByText("業務データ").first()).toBeVisible();
    await expect(page.getByText("コンテキスト管理").first()).toBeVisible();
  });

  test("TC-02-03 新規チャットカード → /chat", async ({ page }) => {
    await page.getByText("AIエージェントに").click();
    await expect(page).toHaveURL(/\/chat/);
  });

  test("TC-02-04 業務データカード → /data", async ({ page }) => {
    await page.getByText("Grid でデータを管理し").click();
    await expect(page).toHaveURL(/\/data/);
  });

  test("TC-02-05 コンテキスト管理カード → /context", async ({ page }) => {
    await page.getByText("蓄積された決定・観察ログを").click();
    await expect(page).toHaveURL(/\/context/);
  });

  test("TC-02-06 ナビバーの全メニュー項目", async ({ page }) => {
    await expect(page.getByRole("link", { name: "新規チャット" })).toBeVisible();
    await expect(page.getByRole("link", { name: "業務データ" })).toBeVisible();
    await expect(page.getByRole("link", { name: "データセット" })).toBeVisible();
    await expect(page.getByRole("link", { name: "コンテキスト管理" })).toBeVisible();
    await expect(page.getByRole("link", { name: "アカウント" })).toBeVisible();
    await expect(page.getByRole("link", { name: "ロール設定" })).toBeVisible();
  });

  test("TC-02-07 非 Admin でログイン履歴メニュー非表示", async ({ page }) => {
    const historyLink = page.getByRole("link", { name: "ログイン履歴" });
    await expect(historyLink).not.toBeVisible();
  });

});
