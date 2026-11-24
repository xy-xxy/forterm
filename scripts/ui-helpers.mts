import type { Page } from "playwright";
import { setTimeout as delay } from "node:timers/promises";
export async function until<T>(
  page: Page,
  predicate: (arg: T) => boolean | Promise<boolean>,
  arg?: T,
  timeout = 15000,
) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (await page.evaluate(predicate, arg as T)) return;
    await delay(40);
  }
  throw new Error("UI state did not reach the expected result before timeout");
}

export async function codeText(page: Page) {
  return page
    .locator('.cm-content[aria-label="远程文件内容"]')
    .evaluate((el: any) => el.cmTile.root.view.state.doc.toString());
}
export async function replaceCode(page: Page, text: string) {
  const editor = page.getByLabel("远程文件内容", { exact: true });
  await editor.click();
  await page.keyboard.press(
    process.platform === "darwin" ? "Meta+a" : "Control+a",
  );
  await page.keyboard.insertText(text);
}
