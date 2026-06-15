import glob
import os
import colorsys
from PIL import Image

SRC = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\assets\images"

def analyze(path):
    try:
        im = Image.open(path).convert("RGBA")
    except Exception:
        return None
    w, h = im.size
    if w < 350 or h < 350:
        return None
    small = im.resize((48, 48))
    px = small.load()
    sat_pixels = []  # (sat, r,g,b)
    light = 0
    opaque = 0
    total = 48 * 48
    for y in range(48):
        for x in range(48):
            r, g, b, a = px[x, y]
            if a > 40:
                opaque += 1
            if r > 200 and g > 200 and b > 200 and a > 40:
                light += 1
            mx, mn = max(r, g, b), min(r, g, b)
            if a > 60 and mx > 80 and (mx - mn) > 18:
                sat_pixels.append((mx - mn, r, g, b))
    if not sat_pixels:
        return None
    sat_pixels.sort(reverse=True)
    # peak = average of the top 30 most saturated pixels (the glow core)
    top = sat_pixels[:30]
    pr = sum(p[1] for p in top) // len(top)
    pg = sum(p[2] for p in top) // len(top)
    pb = sum(p[3] for p in top) // len(top)
    hue = colorsys.rgb_to_hsv(pr / 255, pg / 255, pb / 255)[0] * 360
    return {
        "file": os.path.basename(path),
        "size": (w, h),
        "peak": (pr, pg, pb),
        "hex": f"#{pr:02X}{pg:02X}{pb:02X}",
        "hue": round(hue),
        "light_frac": round(light / max(opaque, 1), 2),
        "colored_frac": round(len(sat_pixels) / total, 2),
    }

rows = []
for p in glob.glob(os.path.join(SRC, "*.png")):
    r = analyze(p)
    if r:
        rows.append(r)

def cat(hue):
    if hue <= 22 or hue >= 330:
        return "PINK/RED"
    if 225 <= hue <= 285:
        return "PURPLE"
    return ""

# glow-like = large, soft (high light_frac OR moderate colored), single soft hue
glows = [r for r in rows if cat(r["hue"]) and r["light_frac"] > 0.25 and r["colored_frac"] < 0.85]
glows.sort(key=lambda r: (-(r["size"][0] * r["size"][1])))

print("=== GLOW CANDIDATES (large, soft, pink or purple) ===")
for r in glows[:20]:
    print(f'{cat(r["hue"]):9} {r["hex"]} hue={r["hue"]:>3} light={r["light_frac"]} colored={r["colored_frac"]} {r["size"][0]}x{r["size"][1]}  {r["file"]}')
