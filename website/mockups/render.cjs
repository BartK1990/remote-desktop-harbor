// Renders the app mockups in app-mockups.html to PNGs in src/assets/screenshots/.
// Real screenshots come from tools/Harbor.Screenshots (Screenshots workflow); this renders only the shots that have
// no real counterpart, or all of them with --all (see README.md).
// Usage (needs Playwright with Chromium): node mockups/render.cjs [--all]
const path = require('path');
const { chromium } = require('playwright');

const shots = ['main', 'quick-connect', 'offline', 'editor', 'editor-accounts', 'editor-display', 'settings', 'settings-vault', 'settings-signin', 'settings-appearance', 'passphrase', 'inuse'];
const outDir = path.join(__dirname, '..', 'src', 'assets', 'screenshots');
// Captured from the real app by tools/Harbor.Screenshots in CI (ADR-0031): rendered only with --all, e.g. to preview a
// design before the app has it. The rest has no real counterpart yet.
const captured = new Set(['main', 'quick-connect', 'offline', 'editor', 'editor-accounts', 'editor-display', 'settings', 'settings-vault',
	'settings-signin', 'settings-appearance', 'inuse']);
const selected = process.argv.includes('--all') ? shots : shots.filter(s => !captured.has(s));

(async () => {
	const browser = await chromium.launch();
	const page = await browser.newPage({ viewport: { width: 1300, height: 1000 }, deviceScaleFactor: 2 });
	await page.goto('file://' + path.join(__dirname, 'app-mockups.html'));
	await page.evaluate(() => document.fonts.ready);
	for (const id of selected) {
		await page.locator('#' + id).screenshot({ path: path.join(outDir, `${id}.png`), omitBackground: true });
	}
	await browser.close();
})();
