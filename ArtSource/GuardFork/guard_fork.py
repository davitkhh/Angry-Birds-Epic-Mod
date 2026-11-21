"""Cel-shaded fork head for MainHand_PigGuardFork (pig_minion_guard_fork).

Keeps the original handle and rope wrap and redraws the blurry, photo-like fork
head in game style: dark outline, flat base, bottom shadow band, crisp top-edge
highlights (palette from the metal wrench MainHand_Mechanic).

The silhouette follows the original: the body edges and each tine's centre and
width are measured from the original alpha and fitted with smooth curves, so
the perspective (upward-curving, fanned tines) is preserved. The tied part
runs under the rope with the original's thin, top-aligned shape; the rope pixels are separated
from the old grey fork by colour and laid back on top.

Length: STICK_CUT shortens the wooden stick (middle squashed, grip end kept)
and moves the rope wrap left with it; HEAD_SHIFT moves the shoulder and tines
left. The metal handle between them is STICK_CUT - HEAD_SHIFT longer than the
original neck. The canvas stays 231x48 (right edge left empty).

usage: python3 guard_fork.py <original MainHand_PigGuardFork.png> <out.png>
"""
import colorsys, sys
import math, statistics
from PIL import Image, ImageDraw, ImageFilter, ImageChops

SS = 8
W, H = 231, 48
ROPE = (119, 144)       # columns of the rope wrap (original pixels re-used)
TIE_END = 141          # left of this the fork is hidden by the rope: flat metal
STICK_CUT = 40          # wooden stick this much shorter (rope moves left with it)
HEAD_SHIFT = 24         # shoulder + tines moved this much left
HANDLE_EXTRA = STICK_CUT - HEAD_SHIFT   # metal handle vs the original neck
GRIP_KEEP = 8           # stick's rounded butt end, left untouched by the squash
HEAD_X = 140            # original pixels right of this are the old fork head
# tied part, traced from the original: grey shows between the rope loops only
# along the top (rows 23-26), so it runs flush with the neck's top edge and
# tapers to a thin end at x~126, above the stick (whose red shows below it)
TANG_TOP = 23.0
TANG_BOTTOM = [(126.0, 24.6), (128.5, 25.3), (133.0, 27.0), (137.0, 28.2),
               (140.0, 31.2), (146.0, None)]
GAP_ROOTS = (184.0, 186.5, 189.5)  # where each tine gap starts in the original

OUTLINE = (10, 42, 70)
BASE = (176, 198, 214)
SHADOW = (112, 138, 170)
DEEP = (92, 104, 146)
HILITE = (236, 246, 248)
TIE_METAL = (200, 216, 228)


def runs(alpha, x, thr=110):
    out, start = [], None
    for y in range(H + 1):
        v = y < H and alpha[y][x] > thr
        if v and start is None:
            start = y
        if not v and start is not None:
            out.append((start, y))      # [start, end)
            start = None
    return out


def fit(xs, ys, deg):
    """Least-squares polynomial (normal equations, Gauss-Jordan); returns f(x)."""
    n = deg + 1
    x0 = sum(xs) / len(xs)                     # centre x for conditioning
    A = [[0.0] * (n + 1) for _ in range(n)]
    for x, y in zip(xs, ys):
        p = [(x - x0) ** k for k in range(n)]
        for i in range(n):
            for j in range(n):
                A[i][j] += p[i] * p[j]
            A[i][n] += p[i] * y
    for i in range(n):
        piv = A[i][i]
        A[i] = [v / piv for v in A[i]]
        for r in range(n):
            if r != i:
                f = A[r][i]
                A[r] = [a - f * b for a, b in zip(A[r], A[i])]
    c = [A[i][n] for i in range(n)]
    return lambda x: sum(ck * (x - x0) ** k for k, ck in enumerate(c))


def measure(alpha):
    """Fitted body edges + 4 tines from the original alpha."""
    bx, btop, bbot = [], [], []
    for x in range(144, 187):
        r = runs(alpha, x)
        if r:
            bx.append(x); btop.append(r[0][0]); bbot.append(r[-1][1])
    body_top = fit(bx, btop, 3)
    body_bot = fit(bx, bbot, 3)

    tines = [[] for _ in range(4)]
    for x in range(189, 231):
        r = runs(alpha, x)
        if len(r) == 4:
            for i, (a, b) in enumerate(r):
                tines[i].append((x, (a + b) / 2, (b - a) / 2))
        elif len(r) in (2, 3):          # lower tines outlast the upper ones
            for i, (a, b) in enumerate(r):
                tines[4 - len(r) + i].append((x, (a + b) / 2, (b - a) / 2))
    fitted = []
    tips = [222.5, 225.5, 228.0, 230.0]
    for t, tip in zip(tines, tips):
        xs = [p[0] for p in t if p[0] < tip - 2]
        cs = [p[1] for p in t if p[0] < tip - 2]
        hs = [p[2] for p in t if p[0] < tip - 2]
        fitted.append((fit(xs, cs, 2), statistics.median(hs), tip))
    return body_top, body_bot, fitted


def build_mask(body_top, body_bot, tines):
    m = Image.new('L', (W * SS, H * SS), 0)
    d = ImageDraw.Draw(m)
    s = SS

    # tied part under the rope (shape traced from the original)
    bottom = [(x if y is None else x - HANDLE_EXTRA, body_bot(x) if y is None else y)
              for x, y in TANG_BOTTOM]
    tip_x, tip_y = bottom[0]
    # top edge runs straight on to x=149, past the dip in the fitted neck curve
    pts = [(tip_x + 0.6, TANG_TOP), (149, TANG_TOP), (149, body_bot(149))] + bottom[::-1]
    pts.append((tip_x - 0.4, (TANG_TOP + tip_y) / 2))
    d.polygon([(x * s, y * s) for x, y in pts], fill=255)

    X0 = 180.0                          # tines start (hidden inside the shoulder)

    def half(i, x):
        c, hw, tip = tines[i]
        t = max(0.0, (x - X0) / (tip - X0))
        w = hw * (1.04 - 0.22 * t)
        if t > 0.86:
            k = (t - 0.86) / 0.14
            w *= math.sqrt(max(0.0, 1 - k * k))
        return w

    # neck + shoulder, blending into the outer edges of the first/last tine
    JOIN = (184.0, 194.0)
    xs = [144 + i * 0.25 for i in range(int((JOIN[1] - 144) * 4) + 1)]
    top, bot = [], []
    for x in xs:
        bt, bb = body_top(min(x, JOIN[0])), body_bot(min(x, JOIN[0]))
        if x > JOIN[0]:
            k = (x - JOIN[0]) / (JOIN[1] - JOIN[0])
            k = k * k * (3 - 2 * k)
            bt += (tines[0][0](x) - half(0, x) - bt) * k
            bb += (tines[3][0](x) + half(3, x) - bb) * k
        top.append((x, bt)); bot.append((x, bb))
    d.polygon([(x * s, y * s) for x, y in top + bot[::-1]], fill=255)

    # tines: taper slightly, rounded tips
    for i, (c, hw, tip) in enumerate(tines):
        n = 48
        T, B = [], []
        for j in range(n + 1):
            x = (JOIN[1] - 0.5) + (tip - JOIN[1] + 0.5) * j / n
            w = half(i, x)
            T.append((x, c(x) - w)); B.append((x, c(x) + w))
        d.polygon([(x * s, y * s) for x, y in T + B[::-1]], fill=255)

    # carve the gaps between tines with rounded roots (roots from the original)
    for i, root in enumerate(GAP_ROOTS):
        cu, cl = tines[i][0], tines[i + 1][0]
        U = [(x, cu(x) + half(i, x)) for x in [root + j * 0.25 for j in range(int((232 - root) * 4))]]
        L = [(x, cl(x) - half(i + 1, x)) for x, _ in U]
        r = (L[0][1] - U[0][1]) / 2
        if r <= 0.2:
            continue
        cy = (L[0][1] + U[0][1]) / 2
        d.polygon([(x * s, y * s) for x, y in U + L[::-1]], fill=0)
        d.ellipse([(root - r) * s, (cy - r) * s, (root + r) * s, (cy + r) * s], fill=0)
    return m


def shifted(mask, dx, dy):
    return ImageChops.offset(mask, int(round(dx * SS)), int(round(dy * SS)))


def erode(mask, px):
    return mask.filter(ImageFilter.MinFilter(int(px * SS) | 1))


def dilate(mask, px):
    return mask.filter(ImageFilter.MaxFilter(int(px * SS) | 1))


def render_fork(mask):
    # outline straddles the traced edge so the silhouette keeps its size
    outer = dilate(mask, 1.0)
    inner = erode(mask, 1.1)
    shadow = ImageChops.subtract(inner, shifted(inner, 0, -2.6))
    deep = ImageChops.subtract(inner, shifted(inner, 0, -1.1))
    hl_band = ImageChops.subtract(erode(inner, 0.6), shifted(erode(inner, 0.6), 0, 1.1))
    # keep highlights off the very tips and the tang under the rope
    clip = Image.new('L', mask.size, 0)
    ImageDraw.Draw(clip).rectangle([(TIE_END - HANDLE_EXTRA + 2) * SS, 0, 222 * SS, H * SS], fill=255)
    hl = ImageChops.multiply(hl_band, clip)

    layer = Image.new('RGBA', mask.size, (0, 0, 0, 0))
    def paint(col, m):
        layer.paste(Image.new('RGBA', mask.size, col + (255,)), (0, 0), m)
    # under the rope the tied part is only ~3px tall and peeks through the
    # loops: give it flat light metal (as in the original), no outline/shadow
    tie = Image.new('L', mask.size, 0)
    ImageDraw.Draw(tie).rectangle([0, 0, (TIE_END - HANDLE_EXTRA) * SS - 1, H * SS], fill=255)
    not_tie = ImageChops.invert(tie)
    paint(OUTLINE, ImageChops.multiply(outer, not_tie))
    paint(BASE, ImageChops.multiply(inner, not_tie))
    paint(SHADOW, ImageChops.multiply(shadow, not_tie))
    paint(DEEP, ImageChops.multiply(ImageChops.multiply(deep, shadow), not_tie))
    paint(HILITE, hl)
    paint(TIE_METAL, ImageChops.multiply(mask, tie))
    return layer.resize((W, H), Image.LANCZOS)


def classify(p):
    r, g, b, a = p
    if a == 0:
        return 'clear'
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    if s < 0.18:
        return 'grey'
    if h < 0.055 or h > 0.9:
        return 'stick'
    return 'rope'


def main(src, out):
    orig = Image.open(src).convert('RGBA')
    A = orig.getchannel('A').load()
    alpha = [[A[x, y] for x in range(W)] for y in range(H)]
    body_top, body_bot, tines = measure(alpha)
    fork = render_fork(build_mask(body_top, body_bot, tines))

    stick = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    rope = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    sp, rp, op = stick.load(), rope.load(), orig.load()
    for y in range(H):
        for x in range(HEAD_X):
            p = op[x, y]
            if x < ROPE[0]:
                sp[x, y] = p
                continue
            kind = classify(p)
            if kind == 'stick':
                sp[x, y] = p
            elif kind == 'rope':
                rp[x, y] = p
    for y in range(H):          # rope wrap reaches a bit past HEAD_X
        for x in range(HEAD_X, ROPE[1]):
            if classify(op[x, y]) == 'rope':
                rp[x, y] = op[x, y]

    # shorter stick: squash its middle (taper and grain stay continuous),
    # keep the butt end, slide the part under the rope left with the rope
    mid = stick.crop((GRIP_KEEP, 0, ROPE[0], H))
    mid = mid.resize((ROPE[0] - GRIP_KEEP - STICK_CUT, H), Image.LANCZOS)
    short = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    short.paste(stick.crop((0, 0, GRIP_KEEP, H)), (0, 0))
    short.paste(mid, (GRIP_KEEP, 0))
    short.alpha_composite(stick.crop((ROPE[0], 0, HEAD_X, H)), (ROPE[0] - STICK_CUT, 0))
    stick = short
    rope = rope.transform(rope.size, Image.AFFINE, (1, 0, STICK_CUT, 0, 1, 0))
    fork = fork.transform(fork.size, Image.AFFINE, (1, 0, HEAD_SHIFT, 0, 1, 0))

    res = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    res.alpha_composite(stick)
    res.alpha_composite(fork)
    res.alpha_composite(rope)
    res.save(out)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
