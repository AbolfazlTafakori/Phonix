from PIL import Image, ImageDraw

SRC = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\assets\exports\499-5224.png"
im = Image.open(SRC).convert("RGB")
CROP = (70, 250, 1885, 975)
panel = im.crop(CROP)
pw, ph = panel.size
VW = 900
sc = VW / pw
vh = int(ph * sc)
view = panel.resize((VW, vh)).convert("RGB")
d = ImageDraw.Draw(view)

# manual polygon in % of the panel box
poly_pct = [
    (3, 6), (27, 6), (30, 16), (96, 16), (99, 24),
    (99, 78), (96, 92), (58, 92), (55, 85), (7, 85), (3, 80),
]
pts = [(int(x / 100 * VW), int(y / 100 * vh)) for (x, y) in poly_pct]
d.line(pts + [pts[0]], fill=(0, 255, 0), width=3)
for p in pts:
    d.ellipse([p[0]-4, p[1]-4, p[0]+4, p[1]+4], fill=(255, 255, 0))

out = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\verify_shape.png"
view.save(out)
print("saved", out)
print("clip-path: polygon(" + ", ".join(f"{x}% {y}%" for x, y in poly_pct) + ")")
