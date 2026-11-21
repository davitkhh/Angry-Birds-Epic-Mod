"""Gift-wrap curse visuals: a combatant hidden inside a lumpy, Christmas-wrapped parcel (paper gathered into a ruffle
at the top, ribbon tied round the neck with a bow, ribbon bands round the body, folds, a strip of tape, a gift tag).

Style = the Christmas presents' (style.py is a snapshot of ArtSource/ChristmasPresents/presents.py): lines inked in a
very dark shade of each piece's own colour (deep()), heavier on the shadow side; hard cel bands from tone()'s
hue-shifted ramp (shadows slide toward blue-violet, golds toward orange, lights warm/pale); diagonal gloss streaks;
satin ribbons with a shaded side, sheen and glint; the same multi-colour patterns.

Pieces (moved separately by Assets/Scripts/Assembly-CSharp/GiftWrapView.cs):
  Body/<Paper>_<Shape>  the wrapped lump + ruffle + neck tie, one per paper x shape (the shapes cover different
                        aspects; the runtime picks the closest and stretches it a little to fit the combatant)
  Bow/<Paper>           the bow on the neck tie (wobbles round its knot)
  Tag                   gift tag on a twine string (swings round the knot)
  Shard/<Paper>_<n>     torn paper scraps (fly in when wrapping, burst out when unwrapping)
  Curl/<Paper>          a curly ribbon scrap
  Puff/Sparkle, Puff/Snow

Writes
  Assets/Resources/GiftWrap.png          atlas
  Assets/Resources/GiftWrapConfig.json   per piece: atlas rect + quad centre relative to the piece's pivot (rig px, y up)
  out/preview.png, out/scraps.png        assembled parcels / loose pieces, 2x

Run: python3 -B ArtSource/GiftWrap/giftwrap.py
"""
import json
import math
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw

ROOT = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(ROOT, "..", ".."))
sys.path.insert(0, ROOT)
import style as S  # noqa: E402
from style import SS, OUTER_W, INNER_W, Canvas, tone, deep  # noqa: E402

OUT_DIR = os.path.join(ROOT, "out")
ATLAS_PATH = os.path.join(PROJECT, "Assets", "Resources", "GiftWrap.png")
CONFIG_PATH = os.path.join(PROJECT, "Assets", "Resources", "GiftWrapConfig.json")

PAPER_BACK = (250, 244, 232)  # the white back of the paper (flipped ruffle flaps, scraps)

# Same colour language as the presents: base, 1-3 pattern colours, a trim colour, ribbon.
PAPERS = [
    dict(name="CandyStripe", base=(220, 30, 54), pats=[(255, 246, 240)], trim=(255, 196, 60), pattern="diag",
         ribbon=(56, 168, 66)),
    dict(name="GreenDots", base=(36, 146, 72), pats=[(255, 248, 238), (255, 132, 132)], trim=(170, 236, 120),
         pattern="dots", ribbon=(232, 38, 54)),
    dict(name="NavyStars", base=(44, 64, 158), pats=[(255, 214, 60), (255, 250, 220)], trim=(150, 190, 255),
         pattern="stars", ribbon=(255, 210, 40)),
    dict(name="IceFlakes", base=(190, 228, 255), pats=[(58, 128, 222), (255, 255, 255), (70, 196, 214)],
         trim=(120, 170, 240), pattern="flakes", ribbon=(232, 38, 52)),
]

# lump width/height (rig px, without the neck/ruffle); bump phases (left, right) make each a different lump
SHAPES = [
    dict(name="Round", w=104, h=94, bump=(0.4, 2.1)),
    dict(name="Tall", w=82, h=120, bump=(1.3, 3.4)),
    dict(name="Wide", w=132, h=82, bump=(2.6, 0.9)),
]

SHARDS_PER_PAPER = 6
SHADOW = -0.72  # the presents' side-face level
DEEP_SHADOW = -1.15
LIT = 0.32


def spec_of(paper):
    return dict(paper, w=60)  # face_flat only reads w for the presents' ribbon width


# ---------------------------------------------------------------- geometry

def geom(shape):
    a = shape["w"] / 2.0
    H = shape["h"]
    return dict(a=a, H=H, n=max(6.0, shape["w"] * 0.085), neck_h=max(7.0, H * 0.075), ruffle_h=H * 0.16 + 9,
                yc=-H * 0.47, rw=max(9.0, min(13.0, shape["w"] * 0.115)))


def half_width(shape, g, t, side):
    """Lump half width at height t (0 bottom .. 1 shoulder/neck), boxy bottom, shoulders gathered into the neck."""
    a, n = g["a"], g["n"]
    if t < 0.45:
        u = min(1.0, (0.45 - t) / 0.45)
        hw = a * max(0.0, 1 - u ** 4.0) ** (1 / 4.0)
    else:
        s = min(1.0, (t - 0.45) / 0.55)
        hw = n + (a - n) * max(0.0, 1 - s ** 2.2) ** (1 / 2.2)
    phase = shape["bump"][0 if side < 0 else 1]
    return hw * (1 + 0.045 * math.sin(2 * math.pi * t * 1.4 + phase) * math.sin(math.pi * min(1.0, t)))


def lump_polygon(shape, g):
    H = g["H"]
    right, left = [], []
    for k in range(65):
        t = 0.012 + (1 - 0.012) * k / 64
        right.append((half_width(shape, g, t, 1), -t * H))
        left.append((-half_width(shape, g, t, -1), -t * H))
    top = -(H + g["neck_h"])
    right.append((g["n"] * 0.92, top))
    left.append((-g["n"] * 0.92, top))
    return [(left[0][0], 0.0)] + list(reversed(left)) + right + [(right[0][0], 0.0)]


def sphere_warp(g):
    """Pattern bends round the lump: squeezed toward its edges like paper round a ball."""
    a, yc, b = g["a"], g["yc"], g["H"] * 0.5

    def f(v, r):
        q = v / r
        if abs(q) <= 1:
            return r * (0.45 * q + 0.55 * math.asin(q) / (math.pi / 2))
        return v

    return lambda x, y: (f(x, a * 1.04), yc + f(y - yc, b * 1.04))


# ---------------------------------------------------------------- drawing helpers

def pattern_image(cv, paper, level, warp=None, seed=0):
    """Full-canvas paper at one cel level, optionally bent by warp (target rig px -> source rig px)."""
    W, H = cv.size
    m = 30
    flat = S.face_flat(W + 2 * m, H + 2 * m, spec_of(paper), level, seed=seed)
    if warp is None:
        return flat.crop((m * SS, m * SS, (W + m) * SS, (H + m) * SS))
    cell = 4
    mesh = []

    def src(x, y):
        sx, sy = warp(x - cv.ox, y - cv.oy)
        return ((sx + cv.ox + m) * SS, (sy + cv.oy + m) * SS)

    for y0 in range(0, H, cell):
        for x0 in range(0, W, cell):
            x1, y1 = min(W, x0 + cell), min(H, y0 + cell)
            mesh.append(((x0 * SS, y0 * SS, x1 * SS, y1 * SS), src(x0, y0) + src(x0, y1) + src(x1, y1) + src(x1, y0)))
    return flat.transform(cv.img.size, Image.MESH, mesh, resample=Image.BICUBIC)


def ellipse_mask(cv, cx, cy, rx, ry):
    m = Image.new("L", cv.img.size, 0)
    X, Y = cv.P(cx, cy)
    ImageDraw.Draw(m).ellipse([X - rx * SS, Y - ry * SS, X + rx * SS, Y + ry * SS], fill=255)
    return m


def AND(a, b):
    return ImageChops.multiply(a, b)


def MINUS(a, b):
    return ImageChops.subtract(a, b)


def tapered(cv, pts, w0, w1, color):
    """A stroke along pts whose width goes w0 -> w1 (rig px): the game's inked creases thin out at their ends."""
    left, right = [], []
    n = len(pts)
    for i, (x, y) in enumerate(pts):
        x2, y2 = pts[min(n - 1, i + 1)]
        x1, y1 = pts[max(0, i - 1)]
        ln = math.hypot(x2 - x1, y2 - y1) or 1
        nx, ny = -(y2 - y1) / ln, (x2 - x1) / ln
        w = (w0 + (w1 - w0) * i / (n - 1)) / 2
        left.append((x + nx * w, y + ny * w))
        right.append((x - nx * w, y - ny * w))
    cv.fill(left + list(reversed(right)), color)


def quad_curve(p0, p1, p2, n=16):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]) for t in (k / n for k in range(n + 1))]


def inked(cv, pts, color, width=OUTER_W):
    """Silhouette line in the piece's own dark colour, heavier toward the lower right (shadow side)."""
    cv.outline(pts, width * 2, color=color)
    cv.outline([(x + 0.6, y + 0.6) for x, y in pts], width * 2, color=color)


def ribbon_strip(cv, left, right, rib, level):
    """A satin band between two edge polylines (same length): base, shaded far side, sheen + glint, dark edges."""
    def lerp(t):
        return [(l[0] + (r[0] - l[0]) * t, l[1] + (r[1] - l[1]) * t) for l, r in zip(left, right)]
    cv.fill(left + list(reversed(right)), tone(rib, level))
    cv.fill(lerp(0.62) + list(reversed(right)), tone(rib, level - 0.55))
    cv.fill(lerp(0.2) + list(reversed(lerp(0.36))), tone(rib, level + 0.65))
    cv.fill(lerp(0.25) + list(reversed(lerp(0.3))), tone(rib, level + 1.05))
    e = deep(rib)
    cv.outline(left, INNER_W * 0.7, closed=False, color=e)
    cv.outline(right, INNER_W * 0.7, closed=False, color=e)


# ---------------------------------------------------------------- body

def ribbon_bands(shape, g):
    """Edge polylines of the vertical (front) and horizontal (belly) ribbon bands, in lump coordinates."""
    a, H, rw = g["a"], g["H"], g["rw"]
    lv, rv = [], []
    for k in range(41):
        t = k / 40
        y = -t * (H + g["neck_h"])
        hw_t = half_width(shape, g, min(1.0, -y / H), 1)
        w = rw * (0.45 + 0.55 * min(1.0, hw_t / a)) / 2
        lv.append((-w + 1.2 * math.sin(t * 2.4), y))
        rv.append((w + 1.2 * math.sin(t * 2.4), y))
    yb, sag = -H * 0.40, H * 0.075
    top_e, bot_e = [], []
    for k in range(61):
        x = -a * 1.1 + 2.2 * a * k / 60
        y = yb + sag * math.sqrt(max(0.0, 1 - (x / (a * 1.1)) ** 2))
        top_e.append((x, y - rw / 2))
        bot_e.append((x, y + rw / 2))
    return (lv, rv), (top_e, bot_e)


def make_body(paper, shape):
    g = geom(shape)
    a, H, n = g["a"], g["H"], g["n"]
    rib = paper["ribbon"]
    line = deep(paper["base"])
    pad = 8
    top = H + g["neck_h"] + g["ruffle_h"] + 6
    size = (int(2 * a * 1.12 + 2 * pad), int(top + 2 * pad))
    origin = (size[0] / 2, top + pad)
    cv = Canvas(size, origin)
    rnd = random.Random(sum(map(ord, paper["name"] + shape["name"])))

    # ---- ruffle: gathered paper fanning out of the neck; outer flaps behind, every other one shows the paper's back
    neck_top = -(H + g["neck_h"])
    flaps = []
    count = 5
    for i in range(count):
        f = (i / (count - 1)) * 2 - 1
        ang = math.radians(f * 52 + rnd.uniform(-5, 5))
        L = g["ruffle_h"] * (1.0 - 0.18 * abs(f)) * rnd.uniform(0.92, 1.06)
        dx, dy = math.sin(ang), -math.cos(ang)
        px_, py_ = -dy, dx
        bx, by = f * n * 0.55, neck_top + 4
        wb, wt = n * 0.5, g["ruffle_h"] * 0.30
        tx, ty = bx + dx * L, by + dy * L
        pts = [(bx - px_ * wb, by - py_ * wb),
               (tx - px_ * wt + dx * 1.5, ty - py_ * wt + dy * 1.5),
               (tx - px_ * wt * 0.35 - dx * 2.5, ty - py_ * wt * 0.35 - dy * 2.5),  # crinkled top edge
               (tx + dx * 2.0, ty + dy * 2.0),
               (tx + px_ * wt * 0.4 - dx * 2.0, ty + py_ * wt * 0.4 - dy * 2.0),
               (tx + px_ * wt, ty + py_ * wt),
               (bx + px_ * wb, by + py_ * wb)]
        flaps.append((abs(f), i, pts, (bx, by), (tx, ty), (px_, py_)))
    flaps.sort(key=lambda fl: -fl[0])
    flat_lit = pattern_image(cv, paper, 0.0, None, seed=3)
    flat_sh = pattern_image(cv, paper, SHADOW, None, seed=3)
    for _, i, pts, base, tip, perp in flaps:
        back = i % 2 == 1
        inked(cv, pts, line)
        mask = cv.poly_mask(pts)
        # the half on the far side of the pleat (from the light) is in shadow
        sgn = 1 if perp[0] > 0 else -1
        half = cv.poly_mask([base, tip, (tip[0] + perp[0] * sgn * 60, tip[1] + perp[1] * sgn * 60),
                             (base[0] + perp[0] * sgn * 60, base[1] + perp[1] * sgn * 60)])
        if back:
            cv.fill(pts, tone(PAPER_BACK, 0.0))
            cv.img.paste(Image.new("RGBA", cv.img.size, tone(PAPER_BACK, SHADOW) + (255,)), (0, 0), AND(mask, half))
        else:
            cv.img.paste(flat_lit, (0, 0), mask)
            cv.img.paste(flat_sh, (0, 0), AND(mask, half))
        tapered(cv, quad_curve(base, ((base[0] + tip[0]) / 2 + 1.5, (base[1] + tip[1]) / 2), tip, 10),
                INNER_W * 0.95, 0.2, line)

    # ---- the lump: paper + ribbon bands painted at each cel level, pasted through that level's region
    lump = lump_polygon(shape, g)
    inked(cv, lump, line)
    lump_mask = cv.poly_mask(lump)
    warp = sphere_warp(g)
    (lv, rv), (te, be) = ribbon_bands(shape, g)

    def painted(level, seed=1):
        layer = Canvas(size, origin)
        layer.img = pattern_image(cv, paper, level, warp, seed=seed)
        ribbon_strip(layer, lv, rv, rib, level)
        ribbon_strip(layer, te, be, rib, level)  # belly band crosses over the front band
        return layer.img

    yc = g["yc"]
    lit_region = ellipse_mask(cv, -a * 0.17, yc - H * 0.10, a * 1.0, H * 0.55)
    deep_lit = ellipse_mask(cv, -a * 0.08, yc - H * 0.05, a * 1.04, H * 0.6)
    glow = ellipse_mask(cv, -a * 0.42, yc - H * 0.20, a * 0.36, H * 0.22)
    streaks = Image.new("L", cv.img.size, 0)
    for s0, s1 in ((-0.62, -0.46), (-0.38, -0.33)):
        p = [(a * s0, -H), (a * s1, -H), (a * s1 - H * 0.55, 0), (a * s0 - H * 0.55, 0)]
        ImageDraw.Draw(streaks).polygon([cv.P(*q) for q in p], fill=255)
    bands = [(0.0, lump_mask),
             (SHADOW, MINUS(lump_mask, lit_region)),
             (DEEP_SHADOW, MINUS(lump_mask, deep_lit)),
             (LIT, AND(lump_mask, glow)),
             (0.6, AND(AND(lump_mask, glow), streaks))]
    cache = {}
    for level, mask in bands:
        if level not in cache:
            cache[level] = painted(level)
        cv.img.paste(cache[level], (0, 0), mask)

    # ---- folds: paper gathered from the neck onto the shoulders (crease + a cel shadow beside it)
    for f in (-1.55, -0.6, 0.55, 1.5):
        s = (f * n * 0.55, -H + 2)
        e = (f * a * 0.40 + rnd.uniform(-3, 3), -H * (0.60 + 0.06 * abs(f)) + rnd.uniform(-3, 3))
        curve = quad_curve(s, ((s[0] + e[0]) / 2 + f * 3, (s[1] + e[1]) / 2), e, 14)
        # cel shadow on the crease's right (light comes from the upper left)
        shifted_c = [(x + 4.5 * (1 - i / 14.0), y + 0.8) for i, (x, y) in enumerate(curve)]
        wedge = AND(cv.poly_mask(curve + list(reversed(shifted_c))), lump_mask)
        lvl = SHADOW if f < 0 else DEEP_SHADOW
        if lvl not in cache:
            cache[lvl] = painted(lvl)
        cv.img.paste(cache[lvl], (0, 0), wedge)
        tapered(cv, curve, INNER_W * 1.05, 0.15, line)
    # paper tucked under at the bottom corners
    for sgn in (-1, 1):
        bx = sgn * half_width(shape, g, 0.02, sgn) * 0.92
        tapered(cv, [(bx, -1.0), (bx - sgn * 5, -9.0), (bx - sgn * 7.5, -14.0)], INNER_W, 0.2, line)
        tapered(cv, [(bx - sgn * 3, -1.0), (bx - sgn * 13, -6.0)], INNER_W * 0.85, 0.2, line)

    # ---- tape strip on the lower left: clear film catching the light
    tc = (-a * 0.50, yc + H * 0.24)
    ang = math.radians(-28)
    ca, sa = math.cos(ang), math.sin(ang)
    tl, th = a * 0.32, 7.0

    def tp(u, v):
        return (tc[0] + u * ca - v * sa, tc[1] + u * sa + v * ca)
    tape = [tp(-tl, -th / 2), tp(tl, -th / 2), tp(tl - 1.2, -th / 6), tp(tl + 0.8, th / 6), tp(tl, th / 2),
            tp(-tl, th / 2), tp(-tl + 1.2, th / 6), tp(-tl - 0.8, -th / 6)]
    film = Image.new("RGBA", cv.img.size, (255, 255, 250, 0))
    film.putalpha(AND(cv.poly_mask(tape), lump_mask).point(lambda v: int(v * 0.40)))
    cv.img.alpha_composite(film)
    gl = Canvas(size, origin)
    gl.outline([tp(-tl, -th / 2), tp(tl, -th / 2)], 0.9, closed=False, color=(255, 255, 255))
    gl.outline([tp(-tl * 0.7, -th * 0.12), tp(tl * 0.15, -th * 0.12)], 1.3, closed=False, color=(255, 255, 255))
    gl.img.putalpha(AND(gl.img.getchannel("A"), lump_mask).point(lambda v: int(v * 0.8)))
    cv.img.alpha_composite(gl.img)

    # ---- specular glint on the lit shoulder
    glint = [(-half_width(shape, g, t, -1) + 6.0, -t * H) for t in (0.50 + 0.2 * k / 11 for k in range(12))]
    tapered(cv, glint, 2.3, 0.6, tone(paper["base"], 1.1))

    # ---- neck tie (the bow sits on its front)
    ty0, ty1 = -(H - 1), -(H + g["neck_h"] + 2)
    tw = n * 1.05 + 2.5
    # a band running sideways: its edges are the tie's top and bottom
    bot = [(-tw + 2 * tw * k / 10, ty0 + 0.8 * math.sin(math.pi * k / 10)) for k in range(11)]
    topl = [(-tw + 2 * tw * k / 10, ty1 + 0.8 * math.sin(math.pi * k / 10)) for k in range(11)]
    inked(cv, topl + list(reversed(bot)), deep(rib), OUTER_W * 0.8)
    ribbon_strip(cv, topl, bot, rib, 0.0)
    cv.fill([(tw * 0.35, ty0 + 1), (tw * 0.35, ty1 + 1), (tw, ty1), (tw, ty0)], tone(rib, SHADOW))
    knot = (0.0, (ty0 + ty1) / 2 + 0.5)
    return cv, knot


# ---------------------------------------------------------------- tag, scraps, curls

def make_tag():
    cv = Canvas((44, 52), (14, 6))
    twine = (176, 128, 82)
    hole = (6.5, 15.0)
    curve = quad_curve((0, 0), (5.5, 6), hole, 10)
    tapered(cv, curve, 2.4, 2.2, deep(twine))
    tapered(cv, curve, 1.2, 1.0, twine)
    ang = math.radians(-14)
    ca, sa = math.cos(ang), math.sin(ang)
    w, h, cut = 17.0, 23.0, 5.0

    def tp(u, v):
        return (hole[0] + u * ca - v * sa, hole[1] - 3.5 + u * sa + v * ca)
    card = (252, 240, 210)
    body = [tp(-w / 2 + cut, 0), tp(w / 2 - cut, 0), tp(w / 2, cut), tp(w / 2, h), tp(-w / 2, h), tp(-w / 2, cut)]
    inked(cv, body, deep(card), OUTER_W * 0.7)
    cv.fill(body, tone(card, 0.0))
    cv.fill_intersect(body, [tp(w * 0.2, -2), tp(w, -2), tp(w, h + 2), tp(w * 0.2, h + 2)], tone(card, -0.5))
    cv.fill_intersect(body, [tp(-w, h * 0.86), tp(w, h * 0.86), tp(w, h + 2), tp(-w, h + 2)], tone(card, -0.35))
    hx, hy = cv.P(*tp(0, 3.6))
    r = 1.7 * SS
    ImageDraw.Draw(cv.img).ellipse([hx - r, hy - r, hx + r, hy + r], fill=deep(card) + (255,))
    # holly: two spiky leaves, three berries
    green = (56, 168, 66)
    for sgn in (-1, 1):
        c = tp(sgn * 3.6, 13.5)
        la = math.radians(-14 + sgn * 35)
        L, Wd = 6.4, 2.6
        top_e = [((k / 12) * L - L / 2, -Wd * math.sin(math.pi * k / 12) * (1.0 if k % 3 else 1.35)) for k in range(13)]
        bot_e = [((k / 12) * L - L / 2, Wd * math.sin(math.pi * k / 12) * (1.0 if k % 3 else 1.35)) for k in range(13)]
        leaf = top_e + list(reversed(bot_e))
        ca2, sa2 = math.cos(la), math.sin(la)
        pts = [(c[0] + x * ca2 - y * sa2 * sgn, c[1] + x * sa2 * sgn + y * ca2) for x, y in leaf]
        inked(cv, pts, deep(green), 0.5)
        cv.fill(pts, tone(green, 0.0))
        half = [(c[0] + x * ca2 - y * sa2 * sgn, c[1] + x * sa2 * sgn + y * ca2) for x, y in bot_e] + \
               [(c[0] + x * ca2, c[1] + x * sa2 * sgn) for x, y in reversed(bot_e)]
        cv.fill(half, tone(green, SHADOW))
        cv.line(pts[0], pts[12], 0.6, color=deep(green))
    red = (232, 38, 54)
    for u, v in ((-1.4, 10.6), (1.5, 10.8), (0.0, 12.6)):
        bx, by = tp(u, v)
        ring = [(bx + 1.8 * math.cos(t * math.pi / 8), by + 1.8 * math.sin(t * math.pi / 8)) for t in range(16)]
        inked(cv, ring, deep(red), 0.45)
        cv.fill(ring, tone(red, 0.0))
        cv.fill_intersect(ring, [(x + 0.8, y + 0.8) for x, y in ring], tone(red, SHADOW))
        cv.fill([(bx - 0.7 + 0.45 * math.cos(t * math.pi / 4), by - 0.7 + 0.45 * math.sin(t * math.pi / 4))
                 for t in range(8)], tone(red, 1.1))
    return cv


def make_shard(paper, k):
    rnd = random.Random(1000 + k * 17 + sum(map(ord, paper["name"])))
    r = rnd.uniform(7.5, 11.5)
    cv = Canvas((40, 40), (20, 20))
    pts = []
    m = 9
    for i in range(m):
        ang = 2 * math.pi * i / m + rnd.uniform(-0.2, 0.2)
        rr = r * rnd.uniform(0.6, 1.0)
        pts.append((rr * math.cos(ang), rr * math.sin(ang) * 0.85))
        rz = rr * rnd.uniform(0.62, 0.8)  # torn edge: a zag between corners
        pts.append((rz * math.cos(ang + math.pi / m), rz * math.sin(ang + math.pi / m) * 0.85))
    inked(cv, pts, deep(paper["base"]), OUTER_W * 0.7)
    shade = [(1, -30), (30, -30), (30, 30), (-3, 30)]
    if k % 3 == 2:
        cv.fill(pts, tone(PAPER_BACK, 0.0))
        cv.fill_intersect(pts, shade, tone(PAPER_BACK, SHADOW))
    else:
        cv.img.paste(pattern_image(cv, paper, 0.0, None, seed=k), (0, 0), cv.poly_mask(pts))
        cv.img.paste(pattern_image(cv, paper, SHADOW, None, seed=k), (0, 0), AND(cv.poly_mask(pts), cv.poly_mask(shade)))
    return cv


def make_curl(paper):
    rib = paper["ribbon"]
    cv = Canvas((44, 30), (22, 15))
    pts = [(-16 + 32 * t, 5.5 * math.sin(t * 3 * math.pi) * (0.6 + 0.4 * t)) for t in (k / 39 for k in range(40))]
    cv.outline(pts, 3.6 + OUTER_W * 1.3, closed=False, color=deep(rib))
    cv.outline(pts, 3.6, closed=False, color=tone(rib, 0.0))
    cv.outline([(x + 0.4, y + 0.9) for x, y in pts[2:-2]], 1.3, closed=False, color=tone(rib, -0.55))
    cv.outline([(x, y - 0.8) for x, y in pts[4:-4]], 1.0, closed=False, color=tone(rib, 0.85))
    return cv


# ---------------------------------------------------------------- main

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    images, info, knots = {}, {}, {}

    def add(key, cv):
        im, c = S.crop(cv)
        images[key] = im
        info[key] = c

    for paper in PAPERS:
        for shape in SHAPES:
            key = "Body/%s_%s" % (paper["name"], shape["name"])
            cv, knot = make_body(paper, shape)
            add(key, cv)
            knots[key] = knot
        add("Bow/" + paper["name"], S.make_bow(dict(bow=27, ribbon=paper["ribbon"])))
        for k in range(SHARDS_PER_PAPER):
            add("Shard/%s_%d" % (paper["name"], k), make_shard(paper, k))
        add("Curl/" + paper["name"], make_curl(paper))
    add("Tag", make_tag())
    for p in ("Sparkle", "Snow"):
        add("Puff/" + p, S.make_puff(p))

    size, pos = S.pack(images)
    atlas = Image.new("RGBA", size, S.OUTLINE + (0,))
    for k, im in images.items():
        atlas.paste(im, pos[k])
    S.bleed(atlas)
    atlas.save(ATLAS_PATH)

    def part(k):
        x, y = pos[k]
        im = images[k]
        cx, cy = info[k]
        return dict(rect=[x, size[1] - y - im.height, im.width, im.height], center=[round(cx, 2), round(-cy, 2)])

    def knot_of(p, s):
        kx, ky = knots["Body/%s_%s" % (p["name"], s["name"])]
        return [round(kx, 2), round(-ky, 2)]

    config = dict(
        note="Generated by ArtSource/GiftWrap/giftwrap.py; do not edit by hand. Units: rig px, y up. center = quad "
             "centre relative to the piece's pivot (body: ground point = bottom centre of the lump; bow/tag: the knot "
             "on the neck tie; scraps: their middle). rect = atlas texels (x, y from bottom, w, h).",
        atlasSize=list(size),
        shapes=[dict(name=s["name"], lumpWidth=s["w"], lumpHeight=s["h"]) for s in SHAPES],
        papers=[dict(name=p["name"], bow=part("Bow/" + p["name"]), curl=part("Curl/" + p["name"]),
                     shards=[part("Shard/%s_%d" % (p["name"], k)) for k in range(SHARDS_PER_PAPER)]) for p in PAPERS],
        bodies=[dict(paper=p["name"], shape=s["name"], body=part("Body/%s_%s" % (p["name"], s["name"])),
                     knot=knot_of(p, s)) for p in PAPERS for s in SHAPES],
        tag=part("Tag"),
        sparkle=part("Puff/Sparkle"),
        snow=part("Puff/Snow"),
    )
    with open(CONFIG_PATH, "w", newline="\n") as f:
        json.dump(config, f, indent=1)

    # previews
    cell_w, cell_h = 170, 190
    prev = Image.new("RGBA", (cell_w * len(SHAPES), cell_h * len(PAPERS)), (214, 232, 214, 255))
    for j, p in enumerate(PAPERS):
        for i, s in enumerate(SHAPES):
            gx, gy = i * cell_w + cell_w // 2, (j + 1) * cell_h - 16
            sh = Image.new("RGBA", prev.size, (0, 0, 0, 0))
            ImageDraw.Draw(sh).ellipse([gx - s["w"] * 0.6, gy - 7, gx + s["w"] * 0.6, gy + 7], fill=(40, 60, 40, 70))
            prev.alpha_composite(sh)
            key = "Body/%s_%s" % (p["name"], s["name"])
            kx, ky = knots[key]
            for k, ox, oy in ((key, 0, 0), ("Tag", kx + 3, ky), ("Bow/" + p["name"], kx, ky)):
                im = images[k]
                cx, cy = info[k]
                prev.alpha_composite(im, (int(round(gx + ox + cx - im.width / 2)), int(round(gy + oy + cy - im.height / 2))))
    prev.resize((prev.width * 2, prev.height * 2), Image.NEAREST).save(os.path.join(OUT_DIR, "preview.png"))
    keys = [k for k in images if k.startswith(("Shard", "Curl", "Puff", "Tag"))]
    sheet = Image.new("RGBA", (48 * 10, 48 * ((len(keys) + 9) // 10)), (214, 232, 214, 255))
    for i, k in enumerate(keys):
        im = images[k]
        sheet.alpha_composite(im, ((i % 10) * 48 + (48 - im.width) // 2, (i // 10) * 48 + (48 - im.height) // 2))
    sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save(os.path.join(OUT_DIR, "scraps.png"))
    print("atlas", size, "->", ATLAS_PATH)


if __name__ == "__main__":
    main()
