"""Packs icon-<size>.png renders into a multi-resolution Windows .ico.
Usage: python3 pack-ico.py <pngDir> <out.ico>"""
import sys
from pathlib import Path
from PIL import Image

src, out = Path(sys.argv[1]), Path(sys.argv[2])
sizes = [16, 20, 24, 32, 40, 48, 64, 256]
images = [Image.open(src / f"icon-{s}.png").convert("RGBA") for s in sizes]
images[-1].save(out, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])
print(f"wrote {out} with sizes {sizes}")
