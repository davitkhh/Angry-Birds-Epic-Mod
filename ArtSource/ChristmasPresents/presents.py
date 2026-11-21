"""Christmas presents that the Santa Pig (pig_caster_santa) throws out of his sack onto the pigs' side of a battle.
Drawn to match the presents peeking out of his sack (PigMage BodyParts/OffHand_SantaPig_Back.png): flat cel colours,
bold patterns, fat satin bows, a thick dark-maroon outline.

Every present is three pieces so they can move separately in Assets/Scripts/Assembly-CSharp/ChristmasPresentView.cs:
  Body  the box below the lid (front face + right side face)
  Lid   the lid (front, side and top faces); it pops on landing
  Bow   the bow on the lid; wobbles around its knot

Writes
  Assets/Resources/ChristmasPresents.png          the atlas (all presents + snow puff sprites)
  Assets/Resources/ChristmasPresentsConfig.json   per present/piece: atlas rect, quad centre and pivot (rig px, y up,
                                                  relative to the present's ground point = centre of its base)
  out/preview.png                                 every present assembled, 2x, for checking by eye

Units: rig px (1 px of the game's HR art); drawn at SS x and box-downsampled.
Run: python3 -B ArtSource/ChristmasPresents/presents.py
"""
import colorsys
import json
import math
import os

from PIL import Image, ImageChops, ImageDraw

ROOT = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(ROOT, "..", ".."))
OUT_DIR = os.path.join(ROOT, "out")
ATLAS_PATH = os.path.join(PROJECT, "Assets", "Resources", "ChristmasPresents.png")
CONFIG_PATH = os.path.join(PROJECT, "Assets", "Resources", "ChristmasPresentsConfig.json")

SS = 4  # supersampling
OUTLINE = (62, 20, 28)  # puff/atlas-bleed line colour; the presents are inked in their own colours (deep())
OUTER_W = 2.6  # silhouette outline, rig px (half sits outside the shape)
INNER_W = 1.3  # edges between faces / ribbon edges

# Oblique projection of the depth axis: right and up (y down in image space), like the presents in the sack.
DEPTH_DX = 0.56
DEPTH_DY = -0.40

# Each present: front width, body height, depth, lid height (rig px); box colour; pattern colours (2-3, cycled across
# the pattern) and a trim colour (pinstripes / small accents); ribbon colour; bow size. Every colour is turned into a
# hue-shifted cel ramp by tone(): shadows slide toward blue-violet (yellows toward orange), lights toward warm yellow.
PRESENTS = [
    # the sack's own teal box: pink polka dots with small white dots between, yellow ribbon (like the game's gift icon)
    dict(name="Dots", w=72, h=50, d=40, lid=14, base=(34, 156, 210), pats=[(255, 128, 186), (255, 172, 210)],
         trim=(250, 250, 255), pattern="dots", ribbon=(255, 210, 40), bow=34),
    # tall candy-cane box: white stripes edged in pink, gold pinstripes, green ribbon
    dict(name="Stripes", w=54, h=70, d=34, lid=14, base=(220, 30, 54), pats=[(255, 246, 240)],
         trim=(255, 196, 60), pattern="diag", ribbon=(56, 168, 66), bow=30),
    # wide flat box: gold, cream and orange stars with dot sparkles, red ribbon
    dict(name="Stars", w=86, h=34, d=46, lid=12, base=(36, 146, 72), pats=[(255, 214, 60), (255, 250, 220), (255, 150, 44)],
         trim=(170, 236, 120), pattern="stars", ribbon=(232, 38, 54), bow=34),
    # purple cube: lilac, pink and mint chevrons with white piping, pink ribbon
    dict(name="Zigzag", w=56, h=46, d=40, lid=13, base=(128, 70, 206), pats=[(210, 176, 255), (255, 132, 198), (112, 228, 214)],
         trim=(255, 255, 255), pattern="zigzag", ribbon=(255, 98, 172), bow=30),
    # the sack's orange box: magenta and yellow stripes with white pinstripes, purple ribbon
    dict(name="Candy", w=46, h=38, d=30, lid=11, base=(255, 136, 50), pats=[(238, 56, 138), (255, 214, 64)],
         trim=(255, 250, 240), pattern="vstripes", ribbon=(146, 86, 226), bow=24),
    # pale ice-blue box: blue, white and teal snowflakes with dots, red ribbon
    dict(name="Snowflake", w=64, h=56, d=38, lid=13, base=(190, 228, 255), pats=[(58, 128, 222), (255, 255, 255), (70, 196, 214)],
         trim=(120, 170, 240), pattern="flakes", ribbon=(232, 38, 52), bow=30),
]

PUFFS = ["Snow", "Sparkle"]  # landing puff sprites


def mul(c, k):
    return tuple(max(0, min(255, int(round(v * k)))) for v in c[:3])


def lighten(c, k):
    return tuple(int(round(v + (255 - v) * k)) for v in c[:3])


def _hue_toward(h, target, amount):
    d = (target - h + 0.5) % 1.0 - 0.5
    step = max(-abs(amount), min(abs(amount), d))
    return (h + step) % 1.0


def tone(c, level):
    """The game's cel ramp: level 0 = the colour itself, > 0 lit (paler, warmer), < 0 shadow (darker, more saturated,
    hue toward blue-violet; yellows/oranges/reds toward red instead, so gold shades to orange)."""
    r, g, b = (v / 255.0 for v in c[:3])
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    if level >= 0:
        k = min(level, 1.2)
        h = _hue_toward(h, 0.14, 0.035 * k) if s > 0.08 else h
        s = s * (1 - 0.42 * min(k, 1))
        v = v + (1 - v) * 0.75 * min(k, 1)
        if k > 1:  # beyond 1: towards white (specular)
            s *= 1 - (k - 1) * 3
            v = v + (1 - v) * (k - 1) * 4
    else:
        k = -level
        warm = h < 0.16 or h > 0.88
        if s > 0.08:
            h = _hue_toward(h, 0.0 if warm else 0.70, (0.045 if warm else 0.055) * k)
        else:  # greys / whites shade cool
            h = 0.62
            s = max(s, 0.18 * min(k, 1))
        s = s + (1 - s) * 0.32 * min(k, 1.5)
        v = v * max(0.12, 1 - 0.36 * k)
    r, g, b = colorsys.hsv_to_rgb(h, max(0, min(1, s)), max(0, min(1, v)))
    return (int(round(r * 255)), int(round(g * 255)), int(round(b * 255)))


def deep(c):
    """Line colour for a piece: a very dark shade of its own colour (the game outlines in the local colour)."""
    return tone(c, -2.0)


# ---------------------------------------------------------------- flat face textures (u right, v down, rig px)

def face_flat(fw, fh, spec, level, band_u=None, band_v=None, seed=0):
    """A flat face at one lighting level: box colour + multi-colour pattern + ribbon band(s), all toned by level."""
    W, H = max(1, int(round(fw * SS))), max(1, int(round(fh * SS)))
    img = Image.new("RGBA", (W, H), tone(spec["base"], level) + (255,))
    d = ImageDraw.Draw(img)
    pats = [tone(p, level) for p in spec["pats"]]
    pats_hi = [tone(p, level + 0.7) for p in spec["pats"]]
    trim = tone(spec["trim"], level)
    n = len(pats)
    p = spec["pattern"]
    s = SS
    if p == "dots":
        step = 13
        for j in range(-1, int(fh / step) + 2):
            for i in range(-1, int(fw / step) + 2):
                cx = (i + (0.5 if j % 2 else 0) + 0.3) * step
                cy = (j + 0.45) * step
                c = (i + j) % n
                r = 3.6
                d.ellipse([(cx - r) * s, (cy - r) * s, (cx + r) * s, (cy + r) * s], fill=pats[c])
                d.ellipse([(cx - r * 0.6) * s, (cy - r * 0.62) * s, (cx - r * 0.05) * s, (cy - r * 0.12) * s],
                          fill=pats_hi[c])
                # small trim dot between the big ones
                tx, ty = cx + step * 0.25, cy + step * 0.5
                d.ellipse([(tx - 1.1) * s, (ty - 1.1) * s, (tx + 1.1) * s, (ty + 1.1) * s], fill=trim)
    elif p == "diag":
        step = 15
        edge = tone(spec["base"], level + 0.5)
        for k in range(-int(fh / step) - 2, int(fw / step) + int(fh / step) + 3):
            x = k * step
            # white stripe with a thin pink edge, gold pinstripe in the middle of the red gap
            d.polygon([((x - 0.6) * s, 0), ((x + 5.6) * s, 0), ((x + 5.6 - fh) * s, fh * s), ((x - 0.6 - fh) * s, fh * s)],
                      fill=edge)
            d.polygon([(x * s, 0), ((x + 5) * s, 0), ((x + 5 - fh) * s, fh * s), ((x - fh) * s, fh * s)], fill=pats[k % n])
            d.polygon([((x + 9.7) * s, 0), ((x + 10.7) * s, 0), ((x + 10.7 - fh) * s, fh * s),
                       ((x + 9.7 - fh) * s, fh * s)], fill=trim)
    elif p == "vstripes":
        step = 11
        for k in range(-1, int(fw / step) + 2):
            x = k * step + 3
            d.rectangle([x * s, 0, (x + 4.5) * s, fh * s], fill=pats[k % n])
            d.rectangle([(x + 6.6) * s, 0, (x + 7.6) * s, fh * s], fill=trim)
    elif p == "zigzag":
        step = 12
        amp = 3.5
        for j in range(-1, int(fh / step) + 2):
            y = j * step + 5
            pts = [(i * 6 * s, (y + (amp if i % 2 else -amp)) * s) for i in range(-1, int(fw / 6) + 3)]
            d.line(pts, fill=pats[j % n], width=int(3.4 * s), joint="curve")
            d.line([(x, yy - 3.4 * s) for x, yy in pts], fill=trim, width=int(0.9 * s), joint="curve")
    elif p == "stars":
        step = 15
        for j in range(-1, int(fh / step) + 2):
            for i in range(-1, int(fw / step) + 2):
                cx = (i + (0.5 if j % 2 else 0) + 0.4) * step
                cy = (j + 0.5) * step
                rot = ((i * 7 + j * 3 + seed) % 5) * 0.25
                c = (i * 2 + j) % n
                star(d, cx * s, cy * s, 4.8 * s, 2.0 * s, rot, pats[c])
                star(d, cx * s, cy * s, 2.2 * s, 0.9 * s, rot, pats_hi[c])
                tx, ty = cx + step * 0.5, cy + step * 0.1
                d.ellipse([(tx - 0.9) * s, (ty - 0.9) * s, (tx + 0.9) * s, (ty + 0.9) * s], fill=trim)
    elif p == "flakes":
        step = 17
        for j in range(-1, int(fh / step) + 2):
            for i in range(-1, int(fw / step) + 2):
                cx = (i + (0.5 if j % 2 else 0) + 0.35) * step
                cy = (j + 0.5) * step
                snowflake(d, cx * s, cy * s, 4.8 * s, pats[(i + 2 * j) % n])
                tx, ty = cx + step * 0.5, cy + step * 0.05
                d.ellipse([(tx - 1.0) * s, (ty - 1.0) * s, (tx + 1.0) * s, (ty + 1.0) * s], fill=trim)
    # ribbon bands: dark edges, lit side, warm shadow side, cream sheen
    rib = spec["ribbon"]
    rw = ribbon_width(spec)
    r_base, r_shade = tone(rib, level), tone(rib, level - 0.55)
    r_sheen, r_spec = tone(rib, level + 0.65), tone(rib, level + 1.05)
    r_edge = deep(rib)
    e = INNER_W * 0.7
    if band_u is not None:
        x0, x1 = (band_u - rw / 2) * s, (band_u + rw / 2) * s
        d.rectangle([x0, 0, x1, H], fill=r_base)
        d.rectangle([x0 + rw * 0.62 * s, 0, x1, H], fill=r_shade)
        d.rectangle([x0 + rw * 0.2 * s, 0, x0 + rw * 0.36 * s, H], fill=r_sheen)
        d.rectangle([x0 + rw * 0.25 * s, 0, x0 + rw * 0.3 * s, H], fill=r_spec)
        d.rectangle([x0, 0, x0 + e * s, H], fill=r_edge)
        d.rectangle([x1 - e * s, 0, x1, H], fill=r_edge)
    if band_v is not None:
        y0, y1 = (band_v - rw / 2) * s, (band_v + rw / 2) * s
        d.rectangle([0, y0, W, y1], fill=r_base)
        d.rectangle([0, y0 + rw * 0.62 * s, W, y1], fill=r_shade)
        d.rectangle([0, y0 + rw * 0.2 * s, W, y0 + rw * 0.36 * s], fill=r_sheen)
        d.rectangle([0, y0, W, y0 + e * s], fill=r_edge)
        d.rectangle([0, y1 - e * s, W, y1], fill=r_edge)
        if band_u is not None:  # where the bands cross: the lengthwise band lies on top
            x0, x1 = (band_u - rw / 2) * s, (band_u + rw / 2) * s
            d.rectangle([x0, y0, x1, y1], fill=r_base)
            d.rectangle([x0 + rw * 0.62 * s, y0, x1, y1], fill=r_shade)
            d.rectangle([x0 + rw * 0.2 * s, y0, x0 + rw * 0.36 * s, y1], fill=r_sheen)
            d.rectangle([x0, y0, x0 + e * s, y1], fill=r_edge)
            d.rectangle([x1 - e * s, y0, x1, y1], fill=r_edge)
    return img


# Cel bands per face kind, painted in order: (level, region) with region in face-normalised (u, v) polygons,
# None = the whole face. Gloss streaks run diagonally across the paper like the game's gift icon.
def _streak(a, b):
    """Diagonal band between the lines u + 0.55 v = a and = b."""
    return [(a, 0), (b, 0), (b - 0.55, 1), (a - 0.55, 1)]


FACE_BANDS = {
    "front": [(0.0, None), (-0.16, [(0, 0.68), (1, 0.68), (1, 1), (0, 1)]),
              (0.55, _streak(0.06, 0.17)), (0.4, _streak(0.24, 0.28)),
              (-0.5, [(0.88, 0), (1, 0), (1, 1), (0.84, 1)]),
              (-0.55, [(0, 0.9), (1, 0.9), (1, 1), (0, 1)]),
              (-0.85, [(0.85, 0.9), (1, 0.9), (1, 1), (0.84, 1)])],
    "side": [(-0.72, None), (-0.86, [(0, 0.66), (1, 0.66), (1, 1), (0, 1)]),
             (-0.42, [(0.86, 0), (1, 0), (1, 1), (0.86, 1)]),
             (-0.5, _streak(0.12, 0.2)),
             (-1.2, [(0, 0.87), (1, 0.87), (1, 1), (0, 1)])],
    "top": [(0.32, None), (0.18, [(0, 0), (1, 0), (1, 0.22), (0, 0.22)]),
            (0.8, [(0, 0.45), (0.38, 1), (0, 1)]),
            (0.62, [(0.45, 0), (0.58, 0), (0.25, 1), (0.12, 1)])],
    "lidfront": [(0.05, None), (0.6, _streak(0.06, 0.12)), (-0.4, [(0.9, 0), (1, 0), (1, 1), (0.88, 1)]),
                 (-0.35, [(0, 0.72), (1, 0.72), (1, 1), (0, 1)])],
    "lidside": [(-0.62, None), (-0.95, [(0, 0.7), (1, 0.7), (1, 1), (0, 1)]),
                (-0.36, [(0.86, 0), (1, 0), (1, 1), (0.86, 1)])],
}


def face_texture(fw, fh, spec, kind, band_u=None, band_v=None, seed=0, extra=()):
    """A face with the game's hard-edged cel bands: the flat face is painted at each band's level and pasted through
    that band's region. extra = more (level, region) bands painted last (e.g. the lid's cast shadow)."""
    W, H = max(1, int(round(fw * SS))), max(1, int(round(fh * SS)))
    out = None
    cache = {}
    for level, region in list(FACE_BANDS[kind]) + list(extra):
        if level not in cache:
            cache[level] = face_flat(fw, fh, spec, level, band_u, band_v, seed)
        flat = cache[level]
        if region is None:
            out = flat.copy()
            continue
        m = Image.new("L", (W, H), 0)
        ImageDraw.Draw(m).polygon([(u * W, v * H) for u, v in region], fill=255)
        out.paste(flat, (0, 0), m)
    return out


def ribbon_width(spec):
    return max(8.0, min(13.0, spec["w"] * 0.16))


def star(d, cx, cy, ro, ri, rot, fill):
    pts = []
    for k in range(10):
        a = rot + k * math.pi / 5 - math.pi / 2
        r = ro if k % 2 == 0 else ri
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    d.polygon(pts, fill=fill)


def snowflake(d, cx, cy, r, fill):
    w = max(1, int(1.3 * SS))
    for k in range(6):
        a = k * math.pi / 3
        ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
        d.line([(cx, cy), (ex, ey)], fill=fill, width=w)
        mx, my = cx + 0.6 * r * math.cos(a), cy + 0.6 * r * math.sin(a)
        for side in (-1, 1):
            b = a + side * 0.75
            d.line([(mx, my), (mx + 0.38 * r * math.cos(b), my + 0.38 * r * math.sin(b))], fill=fill, width=w)
    d.ellipse([cx - 1.4 * SS, cy - 1.4 * SS, cx + 1.4 * SS, cy + 1.4 * SS], fill=fill)


# ---------------------------------------------------------------- canvas helpers

class Canvas:
    """A supersampled RGBA canvas whose coordinates are rig px around an origin (y down)."""

    def __init__(self, size, origin):
        self.size = size
        self.ox, self.oy = origin
        self.img = Image.new("RGBA", (size[0] * SS, size[1] * SS), (0, 0, 0, 0))

    def P(self, x, y):
        return ((x + self.ox) * SS, (y + self.oy) * SS)

    def poly_mask(self, pts):
        m = Image.new("L", self.img.size, 0)
        ImageDraw.Draw(m).polygon([self.P(*p) for p in pts], fill=255)
        return m

    def fill(self, pts, color):
        layer = Image.new("RGBA", self.img.size, color + (255,))
        self.img.paste(layer, (0, 0), self.poly_mask(pts))

    def fill_intersect(self, a, b, color):
        """Fills where polygons a and b overlap (cel light/shadow shapes cut from a shape)."""
        layer = Image.new("RGBA", self.img.size, color + (255,))
        self.img.paste(layer, (0, 0), ImageChops.multiply(self.poly_mask(a), self.poly_mask(b)))

    def outline(self, pts, width, closed=True, color=OUTLINE):
        q = [self.P(*p) for p in pts]
        if closed:
            q = q + q[:2]
        d = ImageDraw.Draw(self.img)
        d.line(q, fill=color + (255,), width=int(round(width * SS)), joint="curve")
        r = width * SS / 2
        ends = q if closed else [q[0], q[-1]]
        for x, y in ends:
            d.ellipse([x - r, y - r, x + r, y + r], fill=color + (255,))

    def line(self, a, b, width, color=OUTLINE):
        self.outline([a, b], width, closed=False, color=color)

    def texture_parallelogram(self, tex, o, eu, ev):
        """Maps a flat texture onto the parallelogram o + s*eu + t*ev (s, t in [0, 1]); eu/ev in rig px."""
        ox, oy = self.P(*o)
        ux, uy = eu[0] * SS, eu[1] * SS
        vx, vy = ev[0] * SS, ev[1] * SS
        tw, th = tex.size
        det = ux * vy - uy * vx
        # output (X, Y) -> (s, t): inverse of [[ux vx][uy vy]]
        a_s, b_s = vy / det, -vx / det
        a_t, b_t = -uy / det, ux / det
        # texture coords: x = s*tw, y = t*th
        coeffs = (a_s * tw, b_s * tw, -(a_s * ox + b_s * oy) * tw,
                  a_t * th, b_t * th, -(a_t * ox + b_t * oy) * th)
        warped = tex.transform(self.img.size, Image.AFFINE, coeffs, resample=Image.BICUBIC)
        pts = [o, (o[0] + eu[0], o[1] + eu[1]), (o[0] + eu[0] + ev[0], o[1] + eu[1] + ev[1]),
               (o[0] + ev[0], o[1] + ev[1])]
        self.img.paste(warped, (0, 0), self.poly_mask(pts))

    def shade(self, pts, color, alpha):
        layer = Image.new("RGBA", self.img.size, color + (0,))
        m = self.poly_mask(pts)
        # only where something is already drawn
        m = Image.composite(m, Image.new("L", m.size, 0), self.img.getchannel("A").point(lambda v: 255 if v > 0 else 0))
        layer.putalpha(m.point(lambda v: int(v * alpha)))
        self.img.alpha_composite(layer)

    def downsampled(self):
        return self.img.resize(self.size, Image.BOX)


def silhouette_outline(cv, pts, color):
    """Outer outline in the piece's own dark colour, drawn *under* the faces so half sits outside the shape; a second
    pass nudged down-right makes the line heavier on the shadow side, as in the game's hand-inked art."""
    cv.outline(pts, OUTER_W * 2, color=color)
    cv.outline([(x + 0.7, y + 0.7) for x, y in pts], OUTER_W * 2, color=color)


# ---------------------------------------------------------------- the pieces

def geometry(spec):
    w, h, D = spec["w"], spec["h"], spec["d"]
    dx, dy = D * DEPTH_DX, D * DEPTH_DY
    over = 2.2  # lid overhang
    lip = 3.0  # lid comes this far down over the body
    # front-bottom-left corner of the body at (x0, 0); ground point (0, 0) = centre of the base
    x0 = -(w + dx) / 2
    y0 = -dy / 2  # base front edge is below the ground point (the base recedes up the screen)
    top = y0 - h
    lid_bot = top + lip
    lid_top = lid_bot - spec["lid"]
    return dict(w=w, h=h, D=D, dx=dx, dy=dy, over=over, x0=x0, y0=y0, top=top, lid_bot=lid_bot, lid_top=lid_top)


def make_body(spec):
    g = geometry(spec)
    w, dx, dy, x0, y0, top = g["w"], g["dx"], g["dy"], g["x0"], g["y0"], g["top"]
    line = deep(spec["base"])
    pad = 6
    size = (int(w + dx + 2 * pad), int(g["h"] - dy + 2 * pad))
    cv = Canvas(size, (-x0 + pad, -top - dy + pad))
    # the open top (hidden under the lid until it pops): inner back wall, the front wall's shadow below it
    sil = [(x0, top), (x0 + dx, top + dy), (x0 + w + dx, top + dy), (x0 + w + dx, y0 + dy), (x0 + w, y0), (x0, y0)]
    silhouette_outline(cv, sil, line)
    cv.fill([(x0, top), (x0 + dx, top + dy), (x0 + w + dx, top + dy), (x0 + w, top)], tone(spec["base"], -1.0))
    cv.fill([(x0, top), (x0 + dx * 0.55, top + dy * 0.55), (x0 + w + dx * 0.55, top + dy * 0.55), (x0 + w, top)],
            tone(spec["base"], -1.6))
    # the lid's cast shadow: a hard band under the lid on both faces
    cast = 5.5 / g["h"]
    cv.texture_parallelogram(face_texture(g["D"], g["h"], spec, "side", band_u=g["D"] / 2, seed=1,
                                          extra=[(-1.25, [(0, 0), (1, 0), (1, cast), (0, cast)])]),
                             (x0 + w, top), (dx, dy), (0, g["h"]))
    cv.texture_parallelogram(face_texture(w, g["h"], spec, "front", band_u=w / 2, seed=0,
                                          extra=[(-0.9, [(0, 0), (1, 0), (1, cast), (0, cast)])]),
                             (x0, top), (w, 0), (0, g["h"]))
    cv.line((x0 + w, top), (x0 + w, y0), INNER_W, color=line)  # front/side edge
    cv.outline([(x0, top), (x0 + w, top), (x0 + w + dx, top + dy)], INNER_W, closed=False, color=line)  # rim
    return cv


def make_lid(spec):
    g = geometry(spec)
    w, dx, dy, o = g["w"], g["dx"], g["dy"], g["over"]
    line = deep(spec["base"])
    lx0, lw = g["x0"] - o, w + 2 * o
    ldx, ldy = dx * (1 + 2 * o / g["D"]), dy * (1 + 2 * o / g["D"])
    lb, lt = g["lid_bot"], g["lid_top"]
    lh = lb - lt
    pad = 6
    size = (int(lw + ldx + 2 * pad), int(lh - ldy + 2 * pad))
    cv = Canvas(size, (-lx0 + pad, -lt - ldy + pad))
    sil = [(lx0, lb), (lx0, lt), (lx0 + ldx, lt + ldy), (lx0 + lw + ldx, lt + ldy), (lx0 + lw + ldx, lb + ldy),
           (lx0 + lw, lb)]
    silhouette_outline(cv, sil, line)
    depth = g["D"] + 2 * o
    cv.texture_parallelogram(face_texture(depth, lh, spec, "lidside", band_u=depth / 2, seed=3),
                             (lx0 + lw, lt), (ldx, ldy), (0, lh))
    cv.texture_parallelogram(face_texture(lw, depth, spec, "top", band_u=lw / 2, band_v=depth / 2, seed=2),
                             (lx0 + ldx, lt + ldy), (lw, 0), (-ldx, -ldy))
    cv.texture_parallelogram(face_texture(lw, lh, spec, "lidfront", band_u=lw / 2, seed=4),
                             (lx0, lt), (lw, 0), (0, lh))
    cv.line((lx0, lt), (lx0 + lw, lt), INNER_W, color=line)  # front/top edge
    cv.line((lx0 + lw, lt), (lx0 + lw, lb), INNER_W, color=line)  # front/side edge
    cv.line((lx0 + lw, lt), (lx0 + lw + ldx, lt + ldy), INNER_W, color=line)  # side/top edge
    knot = (lx0 + lw / 2 + ldx / 2, lt + ldy / 2)  # centre of the top face (ribbon crossing)
    return cv, knot


def teardrop(axis_angle, length, width, pinch=0.45, n=48):
    """Bow loop from the knot (0, 0) along axis_angle (radians, y down)."""
    ca, sa = math.cos(axis_angle), math.sin(axis_angle)
    pts = []
    for k in range(n):
        s = 2 * math.pi * k / n
        a = length * (1 - math.cos(s)) / 2
        b = width / 2 * math.sin(s) * (a / length) ** pinch
        pts.append((a * ca - b * sa, a * sa + b * ca))
    return pts


def shifted(pts, dx, dy):
    return [(x + dx, y + dy) for x, y in pts]


def make_bow(spec):
    """Satin bow in the game's style: dark local-colour line, warm cel shadow on each loop's underside, a lit top with a
    cream sheen streak and a specular glint, deep shaded loop openings, folded notched tails."""
    L = spec["bow"]
    rib = spec["ribbon"]
    line = deep(rib)
    pad = 6
    size = (int(L * 2.3 + 2 * pad), int(L * 1.35 + 2 * pad))
    cv = Canvas(size, (size[0] / 2, L * 0.72 + pad))  # canvas origin = the knot
    # tails lie along the ribbon bands on the lid's top face, so the knot visibly sits on their crossing: the left
    # one along the band running to the front (it ends at the lid's front edge, right where the front band starts),
    # the right one along the band running to the side, drooping a little.
    g = geometry(spec)
    tw = ribbon_width(spec) * 0.9
    to_front = math.atan2(-DEPTH_DY, -DEPTH_DX)  # y down: the depth axis reversed, toward the viewer
    front_len = 0.5 * (g["D"] + 2 * g["over"]) * math.hypot(DEPTH_DX, DEPTH_DY)
    tails = [(-1, to_front, front_len), (1, math.radians(16), L * 0.5)]

    def inked(pts):
        cv.outline(pts, OUTER_W * 1.5, color=line)

    # tails: V-notched, each folded along its length
    for sgn, ang, tl in tails:
        ca, sa = math.cos(ang), math.sin(ang)
        px_, py_ = -sa, ca
        ex, ey = ca * tl, sa * tl
        a0, a1 = (px_ * tw / 2, py_ * tw / 2), (-px_ * tw / 2, -py_ * tw / 2)
        b0, b1 = (ex + px_ * tw / 2, ey + py_ * tw / 2), (ex - px_ * tw / 2, ey - py_ * tw / 2)
        notch = (ex - ca * tw * 0.55, ey - sa * tw * 0.55)
        pts = [a0, b0, notch, b1, a1]
        inked(pts)
        cv.fill(pts, tone(rib, -0.25))
        # the half turned away from the light
        half = [(0, 0), (ex - ca * tw * 0.55 * 0.5, ey - sa * tw * 0.55 * 0.5), notch, b0, a0]
        cv.fill(half if sgn > 0 else [(0, 0), notch, b1, a1], tone(rib, -0.75))
        cv.line((ca * tl * 0.25 - px_ * tw * 0.2, sa * tl * 0.25 - py_ * tw * 0.2),
                (ca * tl * 0.7 - px_ * tw * 0.2, sa * tl * 0.7 - py_ * tw * 0.2), 1.3, color=tone(rib, 0.6))
    # loops: up-left and up-right
    for sgn in (-1, 1):
        ang = math.radians(180 + 28) if sgn < 0 else math.radians(-28)
        ca, sa = math.cos(ang), math.sin(ang)
        loop = teardrop(ang, L * 0.98, L * 0.68)
        inked(loop)
        cv.fill(loop, tone(rib, -0.6))  # underside in shadow
        cv.fill_intersect(loop, shifted(loop, 0, -L * 0.2), tone(rib, 0.05))  # lit top
        cv.fill_intersect(loop, shifted(loop, 0, -L * 0.42), tone(rib, 0.45))  # top rim catching the light
        # loop opening: warm deep shade, darker at its back
        hole = [(x * 0.52 + ca * L * 0.40, y * 0.40 + sa * L * 0.40) for x, y in teardrop(ang, L * 0.98, L * 0.58)]
        cv.fill(hole, tone(rib, -1.0))
        cv.fill_intersect(hole, shifted(hole, 0, L * 0.08), tone(rib, -1.5))
        # sheen streak + glint along the loop's top
        p0 = (ca * L * 0.30 + sa * L * 0.17, sa * L * 0.30 - ca * L * 0.17)
        p1 = (ca * L * 0.70 + sa * L * 0.20, sa * L * 0.70 - ca * L * 0.20)
        cv.line(p0, p1, 2.2, color=tone(rib, 0.85))
        g = (p0[0] * 0.35 + p1[0] * 0.65, p0[1] * 0.35 + p1[1] * 0.65)
        cv.fill([(g[0] + 1.3 * math.cos(t * math.pi / 4), g[1] + 1.3 * math.sin(t * math.pi / 4)) for t in range(8)],
                tone(rib, 1.15))
    # knot
    kr = L * 0.17
    knot = [(kr * 1.15 * math.cos(2 * math.pi * k / 32), kr * math.sin(2 * math.pi * k / 32)) for k in range(32)]
    inked(knot)
    cv.fill(knot, tone(rib, -0.55))
    cv.fill_intersect(knot, shifted(knot, -kr * 0.3, -kr * 0.35), tone(rib, 0.1))
    cv.line((-kr * 0.5, -kr * 0.3), (kr * 0.05, -kr * 0.6), 1.6, color=tone(rib, 0.85))
    return cv


# ---------------------------------------------------------------- puffs

def make_puff(name):
    size = (24, 24)
    cv = Canvas(size, (12, 12))
    if name == "Snow":
        pts = [(9 * math.cos(2 * math.pi * k / 40) * (1 + 0.12 * math.sin(5 * 2 * math.pi * k / 40)),
                9 * math.sin(2 * math.pi * k / 40) * (1 + 0.12 * math.sin(5 * 2 * math.pi * k / 40))) for k in range(40)]
        cv.outline(pts, 2.0, color=(150, 170, 210))
        cv.fill(pts, (255, 255, 255))
        cv.fill([(x * 0.45 - 2.5, y * 0.45 - 2.5) for x, y in pts], (232, 244, 255))
    else:
        d = ImageDraw.Draw(cv.img)
        star(d, *cv.P(0, 0), 11 * SS, 3.2 * SS, 0, (255, 236, 120, 255))
        star(d, *cv.P(0, 0), 6 * SS, 1.8 * SS, 0, (255, 255, 230, 255))
    return cv


# ---------------------------------------------------------------- crop, pack, write

def crop(cv):
    """Downsample and trim; returns (image, centre of the image relative to the canvas origin, rig px, y down)."""
    im = cv.downsampled()
    bb = im.getchannel("A").getbbox()
    bb = (max(0, bb[0] - 1), max(0, bb[1] - 1), min(im.width, bb[2] + 1), min(im.height, bb[3] + 1))
    im = im.crop(bb)
    cx = bb[0] + im.width / 2 - cv.ox
    cy = bb[1] + im.height / 2 - cv.oy
    return im, (cx, cy)


def pack(images, pad=2):
    """Shelf-pack into the smallest power-of-two square-ish atlas; returns (size, {key: (x, y)})."""
    order = sorted(images, key=lambda k: -images[k].height)
    for side in (128, 256, 512, 1024):
        for wd, ht in ((side, side // 2), (side, side)):
            pos, x, y, shelf = {}, pad, pad, 0
            ok = True
            for k in order:
                im = images[k]
                if x + im.width + pad > wd:
                    x, y, shelf = pad, y + shelf + pad, 0
                if y + im.height + pad > ht:
                    ok = False
                    break
                pos[k] = (x, y)
                x += im.width + pad
                shelf = max(shelf, im.height)
            if ok:
                return (wd, ht), pos
    raise RuntimeError("atlas too big")


def bleed(im):
    """Transparent texels take the outline colour so bilinear filtering never pulls in black/white fringes."""
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            if px[x, y][3] == 0:
                px[x, y] = OUTLINE + (0,)
    return im


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    images, info = {}, {}
    for spec in PRESENTS:
        n = spec["name"]
        body = make_body(spec)
        lid, knot = make_lid(spec)
        bow = make_bow(spec)
        for part, cv in (("Body", body), ("Lid", lid)):
            im, c = crop(cv)
            images[n + "/" + part] = im
            info[n + "/" + part] = dict(center=c)
        im, c = crop(bow)
        images[n + "/Bow"] = im
        # bow canvas origin = its knot; on the present it sits exactly where the top face's two ribbon bands cross
        kx, ky = knot
        info[n + "/Bow"] = dict(center=(c[0] + kx, c[1] + ky), pivot=(kx, ky))
        g = geometry(spec)
        info[n + "/Lid"]["pivot"] = (g["x0"] + g["w"] / 2 + g["dx"] / 2, g["lid_bot"] + g["dy"] / 2)
        info[n + "/Body"]["pivot"] = (0.0, 0.0)
        info[n] = dict(top=-(g["lid_top"] + g["dy"]), center_y=-(g["lid_top"] + g["dy"]) / 2,
                       foot=g["w"] + g["dx"])
    for p in PUFFS:
        im, c = crop(make_puff(p))
        images["Puff/" + p] = im
        info["Puff/" + p] = dict(center=(0.0, 0.0), pivot=(0.0, 0.0))

    size, pos = pack(images)
    atlas = Image.new("RGBA", size, OUTLINE + (0,))
    for k, im in images.items():
        atlas.paste(im, pos[k])
    bleed(atlas)
    atlas.save(ATLAS_PATH)

    def part(k):
        x, y = pos[k]
        im = images[k]
        cx, cy = info[k]["center"]
        px_, py_ = info[k]["pivot"]
        # Unity: y up, uv origin bottom-left
        return dict(rect=[x, size[1] - y - im.height, im.width, im.height],
                    center=[round(cx, 2), round(-cy, 2)], pivot=[round(px_, 2), round(-py_, 2)])

    config = dict(
        note="Generated by ArtSource/ChristmasPresents/presents.py; do not edit by hand. Units: rig px, y up, "
             "relative to the present's ground point. rect = atlas texels (x, y from bottom, w, h).",
        atlasSize=list(size),
        presents=[dict(name=s["name"], height=round(info[s["name"]]["top"], 2),
                       centerY=round(info[s["name"]]["center_y"], 2), footprint=round(info[s["name"]]["foot"], 2),
                       body=part(s["name"] + "/Body"), lid=part(s["name"] + "/Lid"), bow=part(s["name"] + "/Bow"))
                  for s in PRESENTS],
        puffs=[dict(name=p, sprite=part("Puff/" + p)) for p in PUFFS],
    )
    with open(CONFIG_PATH, "w", newline="\n") as f:
        json.dump(config, f, indent=1)

    # preview: assemble each present, 2x, on a snowy battleground-ish backdrop
    cell_w, cell_h = 130, 140
    prev = Image.new("RGBA", (cell_w * len(PRESENTS), cell_h), (214, 232, 214, 255))
    for i, s in enumerate(PRESENTS):
        gx, gy = i * cell_w + cell_w // 2, cell_h - 22
        sh = Image.new("RGBA", prev.size, (0, 0, 0, 0))
        f = info[s["name"]]["foot"]
        ImageDraw.Draw(sh).ellipse([gx - f * 0.55, gy - 7, gx + f * 0.55, gy + 7], fill=(40, 60, 40, 70))
        prev.alpha_composite(sh)
        for p in ("Body", "Lid", "Bow"):
            k = s["name"] + "/" + p
            im = images[k]
            cx, cy = info[k]["center"]
            prev.alpha_composite(im, (int(round(gx + cx - im.width / 2)), int(round(gy + cy - im.height / 2))))
    prev = prev.resize((prev.width * 2, prev.height * 2), Image.NEAREST)
    prev.save(os.path.join(OUT_DIR, "preview.png"))
    print("atlas", size, "->", ATLAS_PATH)


if __name__ == "__main__":
    main()
