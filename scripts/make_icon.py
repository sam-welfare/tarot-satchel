import math, os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

W = H = 512
img = Image.new("RGB", (W, H))
d = ImageDraw.Draw(img)

# vertical gradient background (deep purple -> near-black)
top = (58, 34, 84)
bot = (14, 8, 22)
for y in range(H):
    t = y / (H - 1)
    r = int(top[0] * (1 - t) + bot[0] * t)
    g = int(top[1] * (1 - t) + bot[1] * t)
    b = int(top[2] * (1 - t) + bot[2] * t)
    d.line([(0, y), (W, y)], fill=(r, g, b))

# soft glow behind the card
glow = Image.new("RGB", (W, H), (0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse([96, 96, W - 96, W - 96], fill=(120, 70, 160))
glow = glow.filter(ImageFilter.GaussianBlur(60))
img = Image.blend(img, Image.composite(glow, img, Image.new("L", (W, H), 110)), 0.35)
d = ImageDraw.Draw(img)

# tarot card, slightly rotated
card = Image.new("RGBA", (300, 420), (0, 0, 0, 0))
cd = ImageDraw.Draw(card)
cd.rounded_rectangle([0, 0, 299, 419], radius=22, fill=(240, 232, 210, 255), outline=(196, 158, 84, 255), width=6)
cd.rounded_rectangle([14, 14, 285, 405], radius=14, outline=(196, 158, 84, 255), width=2)

# 8-point star
cx, cy, R = 150, 190, 78
pts = []
for i in range(16):
    ang = math.pi / 8 * i - math.pi / 2
    r = R if i % 2 == 0 else R * 0.42
    pts.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
cd.polygon(pts, fill=(196, 158, 84, 255), outline=(120, 92, 40, 255))

# crescent moon
cd.ellipse([84, 300, 156, 372], fill=(196, 158, 84, 255))
cd.ellipse([104, 292, 172, 360], fill=(240, 232, 210, 255))

card = card.rotate(-8, expand=True, resample=Image.BICUBIC)
img.paste(card, (112, 44), card)
d = ImageDraw.Draw(img)

# title text
font = None
for path in [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\simhei.ttf"]:
    if os.path.exists(path):
        try:
            font = ImageFont.truetype(path, 64)
            break
        except Exception:
            pass
text = "塔罗袋"
if font:
    bbox = d.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    d.text(((W - tw) / 2, 448 - th / 2), text, font=font, fill=(238, 214, 150))
    font_small = ImageFont.truetype(os.path.join(os.path.dirname(r"C:\Windows\Fonts\x.ttf"), "msyh.ttc"), 26) if os.path.exists(r"C:\Windows\Fonts\msyh.ttc") else None
    if font_small:
        sub = "TAROT SATCHEL"
        sb = d.textbbox((0, 0), sub, font=font_small)
        d.text(((W - (sb[2] - sb[0])) / 2, 480), sub, font=font_small, fill=(168, 140, 96))

os.makedirs("workshop", exist_ok=True)
img.save("workshop/icon.png")
print("saved workshop/icon.png", img.size)
