#!/usr/bin/env python3
"""Build reviewable contact sheets from *actual* Android emulator screenshots."""
from __future__ import annotations
from pathlib import Path
import sys
from PIL import Image, ImageDraw, ImageOps

root = Path(sys.argv[1] if len(sys.argv) > 1 else "shots")
root.mkdir(parents=True, exist_ok=True)
paths = sorted(p for p in root.glob("*.png") if not p.name.startswith("_"))
if not paths:
    print("No screenshots available: no contact sheets created.")
    sys.exit(0)

def make_sheet(title: str, items: list[Path], columns: int, thumb_w: int, thumb_h: int):
    if not items:
        return
    pad, label_h, gutter, header = 18, 40, 14, 72
    width = pad * 2 + columns * thumb_w + (columns-1)*gutter
    rows = (len(items) + columns - 1)//columns
    height = header + pad + rows*(thumb_h + label_h + gutter) + pad
    sheet = Image.new("RGB", (width, height), "#0B0E1A")
    draw = ImageDraw.Draw(sheet)
    draw.text((pad, 16), title, fill="#EEF3FB")
    draw.text((pad, 37), f"{len(items)} actual emulator screenshots, no generated UI", fill="#E8B54A")
    for i, item in enumerate(items):
        col, row = i%columns, i//columns
        x, y = pad + col*(thumb_w+gutter), header + row*(thumb_h+label_h+gutter)
        try:
            with Image.open(item) as src:
                img = ImageOps.contain(src.convert("RGB"), (thumb_w,thumb_h))
                img_x=x+(thumb_w-img.width)//2
                img_y=y+(thumb_h-img.height)//2
                sheet.paste(img, (img_x,img_y))
        except Exception as exc:
            draw.text((x,y), f"Invalid screenshot: {exc}", fill="#FF7F7F")
        short = item.stem.replace("Page","").replace("_scroll", " · scroll").replace("_"," ")
        short = short[:29]
        draw.text((x, y+thumb_h+8), short, fill="#EEF3FB")
    out = root / ("_contact_sheet_tv.jpg" if title.startswith("TV") else "_contact_sheet_phone.jpg")
    sheet.save(out, quality=86, optimize=True)
    print(f"Contact sheet: {out} ({len(items)} images, {width}x{height})")

phone = [p for p in paths if not p.name.startswith("tv_")]
tv = [p for p in paths if p.name.startswith("tv_")]
make_sheet("Phone pages and scroll states", phone, columns=6, thumb_w=170, thumb_h=360)
make_sheet("TV-sized layouts", tv, columns=5, thumb_w=260, thumb_h=150)
html = ['<!doctype html><meta charset="utf-8"><title>MarkUpTV UI review</title>',
        '<style>body{background:#0b0e1a;color:#eef3fb;font:16px system-ui;padding:24px;}img{width:200px;max-width:100%;border-radius:10px;}figure{display:inline-block;vertical-align:top;margin:12px;}figcaption{max-width:200px;overflow-wrap:anywhere;color:#e8b54a}</style>',
        '<h1>MarkUpTV — actual Android screenshots</h1>',
        '<p>Debug-only navigation preview. Server-backed data may be unavailable.</p>']
for p in paths:
    html.append(f'<figure><img loading="lazy" src="{p.name}"><figcaption>{p.stem}</figcaption></figure>')
(root / "_index.html").write_text("\n".join(html), encoding="utf-8")
print("Wrote", root / "_index.html")
