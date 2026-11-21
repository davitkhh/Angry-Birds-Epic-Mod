"""Cactus Knight gear with prickly-pear fruit growing on it (for a later second phase).

Paints round cel-shaded fruit bulbs (dark outline, flat base, right-side shadow band, top-left
highlight, small navel on top, little glochid tufts) onto the three Cactus Knight pieces, along the
outline and on the face at the spine clusters (user's concept sketch, 2026-11-14). Spines are
layered per fruit: one rooted under a fruit stays behind it, one rooted nearer the viewer goes in front and writes
<name>Fruit.png next to the originals in PigKnight BodyParts. Headgear_CactusKnight_Back is the
helmet's inside and gets no fruit (pack.py copies it as Headgear_CactusKnightFruit_Back).

Fruit that sticks out past the original canvas grows the canvas; the padding per sprite is
written to pads.json so pack.py can keep the original art at the same place on the bone.

Growth stages (STAGES): Fruit, Bud (buds popping out of the fruit tops, crown fruit a bit bigger) and Bloom
(open cup flowers on every fruit, crown fruit bigger); each writes <name><Stage>.png. Flower colours: yellow
on magenta fruit, orange on red (FLOWERS).

usage: python3 cactus_fruit.py          (then python3 pack.py)
"""
import json
import math
import os
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
BODYPARTS = os.path.join(ROOT, 'Assets/content/4_Characters/2_Pigs/2_PigKnight/BodyParts')
SS = 8
FRUIT_SCALE = 1.15   # length/width multiplier for every fruit below
PEEKING_ROOT = 'front'  # spine behind a fruit but its root peeks out beside it: 'front' = draw that spine over
                        # the fruit (root, socket and cast shadow then make sense); 'erase' = paint the stub away
STUB_REACH = 6  # px: a spine piece showing beside a fruit it's behind, no further out than this, is removed
SPINE_DEPTH_MARGIN = 3  # px further in from the outline than a fruit's base for a spine to go in front of it

PALETTES = {
    # outline, shadow, base, light, specular, navel
    'magenta': ((86, 14, 48), (150, 30, 86), (206, 52, 118), (243, 120, 168), (255, 214, 230), (120, 22, 66)),
    'red': ((92, 18, 30), (170, 40, 46), (222, 70, 64), (250, 140, 112), (255, 222, 206), (132, 28, 36)),
}
GLOCHID = (246, 220, 140)
GLOCHID_DARK = (150, 104, 54)
DOT_JITTER = 0.05  # how far (fraction of the fruit) each glochid dot strays from its row slot
BOTTOM_BLEND = 0.08  # lowest part of each fruit (fraction of its length) that melts into the cactus
BOTTOM_OUTLINE = 0.4  # how strongly the outline round the bottom turns cactus-dark
BOTTOM_TINT = 0.2    # how much of the cactus green that part takes on
BASE_TINT = 0.18   # how strongly a fruit's base fades toward the cactus green
RICH_STRENGTH = 0.75  # contrast of the bigger fruits' extra tones (1 = the first, fuller palette)
# growth stages: each writes <name><suffix>.png. crown = size multiplier for the crown fruit.
STAGES = {
    'Fruit': dict(crown=1.0, flower=None),
    'Bud': dict(crown=1.12, flower='bud'),      # mid-point: buds popping out of the fruit tops
    'Bloom': dict(crown=1.25, flower='bloom'),  # every fruit has flowered, crown fruit bigger
}
STAGE = 'Fruit'
# flower colours per fruit palette: outline, shadow, base, light, heart (petal base), stamens, stamen light
FLOWERS = {
    'magenta': ((150, 88, 14), (232, 168, 34), (252, 214, 60), (255, 242, 150), (238, 128, 36), (226, 170, 40), (255, 236, 140)),
    'red': ((140, 50, 16), (232, 116, 38), (252, 166, 58), (255, 214, 130), (214, 62, 40), (236, 186, 52), (255, 238, 150)),
}
STIGMA = ((40, 90, 30), (120, 180, 60), (180, 225, 110))  # outline, base, light
RICH_AREA = 300  # length * width (before FRUIT_SCALE) from which a fruit gets the richer palette + bigger dots


def mix(a, b, k):
    return tuple(int(round(x + (y - x) * k)) for x, y in zip(a, b))

# per sprite: list of fruit (base x, base y, angle deg clockwise from up, length, width, palette, shape)
# base = where the fruit grows out of the pad (an edge or a spine cluster), in original-sprite px
# opts (optional 8th field): front=True keeps this fruit in front of every spine it overlaps
#   (a peeking root is painted away), extra_dots=N adds N glochid dots, jitter=x overrides DOT_JITTER,
#   crown=True grows with the stage (STAGES), rich_top=True gives the top + navel more tones, tilt=deg leans the top toward the camera (shorter fruit, navel slides down onto the face and opens up)
# shape = key of SHAPES (variety: round / egg / pear / squat / lopsided / barrel / lumpy)
FRUIT = {
    'Headgear_CactusKnight': [
        (112, 30, -4, 34, 34, 'magenta', 'pear', {'extra_dots': 4, 'jitter': 0.02, 'rich_top': True, 'crown': True}),  # big one on the crown
        (56, 49, -40, 25, 22, 'red', 'barrel'),         # top-left edge (nudged up so the spine in front only clips its corner)
        (170, 45, 35, 24, 22, 'red', 'egg'),            # top-right edge
        (201, 66, 62, 17, 22, 'magenta', 'squat'),      # right side (angle = outline normal there)
        (93, 84, -10, 20, 22, 'magenta', 'lumpy'),      # on the face, upper-left spine
        (159, 96, 12, 21, 23, 'magenta', 'round', {'front': True}),  # on the face, in front of the big right spine
    ],
    'MainHand_CactusKnight': [
        (176, 20, 60, 16, 17, 'magenta', 'egg'),        # upper arm tip
        (190, 66, 85, 23, 23, 'red', 'pear'),           # main stem tip (middle)
        (126, 98, 150, 15, 16, 'magenta', 'lopsided'),  # lower arm tip
    ],
    'OffHand_CactusKnight': [
        (67, 20, 0, 14, 16, 'red', 'egg'),              # top spine
        (90, 42, 25, 14, 16, 'magenta', 'lumpy'),       # upper-right cluster
        (63, 52, 128, 12, 16, 'red', 'squat'),          # centre cluster (angle = average of bottom 175 + right edge 80)
        (110, 80, 80, 13, 15, 'magenta', 'round'),      # right edge
        (62, 90, 175, 15, 15, 'magenta', 'pear'),       # bottom cluster
    ],
}

# c = where along the fruit it is widest (0 base .. 1 top), r = how far the bulge reaches,
# asym = one side fuller than the other, bend = centreline curves sideways, wob = lumps, top = cap height
SHAPES = {
    'round':    dict(c=0.50, r=0.56, asym=0.0, bend=0.0, wob=0.0, top=0.13),
    'egg':      dict(c=0.60, r=0.64, asym=0.0, bend=0.04, wob=0.0, top=0.16),
    'pear':     dict(c=0.64, r=0.68, asym=0.04, bend=0.0, wob=0.0, top=0.30),
    'squat':    dict(c=0.46, r=0.52, asym=0.0, bend=0.0, wob=0.0, top=0.08),
    'lopsided': dict(c=0.55, r=0.60, asym=0.16, bend=0.12, wob=0.0, top=0.12),
    'barrel':   dict(c=0.50, r=0.66, asym=0.0, bend=0.0, wob=0.0, top=0.17),
    'lumpy':    dict(c=0.52, r=0.58, asym=0.05, bend=-0.05, wob=0.07, top=0.12),
}


class Shape:
    def __init__(self, name, L, Wd):
        self.s, self.L, self.Wd = SHAPES[name], L, Wd

    def axis(self, t):
        """Centreline offset (across) at t."""
        return self.s['bend'] * self.Wd * math.sin(math.pi * t) * t

    def half(self, t, side):
        """Half-width at t on side +1 (right) / -1 (left)."""
        s = self.s
        b = math.sqrt(max(0.0, 1 - ((t - s['c']) / s['r']) ** 2))
        b *= 1 + s['wob'] * math.sin(3 * math.pi * t + (0.8 if side > 0 else 2.1))
        b *= 1 + side * s['asym'] * math.sin(math.pi * t)
        return self.Wd / 2 * b


def frame(bx, by, ang):
    a = math.radians(ang)
    d = (math.sin(a), -math.cos(a))   # along the fruit (up when ang = 0)
    p = (math.cos(a), math.sin(a))    # across, to the fruit's right

    def to(u, v):
        return (bx + v * d[0] + u * p[0], by + v * d[1] + u * p[1])
    return to


def outline_pts(to, sh, grow=0.0, n=48):
    L, Wd = sh.L, sh.Wd
    pts = []
    for i in range(n + 1):
        t = i / n
        pts.append(to(sh.axis(t) + sh.half(t, 1) + grow, t * L))
    # rounded top cap
    for i in range(1, 12):
        k = i / 12
        c = math.cos(math.pi * k)
        u = sh.axis(1) + c * ((sh.half(1, 1) if c > 0 else sh.half(1, -1)) + grow)
        pts.append(to(u, L + grow * 0.8 + Wd * sh.s['top'] * math.sin(math.pi * k)))
    for i in range(n, -1, -1):
        t = i / n
        pts.append(to(sh.axis(t) - sh.half(t, -1) - grow, t * L))
    # rounded bottom
    for i in range(1, 8):
        k = i / 8
        c = -math.cos(math.pi * k)
        u = c * ((sh.half(0, 1) if c > 0 else sh.half(0, -1)) + grow)
        pts.append(to(u, -grow * 0.6 - Wd * 0.12 * math.sin(math.pi * k)))
    return pts


def sspts(pts, ox, oy):
    return [((x + ox) * SS, (y + oy) * SS) for x, y in pts]


def ellipse_pts(to, cu, cv, ru, rv, n=40):
    return [to(cu + ru * math.cos(2 * math.pi * i / n), cv + rv * math.sin(2 * math.pi * i / n)) for i in range(n)]


def draw_fruit(size, ox, oy, fruit, green=(70, 150, 40)):
    """Returns (fruit RGBA layer, contact-shadow mask) at SS, canvas size in original px.
    green = the cactus colour where the fruit grows (base tint + bottom blend)."""
    bx, by, ang, L, Wd, pal, shape = fruit[:7]
    opts = fruit[7] if len(fruit) > 7 else {}
    tilt = math.radians(opts.get('tilt', 0))
    grow = STAGES[STAGE]['crown'] if opts.get('crown') else 1.0
    L, Wd = L * FRUIT_SCALE * math.cos(tilt) * grow, Wd * FRUIT_SCALE * grow
    o, sh, base, li, spec, nav = PALETTES[pal]
    S = Shape(shape, L, Wd)
    ax = S.axis
    to = frame(bx, by, ang)
    W, H = size[0] * SS, size[1] * SS
    layer = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.polygon(sspts(outline_pts(to, S, grow=1.5), ox, oy), fill=o + (255,))
    body = Image.new('L', (W, H), 0)
    ImageDraw.Draw(body).polygon(sspts(outline_pts(to, S), ox, oy), fill=255)

    def clipped(pts, colour):
        m = Image.new('L', (W, H), 0)
        ImageDraw.Draw(m).polygon(sspts(pts, ox, oy), fill=255)
        layer.paste(colour + (255,), (0, 0), ImageChops.multiply(m, body))

    clipped(outline_pts(to, S), base)
    # shadow crescent: the fruit minus itself shifted toward the light (top-left, as in the gear art)
    dx, dy = -0.26 * Wd * SS, -0.2 * Wd * SS
    lit = body.transform(body.size, Image.AFFINE, (1, 0, -dx, 0, 1, -dy))
    rich = fruit[3] * fruit[4] >= RICH_AREA

    def shifted(k, sx=-1, sy=-1):
        return body.transform(body.size, Image.AFFINE, (1, 0, -sx * k * 0.26 * Wd * SS, 0, 1, -sy * k * 0.2 * Wd * SS))

    def crescent(k):
        return ImageChops.multiply(body, ImageChops.invert(shifted(k)))
    if rich:
        # mid-tone glow toward the light before the shadow goes on
        clipped(ellipse_pts(to, ax(0.62) - S.half(0.62, -1) * 0.2, L * 0.6, S.half(0.62, -1) * 0.75, L * 0.38), mix(base, li, RICH_STRENGTH * 0.45))
    layer.paste(sh + (255,), (0, 0), ImageChops.multiply(body, ImageChops.invert(lit)))
    clipped(ellipse_pts(to, 0, -L * 0.05, Wd * 0.8, L * 0.2), sh)
    if rich:
        # deeper core shadow, then a thin reflected-light rim on the shadow edge
        layer.paste(mix(sh, o, RICH_STRENGTH * 0.45) + (255,), (0, 0), crescent(0.5 * RICH_STRENGTH))
        rim = ImageChops.multiply(body, ImageChops.invert(shifted(0.13, 1, 1)))
        rim = ImageChops.multiply(rim, crescent(0.7))
        layer.paste(mix(sh, base, RICH_STRENGTH * 0.45) + (255,), (0, 0), rim)
    # ripening from the top: the base fades smoothly toward a fresh cactus green (over the shading)
    band = Image.new('L', (W, H), 0)
    ImageDraw.Draw(band).polygon(sspts([to(Wd * u, 0.3 * L + Wd * 0.05 * (1 - (u / 1.2) ** 2)) for u in [i / 10 - 1.2 for i in range(25)]]
                                      + [to(Wd * 1.2, -Wd), to(-Wd * 1.2, -Wd)], ox, oy), fill=int(255 * BASE_TINT))
    band = band.filter(ImageFilter.GaussianBlur(L * 0.12 * SS))
    tint = Image.new('RGBA', (W, H), mix(green, (190, 225, 90), 0.3) + (0,))
    tint.putalpha(ImageChops.multiply(band, body))
    layer.alpha_composite(tint)
    # light band down the left
    clipped(ellipse_pts(to, ax(0.6) - S.half(0.6, -1) * 0.4, L * 0.6, S.half(0.6, -1) * 0.36, L * 0.24), li)
    # navel: dark dimple on top with a lit lower lip
    st = math.sin(tilt)
    nv = L + Wd * S.s['top'] * 0.6 - Wd * 0.4 * st
    if opts.get('rich_top'):
        # lit shoulder top-left, shaded rim top-right, before the navel goes on
        clipped(ellipse_pts(to, ax(0.9) - Wd * 0.2, L * 0.93, Wd * 0.17, Wd * 0.09), mix(base, li, 0.55))
        clipped(ellipse_pts(to, ax(0.9) - Wd * 0.24, L * 0.95, Wd * 0.07, Wd * 0.035), mix(li, spec, 0.4))
        clipped(ellipse_pts(to, ax(0.95) + Wd * 0.3, L * 0.97, Wd * 0.14, Wd * 0.1), mix(sh, base, 0.45))
    nw, nh = Wd * 0.2, Wd * (0.08 + 0.1 * st)
    clipped(ellipse_pts(to, ax(1), nv, nw, nh), o)
    if opts.get('rich_top'):
        # lit lower lip, navel bowl, deep core, catch-light on the far wall
        clipped(ellipse_pts(to, ax(1), nv - nh * 0.35, nw * 0.92, nh * 0.7), mix(base, li, 0.35))
        clipped(ellipse_pts(to, ax(1) + Wd * 0.01, nv + nh * 0.05, nw * 0.78, nh * 0.62), nav)
        clipped(ellipse_pts(to, ax(1) + Wd * 0.03, nv + nh * 0.18, nw * 0.5, nh * 0.36), mix(nav, o, 0.55))
        clipped(ellipse_pts(to, ax(1) - nw * 0.3, nv + nh * 0.4, nw * 0.22, nh * 0.18), mix(nav, li, 0.45))
    else:
        clipped(ellipse_pts(to, ax(1) + Wd * 0.02, nv - Wd * 0.01, Wd * 0.13, Wd * (0.045 + 0.07 * st)), nav)
    # specular
    clipped(ellipse_pts(to, ax(0.72) - S.half(0.72, -1) * 0.44, L * 0.72, Wd * 0.06, L * 0.08), spec)
    # glochid tufts: staggered rows, jittered a little (seeded per fruit, so stable between runs),
    # each a tiny spiky star
    rnd = random.Random('%d,%d' % (bx, by))
    spots = [(0.3, -0.25), (0.3, 0.12), (0.52, -0.05), (0.52, 0.3), (0.74, -0.3), (0.74, 0.12)]
    spots += [(0.42, 0.34), (0.64, -0.12), (0.4, -0.38), (0.86, 0.1), (0.62, 0.36), (0.88, -0.22)][:opts.get('extra_dots', 0)]
    for tv, uu in spots:
        jit = opts.get('jitter', DOT_JITTER)
        tv += rnd.uniform(-jit, jit)
        uu += rnd.uniform(-jit, jit)
        u = ax(tv) + uu * 1.6 * (S.half(tv, 1) if uu > 0 else S.half(tv, -1)) * 2
        x, y = to(u, tv * L)
        r = (1.4 if rich else 1.05) * SS * rnd.uniform(0.85, 1.15)
        cx, cy = (x + ox) * SS, (y + oy) * SS
        dark = u - ax(tv) > Wd * 0.05
        rot = rnd.uniform(0, 2 * math.pi)
        star = []
        for kk in range(10):
            rr = r * (1.55 if kk % 2 == 0 else 0.62)
            a = rot + math.pi * kk / 5
            star.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
        m = Image.new('L', (W, H), 0)
        ImageDraw.Draw(m).polygon(star, fill=255)
        layer.paste((GLOCHID_DARK if dark else GLOCHID) + (255,), (0, 0), ImageChops.multiply(m, body))
        if not dark:
            r2 = (0.6 if rich else 0.45) * SS
            ImageDraw.Draw(m := Image.new('L', (W, H), 0)).ellipse([cx - r2 + SS * 0.3, cy - r2 + SS * 0.3, cx + r2 + SS * 0.3, cy + r2 + SS * 0.3], fill=255)
            layer.paste(GLOCHID_DARK + (255,), (0, 0), ImageChops.multiply(m, body))
    # round bottom blends into the cactus: the lowest part takes on the local cactus green and the
    # outline there turns cactus-dark, so the fruit reads as growing out of it (no lip, nothing cut)
    low = Image.new('L', (W, H), 0)
    ImageDraw.Draw(low).polygon(sspts([to(-Wd * 2, BOTTOM_BLEND * L), to(Wd * 2, BOTTOM_BLEND * L), to(Wd * 2, -Wd * 2), to(-Wd * 2, -Wd * 2)], ox, oy), fill=255)
    low = low.filter(ImageFilter.GaussianBlur(L * 0.07 * SS))
    full = layer.getchannel('A')
    ring = ImageChops.subtract(full, body)
    layer.paste(mix(green, (10, 60, 30), 0.5) + (255,), (0, 0), ImageChops.multiply(ImageChops.multiply(ring, low), Image.new('L', (W, H), int(255 * BOTTOM_OUTLINE))))
    tint = Image.new('RGBA', (W, H), green + (0,))
    tint.putalpha(ImageChops.multiply(ImageChops.multiply(low, body), Image.new('L', (W, H), int(255 * BOTTOM_TINT))))
    layer.alpha_composite(tint)
    # contact shadow around the stem end, cast on the pad
    shadow = Image.new('L', (W, H), 0)
    ImageDraw.Draw(shadow).polygon(sspts(ellipse_pts(to, Wd * 0.15, -1, (S.half(0.15, 1) + S.half(0.15, -1)) * 0.75, Wd * 0.45), ox, oy), fill=255)
    shadow = shadow.filter(ImageFilter.GaussianBlur(SS * 1.2))
    # flower stage: bud / open flower on the fruit's top
    kind = STAGES[STAGE]['flower']
    if kind:
        fd = ImageDraw.Draw(layer)
        for poly, colour in flower_shapes(to, S, pal, kind):
            fd.polygon(sspts(poly, ox, oy), fill=colour + (255,))
    return layer, shadow


def petal(to, base, ang, length, width, grow=0.0, n=24, flare=0.0, point=0.025, ruffle=0.0, seed=0.0, start=0.0, end=1.0):
    """Cactus-flower petal polygon (fruit-local frame): obovate - narrow claw at the base, widest about
    two-thirds out, rounded end with a small point (point = fraction of length), optional ruffled edge.
    ang = deg off the fruit axis, flare bends the tip outward (deg). start/end cut a band of the petal
    (for the heart / tip colour bands)."""
    def at(s):
        a = math.radians(ang + flare * s * s)
        du, dv = math.sin(a), math.cos(a)
        return base[0] + du * length * s * (1 - 0.15 * s * s * abs(flare) / 90), base[1] + dv * length * s, du, dv

    def hw(s):
        if point > 0.1:      # pointed (bud) petal: even taper to the tip
            h = (0.3 + 0.7 * min(1.0, s / 0.45) ** 0.8) * (1.0 if s < 0.45 else max(0.0, (1 - s) / 0.55) ** 0.9)
        else:                # broad obovate petal, rounded end
            h = 0.28 + 0.72 * min(1.0, s / 0.66) ** 0.85
            if s > 0.66:
                h *= math.sqrt(max(0.0, 1 - ((s - 0.66) / 0.34) ** 2))
        h *= 1 + ruffle * math.sin(s * math.pi * 6 + seed)
        return width / 2 * h + grow

    ss = [start + (end - start) * i / n for i in range(n + 1)]
    right, left = [], []
    for s in ss:
        cu, cv, du, dv = at(s)
        h = hw(s)
        right.append((cu + dv * h, cv - du * h))
        left.append((cu - dv * h, cv + du * h))
    local = right
    if end >= 1.0 and point <= 0.1:
        cu, cv, du, dv = at(1.0)
        local = right + [(cu + du * (length * point + grow * 0.6), cv + dv * (length * point + grow * 0.6))]
    local = local + left[::-1]
    return [to(u, v) for u, v in local]


def flower_shapes(to, S, pal, kind):
    """[(polygon in sprite px, colour)] in paint order for a prickly-pear bud or open flower on top of a fruit,
    cel-shaded like the gear: outline in a deep shade of the part, flat bands, one shadow side, light tips."""
    if not kind:
        return []
    o, sh, base, li, heart, stam, stam_li = FLOWERS[pal]
    Wd, L = S.Wd, S.L
    top = (S.axis(1), L + Wd * S.s['top'] * 0.35)
    seed0 = sum(to(0, 0))
    out = []
    small = Wd < 22

    def oval(cu, cv, ru, rv, n=24):
        return [to(cu + ru * math.cos(2 * math.pi * i / n), cv + rv * math.sin(2 * math.pi * i / n)) for i in range(n)]

    def add_petal(ang, length, width, fill, flare=0.0, side=1, root=None, point=0.025, tip=True, vein=False, k=0, heart_end=0.3):
        b = root or top
        rf = 0.0 if small else 0.025
        sd = seed0 + k * 1.7
        out.append((petal(to, b, ang, length, width, grow=1.0, flare=flare, point=point, ruffle=rf, seed=sd), o))
        out.append((petal(to, b, ang, length, width, flare=flare, point=point, ruffle=rf, seed=sd), fill))
        # warm heart at the claw, a mid step, then lighter (thin, sunlit) tips
        out.append((petal(to, b, ang, length, width * 0.94, flare=flare, ruffle=rf, seed=sd, end=heart_end), heart))
        if tip:
            out.append((petal(to, b, ang, length, width * 0.86, flare=flare, point=point, ruffle=rf, seed=sd, start=0.72), mix(fill, li, 0.7)))
        # one shaded side (the petal's fold)
        off = width * 0.24 * side
        out.append((petal(to, (b[0] + off, b[1]), ang + 3 * side, length * 0.9, width * 0.34, flare=flare, ruffle=rf, seed=sd, start=0.3, end=0.92), sh))

    if kind == 'bud':
        # pointed cone of petals twisted together, tips just loosening; greenish scales at the foot
        bl, bw = Wd * 1.0, Wd * 0.44
        twist = ((-9, -6, 0.86, mix(base, sh, 0.35)), (9, 6, 0.86, mix(base, sh, 0.2)), (-4, 3, 0.97, base), (4, -3, 1.0, base))
        for kk, (ang, fl, ln, col) in enumerate(twist):
            add_petal(ang, bl * ln, bw, col, flare=fl, side=1 if ang > 0 else -1, point=0.2, k=kk)
        g_o, g_b, g_l = (40, 100, 34), (110, 172, 58), (170, 214, 96)
        tipc = (196, 92, 52)
        for kk, ang in enumerate((-40, 40, -14, 14)):
            r = (top[0], top[1] - Wd * 0.06)
            ln = Wd * (0.34 if abs(ang) > 20 else 0.4)
            out.append((petal(to, r, ang, ln, Wd * 0.2, grow=1.0, point=0.2), g_o))
            out.append((petal(to, r, ang, ln, Wd * 0.2, point=0.2), g_b if abs(ang) > 20 else g_l))
            out.append((petal(to, r, ang, ln, Wd * 0.18, point=0.2, start=0.78), tipc))
        return out

    # open bowl, petals rooted round the rim (an ellipse seen from the side, slightly from above):
    # back half -> petals open up and out, inner faces lit with the warm heart showing;
    # inside the rim -> stamen mound + stigma; front half -> shorter petals curl outward (outer faces).
    cu0, cv0 = top[0], top[1] + Wd * 0.3
    rw, rh = Wd * 0.34, Wd * 0.13
    pw = Wd * (0.5 if small else 0.44)
    nb = 5 if small else 7
    backs = []
    for i in range(nb):
        phi = math.pi * (0.06 + 0.88 * i / (nb - 1))           # 0..pi = back half (higher on screen)
        c = math.cos(phi)
        backs.append((abs(c), c, phi))
    for kk, (_, c, phi) in enumerate(sorted(backs)):            # centre (farthest) first
        root = (cu0 + rw * 0.8 * c, cv0 + rh * 0.8 * math.sin(phi))
        ang = c * 72
        add_petal(ang, Wd * (0.62 - 0.1 * abs(c)), pw, mix(base, li, 0.2), flare=c * 30, side=1 if c >= 0 else -1,
                  root=root, k=kk, heart_end=0.42)
    # stamen mound inside the rim
    dv = cv0 + rh * 0.9
    mw, mh = rw * 0.9, rh * 1.2
    out.append((oval(cu0, dv, mw + 1.0, mh + 1.0, 32), mix(heart, o, 0.45)))
    out.append((oval(cu0, dv, mw, mh, 32), mix(stam, heart, 0.45)))
    rnd = random.Random(int(seed0 * 10))
    nd = 9 if small else 20
    for i in range(nd):
        a = 2 * math.pi * i / nd + rnd.uniform(-0.15, 0.15)
        rr = rnd.uniform(0.45, 0.88)
        cu, cv = cu0 + mw * rr * math.cos(a), dv + mh * rr * math.sin(a) + mh * 0.25
        r = max(0.85, Wd * rnd.uniform(0.04, 0.052))
        lit = math.cos(a) < 0.4
        out.append((oval(cu, cv, r + 0.55, r + 0.55, 8), mix(stam, o, 0.45)))
        out.append((oval(cu, cv + r * 0.15, r, r, 8), stam_li if lit else stam))
    # stigma: pale style with a green star of lobes on top, standing out of the mound
    so, sb, sl = STIGMA
    st_top = dv + Wd * 0.17
    sw = max(0.9, Wd * 0.035)
    for g_, col in ((1.0, so), (0.0, mix(sb, (240, 240, 200), 0.5))):
        out.append(([to(cu0 - sw - g_, dv), to(cu0 + sw + g_, dv), to(cu0 + sw + g_, st_top), to(cu0 - sw - g_, st_top)], col))
    lobes = 5 if small else 7
    for g_, col in ((1.0, so), (0.0, sb)):
        for i in range(lobes):
            a = 2 * math.pi * i / lobes + 0.3
            out.append((oval(cu0 + math.cos(a) * Wd * 0.08, st_top + math.sin(a) * Wd * 0.045, Wd * 0.04 + g_, Wd * 0.03 + g_, 10), col))
    out.append((oval(cu0, st_top, Wd * 0.05, Wd * 0.035, 12), sb))
    out.append((oval(cu0 - Wd * 0.03, st_top + Wd * 0.02, Wd * 0.025, Wd * 0.018, 10), sl))
    # front half: petals rooted along the near rim, curling outward and down a little; centre one last
    nf = 2 if small else 4
    fronts = []
    for i in range(nf):
        phi = math.pi + math.pi * (0.12 + 0.76 * i / (nf - 1))
        c = math.cos(phi)
        fronts.append((-abs(c), c, phi))
    if not small:
        fronts.append((-0.0, 0.0, 1.5 * math.pi))
    for kk, (_, c, phi) in enumerate(sorted(fronts)):
        root = (cu0 + rw * 0.55 * c, cv0 + rh * 0.4 * math.sin(phi) - Wd * 0.1)
        ang = c * 68
        add_petal(ang, Wd * (0.26 + 0.1 * abs(c)), pw * 0.86, base, flare=c * 60, side=1 if c >= 0 else -1,
                  root=root, k=kk + 20, heart_end=0.22)
    return out


def bounds(fruit):
    bx, by, ang, L, Wd, _, shape = fruit[:7]
    opts = fruit[7] if len(fruit) > 7 else {}
    tilt = math.radians(opts.get('tilt', 0))
    grow = STAGES[STAGE]['crown'] if opts.get('crown') else 1.0
    L, Wd = L * FRUIT_SCALE * math.cos(tilt) * grow, Wd * FRUIT_SCALE * grow
    to = frame(bx, by, ang)
    S = Shape(shape, L, Wd)
    pts = outline_pts(to, S, grow=2.5)
    for poly, _ in flower_shapes(to, S, fruit[5], STAGES[STAGE]['flower']):
        pts += [(x + dx, y + dy) for x, y in poly for dx in (-2, 2) for dy in (-2, 2)]
    xs, ys = zip(*pts)
    return min(xs), min(ys), max(xs), max(ys)


def spines(src):
    """Spine components of the original art: [(pixels, root (x, y))]. Spines are the light blue-green
    cones; the root is the wide end (where it sits in the cactus). Rim highlights / specks dropped."""
    px = src.load()
    w, h = src.size
    on = set()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a > 40 and g > 185 and b > 88 and abs(r - b) < 30:
                on.add((x, y))
    out, seen = [], set()
    for s in on:
        if s in seen:
            continue
        comp, stack = [], [s]
        seen.add(s)
        while stack:
            x, y = stack.pop()
            comp.append((x, y))
            for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if q in on and q not in seen:
                    seen.add(q)
                    stack.append(q)
        if len(comp) < 25:
            continue
        n = len(comp)
        mx = sum(p[0] for p in comp) / n
        my = sum(p[1] for p in comp) / n
        sxx = sum((p[0] - mx) ** 2 for p in comp)
        syy = sum((p[1] - my) ** 2 for p in comp)
        sxy = sum((p[0] - mx) * (p[1] - my) for p in comp)
        th = 0.5 * math.atan2(2 * sxy, sxx - syy)
        ex, ey = math.cos(th), math.sin(th)
        proj = [((p[0] - mx) * ex + (p[1] - my) * ey, p) for p in comp]
        lo, hi = min(v for v, _ in proj), max(v for v, _ in proj)
        band = (hi - lo) * 0.25
        ends = [[p for v, p in proj if v <= lo + band], [p for v, p in proj if v >= hi - band]]
        wide = max(ends, key=len)
        root = (sum(p[0] for p in wide) / len(wide), sum(p[1] for p in wide) / len(wide))
        out.append((comp, root))
    return out


def depth_map(src):
    """Chamfer distance (px) from the transparent outside: how far into the cactus, i.e. how much it faces the viewer."""
    w, h = src.size
    a = src.getchannel('A').load()
    INF = 10 ** 6
    d = [[0 if a[x, y] < 128 else INF for x in range(w)] for y in range(h)]
    for y in range(h):
        for x in range(w):
            if d[y][x]:
                for dx, dy, c in ((-1, 0, 1), (0, -1, 1), (-1, -1, 1.4), (1, -1, 1.4)):
                    X, Y = x + dx, y + dy
                    d[y][x] = min(d[y][x], (d[Y][X] if 0 <= X < w and 0 <= Y < h else 0) + c)
    for y in range(h - 1, -1, -1):
        for x in range(w - 1, -1, -1):
            if d[y][x]:
                for dx, dy, c in ((1, 0, 1), (0, 1, 1), (1, 1, 1.4), (-1, 1, 1.4)):
                    X, Y = x + dx, y + dy
                    d[y][x] = min(d[y][x], (d[Y][X] if 0 <= X < w and 0 <= Y < h else 0) + c)
    return lambda x, y: d[min(h - 1, max(0, int(round(y))))][min(w - 1, max(0, int(round(x))))]


def spine_shadows(src, pad, stubs, reach=4, max_size=500):
    """The dark-green cast shadow each spine drops on the cactus (not the teal outline): the dark
    components touching (within `reach` px) the removed spine stubs. Returned in padded coords."""
    px = src.load()
    w, h = src.size
    def dark(x, y):
        r, g, b, a = px[x, y]
        return a > 200 and r < 45 and g < 80 and b < 40
    seeds = set()
    for sx, sy in stubs:
        x0, y0 = sx - pad['l'], sy - pad['t']
        for dx in range(-reach, reach + 1):
            for dy in range(-reach, reach + 1):
                x, y = x0 + dx, y0 + dy
                if 0 <= x < w and 0 <= y < h and dark(x, y):
                    seeds.add((x, y))
    out, seen = set(), set()
    for s in seeds:
        if s in seen:
            continue
        comp, stack = [], [s]
        seen.add(s)
        while stack and len(comp) <= max_size:
            x, y = stack.pop()
            comp.append((x, y))
            for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= q[0] < w and 0 <= q[1] < h and q not in seen and dark(*q):
                    seen.add(q)
                    stack.append(q)
        if len(comp) <= max_size:
            out.update((x + pad['l'], y + pad['t']) for x, y in comp)
    return out


def inpaint(im, pixels):
    """Fills the given pixels (plus their 1px anti-aliased fringe, unless that is fruit-free art we keep)
    from the surrounding image, onion-peel style in premultiplied space, so a removed spine stub
    takes on the cactus around it (or transparency outside the outline)."""
    w, h = im.size
    todo = set(pixels)
    for x, y in list(pixels):
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                q = (x + dx, y + dy)
                if 0 <= q[0] < w and 0 <= q[1] < h:
                    todo.add(q)
    px = im.convert('RGBa').load()
    vals = {}
    while todo:
        ready = {}
        for x, y in todo:
            acc, n = [0, 0, 0, 0], 0
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
                q = (x + dx, y + dy)
                if not (0 <= q[0] < w and 0 <= q[1] < h) or q in todo:
                    continue
                v = vals.get(q, px[q])
                acc = [a + b for a, b in zip(acc, v)]
                n += 1
            if n >= 2 or (n and len(todo) < 4):
                ready[(x, y)] = tuple(int(round(a / n)) for a in acc)
        if not ready:
            break
        vals.update(ready)
        todo -= set(ready)
    out = im.convert('RGBa')
    o = out.load()
    for q, v in vals.items():
        o[q] = v
    return out.convert('RGBA')


def pad_green(src, x0, y0, rad=5):
    """Average cactus skin colour around (x0, y0): no spines, outline teal, spine shadows or background."""
    px = src.load()
    w, h = src.size
    acc, n = [0, 0, 0], 0
    for x in range(int(x0) - rad, int(x0) + rad + 1):
        for y in range(int(y0) - rad, int(y0) + rad + 1):
            if 0 <= x < w and 0 <= y < h:
                r, g, b, a = px[x, y]
                spine = g > 185 and b > 88 and abs(r - b) < 30
                if a > 200 and not spine and g > 80 and b < 0.55 * g:
                    acc = [acc[0] + r, acc[1] + g, acc[2] + b]
                    n += 1
    return tuple(int(c / n) for c in acc) if n else (70, 150, 40)


def down(im):
    """SS -> 1x in premultiplied space."""
    return im.convert('RGBa').resize((im.width // SS, im.height // SS), Image.LANCZOS).convert('RGBA')


def build(name, fruits):
    src = Image.open(os.path.join(BODYPARTS, name + '.png')).convert('RGBA')
    w, h = src.size
    x0 = min([0] + [math.floor(bounds(f)[0]) for f in fruits])
    y0 = min([0] + [math.floor(bounds(f)[1]) for f in fruits])
    x1 = max([w] + [math.ceil(bounds(f)[2]) for f in fruits])
    y1 = max([h] + [math.ceil(bounds(f)[3]) for f in fruits])
    pad = {'l': -x0, 't': -y0, 'r': x1 - w, 'b': y1 - h}
    size = (x1 - x0, y1 - y0)
    out = Image.new('RGBA', size, (0, 0, 0, 0))
    out.alpha_composite(src, (pad['l'], pad['t']))
    order = sorted(fruits, key=lambda f: f[1])
    layers, shade = [], Image.new('L', (size[0] * SS, size[1] * SS), 0)
    for f in order:
        layer, sh = draw_fruit(size, pad['l'], pad['t'], f, pad_green(src, f[0], f[1]))
        layers.append(down(layer))
        shade = ImageChops.lighter(shade, sh)
    fa = [l.getchannel('A').load() for l in layers]
    # spines vs fruit: a spine whose root is under the fruit stays behind it (it grows out from under
    # the fruit); a spine rooted elsewhere and further in from the outline than the fruit's base (= nearer
    # the viewer on the dome / pad) is laid back over it. A spine behind a fruit must not show its root
    # end beside the fruit (looked like the fruit sat on the spine): that stub is painted over with the
    # surrounding cactus, so the spine seems to come out from behind the fruit.
    depth = depth_map(src)
    stubs = set()
    fronts = []
    for comp, (rx, ry) in spines(src):
        pts = [(x + pad['l'], y + pad['t']) for x, y in comp]
        keep_out = Image.new('L', size, 0)     # where an "in front" fruit must hide this spine
        hits = []
        for f, al in zip(order, fa):
            covered = [p for p in pts if al[p] > 0]
            if not covered:
                continue
            rpx = (int(round(rx)) + pad['l'], int(round(ry)) + pad['t'])
            under_fruit = any(al[min(size[0] - 1, max(0, rpx[0] + dx)), min(size[1] - 1, max(0, rpx[1] + dy))] > 0
                              for dx in (-2, 0, 2) for dy in (-2, 0, 2))
            force = len(f) > 7 and f[7].get('front')
            front = not force and not under_fruit and depth(rx, ry) > depth(f[0], f[1]) + SPINE_DEPTH_MARGIN
            hits.append((f[0], f[1], 'over' if front else 'under'))
            if not front:
                keep_prev = keep_out
                keep_out = ImageChops.lighter(keep_out, Image.frombytes('L', size, bytes(al[x, y] for y in range(size[1]) for x in range(size[0]))))
                # stubs: pieces of this spine left showing around the fruit (also under its soft edge)
                # that never get further than STUB_REACH px from it. Longer pieces are shafts coming
                # out from behind the fruit and stay.
                near = Image.frombytes('L', size, bytes(255 if al[x, y] else 0 for y in range(size[1]) for x in range(size[0])))
                near = near.filter(ImageFilter.MaxFilter(2 * STUB_REACH + 1)).load()
                loose = {p for p in pts if al[p] < 230}
                peeks = []
                seen = set()
                for s0 in loose:
                    if s0 in seen:
                        continue
                    piece, stack = [], [s0]
                    seen.add(s0)
                    while stack:
                        x, y = stack.pop()
                        piece.append((x, y))
                        for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                            if q in loose and q not in seen:
                                seen.add(q)
                                stack.append(q)
                    if all(near[q] for q in piece):
                        peeks.append(piece)
                if peeks and PEEKING_ROOT == 'front' and not force:
                    hits[-1] = (f[0], f[1], 'over')
                    keep_out = keep_prev
                else:
                    for piece in peeks:
                        stubs.update(piece)
                        print('   spine root (%d,%d): behind (%d,%d), hid %d stub px' % (rx, ry, f[0], f[1], len(piece)))
        if hits and any(hdx == 'over' for *_, hdx in hits):
            print('   spine root (%d,%d): %s' % (rx, ry, hits))
            fronts.append((pts, keep_out))
    if stubs:
        cast = spine_shadows(src, pad, stubs)
        print('   removed %d stub px + %d spine-shadow px' % (len(stubs), len(cast)))
        out = inpaint(out, stubs | cast)
    # contact shadows (only on the existing art), then every fruit, then the spines in front
    shade = shade.resize(size, Image.LANCZOS)
    # spines stand up off the surface: the fruits' contact shadows don't fall on them
    sm = Image.new('L', size, 0)
    smp = sm.load()
    for comp, _ in spines(src):
        for x, y in comp:
            smp[x + pad['l'], y + pad['t']] = 255
    shade = ImageChops.subtract(shade, sm.filter(ImageFilter.MaxFilter(3)))
    r, g, b, a = out.split()
    k = shade.point(lambda v: 255 - int(v * 0.35))
    out = Image.merge('RGBA', (ImageChops.multiply(r, k), ImageChops.multiply(g, k), ImageChops.multiply(b, k), a))
    for layer in layers:
        out.alpha_composite(layer)
    shifted = Image.new('RGBA', size, (0, 0, 0, 0))
    shifted.paste(src, (pad['l'], pad['t']))
    for pts, keep_out in fronts:
        m = Image.new('L', size, 0)
        mp = m.load()
        for p in pts:
            mp[p] = 255
        # the spine's own anti-aliased edge only (pale blue-green blends), not the cactus around it,
        # so a spine in front doesn't cut more out of the fruit than its own shape
        ring = m.filter(ImageFilter.MaxFilter(3))
        rp_, sp = ring.load(), shifted.load()
        for y in range(size[1]):
            for x in range(size[0]):
                if rp_[x, y] and not mp[x, y]:
                    r_, g_, b_, a_ = sp[x, y]
                    mp[x, y] = 140 if (a_ > 0 and g_ > 150 and b_ > 65) else 0
        m = ImageChops.subtract(m, keep_out)
        lay = Image.new('RGBA', size, (0, 0, 0, 0))
        lay.paste(shifted, (0, 0), m)
        out.alpha_composite(lay)
    out.save(os.path.join(BODYPARTS, name + STAGE + '.png'))
    return pad, size


def main():
    global STAGE
    pads = {}
    for STAGE in STAGES:
        for name, fruits in FRUIT.items():
            pad, size = build(name, fruits)
            pads[name + STAGE] = dict(pad, w=size[0], h=size[1])
            print(name + STAGE, size, pad)
    json.dump(pads, open(os.path.join(HERE, 'pads.json'), 'w'), indent=1)


if __name__ == '__main__':
    main()
