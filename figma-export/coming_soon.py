from PIL import Image, ImageFilter, ImageDraw, ImageFont

REND = r"C:\Users\Abolfazl\Desktop\Phonix\figma-export\assets\exports\499-5224.png"
LOGO = r"C:\Users\Abolfazl\Desktop\Phonix\frontend\public\figma\logo-phoenix.png"
OUT = r"C:\Users\Abolfazl\Desktop\Phonix\coming-soon.png"

W, H = 1920, 1080
im = Image.open(REND).convert("RGB")
base = im.crop((0, 0, W, H))

# heavy blur + dark overlay for the "coming soon" mood
base = base.filter(ImageFilter.GaussianBlur(20))
base = Image.blend(base, Image.new("RGB", (W, H), (6, 6, 14)), 0.52)

# subtle pink/blue glow accents
glow = Image.new("RGB", (W, H), (0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse([-300, 400, 700, 1400], fill=(80, 0, 30))
gd.ellipse([1300, -400, 2300, 600], fill=(20, 20, 90))
glow = glow.filter(ImageFilter.GaussianBlur(220))
# blend the glow lightly for colour mood
base = Image.blend(base, glow, 0.18)

draw = ImageDraw.Draw(base)

def font(sz):
    for f in (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\segoeuib.ttf", r"C:\Windows\Fonts\Arial.ttf"):
        try:
            return ImageFont.truetype(f, sz)
        except Exception:
            pass
    return ImageFont.load_default()

def center(y, text, fnt, fill, spacing=0, shadow=None):
    widths = [draw.textlength(ch, font=fnt) for ch in text]
    total = sum(widths) + spacing * (len(text) - 1)
    x0 = (W - total) / 2
    if shadow:
        x = x0 + 4
        for ch, w in zip(text, widths):
            draw.text((x, y + 4), ch, font=fnt, fill=shadow)
            x += w + spacing
    x = x0
    for ch, w in zip(text, widths):
        draw.text((x, y), ch, font=fnt, fill=fill)
        x += w + spacing
    return total

# only COMING SOON, vertically centred
center(460, "COMING SOON", font(165), (255, 255, 255), spacing=16, shadow=(0, 0, 0))

# accent underline
lw2 = 380
draw.rounded_rectangle([(W - lw2) // 2, 690, (W + lw2) // 2, 699], radius=4, fill=(230, 0, 83))

base.save(OUT, quality=95)
print("saved:", OUT, base.size)
