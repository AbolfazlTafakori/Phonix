import colorsys
from PIL import Image

IMG = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\assets\exports\372-1519.png"
im = Image.open(IMG).convert("RGB")
w, h = im.size
print(f"image {w}x{h}")

def region_avg(x0, y0, x1, y1):
    r = im.crop((x0, y0, x1, y1)).resize((24, 24))
    px = list(r.getdata())
    rr = sum(p[0] for p in px) // len(px)
    gg = sum(p[1] for p in px) // len(px)
    bb = sum(p[2] for p in px) // len(px)
    return rr, gg, bb

def hx(c):
    return f"#{c[0]:02X}{c[1]:02X}{c[2]:02X}"

corners = {
    "TOP-LEFT   ": (0, 0, w // 3, h // 3),
    "TOP-RIGHT  ": (2 * w // 3, 0, w, h // 3),
    "BOTTOM-LEFT": (0, 2 * h // 3, w // 3, h),
    "BOTTOM-RIGHT": (2 * w // 3, 2 * h // 3, w, h),
    "CENTER     ": (w // 3, h // 3, 2 * w // 3, 2 * h // 3),
}
print("\n=== average colour of each region ===")
for name, box in corners.items():
    c = region_avg(*box)
    hsv = colorsys.rgb_to_hsv(c[0] / 255, c[1] / 255, c[2] / 255)
    print(f"{name}  {hx(c)}  rgb{c}  hue={round(hsv[0]*360)}")

# scan a coarse grid for the most saturated (most colourful) cells -> the glow cores
print("\n=== most colourful spots (glow cores) ===")
gx, gy = 32, 20
cells = []
for j in range(gy):
    for i in range(gx):
        x0 = i * w // gx
        y0 = j * h // gy
        c = region_avg(x0, y0, x0 + w // gx, y0 + h // gy)
        mx, mn = max(c), min(c)
        sat = mx - mn
        cells.append((sat, c, x0, y0))
cells.sort(reverse=True)
seen = []
for sat, c, x0, y0 in cells[:60]:
    hsv = colorsys.rgb_to_hsv(c[0] / 255, c[1] / 255, c[2] / 255)
    hue = round(hsv[0] * 360)
    pos = f"({round(x0/w*100)}%,{round(y0/h*100)}%)"
    key = (hue // 25, round(x0 / w * 4), round(y0 / h * 4))
    if key in seen:
        continue
    seen.append(key)
    print(f"{hx(c)}  rgb{c}  hue={hue:>3} sat={sat:>3}  at {pos}")
    if len(seen) >= 10:
        break
