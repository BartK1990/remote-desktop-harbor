# App icon

`app-icon.svg` (32 px and up) and `app-icon-small.svg` (16–24 px, pixel-aligned) are the sources.
`src/Harbor.App/Assets/AppIcon.ico` is generated from them and embedded in the exe (`ApplicationIcon`) and used by
all windows.

Regenerate after editing the SVGs (needs Node + Playwright with Chromium, and Python + Pillow):

```bash
node assets/icon/build-icon.cjs /tmp/icon
```

```bash
python3 assets/icon/pack-ico.py /tmp/icon src/Harbor.App/Assets/AppIcon.ico
```
