# App mockups

Remote Desktop Harbor is a WPF app and only runs on Windows, so the images in the docs are **mockups**:
`app-mockups.html` recreates each window from its XAML (`src/Harbor.App/Views/*.xaml`) with the same labels and
layout, styled like the WPF UI (lepoco) light theme the app uses (ADR-0020): Fluent title bar, Mica background,
icon buttons. The data shown is fictional.

When a window's XAML changes, update `app-mockups.html` and re-render:

```bash
node website/mockups/render.cjs
```

This needs Playwright with Chromium (`npm i -g playwright && npx playwright install chromium`). Output goes to
`website/src/assets/screenshots/`. Real screenshots from Windows can replace the PNGs at any time (same file names).
