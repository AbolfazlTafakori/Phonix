import json
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PUB = r"C:\Users\Abolfazl\Desktop\Phonix\frontend\public\figma"

with open(os.path.join(HERE, "content_499-5224.json"), encoding="utf-8") as f:
    content = json.load(f)
seen = {}
for im in content["images"]:
    if im["ref"] not in seen:
        seen[im["ref"]] = im
items = list(seen.values())

# focus: best-seller photos and brand logos
idx = [12, 13, 28, 29, 35, 39, 41, 43, 47, 45, 33, 34, 36, 38, 40, 42, 44, 46, 48]
cols = 4
cell = 300
label_h = 26
rows = (len(idx) + cols - 1) // cols
sheet = Image.new("RGB", (cols * cell, rows * (cell + label_h)), (235, 235, 240))
draw = ImageDraw.Draw(sheet)
try:
    font = ImageFont.truetype("arialbd.ttf", 18)
except Exception:
    font = ImageFont.load_default()

for k, i in enumerate(idx):
    im = items[i]
    c = k % cols
    rr = k // cols
    x = c * cell
    y = rr * (cell + label_h)
    box = Image.new("RGB", (cell - 10, cell - 10), (60, 60, 75))
    p = os.path.join(PUB, im["ref"] + ".png")
    if os.path.exists(p):
        img = Image.open(p).convert("RGBA")
        img.thumbnail((cell - 24, cell - 24))
        box.paste(img, ((box.width - img.width) // 2, (box.height - img.height) // 2), img)
    sheet.paste(box, (x + 5, y + 5))
    draw.text((x + 6, y + cell - 4), f"#{i}  {im['w']}x{im['h']}", fill=(10, 10, 10), font=font)

out = os.path.join(HERE, "contact_sheet2.png")
sheet.save(out)
print("saved", out, sheet.size)
