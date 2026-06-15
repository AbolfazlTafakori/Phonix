import glob
import os
import colorsys
from PIL import Image, ImageDraw, ImageFont

SRC = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\assets\images"

def analyze(path):
    try:
        im = Image.open(path).convert("RGBA")
    except Exception:
        return None
    w, h = im.size
    if w < 500 or h < 500:
        return None
    small = im.resize((40, 40))
    px = small.load()
    transp = 0
    sat = []
    for y in range(40):
        for x in range(40):
            r, g, b, a = px[x, y]
            if a < 30:
                transp += 1
                continue
            mx, mn = max(r, g, b), min(r, g, b)
            if mx > 70 and (mx - mn) > 14:
                sat.append((mx - mn, r, g, b))
    if not sat:
        return None
    sat.sort(reverse=True)
    top = sat[:25]
    pr = sum(p[1] for p in top) // len(top)
    pg = sum(p[2] for p in top) // len(top)
    pb = sum(p[3] for p in top) // len(top)
    hue = colorsys.rgb_to_hsv(pr / 255, pg / 255, pb / 255)[0] * 360
    # glow = soft (few strongly-saturated pixels), large
    return {
        "path": path, "size": (w, h), "peak": (pr, pg, pb),
        "hex": f"#{pr:02X}{pg:02X}{pb:02X}", "hue": round(hue),
        "transp": round(transp / 1600, 2), "satfrac": round(len(sat) / 1600, 2),
    }

cands = []
for p in glob.glob(os.path.join(SRC, "*.png")):
    r = analyze(p)
    if not r:
        continue
    hue = r["hue"]
    is_pink = hue <= 25 or hue >= 320
    is_purple = 220 <= hue <= 290
    if (is_pink or is_purple):
        cands.append(r)

# prefer large, soft (low satfrac), single hue
cands.sort(key=lambda r: (-(r["size"][0] * r["size"][1])))
cands = cands[:18]

cols = 3
cell = 300
lh = 40
rows = (len(cands) + cols - 1) // cols
sheet = Image.new("RGB", (cols * cell, rows * (cell + lh)), (240, 240, 245))
draw = ImageDraw.Draw(sheet)
try:
    font = ImageFont.truetype("arialbd.ttf", 16)
except Exception:
    font = ImageFont.load_default()

for i, r in enumerate(cands):
    c = i % cols
    rr = i // cols
    x = c * cell
    y = rr * (cell + lh)
    box = Image.new("RGB", (cell - 10, cell - 10), (255, 255, 255))
    img = Image.open(r["path"]).convert("RGBA")
    img.thumbnail((cell - 24, cell - 24))
    box.paste(img, ((box.width - img.width) // 2, (box.height - img.height) // 2), img)
    sheet.paste(box, (x + 5, y + 5))
    draw.text((x + 6, y + cell - 6), f'#{i} {r["hex"]} hue{r["hue"]} {r["size"][0]}x{r["size"][1]}', fill=(0, 0, 0), font=font)

out = os.path.join(os.path.dirname(SRC), "..", "glow_candidates.png")
out = os.path.abspath(os.path.join(r"C:\Users\Abolfazl\Desktop\Phonix\figma-export", "glow_candidates.png"))
sheet.save(out)
print("saved", out, sheet.size, "candidates:", len(cands))
for i, r in enumerate(cands):
    print(f'#{i}\t{r["hex"]}\thue={r["hue"]}\ttransp={r["transp"]}\tsat={r["satfrac"]}\t{r["size"][0]}x{r["size"][1]}\t{os.path.basename(r["path"])}')
