from PIL import Image

IMG = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\assets\exports\499-5224.png"
im = Image.open(IMG).convert("RGB")
w, h = im.size

def hx(c):
    return f"#{c[0]:02X}{c[1]:02X}{c[2]:02X}"

def avg(x0, y0, x1, y1):
    r = im.crop((x0, y0, x1, y1)).resize((12, 12))
    px = list(r.getdata())
    n = len(px)
    return (sum(p[0] for p in px) // n, sum(p[1] for p in px) // n, sum(p[2] for p in px) // n)

# render is 1920 wide. header is around y 30..95
spots = {
    "account btn purple (L)": (120, 55, 170, 80),
    "account btn purple (R)": (270, 55, 320, 80),
    "cart pill area        ": (470, 55, 540, 80),
    "search pill bg        ": (700, 55, 900, 80),
    "read-more btn left    ": (180, 855, 230, 880),
    "read-more btn right   ": (520, 855, 580, 880),
    "section pill (محصولات)": (880, 1660, 1040, 1700),
}
for name, box in spots.items():
    c = avg(*box)
    print(f"{name}  {hx(c)}  rgb{c}")
