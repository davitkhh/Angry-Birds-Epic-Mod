#!/usr/bin/env python3
"""Options-strip icons for the world-map gear toggle (WorldMapGear.cs).

  WorldMapGear_Bird.png      the strip button: a dotted outline of Red (GenericElements RedBird)
  WorldMapGear_MainHand.png  a solid outline of Red's starter sword (MainHand_Sword_01), tilted 45 degrees
  WorldMapGear_Headgear.png  a solid outline of the knight helmet's metal (Headgear_Knight) with its three visor slits
  WorldMapGear_OffHand.png   a solid outline of Red's starter shield (OffHand_Shield_01) with the egg on it
  WorldMapGear_All.png       three stacked round buttons (the usual "select all" look)
  WorldMapGear_Check.png     GenericElements' Check_Small, copied out (the popup's icons are UITextures)
  WorldMapGear_Cross.png     a red cross in the style of Check_Small
  WorldMapGear_Tail.png      a white triangle; the popup's tail, tinted like the strip background in game

Line icons follow the strip's style (see ArtSource/TargetingStyleIcons): white lines, a brown (153,102,0) outline and
a brown drop shadow down-right; everything else transparent so the yellow (or darkened) button shows.

Pure PIL (no numpy here). Run from anywhere:
    python3 ArtSource/WorldMapGearIcons/make_icons.py [preview.png]
"""
import math
import os
import re
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ATLAS = os.path.join(ROOT, "Assets", "Texture2D", "GenericElements.png")
ATLAS_PREFAB = os.path.join(ROOT, "Assets", "Prefab", "GenericElements.prefab")
RED_PARTS = os.path.join(ROOT, "Assets", "content", "4_Characters", "1_Birds", "1_RedBird", "BodyParts")
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "UI")

SIZE = 128          # output size (shown at 66 on the 84 button)
SS = 4              # supersampling
S = SIZE * SS

WHITE = (255, 255, 255, 255)
LINE = (153, 102, 0, 255)        # the strip icons' brown

LINE_W = 7                       # white line width, icon pixels
OUTLINE_W = 3                    # brown outline around the white line, icon pixels
SHADOW = (4, 5)                  # icon pixels, down-right
FIT = 104                        # the drawing's longest side, icon pixels


def atlas_sprite(name):
    text = open(ATLAS_PREFAB).read()
    m = re.search(r"- name: %s\n\s+x: (\d+)\n\s+y: (\d+)\n\s+width: (\d+)\n\s+height: (\d+)" % re.escape(name), text)
    x, y, w, h = map(int, m.groups())
    return Image.open(ATLAS).convert("RGBA").crop((x, y, x + w, y + h))


def part(name):
    return Image.open(os.path.join(RED_PARTS, name + ".png")).convert("RGBA")


def mask_of(img, keep):
    """L mask (0/255) of the pixels keep(r, g, b, a) accepts."""
    out = Image.new("L", img.size, 0)
    src = img.load()
    dst = out.load()
    for y in range(img.height):
        for x in range(img.width):
            if keep(*src[x, y]):
                dst[x, y] = 255
    return out


def components(mask):
    """Connected components (4-neighbour) of a 0/255 mask as lists of pixels."""
    px = mask.load()
    w, h = mask.size
    seen = set()
    comps = []
    for y in range(h):
        for x in range(w):
            if px[x, y] == 0 or (x, y) in seen:
                continue
            stack = [(x, y)]
            seen.add((x, y))
            comp = []
            while stack:
                cx, cy = stack.pop()
                comp.append((cx, cy))
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < w and 0 <= ny < h and px[nx, ny] and (nx, ny) not in seen:
                        seen.add((nx, ny))
                        stack.append((nx, ny))
            comps.append(comp)
    return comps


def only(mask, pick):
    """The mask reduced to the components pick(list of components) returns."""
    out = Image.new("L", mask.size, 0)
    px = out.load()
    for comp in pick(components(mask)):
        for p in comp:
            px[p] = 255
    return out


def fill_holes(mask):
    """Everything not reachable from the border becomes part of the mask."""
    w, h = mask.size
    inv = ImageChops.invert(mask)
    outside = Image.new("L", (w + 2, h + 2), 255)
    outside.paste(inv, (1, 1))
    ImageDraw.floodfill(outside, (0, 0), 128)
    outside = outside.crop((1, 1, w + 1, h + 1)).point(lambda v: 0 if v == 128 else 255)
    return outside


def smooth(mask, radius):
    return mask.filter(ImageFilter.GaussianBlur(radius)).point(lambda v: 255 if v >= 128 else 0)


class Drawing:
    """Masks placed on the S x S canvas with one shared transform (scale, then centred)."""

    def __init__(self, size, fit=FIT):
        self.k = fit * SS / float(max(size))
        self.size = (int(size[0] * self.k), int(size[1] * self.k))
        self.off = ((S - self.size[0]) // 2, (S - self.size[1]) // 2)

    def place(self, mask, blur=2):
        big = mask.resize(self.size, Image.BILINEAR).point(lambda v: 255 if v >= 128 else 0)
        out = Image.new("L", (S, S), 0)
        out.paste(big, self.off)
        return smooth(out, blur * SS)


def ring(mask, width=LINE_W):
    """A line of the given width (icon pixels) centred on the mask's edge."""
    r = max(1, int(width * SS / 2))
    grown = mask.filter(ImageFilter.MaxFilter(2 * r + 1))
    shrunk = mask.filter(ImageFilter.MinFilter(2 * r + 1))
    return ImageChops.subtract(grown, shrunk)


def dashed(line, centre, dashes, fill=0.6):
    """Keeps the parts of line whose angle around centre falls in a dash."""
    out = line.copy()
    px = out.load()
    step = 2 * math.pi / dashes
    for y in range(out.height):
        for x in range(out.width):
            if px[x, y] == 0:
                continue
            a = math.atan2(y - centre[1], x - centre[0]) % (2 * math.pi)
            if (a % step) / step > fill:
                px[x, y] = 0
    return out


def styled(lines, outline_w=OUTLINE_W):
    """White lines, brown outline, brown drop shadow."""
    white = Image.new("RGBA", (S, S), WHITE)
    white.putalpha(lines)
    grown = lines.filter(ImageFilter.MaxFilter(2 * outline_w * SS + 1))
    body = Image.new("RGBA", (S, S), LINE)
    body.putalpha(grown)
    body.alpha_composite(white)
    shadow_alpha = Image.new("L", (S, S), 0)
    shadow_alpha.paste(grown, (SHADOW[0] * SS, SHADOW[1] * SS))
    out = Image.new("RGBA", (S, S), LINE)
    out.putalpha(shadow_alpha)
    out.alpha_composite(body)
    return out


def union(*masks):
    out = masks[0]
    for m in masks[1:]:
        out = ImageChops.lighter(out, m)
    return out


def centroid(mask):
    px = mask.load()
    sx = sy = n = 0
    for y in range(0, mask.height, 2):
        for x in range(0, mask.width, 2):
            if px[x, y]:
                sx += x
                sy += y
                n += 1
    return (sx / float(n), sy / float(n))


# --- icons -----------------------------------------------------------------------------------------------------------

def edge_path(mask):
    """The outer boundary of the mask's largest blob, traced in order (Moore neighbourhood), thinned to every 4th
    pixel and lightly smoothed."""
    px = mask.load()
    w, h = mask.size

    def on(x, y):
        return 0 <= x < w and 0 <= y < h and px[x, y] > 0

    start = None
    for y in range(h):
        for x in range(w):
            if on(x, y):
                start = (x, y)
                break
        if start:
            break
    dirs = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)]
    path = [start]
    cur = start
    back = 4  # came from the left (west)
    for _ in range(w * h):
        found = False
        for i in range(8):
            d = (back + 1 + i) % 8
            nx, ny = cur[0] + dirs[d][0], cur[1] + dirs[d][1]
            if on(nx, ny):
                back = (d + 4) % 8
                cur = (nx, ny)
                found = True
                break
        if not found or cur == start:
            break
        path.append(cur)
    path = path[::4]
    n = len(path)
    k = 3
    return [(sum(path[(i + j) % n][0] for j in range(-k, k + 1)) / (2.0 * k + 1),
             sum(path[(i + j) % n][1] for j in range(-k, k + 1)) / (2.0 * k + 1)) for i in range(n)]


def dotted(path, dashes, fill, width):
    """Round-capped dashes of equal arc length along a closed path."""
    total = 0.0
    seg = []
    for i in range(len(path)):
        p0, p1 = path[i], path[(i + 1) % len(path)]
        l = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
        seg.append((p0, p1, total, l))
        total += l
    period = total / dashes

    def at(s):
        s %= total
        for p0, p1, start, l in seg:
            if start <= s <= start + l:
                t = (s - start) / l if l else 0
                return (p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t)
        return path[0]

    out = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(out)
    r = width / 2.0
    for k in range(dashes):
        s0 = k * period
        pts = [at(s0 + period * fill * j / 8.0) for j in range(9)]
        d.line(pts, fill=255, width=int(width), joint="curve")
        for p in (pts[0], pts[-1]):
            d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=255)
    return out


def bird_icon():
    img = atlas_sprite("RedBird")

    def keep(r, g, b, a):
        # without the sprite's light-gray sticker rim, so the crest and the tail feathers stand out
        return a > 128 and not (max(r, g, b) - min(r, g, b) < 40 and r > 140)

    sil = fill_holes(mask_of(img, keep))
    sil = only(sil, lambda cs: [max(cs, key=len)])
    d = Drawing(sil.size, fit=110)
    placed = d.place(sil, blur=1)
    return styled(dotted(edge_path(placed), dashes=17, fill=0.4, width=7 * SS), outline_w=2)


def sword_icon():
    img = part("MainHand_Sword_01").rotate(45, resample=Image.BICUBIC, expand=True)
    sil = fill_holes(mask_of(img, lambda r, g, b, a: a > 128))
    sil = only(sil, lambda cs: [max(cs, key=len)])
    d = Drawing(sil.size, fit=112)
    return styled(ring(d.place(sil)))


def is_plume(r, g, b, a):
    return a > 128 and r > 150 and r > g + 60 and r > b + 60


def helmet_icon():
    img = part("Headgear_Knight")
    metal = mask_of(img, lambda r, g, b, a: a > 128 and not is_plume(r, g, b, a))
    metal = only(fill_holes(metal), lambda cs: [max(cs, key=len)])
    metal = smooth(metal, 2)
    # visor slits: dark pixels in the helmet's upper band, as small separate blobs
    dark = mask_of(img, lambda r, g, b, a: a > 200 and (r + g + b) / 3.0 < 75 and b >= r)
    dark = smooth(dark, 1)
    w, h = img.size

    def slits(cs):
        out = []
        for c in cs:
            if not (30 <= len(c) <= 700):
                continue
            xs = [p[0] for p in c]
            ys = [p[1] for p in c]
            cy = sum(ys) / float(len(ys))
            bw, bh = max(xs) - min(xs) + 1, max(ys) - min(ys) + 1
            # the three squares sit on the upper band; the underside's dark is long and lower down
            if cy < h * 0.62 and bw < w * 0.2 and bh < h * 0.25 and min(xs) > 0 and min(ys) > 0:
                out.append(c)
        return out

    slit = only(dark, slits)
    d = Drawing(metal.size, fit=112)
    return styled(union(ring(d.place(metal)), ring(d.place(slit), 6)))


def shield_icon():
    img = part("OffHand_Shield_01")
    sil = only(fill_holes(mask_of(img, lambda r, g, b, a: a > 128)), lambda cs: [max(cs, key=len)])
    egg = mask_of(img, lambda r, g, b, a: a > 200 and r > 200 and g > 185 and b > 90)
    egg = only(smooth(egg, 2), lambda cs: [max(cs, key=len)])
    # the planks' grain breaks the egg up: draw a clean egg in its bounding box instead (narrower at the top)
    x0, y0, x1, y1 = egg.getbbox()
    egg = Image.new("L", sil.size, 0)
    ed = ImageDraw.Draw(egg)
    mid = y0 + (y1 - y0) * 0.55
    ed.ellipse([x0, y0, x1, y0 + 2 * (mid - y0)], fill=255)
    ed.ellipse([x0, mid - (y1 - mid), x1, y1], fill=255)
    ed.ellipse([x0 + (x1 - x0) * 0.12, y0, x1 - (x1 - x0) * 0.12, y1], fill=255)
    d = Drawing(sil.size, fit=108)
    return styled(union(ring(d.place(sil)), ring(d.place(egg), 6)))


def all_icon():
    """Three round buttons stacked from the top left to the bottom right; front ones hide the ones behind."""
    lines = Image.new("L", (S, S), 0)
    r = 30 * SS
    w = int(LINE_W * SS)
    centres = [(40, 40), (64, 64), (88, 88)]
    for cx, cy in centres:
        cx, cy = cx * SS, cy * SS
        d = ImageDraw.Draw(lines)
        # clear what this circle covers, then draw its rim
        d.ellipse([cx - r - w, cy - r - w, cx + r + w, cy + r + w], fill=0)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=255, width=w)
    # the outline of a front circle must also cut the circles behind it: grow the clear area by the brown outline
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    for i, (cx, cy) in enumerate(centres):
        layer = Image.new("L", (S, S), 0)
        ImageDraw.Draw(layer).ellipse([cx * SS - r, cy * SS - r, cx * SS + r, cy * SS + r], outline=255, width=w)
        icon = styled(layer)
        # punch the front circle's disc (with its outline) out of what is already drawn
        hole = Image.new("L", (S, S), 0)
        g = r + w // 2 + OUTLINE_W * SS
        ImageDraw.Draw(hole).ellipse([cx * SS - g, cy * SS - g, cx * SS + g, cy * SS + g], fill=255)
        out.putalpha(ImageChops.multiply(out.split()[3], ImageChops.invert(hole)))
        out.alpha_composite(icon)
    return out


def cross_icon():
    """A red X shaded like Check_Small: light top, darker bottom, dark rim and a soft highlight."""
    n = 60
    s = n * SS
    shape = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(shape)
    m = 12 * SS
    w = 15 * SS
    for p0, p1 in (((m, m), (s - m, s - m)), ((s - m, m), (m, s - m))):
        d.line([p0, p1], fill=255, width=w)
        for p in (p0, p1):
            d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=255)
    rim = shape.filter(ImageFilter.MaxFilter(2 * 3 * SS + 1))
    out = Image.new("RGBA", (s, s), (110, 0, 0, 255))
    out.putalpha(rim)
    fill = Image.new("RGBA", (s, s))
    fp = fill.load()
    for y in range(s):
        t = y / float(s - 1)
        col = (int(255 - 60 * t), int(70 - 50 * t), int(60 - 45 * t), 255)
        for x in range(s):
            fp[x, y] = col
    fill.putalpha(shape)
    out.alpha_composite(fill)
    # highlight: the upper half of the shape shrunk, faintly lighter
    hi = shape.filter(ImageFilter.MinFilter(2 * 3 * SS + 1))
    hp = hi.load()
    for y in range(s // 2, s):
        for x in range(s):
            hp[x, y] = 0
    hi = hi.filter(ImageFilter.GaussianBlur(2 * SS)).point(lambda v: v * 90 // 255)
    light = Image.new("RGBA", (s, s), (255, 190, 180, 255))
    light.putalpha(hi)
    out.alpha_composite(light)
    return out.resize((n, n), Image.LANCZOS)


def tail_icon():
    s = 32 * SS
    img = Image.new("RGBA", (s, s), (255, 255, 255, 0))
    ImageDraw.Draw(img).polygon([(0, 0), (s, 0), (s // 2, s * 3 // 4)], fill=WHITE)
    return img.resize((32, 32), Image.LANCZOS)


def save(img, name, size=SIZE):
    path = os.path.join(OUT_DIR, name)
    if img.size != (size, size):
        img = img.resize((size, size), Image.LANCZOS)
    img.save(path)
    print("wrote", path)


def preview(path):
    """Each icon on the strip's yellow button (lit and darkened), the way the popup shows them."""
    button = atlas_sprite("Button_Round_SubSmall")
    dark = button.copy()
    dark_px = dark.load()
    for y in range(dark.height):
        for x in range(dark.width):
            r, g, b, a = dark_px[x, y]
            dark_px[x, y] = (r * 4 // 10, g * 4 // 10, b * 4 // 10, a)
    check = atlas_sprite("Check_Small")
    names = ["WorldMapGear_Bird", "WorldMapGear_MainHand", "WorldMapGear_Headgear", "WorldMapGear_OffHand", "WorldMapGear_All"]
    sheet = Image.new("RGBA", (len(names) * 100, 200), (40, 40, 40, 255))
    for row, bg in enumerate((button, dark)):
        for i, name in enumerate(names):
            ox, oy = i * 100, row * 100
            sheet.alpha_composite(bg, (ox + 8, oy + 8))
            icon = Image.open(os.path.join(OUT_DIR, name + ".png")).resize((66, 66), Image.LANCZOS)
            sheet.alpha_composite(icon, (ox + 50 - 33, oy + 50 - 33))
            if name == "WorldMapGear_All":
                mark = check if row == 0 else Image.open(os.path.join(OUT_DIR, "WorldMapGear_Cross.png"))
                mark = mark.resize((44, 44), Image.LANCZOS)
                sheet.alpha_composite(mark, (ox + 54, oy + 4))
    sheet.resize((sheet.width * 2, sheet.height * 2), Image.LANCZOS).save(path)


if __name__ == "__main__":
    save(bird_icon(), "WorldMapGear_Bird.png")
    save(sword_icon(), "WorldMapGear_MainHand.png")
    save(helmet_icon(), "WorldMapGear_Headgear.png")
    save(shield_icon(), "WorldMapGear_OffHand.png")
    save(all_icon(), "WorldMapGear_All.png")
    save(atlas_sprite("Check_Small"), "WorldMapGear_Check.png", 60)
    save(cross_icon(), "WorldMapGear_Cross.png", 60)
    save(tail_icon(), "WorldMapGear_Tail.png", 32)
    if len(sys.argv) > 1:
        preview(sys.argv[1])
