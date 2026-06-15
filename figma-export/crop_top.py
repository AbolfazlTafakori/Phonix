import colorsys
from PIL import Image

IMG = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\assets\exports\499-5224.png"
im = Image.open(IMG).convert("RGB")
w, h = im.size
print(f"full render: {w}x{h}")

# crop top header + hero region
top = im.crop((0, 0, w, 980))
top.thumbnail((1100, 1100))
out = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\hero_top.png"
top.save(out)
print("saved crop:", out, top.size)

def hx(c):
    return f"#{c[0]:02X}{c[1]:02X}{c[2]:02X}"

def avg(x0, y0, x1, y1):
    r = im.crop((x0, y0, x1, y1)).resize((16, 16))
    px = list(r.getdata())
    return (sum(p[0] for p in px) // 256, sum(p[1] for p in px) // 256, sum(p[2] for p in px) // 256)

# sample the hero panel gradient (panel spans roughly y 300..900, x 60..1860)
print("\n=== hero panel gradient samples ===")
pts = {
    "top-left   ": (120, 360),
    "top-mid    ": (960, 360),
    "top-right  ": (1780, 360),
    "mid-left   ": (120, 600),
    "center     ": (960, 600),
    "mid-right  ": (1780, 600),
    "bot-left   ": (120, 840),
    "bot-right  ": (1780, 840),
}
for name, (x, y) in pts.items():
    c = avg(x - 30, y - 30, x + 30, y + 30)
    print(f"{name} {hx(c)} rgb{c}")
