// Renders the app mockups in app-mockups.html to PNGs in src/assets/screenshots/.
// The WPF app only runs on Windows, so the docs use faithful mockups built from the XAML (see README.md).
// Usage (needs Playwright with Chromium): node mockups/render.cjs
const path = require('path');
const { chromium } = require('playwright');

const shots = ['main', 'quick-connect', 'offline', 'editor', 'editor-accounts', 'editor-display', 'settings', 'settings-vault', 'settings-signin', 'settings-appearance', 'passphrase', 'inuse'];
const outDir = path.join(__dirname, '..', 'src', 'assets', 'screenshots');

(async () => {
	const browser = await chromium.launch();
	const page = await browser.newPage({ viewport: { width: 1300, height: 1000 }, deviceScaleFactor: 2 });
	await page.goto('file://' + path.join(__dirname, 'app-mockups.html'));
	await page.evaluate(() => document.fonts.ready);
	for (const id of shots) {
		await page.locator('#' + id).screenshot({ path: path.join(outDir, `${id}.png`), omitBackground: true });
	}
	await browser.close();
})();
