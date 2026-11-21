"""Two small brass bells hanging off an iron ring on the Dark Cupid scythe (MainHand_DarkCupid), drawn at runtime by
Assets/Scripts/Assembly-CSharp/ScytheBells.cs so they can swing and ring.

Writes
  Assets/Resources/ScytheBells.png   one small atlas: ring, big/small bell, big/small clapper, "ding" mark
  Assets/Resources/ScytheBellsConfig.json  where each piece is in the atlas + its pivot, and where the ring sits on the scythe
  out/preview.png                    the pieces on the scythe at rest (8x), for checking by eye

Units: rig px (1 px of the game's HR art). The atlas holds TEX texels per rig px.
Palette: bells = the approved gold recipe (ArtSource/GoldTeeth/GOLD_TOOTH_STYLE.md: lit #FFE254 top-left, body
#FAB61E, shade #C4700A bottom-right, warm dark outline); ring + clappers = the scythe blade's own greys.

Run: python3 -B ArtSource/ScytheBells/bells.py
"""
import json
import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
SCYTHE = os.path.join(ROOT, 'Assets', 'content', '4_Characters', '2_Pigs', '1_MinionPig', 'BodyParts', 'MainHand_DarkCupid.png')
OUT_PNG = os.path.join(ROOT, 'Assets', 'Resources', 'ScytheBells.png')
OUT_JSON = os.path.join(ROOT, 'Assets', 'Resources', 'ScytheBellsConfig.json')

TEX = 2           # atlas texels per rig px
SS = 8            # supersampling per texel
K = TEX * SS      # hi-res px per rig px

LIT = (255, 226, 84)
BODY = (250, 182, 30)
SHADE = (196, 112, 10)
DEEP = (150, 78, 6)
OUTLINE = (92, 40, 6)
GLINT = (255, 248, 214)

# Bell metals. The recording (bell_ring.wav) is the reference, taken as brass (the original gold-looking bells).
# For one bell shape, f ~ sqrt(E / density) (the metal's speed of sound), so a bell's pitch = its size factor times
# sqrt((E/rho) / (E_ref/rho_ref)). Mass ~ density (swing inertia, bell-on-bell knocks, how loud a knock is).
# 'decay' = how long its ring lasts against brass (internal damping): bell bronze rings longest (why bells are cast in
# it), sterling silver is softer and dies sooner. E in GPa, density in kg/m^3 (handbook values for the alloys).
# Surface: 'roughness' 0 (mirror polish) .. 1 (rough cast): the art's highlight (polished = hard cel bands and a sharp
# glint; rough = soft matte gradients, cast pitting) and the friction of the hanger loop on the ring. Polished spun brass
# and polished sterling are smooth; a bell-bronze bell is left as cast (satin, pitted). 'patina' 0..1: tarnish in the
# recesses (bronze's dull green-brown). 'restitution': bounce of the iron clapper off this metal, from its hardness
# (bell bronze ~200 HB is hard and rebounds most, brass ~100 HB, sterling ~70 HB the least).
MATERIALS = [
    {'name': 'Brass', 'label': 'Brass', 'suffix': '', 'youngs': 110.0, 'density': 8530.0, 'decay': 1.0,
     'roughness': 0.15, 'patina': 0.0, 'restitution': 0.35},
    {'name': 'Silver', 'label': 'Silver (sterling)', 'suffix': '_Silver', 'youngs': 76.0, 'density': 10360.0, 'decay': 0.75,
     'roughness': 0.08, 'patina': 0.0, 'restitution': 0.28},
    {'name': 'Bronze', 'label': 'Bronze (bell metal)', 'suffix': '_Bronze', 'youngs': 104.0, 'density': 8770.0, 'decay': 1.35,
     'roughness': 0.6, 'patina': 0.55, 'restitution': 0.45},
]
PATINA = (86, 120, 98)
REFERENCE = 'Brass'
PALETTES = {
    'Brass': dict(LIT=LIT, BODY=BODY, SHADE=SHADE, DEEP=DEEP, OUTLINE=OUTLINE, GLINT=GLINT),
    'Silver': dict(LIT=(250, 252, 255), BODY=(206, 212, 222), SHADE=(142, 150, 166), DEEP=(100, 106, 124),
                   OUTLINE=(46, 50, 64), GLINT=(255, 255, 255)),
    'Bronze': dict(LIT=(242, 184, 120), BODY=(205, 127, 64), SHADE=(150, 84, 38), DEEP=(108, 56, 24),
                   OUTLINE=(64, 30, 10), GLINT=(255, 232, 200)),
}
# amplitude decay time of the recorded strikes (s), measured on their envelopes (-47 dB/s, T60 ~1.27 s)
SOUND_DECAY = 0.184

IRON_OUT = (58, 50, 50)
IRON = (126, 118, 118)
IRON_LIT = (170, 162, 162)
IRON_SHADE = (90, 82, 82)

# The scythe texture (124x141) and where its pivot sits (MinionPig_Atlas_02 defaultPivotOffsets for MainHand_DarkCupid).
SCYTHE_SIZE = (124, 141)
SCYTHE_PIVOT = (-0.27321428, -0.14840426)

# Iron ring under the blade's collar (scythe image px, y down). Its top passes behind the handle.
RING_CENTRE = (90.5, 133.8)
RING_R = 3.5      # wire centre line
RING_WIRE = 1.7   # wire thickness without outline
RING_OUTLINE = 0.75
RING_BOX = (82, 124, 100, 143)   # crop of scythe space the ring cell covers

# Bell profile (rig px, pivot = top inside of the hanger loop, y down). (half-width, y) down the right side.
BELL_SIDE = [(0.0, 2.5), (2.3, 2.6), (3.7, 3.2), (4.5, 4.4), (5.0, 6.4), (5.4, 9.2), (6.0, 11.8), (7.0, 13.9),
             (8.3, 15.4), (9.3, 16.4)]
LIP_Y = 16.4       # where the flare ends and the lip band starts
LIP_BOTTOM = 18.5  # lowest point of the lip (centre)
LIP_RY = 1.0       # the lip's bottom edge bows down this much at the middle
LOOP_C = (0.0, 1.25)
LOOP_R_OUT = 2.0
LOOP_R_IN = 1.05
CLAPPER_PIVOT = (0.0, 4.6)   # inside the crown
CLAPPER_ROD = 13.4
CLAPPER_BALL = 2.0
SMALL = 0.64                 # small bell scale
OUTLINE_W = 1.05

# Strike sounds: each bell plays one strike cut out of this recording (one bell, struck ~40 times). Onsets found with
# ArtSource/ScytheBells/strikes.py: these two have the longest clean decay before the recording's next strike.
# Pitch: bells of one shape ring at a frequency inversely proportional to their size, so the small bell plays 1/SMALL.
SOUND_CLIP = 'Sound/Characters/Generic/bell_ring'
STRIKES = [(2.4981, 2.9462), (4.8830, 5.3310)]   # (start, end) seconds: big bell, small bell


def hires(w, h):
    return Image.new('RGBA', (int(round(w * K)), int(round(h * K))), (0, 0, 0, 0))


def mask(w, h):
    return Image.new('L', (int(round(w * K)), int(round(h * K))), 0)


def P(p, o):
    return ((p[0] + o[0]) * K, (p[1] + o[1]) * K)


def fill(img, m, colour):
    layer = Image.new('RGBA', img.size, colour + (255,))
    img.paste(layer, (0, 0), m)


def dilate(m, r):
    size = max(3, int(round(r * K)) * 2 + 1)
    return m.filter(ImageFilter.MaxFilter(size))


def down(img):
    return img.resize((img.width // SS, img.height // SS), Image.BOX)


def finish(img, shape, body, rough, patina, seed):
    """A metal's surface on a drawn bell (before its outline): rough metal loses the hard cel bands and the sharp glint
    to soft broad gradients and gets cast pitting; patina tints the shaded recesses."""
    import random
    rnd = random.Random(seed)
    if rough >= 0.3:
        base = Image.new('RGBA', img.size, BODY + (255,))
        base.alpha_composite(img)
        soft = base.filter(ImageFilter.GaussianBlur(rough * 0.75 * K))
        # a matte surface also flattens contrast toward the body colour
        soft = Image.blend(soft, Image.new('RGBA', img.size, BODY + (255,)), rough * 0.12)
        img.paste(soft, (0, 0), shape)
    if rough > 0.2:
        # pitting at the texel scale: dark pits, a few bright grains
        tw, th = img.width // SS, img.height // SS
        dark = Image.new('L', (tw, th), 0)
        lite = Image.new('L', (tw, th), 0)
        dp, lp = dark.load(), lite.load()
        for y in range(th):
            for x in range(tw):
                r = rnd.random()
                if r < rough * 0.07:
                    dp[x, y] = int(255 * rnd.uniform(0.18, 0.38))
                elif r > 1 - rough * 0.03:
                    lp[x, y] = int(255 * rnd.uniform(0.15, 0.3))
        dark = ImageChops.multiply(dark.resize(img.size, Image.NEAREST), body)
        lite = ImageChops.multiply(lite.resize(img.size, Image.NEAREST), body)
        img.paste(Image.new('RGBA', img.size, DEEP + (255,)), (0, 0), dark)
        img.paste(Image.new('RGBA', img.size, LIT + (255,)), (0, 0), lite)
    if patina > 0:
        # tarnish where the light doesn't reach: the shaded side, the shoulder groove, under the lip
        lum = img.convert('L')
        cut = (SHADE[0] * 299 + SHADE[1] * 587 + SHADE[2] * 114) // 1000 + 12
        recess = lum.point(lambda v: 255 if v <= cut else 0)
        recess = recess.filter(ImageFilter.GaussianBlur(0.6 * K))
        tw, th = img.width // SS, img.height // SS
        blot = Image.new('L', (tw, th), 0)
        bp = blot.load()
        for y in range(th):
            for x in range(tw):
                bp[x, y] = int(255 * patina * rnd.uniform(0.35, 1.0))
        blot = blot.resize(img.size, Image.BILINEAR)
        tint = ImageChops.multiply(ImageChops.multiply(recess, blot), body)
        img.paste(Image.new('RGBA', img.size, PATINA + (255,)), (0, 0), tint)


def bell(scale, metal=None):
    """Returns (texture, pivot in texels, size in rig px) for a bell of the given scale."""
    s = scale
    w = 2 * (9.6 * s + OUTLINE_W + 0.6)
    h = (LIP_BOTTOM + 0.2) * s + OUTLINE_W + 0.8 + (OUTLINE_W + 0.6)
    w = math.ceil(w)
    h = math.ceil(h)
    o = (w / 2.0, OUTLINE_W + 0.6)   # pivot position in the cell
    side = [(x * s, y * s) for x, y in BELL_SIDE]
    lip_y = LIP_Y * s
    bottom = LIP_BOTTOM * s
    half = side[-1][0]
    # lip: from the flare's end, a short vertical band then a bowed bottom edge
    arc = []
    for i in range(21):
        t = i / 20.0
        x = half * (1 - 2 * t)
        # bowed: centre at `bottom`, ends at bottom - LIP_RY*s
        y = bottom - LIP_RY * s * (1 - 2 * t) ** 2
        arc.append((x, y))
    outline_poly = side + arc + [(-x, y) for x, y in reversed(side)]
    body = mask(w, h)
    d = ImageDraw.Draw(body)
    d.polygon([P(p, o) for p in outline_poly], fill=255)
    loop = mask(w, h)
    dl = ImageDraw.Draw(loop)
    lc = (LOOP_C[0] * s, LOOP_C[1] * s)
    ro, ri = LOOP_R_OUT * max(s, 0.8), LOOP_R_IN * max(s, 0.8)
    dl.ellipse([P((lc[0] - ro, lc[1] - ro), o), P((lc[0] + ro, lc[1] + ro), o)], fill=255)
    hole = mask(w, h)
    ImageDraw.Draw(hole).ellipse([P((lc[0] - ri, lc[1] - ri), o), P((lc[0] + ri, lc[1] + ri), o)], fill=255)
    shape = ImageChops.lighter(body, loop)
    shape = ImageChops.subtract(shape, hole)
    out_m = ImageChops.subtract(dilate(shape, OUTLINE_W), shape)
    # the loop's hole keeps an inner outline too
    hole_ring = ImageChops.subtract(hole, ImageChops.subtract(hole, dilate(ImageChops.subtract(shape, hole), OUTLINE_W * 0.7)))
    out_m = ImageChops.lighter(out_m, ImageChops.multiply(hole_ring, hole))
    img = hires(w, h)
    fill(img, shape, BODY)

    def band(points, colour, clip=body):
        m = mask(w, h)
        ImageDraw.Draw(m).polygon([P(p, o) for p in points], fill=255)
        fill(img, ImageChops.multiply(m, clip), colour)

    def ys(t):
        return side[0][1] + t * (lip_y - side[0][1])

    def hw(y):
        for (x0, y0), (x1, y1) in zip(side, side[1:]):
            if y0 <= y <= y1:
                return x0 + (x1 - x0) * (y - y0) / max(1e-6, y1 - y0)
        return side[-1][0]

    rows = [ys(i / 30.0) for i in range(31)]
    # shade: the right third, widening down the flare
    band([(hw(y) * 0.30 + 0.25 * s, y) for y in rows] + [(hw(y) + 1, y) for y in reversed(rows)], SHADE)
    band([(hw(y) * 0.74, y) for y in rows] + [(hw(y) + 1, y) for y in reversed(rows)], DEEP)
    # lit stripe on the left
    band([(-hw(y) * 0.78, y) for y in rows] + [(-hw(y) * 0.40, y) for y in reversed(rows)], LIT)
    # lip: a band of its own, lit along its top edge, darker underneath
    lip_top = [(x, y - 1.55 * s) for x, y in arc]
    m = mask(w, h)
    ImageDraw.Draw(m).polygon([P(p, o) for p in [(half + 2, bottom + 3), (-half - 2, bottom + 3)] + [(x, y) for x, y in reversed(lip_top)]], fill=255)
    lip_m = ImageChops.multiply(m, body)
    fill(img, lip_m, BODY)
    band([(x * 0.98, y - 1.55 * s) for x, y in arc] + [(x * 0.98, y - 1.05 * s) for x, y in reversed(arc)], LIT, lip_m)
    band([(x, y - 0.45 * s) for x, y in arc] + [(x, y + 2) for x, y in reversed(arc)], SHADE, lip_m)
    band([(x, y - 1.6 * s) for x, y in arc if x > half * 0.45] + [(half + 2, bottom + 3)], SHADE, lip_m)
    # a thin groove around the shoulder
    gy = 6.0 * s
    band([(-hw(gy) - 1, gy), (hw(gy) + 1, gy), (hw(gy + 0.55 * s) + 1, gy + 0.55 * s), (-hw(gy + 0.55 * s) - 1, gy + 0.55 * s)], SHADE)
    band([(-hw(gy) - 1, gy - 0.5 * s), (hw(gy) * 0.2, gy - 0.5 * s), (hw(gy) * 0.2, gy), (-hw(gy) - 1, gy)], LIT)
    # the hanger loop: lit top-left, shaded bottom-right
    lm = ImageChops.subtract(loop, hole)
    sh = mask(w, h)
    ImageDraw.Draw(sh).polygon([P((lc[0] + 3, lc[1] - 3), o), P((lc[0] + 3, lc[1] + 3), o), P((lc[0] - 3, lc[1] + 3), o)], fill=255)
    fill(img, ImageChops.multiply(lm, sh), SHADE)
    # glint
    g = mask(w, h)
    gx, gyy = -hw(8.0 * s) * 0.58, 8.6 * s
    ImageDraw.Draw(g).ellipse([P((gx - 0.55 * s, gyy - 1.6 * s), o), P((gx + 0.55 * s, gyy + 1.6 * s), o)], fill=255)
    fill(img, ImageChops.multiply(g, body), GLINT)
    if metal:
        finish(img, ImageChops.subtract(shape, hole), body, metal.get('roughness', 0), metal.get('patina', 0),
               '%s/%s' % (metal['name'], scale))
    fill(img, out_m, OUTLINE)
    pivot = (o[0] * TEX, o[1] * TEX)
    return down(img), pivot, (w, h)


def clapper(scale):
    s = scale
    r = CLAPPER_BALL * max(s, 0.75)
    rod = CLAPPER_ROD * s
    w = math.ceil(2 * (r + OUTLINE_W + 0.5))
    h = math.ceil(rod + r + 2 * (OUTLINE_W + 0.5) + 0.5)
    o = (w / 2.0, OUTLINE_W + 0.5)
    shape = mask(w, h)
    d = ImageDraw.Draw(shape)
    d.rectangle([P((-0.5, 0), o), P((0.5, rod), o)], fill=255)
    d.ellipse([P((-r, rod - r), o), P((r, rod + r), o)], fill=255)
    out_m = ImageChops.subtract(dilate(shape, OUTLINE_W * 0.8), shape)
    img = hires(w, h)
    fill(img, shape, IRON)
    sh = mask(w, h)
    ImageDraw.Draw(sh).ellipse([P((-r * 0.35, rod - r * 0.35), o), P((r * 1.6, rod + r * 1.6), o)], fill=255)
    fill(img, ImageChops.multiply(shape, sh), IRON_SHADE)
    li = mask(w, h)
    ImageDraw.Draw(li).ellipse([P((-r * 0.7, rod - r * 0.75), o), P((-r * 0.05, rod - r * 0.1), o)], fill=255)
    fill(img, ImageChops.multiply(shape, li), IRON_LIT)
    fill(img, out_m, IRON_OUT)
    return down(img), (o[0] * TEX, o[1] * TEX), (w, h)


def ring(scythe):
    x0, y0, x1, y1 = RING_BOX
    w, h = x1 - x0, y1 - y0
    o = (-x0, -y0)
    c = RING_CENTRE
    ro = RING_R + RING_WIRE / 2
    ri = RING_R - RING_WIRE / 2
    shape = mask(w, h)
    d = ImageDraw.Draw(shape)
    d.ellipse([P((c[0] - ro, c[1] - ro), o), P((c[0] + ro, c[1] + ro), o)], fill=255)
    d.ellipse([P((c[0] - ri, c[1] - ri), o), P((c[0] + ri, c[1] + ri), o)], fill=0)
    out_m = ImageChops.subtract(dilate(shape, RING_OUTLINE), shape)
    img = hires(w, h)
    fill(img, shape, IRON)
    # lit upper-left arc, shaded lower-right arc
    for colour, a0, a1 in ((IRON_LIT, 150, 260), (IRON_SHADE, -40, 80)):
        m = mask(w, h)
        ImageDraw.Draw(m).pieslice([P((c[0] - ro - 1, c[1] - ro - 1), o), P((c[0] + ro + 1, c[1] + ro + 1), o)], a0, a1, fill=255)
        fill(img, ImageChops.multiply(shape, m), colour)
    fill(img, out_m, IRON_OUT)
    # the top passes behind the handle: cut it where the scythe is opaque
    crop = scythe.crop((x0, y0, x1, y1)).resize((w * K, h * K), Image.BILINEAR).split()[3]
    cover = crop.point(lambda a: 255 if a > 140 else 0)
    a = img.split()[3]
    img.putalpha(ImageChops.subtract(a, cover))
    return down(img)


def ring_plain():
    """The same iron ring, whole (for bells placed anywhere in the Combatant Forge)."""
    ro = RING_R + RING_WIRE / 2
    w = h = math.ceil(2 * (ro + RING_OUTLINE + 0.5))
    c = (w / 2.0, h / 2.0)
    o = (0, 0)
    ri = RING_R - RING_WIRE / 2
    shape = mask(w, h)
    d = ImageDraw.Draw(shape)
    d.ellipse([P((c[0] - ro, c[1] - ro), o), P((c[0] + ro, c[1] + ro), o)], fill=255)
    d.ellipse([P((c[0] - ri, c[1] - ri), o), P((c[0] + ri, c[1] + ri), o)], fill=0)
    out_m = ImageChops.subtract(dilate(shape, RING_OUTLINE), shape)
    img = hires(w, h)
    fill(img, shape, IRON)
    for colour, a0, a1 in ((IRON_LIT, 150, 260), (IRON_SHADE, -40, 80)):
        m = mask(w, h)
        ImageDraw.Draw(m).pieslice([P((c[0] - ro - 1, c[1] - ro - 1), o), P((c[0] + ro + 1, c[1] + ro + 1), o)], a0, a1, fill=255)
        fill(img, ImageChops.multiply(shape, m), colour)
    fill(img, out_m, IRON_OUT)
    return down(img), (c[0] * TEX, c[1] * TEX), (w, h)


def ding():
    """A short curved 'ring' stroke (bright, outlined) drawn beside a bell when it rings."""
    w, h = 6, 11
    o = (0.0, 0.0)
    shape = mask(w, h)
    d = ImageDraw.Draw(shape)
    cx, cy, r = -3.0, 5.5, 6.6
    d.arc([P((cx - r, cy - r), o), P((cx + r, cy + r), o)], -48, 48, fill=255, width=int(1.3 * K))
    out_m = ImageChops.subtract(dilate(shape, 0.65), shape)
    img = hires(w, h)
    fill(img, shape, GLINT)
    fill(img, out_m, OUTLINE)
    return down(img), (0, h * TEX / 2.0), (w, h)


def main():
    scythe = Image.open(SCYTHE).convert('RGBA')
    assert scythe.size == SCYTHE_SIZE, scythe.size
    pieces = {}
    pieces['Ring'] = (ring(scythe), None, (RING_BOX[2] - RING_BOX[0], RING_BOX[3] - RING_BOX[1]))
    for m in MATERIALS:
        globals().update(PALETTES[m['name']])
        pieces['BellLarge' + m['suffix']] = bell(1.0, m)
        pieces['BellSmall' + m['suffix']] = bell(SMALL, m)
    globals().update(PALETTES['Brass'])
    pieces['ClapperLarge'] = clapper(1.0)
    pieces['ClapperSmall'] = clapper(SMALL)
    pieces['Ding'] = ding()
    pieces['RingPlain'] = ring_plain()
    # shelf pack into a 128-wide atlas
    W = 128
    x = y = row = 0
    rects = {}
    for name in ['Ring', 'BellLarge', 'BellSmall', 'ClapperLarge', 'ClapperSmall', 'Ding', 'RingPlain'] + \
            ['Bell%s%s' % (size, m['suffix']) for m in MATERIALS if m['suffix'] for size in ('Large', 'Small')]:
        im = pieces[name][0]
        if x + im.width + 2 > W:
            x, y, row = 0, y + row + 2, 0
        rects[name] = (x + 1, y + 1)
        x += im.width + 2
        row = max(row, im.height)
    H = 1
    while H < y + row + 2:
        H *= 2
    atlas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    sprites = []
    for name, (im, pivot, size) in pieces.items():
        px, py = rects[name]
        atlas.paste(im, (px, py))
        # rect in UV-friendly texels with y up (Unity's texture origin is bottom-left)
        entry = {
            'name': name,
            'rect': [px, H - py - im.height, im.width, im.height],
            'size': [im.width / float(TEX), im.height / float(TEX)],
            'pivot': [pivot[0] / TEX, pivot[1] / TEX] if pivot else [0, 0],
        }
        sprites.append(entry)
    atlas.save(OUT_PNG)
    # scythe art space -> sprite space: art pixel (x, y down) sits at (x - w/2, h/2 - y) - pivot * size
    sw, sh = SCYTHE_SIZE
    cx = -SCYTHE_PIVOT[0] * sw
    cy = -SCYTHE_PIVOT[1] * sh

    def sprite_space(p):
        return [round(p[0] - sw / 2.0 + cx, 3), round(sh / 2.0 - p[1] + cy, 3)]

    config = {
        'atlas': 'ScytheBells',
        'texel': TEX,
        'sprites': sprites,
        'weapons': [{
            'builtin': True,
            'items': ['pig_weapon_minion_cupid_dark_bells_01'],
            'texture': '',
            'slot': 'MainHand',
            'ring': 'Ring',
            'on': ['Always'],
            'cover': '',
            'coverPivot': [0, 0],
            # ring cell's top-left corner and the ring's centre, in the slot's sprite space (rig px, y up)
            'ringCorner': sprite_space((RING_BOX[0], RING_BOX[1])),
            'ringCentre': sprite_space(RING_CENTRE),
            # bells ride on the ring's inner edge
            'hangRadius': round(RING_R - RING_WIRE / 2, 3),
        }] + forge_entries(),
        # Combatant Forge (Tools/CombatantForge/bells.py) presets: where the ring goes on art it knows, in that art's
        # space (px from its centre, y up), and which ring sprite suits it there
        'presets': [{'art': 'MainHand_DarkCupid', 'ring': 'Ring',
                     'ringCentre': [round(RING_CENTRE[0] - sw / 2.0, 3), round(sh / 2.0 - RING_CENTRE[1], 3)]}],
        'bells': [
            {'sprite': 'BellLarge', 'clapper': 'ClapperLarge', 'scale': 1.0, 'clapperPivot': list(CLAPPER_PIVOT),
             'clapperLength': CLAPPER_ROD, 'centreOfMass': 10.5, 'mouth': 9.3, 'lip': LIP_BOTTOM, 'pitch': 1.0,
             'soundClip': SOUND_CLIP, 'soundStart': STRIKES[0][0], 'soundEnd': STRIKES[0][1], 'soundDecay': SOUND_DECAY,
             'material': REFERENCE},
            {'sprite': 'BellSmall', 'clapper': 'ClapperSmall', 'scale': SMALL,
             'clapperPivot': [CLAPPER_PIVOT[0] * SMALL, CLAPPER_PIVOT[1] * SMALL],
             'clapperLength': round(CLAPPER_ROD * SMALL, 3), 'centreOfMass': round(10.5 * SMALL, 3),
             'mouth': round(9.3 * SMALL, 3), 'lip': round(LIP_BOTTOM * SMALL, 3), 'pitch': round(1.0 / SMALL, 4),
             'soundClip': SOUND_CLIP, 'soundStart': STRIKES[1][0], 'soundEnd': STRIKES[1][1], 'soundDecay': SOUND_DECAY,
             'material': REFERENCE},
        ],
        # the bell metals ('suffix' picks the bell sprites; the recording is REFERENCE)
        'reference': REFERENCE,
        'materials': MATERIALS,
    }
    with open(OUT_JSON, 'w', newline='\n') as f:
        json.dump(config, f, indent=1)
        f.write('\n')
    preview(scythe, pieces)
    print('atlas', W, H, {k: rects[k] for k in rects})


def forge_entries():
    """Entries the Combatant Forge wrote (not builtin): kept when the art is regenerated."""
    try:
        with open(OUT_JSON) as f:
            return [w for w in json.load(f).get('weapons') or [] if not w.get('builtin') and w.get('texture')]
    except (OSError, ValueError):
        return []


def preview(scythe, pieces):
    os.makedirs(os.path.join(HERE, 'out'), exist_ok=True)
    Z = 8
    x0, y0, x1, y1 = 56, 92, 124, 172
    canvas = Image.new('RGBA', ((x1 - x0) * TEX, (y1 - y0) * TEX), (236, 232, 222, 255))
    sc = scythe.resize((scythe.width * TEX, scythe.height * TEX), Image.BILINEAR)
    canvas.alpha_composite(sc, (-x0 * TEX, -y0 * TEX))
    canvas.alpha_composite(pieces['Ring'][0], ((RING_BOX[0] - x0) * TEX, (RING_BOX[1] - y0) * TEX))
    hang = RING_R - RING_WIRE / 2
    for name, clap, scale, ang in (('BellLarge', 'ClapperLarge', 1.0, -11), ('BellSmall', 'ClapperSmall', SMALL, 13)):
        im, pivot, _ = pieces[name]
        cim, cpivot, _ = pieces[clap]
        a = math.radians(ang)
        px = RING_CENTRE[0] + math.sin(-a) * -hang
        py = RING_CENTRE[1] + math.cos(a) * hang
        layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
        cx = (px - x0) * TEX + (CLAPPER_PIVOT[0] * scale) * TEX
        cy = (py - y0) * TEX + (CLAPPER_PIVOT[1] * scale) * TEX
        layer.alpha_composite(cim, (int(round(cx - cpivot[0])), int(round(cy - cpivot[1]))))
        layer.alpha_composite(im, (int(round((px - x0) * TEX - pivot[0])), int(round((py - y0) * TEX - pivot[1]))))
        layer = layer.rotate(ang, center=((px - x0) * TEX, (py - y0) * TEX), resample=Image.BICUBIC)
        canvas.alpha_composite(layer)
    d, _, _ = pieces['Ding']
    canvas.alpha_composite(d, (int((RING_CENTRE[0] - x0 + 12) * TEX), int((RING_CENTRE[1] - y0 + 8) * TEX)))
    canvas.resize((canvas.width * Z // TEX, canvas.height * Z // TEX), Image.NEAREST).save(os.path.join(HERE, 'out', 'preview.png'))
    canvas.save(os.path.join(HERE, 'out', 'preview_1x.png'))


if __name__ == '__main__':
    main()
