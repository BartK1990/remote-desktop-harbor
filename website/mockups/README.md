# App mockups

The images in the docs (`website/src/assets/screenshots/`) are **captured from the real app** by
[`tools/Harbor.Screenshots`](../../tools/Harbor.Screenshots/README.md), which the
[Screenshots workflow](../../.github/workflows/screenshots.yml) runs on Windows (ADR-0031). Run that workflow by hand
with **commit** to update them after a UI change.

`app-mockups.html` recreates each window from its XAML (`src/Harbor.App/Views/*.xaml`) with the same labels and
layout, styled like the app's Fluent theme and tokens. The data shown is fictional. It is used

* for the shots the capture tool cannot take yet (`passphrase.png`), and
* to preview a design before the app has it.

```bash
node website/mockups/render.cjs          # only shots without a real capture
node website/mockups/render.cjs --all    # every shot (overwrites the captured PNGs — don't commit those)
```

This needs Playwright with Chromium (`npm i -g playwright && npx playwright install chromium`).
