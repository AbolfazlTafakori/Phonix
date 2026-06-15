from PIL import Image, ImageFilter, ImageDraw

SRC = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\assets\exports\499-5224.png"
im = Image.open(SRC).convert("RGB")
W, H = im.size  # 1920 x 5800

# crop the hero panel region (below the header, above the stats bar)
CROP = (0, 180, W, 1010)
region = im.crop(CROP)
rw, rh = region.size

# downscale for tracing
PW = 480
scale = PW / rw
PH = int(rh * scale)
small = region.resize((PW, PH))

# binary mask: foreground = panel composition (brighter than dark bg)
THRESH = 80
gray = small.convert("L")
px = small.load()
mask = Image.new("L", (PW, PH), 0)
mp = mask.load()
for y in range(PH):
    for x in range(PW):
        r, g, b = px[x, y]
        if (r + g + b) > THRESH:
            mp[x, y] = 255

# morphological open to drop small dots / texture, then close to fill
mask = mask.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(3))
mask.save(r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\mask.png")

# keep only the largest blob via flood fill from the centre
from collections import deque
mp = mask.load()
seed = (PW // 2, PH // 2)
if mp[seed] == 0:
    # search outward for a fg seed
    for rad in range(1, PH):
        found = False
        for dy in (-rad, rad):
            for x in range(max(0, PW//2-rad), min(PW, PW//2+rad)):
                if 0 <= seed[1]+dy < PH and mp[x, seed[1]+dy] == 255:
                    seed = (x, seed[1]+dy); found = True; break
            if found: break
        if found: break

comp = Image.new("L", (PW, PH), 0)
cp = comp.load()
dq = deque([seed]); cp[seed] = 255
while dq:
    x, y = dq.popleft()
    for nx, ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
        if 0 <= nx < PW and 0 <= ny < PH and mp[nx, ny] == 255 and cp[nx, ny] == 0:
            cp[nx, ny] = 255; dq.append((nx, ny))

# fill horizontal gaps inside the blob (per row, fill between first & last fg)
for y in range(PH):
    xs = [x for x in range(PW) if cp[x, y] == 255]
    if xs:
        for x in range(min(xs), max(xs)+1):
            cp[x, y] = 255

# Moore boundary trace (clockwise) starting at topmost-leftmost fg pixel
def first_fg():
    for y in range(PH):
        for x in range(PW):
            if cp[x, y] == 255:
                return (x, y)
    return None

start = first_fg()
boundary = []
if start:
    nbrs = [(-1,-1),(0,-1),(1,-1),(1,0),(1,1),(0,1),(-1,1),(-1,0)]
    cur = start
    b_idx = 7  # came from "left"
    boundary.append(cur)
    safety = 0
    while safety < 20000:
        safety += 1
        found_next = False
        for k in range(8):
            idx = (b_idx + 1 + k) % 8
            nx, ny = cur[0]+nbrs[idx][0], cur[1]+nbrs[idx][1]
            if 0 <= nx < PW and 0 <= ny < PH and cp[nx, ny] == 255:
                b_idx = (idx + 4) % 8
                cur = (nx, ny)
                boundary.append(cur)
                found_next = True
                break
        if not found_next:
            break
        if cur == start and len(boundary) > 3:
            break

# Douglas-Peucker simplify
def dp(points, eps):
    if len(points) < 3:
        return points
    def dist(p, a, b):
        if a == b:
            return ((p[0]-a[0])**2 + (p[1]-a[1])**2) ** 0.5
        num = abs((b[0]-a[0])*(a[1]-p[1]) - (a[0]-p[0])*(b[1]-a[1]))
        den = ((b[0]-a[0])**2 + (b[1]-a[1])**2) ** 0.5
        return num/den
    dmax, idx = 0, 0
    for i in range(1, len(points)-1):
        d = dist(points[i], points[0], points[-1])
        if d > dmax:
            dmax, idx = d, i
    if dmax > eps:
        left = dp(points[:idx+1], eps)
        right = dp(points[idx:], eps)
        return left[:-1] + right
    return [points[0], points[-1]]

simp = dp(boundary, eps=6) if boundary else []
print(f"boundary pts: {len(boundary)} -> simplified: {len(simp)}")

# draw preview
prev = small.convert("RGB")
d = ImageDraw.Draw(prev)
if simp:
    d.line(simp + [simp[0]], fill=(0, 255, 0), width=2)
    for p in simp:
        d.ellipse([p[0]-3, p[1]-3, p[0]+3, p[1]+3], fill=(255, 255, 0))
prev.save(r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\shape_preview.png")

# clip-path in % of the panel box
print("\nclip-path: polygon(")
poly = []
for (x, y) in simp:
    px_ = round(x / PW * 100, 1)
    py_ = round(y / PH * 100, 1)
    poly.append(f"{px_}% {py_}%")
print("  " + ", ".join(poly))
print(")")
