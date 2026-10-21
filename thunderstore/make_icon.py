"""Draws the Thunderstore icon (256x256): a raven flying over hills at dawn, along a dashed route from a
small roost to a red mark far away.

Same frame as Raven Post's icon, so the two read as a family. Drawn at 1024 px and scaled down for smooth
edges. Run: python thunderstore/make_icon.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# Background: rounded square, dawn sky (deep blue at the top, warm near the horizon), thin gold frame.
bg = Image.new("RGBA", (S, S))
bd = ImageDraw.Draw(bg)
for y in range(S):
    t = y / S
    if t < 0.62:
        k = t / 0.62
        r = int(28 * (1 - k) + 196 * k)
        g = int(44 * (1 - k) + 132 * k)
        b = int(72 * (1 - k) + 96 * k)
    else:
        r, g, b = 196, 132, 96
    bd.line([(0, y), (S, y)], fill=(r, g, b, 255))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([16, 16, S - 16, S - 16], radius=150, fill=255)
img.paste(bg, (0, 0), mask)

# Low sun glow on the horizon, right.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([620, 480, 940, 800], fill=(255, 214, 150, 150))
glow = glow.filter(ImageFilter.GaussianBlur(60))
img.alpha_composite(glow, (0, 0))


def hills(base, amp, freq, phase, color):
    """A band of rolling hills from y=base down to the bottom, clipped to the frame."""
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    pts = [(0, S)]
    for x in range(0, S + 1, 8):
        y = base - amp * math.sin(x / S * math.pi * freq + phase) - amp * 0.4 * math.sin(x / S * math.pi * freq * 2.3 + phase)
        pts.append((x, y))
    pts.append((S, S))
    ld.polygon(pts, fill=color)
    clipped = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    clipped.paste(layer, (0, 0), mask)
    img.alpha_composite(clipped)


# Far mountains, then forest hills, then the near meadow.
hills(600, 70, 3.0, 0.6, (92, 84, 110, 255))
hills(690, 45, 4.0, 2.0, (46, 70, 58, 255))
hills(800, 30, 2.5, 1.0, (64, 98, 60, 255))

# Little pine trees on the middle hills.
for x, y, h in [(150, 690, 70), (205, 700, 55), (260, 684, 80), (720, 676, 60), (780, 690, 74), (835, 680, 58)]:
    d.polygon([(x, y - h), (x - h * 0.32, y), (x + h * 0.32, y)], fill=(30, 48, 40, 255))

# The roost, bottom left: post with a box.
d.rectangle([150, 800, 250, 860], fill=(112, 76, 44, 255), outline=(78, 52, 30, 255), width=6)
d.rectangle([262, 700, 286, 860], fill=(126, 86, 50, 255))
d.rectangle([254, 690, 294, 706], fill=(92, 62, 36, 255))

# Dashed route from the roost, arcing up and away to the far mark.
INK = (250, 238, 210, 235)
start, end = (274, 690), (830, 300)
ctrl = (380, 330)
steps = 90
for i in range(steps):
    if i % 6 >= 3:
        continue
    t0, t1 = i / steps, (i + 1) / steps
    p0 = [(1 - t0) ** 2 * start[k] + 2 * (1 - t0) * t0 * ctrl[k] + t0 ** 2 * end[k] for k in (0, 1)]
    p1 = [(1 - t1) ** 2 * start[k] + 2 * (1 - t1) * t1 * ctrl[k] + t1 ** 2 * end[k] for k in (0, 1)]
    d.line([tuple(p0), tuple(p1)], fill=INK, width=12)

# Red mark at the end of the route.
RED = (176, 32, 24, 255)
for dx, dy in ((1, 1), (1, -1)):
    d.line([(end[0] - 44 * dx, end[1] - 44 * dy), (end[0] + 44 * dx, end[1] + 44 * dy)], fill=RED, width=22)

# The raven, flying right along the route, wings up.
BLACK = (16, 19, 24, 255)
SHEEN = (44, 56, 76, 255)
cx, cy = 466, 405
# far wing (behind the body)
d.polygon([(cx - 20, cy - 10), (cx - 120, cy - 190), (cx - 40, cy - 170), (cx + 60, cy - 30)], fill=SHEEN)
# body
d.ellipse([cx - 130, cy - 40, cx + 110, cy + 50], fill=BLACK)
# tail
d.polygon([(cx - 110, cy - 10), (cx - 250, cy - 50), (cx - 260, cy + 40), (cx - 110, cy + 30)], fill=BLACK)
# head and beak
d.ellipse([cx + 70, cy - 60, cx + 170, cy + 30], fill=BLACK)
d.polygon([(cx + 160, cy - 30), (cx + 250, cy - 8), (cx + 162, cy + 12)], fill=(30, 33, 38, 255))
d.ellipse([cx + 118, cy - 34, cx + 140, cy - 12], fill=(226, 178, 64, 255))
# near wing (in front), with long fingered tips
d.polygon([(cx - 60, cy - 10), (cx - 260, cy - 230), (cx - 200, cy - 250), (cx - 120, cy - 230),
           (cx - 40, cy - 200), (cx + 70, cy - 20)], fill=BLACK)
for i, (tx, ty) in enumerate([(cx - 300, cy - 260), (cx - 270, cy - 290), (cx - 220, cy - 300)]):
    d.polygon([(cx - 230 + i * 30, cy - 220), (tx, ty), (cx - 200 + i * 30, cy - 240)], fill=BLACK)
d.line([(cx - 200, cy - 200), (cx + 40, cy - 30)], fill=SHEEN, width=10)

d.rounded_rectangle([16, 16, S - 16, S - 16], radius=150, outline=(201, 162, 74, 255), width=14)

out = img.resize((256, 256), Image.LANCZOS)
path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon.png")
out.save(path)
print("saved", path, out.size)
