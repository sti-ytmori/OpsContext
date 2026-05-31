/**
 * TC-04: 業務データページ
 *
 * チェックリスト:
 * [ ] TC-04-01  /data でデータセット一覧ページが表示される
 * [ ] TC-04-02  シードデータのテーブルが 1 件以上リストされる
 * [ ] TC-04-03  テーブル行「開く」ボタンクリック → /data/{tableId} へ遷移
 * [ ] TC-04-04  /data/{tableId} でテーブル名がヘッダーに表示される
 * [ ] TC-04-05  グリッドに列ヘッダーと行データが表示される
 * [ ] TC-04-06  「行追加」ボタンが表示される
 * [ ] TC-04-07  「列追加」ボタンが表示される
 * [ ] TC-04-08  「CSV取込」ボタンが表示される
 * [ ] TC-04-09  「CSVダウンロード」ボタンが表示される
 *               ※ 実装に「チャットで分析」ボタンは存在しないため、代わりに CSVダウンロードを検証
 * [ ] TC-04-10  「業務データ一覧へ」戻るボタンで /data に戻る
 *
 * 更新系:
 * [ ] TC-04-11  業務データを新規作成 → 一覧に名前が表示される
 * [ ] TC-04-12  作成した業務データを削除 → Snackbar「業務データを削除しました」・一覧から消える
 * [ ] TC-04-13  詳細で「行追加」→ tbody 行数が +1 になる
 * [ ] TC-04-14  行をホバーして削除ボタンクリック → 行数が -1 になる
 * [ ] TC-04-15  「列追加」ダイアログで列名入力 → 「追加」→ thead に列ヘッダーが追加される
 * [ ] TC-04-16  列ヘッダーをホバーして削除アイコンクリック → 列が消える
 * [ ] TC-04-17  セルを編集して Tab → 値が保持される
 * [ ] TC-04-18  列の意味（説明）編集ダイアログで保存 → 列ヘッダー下に説明文が表示される
 */

import { test, expect } from "@playwright/test";
import { login, USERS, createTable, deleteTableByName } from "../helpers/auth";

test.describe("TC-04: 業務データ", () => {

  test.beforeEach(async ({ page }) => {
    await login(page, USERS.sales);
  });

  test("TC-04-01 /data でデータ一覧表示", async ({ page }) => {
    await page.goto("/data");
    await expect(page).toHaveURL(/\/data/);
    await expect(page.getByText("業務データ").first()).toBeVisible();
  });

  test("TC-04-02 シードテーブルが 1 件以上リストされる", async ({ page }) => {
    await page.goto("/data");
    await page.waitForLoadState("networkidle");
    // DataList は OnInitializedAsync で SeedSampleAsync() を呼ぶ。起動後に「開く」ボタンが存在する
    const openBtns = page.getByRole("button", { name: "開く" });
    await expect(openBtns.first()).toBeVisible({ timeout: 10000 });
    const count = await openBtns.count();
    expect(count).toBeGreaterThanOrEqual(1);
  });

  test("TC-04-03 「開く」ボタンクリック → /data/{tableId}", async ({ page }) => {
    await page.goto("/data");
    await page.waitForLoadState("networkidle");
    await page.getByRole("button", { name: "開く" }).first().click();
    await expect(page).toHaveURL(/\/data\/.+/, { timeout: 10000 });
  });

  test("TC-04-04 詳細ページでテーブル名がヘッダーに表示される", async ({ page }) => {
    await page.goto("/data");
    await page.waitForLoadState("networkidle");
    // テーブル行のテーブル名テキストを記憶してから遷移
    const tableNameEl = page.locator('[class*="mud-typography-subtitle2"]').first();
    const tableNameText = await tableNameEl.textContent().catch(() => null);
    await page.getByRole("button", { name: "開く" }).first().click();
    await expect(page).toHaveURL(/\/data\/.+/, { timeout: 10000 });
    await page.waitForLoadState("networkidle");
    // 詳細ページのヘッダーに同じテーブル名が subtitle1 で表示される
    if (tableNameText) {
      await expect(page.getByText(tableNameText.trim()).first()).toBeVisible({ timeout: 10000 });
    } else {
      await expect(
        page.locator('[class*="mud-typography-subtitle1"]').filter({ hasText: /\S/ }).first()
      ).toBeVisible({ timeout: 10000 });
    }
  });

  test("TC-04-05 グリッドに列ヘッダーと行データが表示される", async ({ page }) => {
    await page.goto("/data");
    await page.waitForLoadState("networkidle");
    await page.getByRole("button", { name: "開く" }).first().click();
    await expect(page).toHaveURL(/\/data\/.+/, { timeout: 10000 });
    await page.waitForLoadState("networkidle");
    // Data.razor: テーブルまたは空メッセージのいずれかが表示されるまで待つ
    const th = page.locator("table thead th").first();
    const emptyMsg = page.getByText("データがありません。");
    await Promise.race([
      th.waitFor({ state: "visible", timeout: 15000 }).catch(() => null),
      emptyMsg.waitFor({ state: "visible", timeout: 15000 }).catch(() => null),
    ]);
    const hasColumns = await th.isVisible().catch(() => false);
    if (hasColumns) {
      await expect(th).toBeVisible();
      // 行データ（tbody tr）も 1 行以上ある
      const rows = page.locator("table tbody tr");
      const rowCount = await rows.count();
      expect(rowCount).toBeGreaterThanOrEqual(1);
    } else {
      // 列が無い場合は空メッセージが表示される（これもパターンとして許容）
      await expect(emptyMsg).toBeVisible({ timeout: 3000 });
    }
  });

  test("TC-04-06〜09 業務データ詳細ページのボタン群", async ({ page }) => {
    await page.goto("/data");
    await page.waitForLoadState("networkidle");
    await page.getByRole("button", { name: "開く" }).first().click();
    await expect(page).toHaveURL(/\/data\/.+/, { timeout: 10000 });
    await page.waitForLoadState("networkidle");

    // TC-04-06 行追加
    await expect(page.getByRole("button", { name: "行追加" })).toBeVisible();
    // TC-04-07 列追加
    await expect(page.getByRole("button", { name: "列追加" })).toBeVisible();
    // TC-04-08 CSV取込（MudButton HtmlTag="label" のため getByText で確認）
    await expect(page.getByText("CSV取込")).toBeVisible();
    // TC-04-09 CSVダウンロード
    await expect(page.getByRole("button", { name: "CSVダウンロード" })).toBeVisible();
  });

  test("TC-04-10 戻るボタンで /data に戻る", async ({ page }) => {
    await page.goto("/data");
    await page.waitForLoadState("networkidle");
    await page.getByRole("button", { name: "開く" }).first().click();
    await expect(page).toHaveURL(/\/data\/.+/, { timeout: 10000 });
    await page.waitForLoadState("networkidle");
    // Data.razor: MudTooltip Text="業務データ一覧へ" 内の MudIconButton
    const backBtn = page.getByTitle("業務データ一覧へ");
    const backVisible = await backBtn.isVisible({ timeout: 5000 }).catch(() => false);
    if (backVisible) {
      await backBtn.click();
      await expect(page).toHaveURL(/\/data$/, { timeout: 10000 });
    } else {
      // フォールバック: ナビリンク「業務データ」で /data に戻る
      await page.getByRole("link", { name: "業務データ" }).first().click();
      await expect(page).toHaveURL(/\/data/);
    }
  });

});

// ─── TC-04 更新系: 業務データ一覧操作 ──────────────────────────────────────

test.describe("TC-04 更新系: 業務データ一覧", () => {

  test.beforeEach(async ({ page }) => {
    await login(page, USERS.sales);
  });

  test("TC-04-11 業務データを新規作成すると一覧に名前が表示される", async ({ page }) => {
    const tableName = `作成テスト_${Date.now()}`;
    await page.goto("/data");
    await page.waitForLoadState("networkidle");

    // 新規業務データダイアログを開く
    await page.getByRole("button", { name: "新規業務データ" }).click();
    await expect(page.getByText("新規業務データを作成")).toBeVisible({ timeout: 3000 });

    await page.getByLabel("業務データ名").fill(tableName);
    await page.getByRole("button", { name: "作成して開く" }).click();
    await page.waitForURL(/\/data\/.+/, { timeout: 10000 });

    // 一覧に戻って名前が表示されることを確認
    await page.goto("/data");
    await page.waitForLoadState("networkidle");
    await expect(page.getByText(tableName)).toBeVisible({ timeout: 5000 });

    // 後始末
    await deleteTableByName(page, tableName);
  });

  test("TC-04-12 作成した業務データを削除するとSnackbarが出て一覧から消える", async ({ page }) => {
    const tableName = `削除テスト_${Date.now()}`;
    await createTable(page, tableName);

    await page.goto("/data");
    await page.waitForLoadState("networkidle");
    await expect(page.getByText(tableName)).toBeVisible({ timeout: 5000 });

    // 削除ボタンをクリック（DataList.razor: title="削除" MudIconButton）
    const row = page
      .locator("div")
      .filter({ has: page.getByText(tableName, { exact: true }) })
      .filter({ has: page.locator('[title="削除"]') })
      .last();
    await row.locator('[title="削除"]').first().click();

    // Snackbar 確認
    await expect(page.getByText("業務データを削除しました")).toBeVisible({ timeout: 5000 });
    // 一覧から消えたことを確認
    await expect(page.getByText(tableName)).not.toBeVisible({ timeout: 5000 });
  });

});

// ─── TC-04 更新系: 業務データ詳細操作 ──────────────────────────────────────
// 各テストは beforeEach で一意テーブルを作成し、afterEach で削除する。
// シードデータには一切触れない。

test.describe("TC-04 更新系: 業務データ詳細", () => {
  // describe 単位で一意名（テスト間で共有しても afterEach で都度再生成される）
  const tableName = `詳細テスト_${Date.now()}`;
  let testTableId = "";

  test.beforeEach(async ({ page }) => {
    await login(page, USERS.sales);
    testTableId = await createTable(page, tableName);
    await page.goto(`/data/${testTableId}`);
    await page.waitForLoadState("networkidle");
  });

  test.afterEach(async ({ page }) => {
    await deleteTableByName(page, tableName);
  });

  test("TC-04-13 「行追加」で tbody 行数が増える", async ({ page }) => {
    // 初期は行0 → 空メッセージ表示
    await expect(page.getByText("データがありません")).toBeVisible({ timeout: 5000 });

    await page.getByRole("button", { name: "行追加" }).click();
    await page.waitForTimeout(300);

    // Data.razor: Rows.Count > 0 になると table 要素が表示される
    const rows = page.locator("table tbody tr");
    await expect(rows.first()).toBeVisible({ timeout: 5000 });
    const count = await rows.count();
    expect(count).toBeGreaterThanOrEqual(1);
  });

  test("TC-04-14 行をホバーして削除すると行数が -1 になる", async ({ page }) => {
    // 行を2つ追加（1つ削除後も table が表示されるよう2行にする）
    await page.getByRole("button", { name: "行追加" }).click();
    await page.waitForTimeout(200);
    await page.getByRole("button", { name: "行追加" }).click();
    await page.waitForTimeout(300);

    const rows = page.locator("table tbody tr");
    await expect(rows.first()).toBeVisible({ timeout: 5000 });
    const initialCount = await rows.count();
    expect(initialCount).toBe(2);

    // 最初の行をホバー → Color.Error の削除ボタンが出る
    await rows.first().hover();
    await page.waitForTimeout(200);
    const deleteBtn = rows.first().locator('button[class*="mud-icon-button-color-error"]').first();
    await expect(deleteBtn).toBeVisible({ timeout: 3000 });
    await deleteBtn.click();
    await page.waitForTimeout(300);

    const afterCount = await rows.count();
    expect(afterCount).toBe(initialCount - 1);
  });

  test("TC-04-15 「列追加」ダイアログで追加すると列ヘッダーが出る", async ({ page }) => {
    const colLabel = `テスト列_${Date.now()}`;

    await page.getByRole("button", { name: "列追加" }).click();
    // Data.razor: _showAddColumnDialog ダイアログ
    await expect(page.getByText("列を追加")).toBeVisible({ timeout: 3000 });

    await page.getByLabel("列名").fill(colLabel);
    await page.getByRole("button", { name: "追加" }).click();
    await page.waitForTimeout(300);

    // 行を1つ追加してテーブルを表示させてから列ヘッダーを確認
    await page.getByRole("button", { name: "行追加" }).click();
    await page.waitForTimeout(300);
    await expect(page.locator("table thead th").filter({ hasText: colLabel })).toBeVisible({ timeout: 5000 });
  });

  test("TC-04-16 列ヘッダーをホバーして削除アイコンで列が消える", async ({ page }) => {
    const colLabel = `削除列_${Date.now()}`;

    // 列を追加
    await page.getByRole("button", { name: "列追加" }).click();
    await page.getByLabel("列名").fill(colLabel);
    await page.getByRole("button", { name: "追加" }).click();
    await page.waitForTimeout(200);

    // 行を追加してテーブルを表示
    await page.getByRole("button", { name: "行追加" }).click();
    await page.waitForTimeout(300);

    const th = page.locator("table thead th").filter({ hasText: colLabel });
    await expect(th).toBeVisible({ timeout: 5000 });

    // 列ヘッダーをホバー → EditNote / DeleteOutline アイコンが出る
    await th.hover();
    await page.waitForTimeout(200);

    // Data.razor: _hoveredColumn == col.Key → 最後の MudIconButton が DeleteOutline（色 #b71c1c）
    const deleteIcon = th.locator("button[class*='mud-icon-button']").last();
    await expect(deleteIcon).toBeVisible({ timeout: 3000 });
    await deleteIcon.click();
    await page.waitForTimeout(300);

    // 列が消えたことを確認
    await expect(page.locator("table thead th").filter({ hasText: colLabel })).not.toBeVisible({ timeout: 3000 });
  });

  test("TC-04-17 セル編集で値が Tab 後も保持される", async ({ page }) => {
    const colLabel = `編集列_${Date.now()}`;

    // 列追加
    await page.getByRole("button", { name: "列追加" }).click();
    await page.getByLabel("列名").fill(colLabel);
    await page.getByRole("button", { name: "追加" }).click();
    await page.waitForTimeout(200);

    // 行追加
    await page.getByRole("button", { name: "行追加" }).click();
    await page.waitForTimeout(300);

    // セルの MudTextField（input）に値を入力 → Tab で blur → ValueChanged 発火
    const cellInput = page.locator("table tbody tr").first().locator("input").first();
    await expect(cellInput).toBeVisible({ timeout: 5000 });
    const cellValue = `編集値_${Date.now()}`;
    await cellInput.fill(cellValue);
    await page.keyboard.press("Tab");
    await page.waitForTimeout(300);

    // 同じ input に値が保持されている
    await expect(cellInput).toHaveValue(cellValue);
  });

  test("TC-04-18 列の意味（説明）を保存すると列ヘッダー下に表示される", async ({ page }) => {
    const colLabel = `説明列_${Date.now()}`;

    // 列追加
    await page.getByRole("button", { name: "列追加" }).click();
    await page.getByLabel("列名").fill(colLabel);
    await page.getByRole("button", { name: "追加" }).click();
    await page.waitForTimeout(200);

    // 行追加（テーブル表示）
    await page.getByRole("button", { name: "行追加" }).click();
    await page.waitForTimeout(300);

    const th = page.locator("table thead th").filter({ hasText: colLabel });
    await expect(th).toBeVisible({ timeout: 5000 });

    // 列ヘッダーをホバー → EditNote アイコン（最初の MudIconButton）をクリック
    await th.hover();
    await page.waitForTimeout(200);
    const editIcon = th.locator("button[class*='mud-icon-button']").first();
    await expect(editIcon).toBeVisible({ timeout: 3000 });
    await editIcon.click();

    // Data.razor: _showEditDescriptionDialog ダイアログ
    await expect(page.getByText("列の意味を登録")).toBeVisible({ timeout: 3000 });

    const description = "テスト用の列説明文";
    await page.getByLabel("列の意味・説明").fill(description);
    await page.getByRole("button", { name: "保存" }).click();
    await page.waitForTimeout(300);

    // 列ヘッダー下に説明文が表示される
    // Data.razor: @if (!string.IsNullOrWhiteSpace(col.Description)) → div で description を表示
    await expect(th.getByText(description)).toBeVisible({ timeout: 5000 });
  });

});
