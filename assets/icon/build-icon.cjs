// Renders app-icon.svg / app-icon-small.svg to PNGs with Chromium (Playwright).
// Usage: node assets/icon/build-icon.cjs <outDir>   then pack with pack-ico.py.
const { chromium } = require('playwright');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');

const here = __dirname;
const outDir = process.argv[2] ?? here;
const big = readFileSync(join(here, 'app-icon.svg'), 'utf8');
const small = readFileSync(join(here, 'app-icon-small.svg'), 'utf8');
const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256, 512];

(async () => {
const browser = await chromium.launch();
const page = await browser.newPage();
for (const size of sizes) {
  const svg = (size <= 24 ? small : big).replace(/width="\d+" height="\d+"/, `width="${size}" height="${size}"`);
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg}</body></html>`);
  await page.locator('svg').screenshot({ path: join(outDir, `icon-${size}.png`), omitBackground: true });
}
await browser.close();
})();
