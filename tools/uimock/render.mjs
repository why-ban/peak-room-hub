import { chromium } from 'playwright';
import path from 'node:path';
import process from 'node:process';

const target = process.argv[2];
const out = process.argv[3];
if (!target || !out) {
  console.error('usage: node render.mjs <html> <png> [width] [height]');
  process.exit(2);
}
const width = Number(process.argv[4] || 1920);
const height = Number(process.argv[5] || 1080);

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
await page.goto('file://' + path.resolve(target));
await page.waitForTimeout(350);
await page.screenshot({ path: path.resolve(out), fullPage: false });
await browser.close();
console.log('wrote ' + out);
