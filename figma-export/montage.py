import json
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PUB = r"C:\Users\Abolfazl\Desktop\Phonix\frontend\public\figma"

with open(os.path.join(HERE, "content_499-5224.json"), encoding="utf-8") as f:
    content = json.load(f)

# unique refs in document order, with first path + size
seen = {}
for im in content["images"]:
    r = im["ref"]
    if r not in seen:
        seen[r] = im
items = list(seen.values())

cols = 6
cell = 230
pad = 8
label_h = 34
rows = (len(items) + cols - 1) // cols
W = cols * cell
H = rows * (cell + label_h)
sheet = Image.new("RGB", (W, H), (24, 24, 32))
draw = ImageDraw.Draw(sheet)
try:
    font = ImageFont.truetype("arial.ttf", 13)
except Exception:
    font = ImageFont.load_default()

for i, im in enumerate(items):
    r = im["ref"]
    c = i % cols
    rr = i // cols
    x = c * cell
    y = rr * (cell + label_h)
    # checker bg to reveal transparency
    box = Image.new("RGB", (cell - pad * 2, cell - pad * 2), (45, 45, 60))
    p = os.path.join(PUB, r + ".png")
    if os.path.exists(p):
        try:
            img = Image.open(p).convert("RGBA")
            img.thumbnail((cell - pad * 2 - 6, cell - pad * 2 - 6))
            bx = (box.width - img.width) // 2
            by = (box.height - img.height) // 2
            box.paste(img, (bx, by), img)
        except Exception as e:
            draw.text((x + 10, y + 10), "ERR", fill=(255, 80, 80), font=font)
    sheet.paste(box, (x + pad, y + pad))
    label = f"#{i}  {r[:10]}  {im['w']}x{im['h']}"
    draw.text((x + pad, y + cell - 2), label, fill=(230, 230, 240), font=font)
    draw.text((x + pad, y + cell + 14), im["path"].split("/")[-1][:34], fill=(150, 150, 170), font=font)

out = os.path.join(HERE, "contact_sheet.png")
sheet.save(out)
print("saved", out, sheet.size)
# also print index map
for i, im in enumerate(items):
    print(f'#{i}\t{im["ref"]}\t{im["w"]}x{im["h"]}\t{im["path"]}')
