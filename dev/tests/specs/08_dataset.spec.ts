/**
 * TC-08: 基幹データセットページ (/dataset)
 *
 * チェックリスト:
 * [ ] TC-08-01  ページタイトルが "OpsContext - 基幹データセット" である
 * [ ] TC-08-02  「基幹データセット」見出しと説明文が表示される
 * [ ] TC-08-03  データセットタブ（MudTabs）が表示され 1 つ以上のタブがある
 * [ ] TC-08-04  「データ更新（未実装）」ボタンが表示される
 * [ ] TC-08-05  「同期設定」カードと「最終同期」表示がある
 * [ ] TC-08-06  ナビバー「データセット」リンクから /dataset へ遷移できる
 */

import { test, expect } from "@playwright/test";
import { login, USERS } from "../helpers/auth";

test.describe("TC-08: 基幹データセット", () => {

  test.beforeEach(async ({ page }) => {
    await login(page, USERS.sales);
  });

  test("TC-08-01 ページタイトル", async ({ page }) => {
    await page.goto("/dataset");
    await expect(page).toHaveTitle("OpsContext - 基幹データセット");
  });

  test("TC-08-02 見出しと説明文表示", async ({ page }) => {
    await page.goto("/dataset");
    await page.waitForLoadState("networkidle");
    await expect(page.getByText("基幹データセット").first()).toBeVisible();
    // Dataset.razor: 「基幹システムから同期したマスタ・トランザクションデータ」キャプション
    await expect(page.getByText("基幹システムから同期した")).toBeVisible();
  });

  test("TC-08-03 データセットタブが 1 つ以上表示される", async ({ page }) => {
    await page.goto("/dataset");
    await page.waitForLoadState("networkidle");
    // Dataset.razor: MudTabs + MudTabPanel でデータセット種別を切り替え
    const tabs = page.locator('[role="tab"]');
    await expect(tabs.first()).toBeVisible({ timeout: 10000 });
    const count = await tabs.count();
    expect(count).toBeGreaterThanOrEqual(1);
  });

  test("TC-08-04 「データ更新（未実装）」ボタンが表示される", async ({ page }) => {
    await page.goto("/dataset");
    await page.waitForLoadState("networkidle");
    await expect(page.getByText("データ更新（未実装）")).toBeVisible();
  });

  test("TC-08-05 同期設定カードと最終同期が表示される", async ({ page }) => {
    await page.goto("/dataset");
    await page.waitForLoadState("networkidle");
    // Dataset.razor: 「同期設定」見出し
    await expect(page.getByText("同期設定")).toBeVisible();
    // 「最終同期」ラベル（最終同期日時を表示するカード）
    await expect(page.getByText("最終同期").first()).toBeVisible();
  });

  test("TC-08-06 ナビバー「データセット」リンクから /dataset に遷移", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("link", { name: "データセット" }).click();
    await expect(page).toHaveURL(/\/dataset/, { timeout: 10000 });
  });

});
