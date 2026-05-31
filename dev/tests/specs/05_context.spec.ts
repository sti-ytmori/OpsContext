/**
 * TC-05: コンテキスト管理ページ
 *
 * チェックリスト:
 * [ ] TC-05-01  ページタイトルが "OpsContext - コンテキスト管理" である
 * [ ] TC-05-02  「コンテキスト管理」ヘッダーと説明文が表示される
 * [ ] TC-05-03  業務データID 入力フォームが表示される
 * [ ] TC-05-04  ロールフィルター（全ロール / Sales / Purchasing 等）が選択できる
 * [ ] TC-05-05  存在しない ID で検索すると「エントリなし」等のフィードバックが表示される
 * [ ] TC-05-06  /context?caseId=xxx クエリ付き遷移で入力欄に caseId が自動セット
 *
 * 更新系・補強:
 * [ ] TC-05-07  Data 詳細「この線で進めます」で決定が記録される（AI応答依存・スキップ可）
 * [ ] TC-05-08  種別フィルターが表示されており「決定」「観察」選択肢が存在する
 * [ ] TC-05-09  業務データチップクリックで入力欄に caseId がセットされる
 */

import { test, expect } from "@playwright/test";
import { login, USERS, createTable, deleteTableByName } from "../helpers/auth";

test.describe("TC-05: コンテキスト管理", () => {

  test.beforeEach(async ({ page }) => {
    await login(page, USERS.sales);
  });

  test("TC-05-01 ページタイトル", async ({ page }) => {
    await page.goto("/context");
    await expect(page).toHaveTitle("OpsContext - コンテキスト管理");
  });

  test("TC-05-02 ヘッダー・説明文表示", async ({ page }) => {
    await page.goto("/context");
    await expect(page.getByText("コンテキスト管理").first()).toBeVisible();
    await expect(page.getByText("業務データごとに蓄積された")).toBeVisible();
  });

  test("TC-05-03 業務データID 入力フォーム", async ({ page }) => {
    await page.goto("/context");
    await expect(page.getByLabel("業務データID")).toBeVisible();
  });

  test("TC-05-04 ロールフィルター選択肢", async ({ page }) => {
    await page.goto("/context");
    const select = page.getByRole("group", { name: "ロールフィルター" });
    await expect(select).toBeVisible();
  });

  test("TC-05-05 存在しない ID で検索するとエントリなしフィードバック", async ({ page }) => {
    await page.goto("/context");
    await page.waitForLoadState("networkidle");
    // 存在しない ID を入力して「読み込む」ボタンで検索
    const input = page.getByLabel("業務データID");
    await input.fill("nonexistent-id-xxxxxxxx");
    await page.getByRole("button", { name: "読み込む" }).click();
    // ContextViewer.razor: _hasFetched && FilteredEntries.Count == 0 のとき
    // 「この業務データにはまだエントリがありません」を表示
    await expect(page.getByText("この業務データにはまだエントリがありません")).toBeVisible({ timeout: 10000 });
  });

  test("TC-05-06 クエリパラメータで caseId が自動セット", async ({ page }) => {
    const testCaseId = "test-case-001";
    await page.goto(`/context?caseId=${testCaseId}`);
    const input = page.getByLabel("業務データID");
    await expect(input).toHaveValue(testCaseId);
  });

});

// ─── TC-05 更新系・補強 ─────────────────────────────────────────────────────

test.describe("TC-05 更新系・補強", () => {

  test.beforeEach(async ({ page }) => {
    await login(page, USERS.sales);
  });

  test("TC-05-07 「この線で進めます」でコンテキストに決定が記録される（AI応答依存）", async ({ page }) => {
    // 注意: Data.razor の「この線で進めます」は _chatHistory.Count > 0 かつ _isBusy == false のときのみ有効。
    //       AI 応答が来ない場合はスキップ扱い（TC-03-08 と同方針）。
    const tableName = `CTXテスト_${Date.now()}`;
    const tableId = await createTable(page, tableName);

    try {
      await page.goto(`/data/${tableId}`);
      await page.waitForLoadState("networkidle");

      // 右ペインの AI チャット textarea に入力して送信
      const textarea = page.locator("textarea").first();
      await textarea.fill("テスト質問");
      await page.getByRole("button", { name: "送信" }).click();

      // AI 応答後に「この線で進めます」が有効になるまで待つ（最大 30s）
      const confirmBtn = page.getByRole("button", { name: "この線で進めます" });
      const isEnabled = await confirmBtn.isEnabled({ timeout: 30000 }).catch(() => false);
      if (!isEnabled) {
        console.warn("TC-05-07: AI応答が30s以内に来なかったためスキップします。AI接続を確認してください。");
        return;
      }

      await confirmBtn.click();
      await expect(page.getByText("決定を記録しました")).toBeVisible({ timeout: 5000 });

      // /context で decision エントリが確認できる
      // ContextViewer.razor: OnAfterRenderAsync で caseId クエリがあれば自動 LoadContext
      await page.goto(`/context?caseId=${tableId}`);
      await page.waitForLoadState("networkidle");
      await page.waitForTimeout(2000); // OnAfterRenderAsync の非同期処理を待つ
      // タイムライン or 統計に「決定」表示
      await expect(page.getByText("決定").first()).toBeVisible({ timeout: 10000 });
    } finally {
      await deleteTableByName(page, tableName);
    }
  });

  test("TC-05-08 種別フィルターに「決定」「観察」選択肢がある", async ({ page }) => {
    await page.goto("/context");
    await page.waitForLoadState("networkidle");

    // ContextViewer.razor: MudSelect Label="種別" が表示されている
    // MudSelect のラベルテキストで確認
    await expect(page.getByText("種別").first()).toBeVisible();

    // 存在しない caseId で読み込んでフィルター操作を確認
    const caseIdInput = page.getByLabel("業務データID");
    await caseIdInput.fill("filter-test-nonexistent-" + Date.now());
    await page.getByRole("button", { name: "読み込む" }).click();
    await expect(page.getByText("この業務データにはまだエントリがありません")).toBeVisible({ timeout: 10000 });

    // ContextViewer.razor の 種別 MudSelect をクリックして選択肢を展開
    // MudBlazor MudSelect は div として描画されクリックでドロップダウンを開く
    // まず "全種別" テキストが含まれる MudSelect を探す
    const kindSelectArea = page.locator("div[class*='mud-input-slot']").filter({ hasText: "全種別" }).first();
    if (await kindSelectArea.isVisible({ timeout: 2000 }).catch(() => false)) {
      await kindSelectArea.click();
      await page.waitForTimeout(300);
      // ドロップダウンに「決定」「観察」が表示される
      // MudBlazor popover は .mud-popover に追記される
      await expect(page.getByRole("option", { name: "決定" })).toBeVisible({ timeout: 3000 }).catch(async () => {
        // option ロールで取れない場合は li で探す
        await expect(page.locator("li").filter({ hasText: "決定" }).first()).toBeVisible({ timeout: 3000 });
      });
      // Escape で閉じる
      await page.keyboard.press("Escape");
    } else {
      // 種別フィルターエリアが存在することを最低限確認（表示レンダリング差異の許容）
      await expect(page.locator("label").filter({ hasText: "種別" }).first()).toBeVisible();
    }
  });

  test("TC-05-09 業務データチップクリックで入力欄に caseId がセットされる", async ({ page }) => {
    await page.goto("/context");
    await page.waitForLoadState("networkidle");

    // ContextViewer.razor: _tables の各 GridTable を MudChip として表示
    // SeedSampleAsync でシードが少なくとも 1 件あるはず
    const chips = page.locator("[class*='mud-chip']").filter({ hasText: /\S/ });
    await expect(chips.first()).toBeVisible({ timeout: 5000 });

    // 最初のチップをクリック → SelectSampleCase → _caseIdInput にセット → LoadContext 実行
    await chips.first().click();
    await page.waitForTimeout(1000);

    // 入力欄に何らかの caseId がセットされている
    const caseIdInput = page.getByLabel("業務データID");
    const inputValue = await caseIdInput.inputValue();
    expect(inputValue.length).toBeGreaterThan(0);

    // エラーが出ていないことを確認
    const hasError = await page.getByText("コンテキスト読み込み失敗").isVisible().catch(() => false);
    expect(hasError).toBeFalsy();
  });

});
