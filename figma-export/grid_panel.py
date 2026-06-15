from PIL import Image, ImageDraw, ImageFont

SRC = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\assets\exports\499-5224.png"
im = Image.open(SRC).convert("RGB")

# generous crop around the hero panel
CROP = (70, 250, 1885, 975)
panel = im.crop(CROP)
pw, ph = panel.size

# scale to a viewable width
VW = 900
sc = VW / pw
vh = int(ph * sc)
view = panel.resize((VW, vh)).convert("RGB")

d = ImageDraw.Draw(view)
try:
    font = ImageFont.truetype("arial.ttf", 14)
except Exception:
    font = ImageFont.load_default()

# 10% grid
for i in range(0, 11):
    x = int(VW * i / 10)
    d.line([(x, 0), (x, vh)], fill=(0, 255, 120), width=1)
    d.text((x + 2, 2), f"{i*10}", fill=(0, 255, 120), font=font)
    y = int(vh * i / 10)
    d.line([(0, y), (VW, y)], fill=(0, 200, 255), width=1)
    d.text((2, y + 1), f"{i*10}", fill=(0, 200, 255), font=font)

out = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\panel_grid.png"
view.save(out)
print("saved", out, view.size, "crop=", CROP)
