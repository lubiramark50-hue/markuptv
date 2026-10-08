#!/usr/bin/env python3
"""Measure rendered UI against Android guidance: 48dp touch targets, labels, TV overscan."""
import sys, glob, os, re
import xml.etree.ElementTree as ET

OUT = sys.argv[1] if len(sys.argv) > 1 else "shots"
BOUNDS = re.compile(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]")

def parse(path):
    try:
        return ET.parse(path).getroot()
    except Exception:
        return None

def box(node):
    m = BOUNDS.match(node.get("bounds", ""))
    return tuple(int(x) for x in m.groups()) if m else None

def has_label(node):
    for n in node.iter("node"):
        if (n.get("text") or "").strip() or (n.get("content-desc") or "").strip():
            return True
    return False

def label(node):
    for n in node.iter("node"):
        t = (n.get("text") or "").strip() or (n.get("content-desc") or "").strip()
        if t:
            return t[:28]
    return "(no label)"

tot = {"pages": 0, "interactive": 0, "small": 0, "tiny": 0, "unlabeled": 0, "overscan": 0}
for path in sorted(glob.glob(os.path.join(OUT, "ui_*.xml"))):
    name = os.path.basename(path)[3:-4]
    root = parse(path)
    if root is None:
        continue
    tv = name.startswith("tv_")
    density = 1.5 if tv else 2.625
    top = next(root.iter("node"), None)
    sb = box(top) if top is not None else None
    if not sb:
        continue
    W, H = sb[2], sb[3]
    inter = small = tiny = unl = over = 0
    worst, ov_examples = [], []
    for n in root.iter("node"):
        if n.get("package") != "com.markup.markuptv":
            continue
        b = box(n)
        if not b:
            continue
        w, h = (b[2] - b[0]) / density, (b[3] - b[1]) / density
        if n.get("clickable") == "true" or (n.get("focusable") == "true" and n.get("scrollable") != "true" and n.get("class", "").endswith(("Button", "ViewGroup", "View"))):
            if w < 2 or h < 2 or w >= (W / density) - 1:
                continue
            inter += 1
            if min(w, h) < 48:
                small += 1
                if min(w, h) < 40:
                    tiny += 1
                worst.append((min(w, h), f'"{label(n)}" {w:.0f}x{h:.0f}dp'))
            if not has_label(n):
                unl += 1
        if tv and ((n.get("text") or "").strip() or n.get("clickable") == "true"):
            l, t, r, bt = b
            if l < 48 * density or r > W - 48 * density or t < 27 * density or bt > H - 27 * density:
                over += 1
                if len(ov_examples) < 3:
                    ov_examples.append(f'"{label(n)}" at {l/density:.0f},{t/density:.0f}')
    worst.sort()
    tot["pages"] += 1
    for k, v in (("interactive", inter), ("small", small), ("tiny", tiny), ("unlabeled", unl), ("overscan", over)):
        tot[k] += v
    line = f"AUDIT {name}: interactive={inter} under48dp={small} under40dp={tiny} unlabeled={unl}"
    if tv:
        line += f" overscan_violations={over}"
    print(line)
    if worst:
        print("   smallest:", "; ".join(w[1] for w in worst[:5]))
    if ov_examples:
        print("   overscan e.g.:", "; ".join(ov_examples))
print("AUDIT TOTAL", tot)
