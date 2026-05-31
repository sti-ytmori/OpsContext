import { Page } from "@playwright/test";

export const BASE_URL = "http://localhost:5179";

// ─── 業務データ操作ヘルパー ─────────────────────────────────────────────────

/**
 * /data で業務データを新規作成し tableId を返す。
 * 呼び出し前にログイン済みであること。
 */
export async function createTable(page: Page, name: string): Promise<string> {
  await page.goto("/data");
  await page.waitForLoadState("networkidle");
  await page.getByRole("button", { name: "新規業務データ" }).click();
  await page.getByLabel("業務データ名").fill(name);
  await page.getByRole("button", { name: "作成して開く" }).click();
  await page.waitForURL(/\/data\/.+/, { timeout: 10000 });
  return page.url().split("/data/")[1];
}

/**
 * /data の一覧から指定名の業務データを削除する（後始末用）。
 * 削除ボタンが見つからない場合はスキップ（テスト失敗時の後始末を考慮）。
 */
export async function deleteTableByName(page: Page, name: string): Promise<void> {
  try {
    await page.goto("/data");
    await page.waitForLoadState("networkidle");
    // テーブル名テキストと削除ボタン (title="削除") を両方含む div コンテナを特定
    const row = page
      .locator("div")
      .filter({ has: page.getByText(name, { exact: true }) })
      .filter({ has: page.locator('[title="削除"]') })
      .last();
    const deleteBtn = row.locator('[title="削除"]').first();
    await deleteBtn.waitFor({ state: "visible", timeout: 3000 });
    await deleteBtn.click();
    await page.waitForTimeout(300);
  } catch {
    // 削除ボタンが見つからない場合はスキップ（後始末なので失敗を許容）
  }
}

export const USERS = {
  admin:      { userName: "user_system",      password: "Demo@2026", displayName: "システム管理者", role: "Admin" },
  sales:      { userName: "user_sales",       password: "Demo@2026", displayName: "田中 一郎",     role: "Sales" },
  purchasing: { userName: "user_purchasing",  password: "Demo@2026", displayName: "鈴木 花子",     role: "Purchasing" },
  production: { userName: "user_production",  password: "Demo@2026", displayName: "佐藤 次郎",     role: "Production" },
  accounting: { userName: "user_accounting",  password: "Demo@2026", displayName: "山田 三郎",     role: "Accounting" },
} as const;

export async function login(page: Page, user: (typeof USERS)[keyof typeof USERS]) {
  for (let attempt = 0; attempt < 3; attempt++) {
    await page.goto("/login");
    await page.waitForSelector('input[name="userName"]');
    await page.fill('input[name="userName"]', "");
    await page.fill('input[name="password"]', "");
    await page.fill('input[name="userName"]', user.userName);
    await page.fill('input[name="password"]', user.password);
    try {
      await Promise.all([
        page.waitForURL("**/", { timeout: 10000 }),
        page.click('button[type="submit"]'),
      ]);
      return;
    } catch {
      // retry
    }
  }
  throw new Error(`Login failed for ${user.userName} after 3 attempts`);
}

export async function logout(page: Page) {
  await page.click('button[title="ログアウト"]');
  await page.waitForURL("/login");
}
