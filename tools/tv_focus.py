#!/usr/bin/env python3
"""Read a uiautomator dump from stdin: which element has focus, and how far the content is from the screen edges."""
import sys, re
import xml.etree.ElementTree as ET

density = float(sys.argv[1])
label = sys.argv[2] if len(sys.argv) > 2 else ""
xml = sys.stdin.read()
try:
    root = ET.fromstring(xml[xml.index("<"):])
except Exception:
    print(f"TVFOCUS {label}: no ui dump")
    sys.exit(0)
B = re.compile(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]")

def box(n):
    m = B.match(n.get("bounds", ""))
    return tuple(int(x) for x in m.groups()) if m else None

def lab(n):
    for m in n.iter("node"):
        t = (m.get("text") or m.get("content-desc") or "").strip()
        if t:
            return t[:30]
    return "(no label)"

W = H = 0
focused = None
texts = []
for n in root.iter("node"):
    b = box(n)
    if b:
        W, H = max(W, b[2]), max(H, b[3])
for n in root.iter("node"):
    if n.get("package") != "com.markup.markuptv":
        continue
    if n.get("focused") == "true" and focused is None:
        focused = n
    b = box(n)
    t = (n.get("text") or n.get("content-desc") or "").strip()
    if b and t:
        texts.append((b, t))
foc = "none" if focused is None else f'"{lab(focused)}" {box(focused)}'
if texts:
    left = min(b[0] for b, _ in texts) / density
    top = min(b[1] for b, _ in texts) / density
    right = (W - max(b[2] for b, _ in texts)) / density
    bottom = (H - max(b[3] for b, _ in texts)) / density
    ext = f"content margin dp left={left:.0f} top={top:.0f} right={right:.0f} bottom={bottom:.0f}"
else:
    ext = "no text"
print(f"TVFOCUS {label}: focused={foc}; {ext}; screen={W}x{H}")
