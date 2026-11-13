"""Gold-tooth recolour in the house style (see GOLD_TOOTH_STYLE.md next to this file).

Library:
    from gold_tooth import gild
    gild(target_img, seed, clean_img=None)   # recolours in place, returns the tooth mask

CLI:
    python3 ArtSource/GoldTeeth/gold_tooth.py <target.png> <x> <y> [--clean <clean.png>] [--out <out.png>]

<target.png>  sprite to paint the gold tooth into (overwritten unless --out is given)
<x> <y>       any pixel inside the tooth's pale body (sprite pixel coords, top-left origin)
--clean       same-size sprite where that tooth is still pale/white (e.g. the base sprite of
              an XPirate variant). Shape and anti-aliasing are read from it, so a tooth that
              was already recoloured (yellow, badly gilded...) can be redone cleanly. Pixels
              around the tooth that differ from --clean but aren't tooth (tint left by an
              earlier recolour) are restored from it.
"""
import sys

from PIL import Image

DARK = (48, 14, 14)          # mouth interior / outline reference for "how pale is this pixel"
FRINGE = (70, 24, 8)         # warm dark brown the gold fades into at the anti-aliased edge
G_LIGHT = (255, 226, 84)     # lit corner (top-left)
G_MID = (250, 182, 30)       # body
G_DARK = (196, 112, 10)      # shade (bottom-right) and right rim
G_HI = (255, 252, 210)       # vertical shine stripe
GLINT = (255, 255, 255)
GLINT_MIN = 9                # tooth must be at least this wide and tall to get the glint


def lum(c):
    return c[0] * .3 + c[1] * .59 + c[2] * .11


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def tval(c):
    """0 at the dark outline, 1 at a fully pale tooth pixel."""
    return max(0.0, min(1.0, (lum(c) - lum(DARK)) / (235 - lum(DARK))))


def toothish(c):
    return c[3] > 200 and max(c[:3]) - min(c[:3]) < 70 and lum(c) > lum(DARK) + 8


def is_core(c):
    return c[3] > 200 and min(c[:3]) > 180


def tooth_mask(img, seed):
    """Pale core flood-filled from seed, plus its 1px anti-aliased fringe."""
    px = img.load()
    W, H = img.size
    if not is_core(px[seed]):
        near = [(seed[0] + dx, seed[1] + dy) for r in (1, 2) for dx in range(-r, r + 1) for dy in range(-r, r + 1)]
        near = [q for q in near if 0 <= q[0] < W and 0 <= q[1] < H and is_core(px[q])]
        if not near:
            raise ValueError('no pale tooth pixel at or near %s' % (seed,))
        seed = near[0]
    core = {seed}
    st = [seed]
    while st:
        x, y = st.pop()
        for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= q[0] < W and 0 <= q[1] < H and q not in core and is_core(px[q]):
                core.add(q)
                st.append(q)
    fringe = {(x + dx, y + dy) for x, y in core for dx in (-1, 0, 1) for dy in (-1, 0, 1)}
    return {q for q in fringe if 0 <= q[0] < W and 0 <= q[1] < H and toothish(px[q])}


def gild(target, seed, clean=None):
    src = clean if clean is not None else target.copy()
    o = src.load()
    p = target.load()
    W, H = target.size
    mask = tooth_mask(src, seed)
    if clean is not None:
        # undo tint an earlier recolour left on the outline around the tooth
        ring = {(x + dx, y + dy) for x, y in mask for dx in range(-2, 3) for dy in range(-2, 3)} - mask
        for q in ring:
            if 0 <= q[0] < W and 0 <= q[1] < H and not toothish(o[q]) and abs(o[q][3] - p[q][3]) <= 8:
                p[q] = o[q][:3] + (p[q][3],)
    xs = [q[0] for q in mask]
    ys = [q[1] for q in mask]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    # edge fades into warm brown on the usual dark-red mouth; into the outline's own colour
    # when the tooth sits in line art of another hue (Prince Porky's dark green)
    ring = {(x + dx, y + dy) for x, y in mask for dx in (-1, 0, 1) for dy in (-1, 0, 1)} - mask
    line = [o[q] for q in ring if 0 <= q[0] < W and 0 <= q[1] < H and o[q][3] > 200 and lum(o[q]) < 110]
    fringe = FRINGE
    if line:
        mean = tuple(sum(c[i] for c in line) / len(line) for i in range(3))
        if mean[1] > mean[0]:
            fringe = tuple(int(v) for v in mean)
    for (x, y) in mask:
        t = tval(o[x, y])
        u = (x - x0) / max(1, x1 - x0)
        v = (y - y0) / max(1, y1 - y0)
        k = max(0, min(1, 0.55 * u + 0.45 * v))
        g = lerp(G_LIGHT, G_MID, min(1, k * 1.6)) if k < 0.62 else lerp(G_MID, G_DARK, (k - 0.62) / 0.38)
        if u > 0.78:
            g = lerp(g, G_DARK, min(1, (u - 0.78) / 0.22) * 0.8)
        hs = max(0, 1 - abs(u - 0.28) / 0.12) * max(0, 1 - max(0, v - 0.55) / 0.3)
        g = lerp(g, G_HI, hs * 0.9)
        p[x, y] = lerp(fringe, g, t) + (p[x, y][3],)
    if x1 - x0 + 1 >= GLINT_MIN and y1 - y0 + 1 >= GLINT_MIN:
        gx, gy = x0 + round((x1 - x0) * 0.3), y0 + 3
        for dx, dy, a in [(0, 0, 1), (1, 0, .6), (-1, 0, .6), (0, 1, .6), (0, -1, .6),
                          (2, 0, .25), (-2, 0, .25), (0, 2, .25), (0, -2, .25)]:
            q = (gx + dx, gy + dy)
            if q in mask:
                c = p[q]
                p[q] = lerp(c, GLINT, a) + (c[3],)
    return mask


TUSK_PALE = 235              # luminance of the pale tusk body; darker tusk pixels become darker gold


def tusk_mask(img, seed):
    """Whole tusk inside its dark outline (pale body + olive shading stripes), plus AA rim."""
    px = img.load()
    W, H = img.size

    def body(c):
        r, g, b, a = c
        # warm tusk colours; very pale pixels may lean a little green where the lip tints them
        return a > 200 and lum(c) > 60 and r >= g - (25 if lum(c) > 200 else 12) and b < g and r - g < 60
    if not body(px[seed]):
        raise ValueError('seed %s is not on a tusk' % (seed,))
    inner = {seed}
    st = [seed]
    while st:
        x, y = st.pop()
        for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= q[0] < W and 0 <= q[1] < H and q not in inner and body(px[q]):
                inner.add(q)
                st.append(q)
    rim = {(x + dx, y + dy) for x, y in inner for dx in (-1, 0, 1) for dy in (-1, 0, 1)} - inner
    rim = {q for q in rim if 0 <= q[0] < W and 0 <= q[1] < H and px[q][3] > 0
           and (px[q][0] >= px[q][1] - 12 and lum(px[q]) > lum(DARK) + 8 or lip_blend(img, q, inner)[1] > 0.12)}
    # one more ring for slivers that run under the lip: mostly-tusk pixels touching only the rim
    m = inner | rim
    ring = {(x + dx, y + dy) for x, y in m for dx in (-1, 0, 1) for dy in (-1, 0, 1)} - m
    rim |= {q for q in ring if 0 <= q[0] < W and 0 <= q[1] < H and px[q][3] > 0
            and px[q][1] > px[q][0] + 12 and lip_blend(img, q, m)[1] > 0.3}
    return inner, rim


def lip_blend(img, q, inner):
    """For an edge pixel where a tusk meets non-outline art (the green lip): (neighbour colour it
    blends with, fraction of tusk in it). Fraction is the projection of the pixel onto the line from
    that neighbour's colour to the pale tusk colour."""
    px = img.load()
    W, H = img.size
    c = px[q]
    T = (255, 240, 150)
    outs = [px[n] for n in ((q[0] + dx, q[1] + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1))
            if 0 <= n[0] < W and 0 <= n[1] < H and n != q and n not in inner and px[n][3] > 200]
    if not outs or c[3] == 0:
        return None, 0.0
    Bc = max(outs, key=lambda o: sum((o[i] - T[i]) ** 2 for i in range(3)))
    d = [T[i] - Bc[i] for i in range(3)]
    n2 = sum(v * v for v in d) or 1
    f = sum((c[i] - Bc[i]) * d[i] for i in range(3)) / n2
    return Bc, max(0.0, min(1.0, f))


def gild_tusk(target, seed, clean=None):
    """Gold tusk: keeps the art's own shading (olive stripes -> deep gold), light and shine follow each row."""
    src = clean if clean is not None else target.copy()
    o = src.load()
    p = target.load()
    inner, rim = tusk_mask(src, seed)
    mask = inner | rim
    ys = [q[1] for q in mask]
    y0, y1 = min(ys), max(ys)
    rows = {}
    for x, y in inner:
        lo, hi = rows.get(y, (x, x))
        rows[y] = (min(lo, x), max(hi, x))
    for (x, y) in mask:
        lo, hi = rows.get(y, rows[min(rows, key=lambda r: abs(r - y))])
        u = max(0, min(1, (x - lo) / max(1, hi - lo)))
        v = (y - y0) / max(1, y1 - y0)
        k = max(0, min(1, 0.55 * u + 0.45 * v))
        g = lerp(G_LIGHT, G_MID, min(1, k * 1.6)) if k < 0.62 else lerp(G_MID, G_DARK, (k - 0.62) / 0.38)
        if u > 0.78:
            g = lerp(g, G_DARK, min(1, (u - 0.78) / 0.22) * 0.8)
        s = lum(o[x, y]) / TUSK_PALE
        g = lerp(g, G_DARK, max(0, min(1, (1 - s) / 0.3)) * 0.85)
        if s > 0.9:
            hs = max(0, 1 - abs(u - 0.28) / 0.14) * max(0, 1 - max(0, v - 0.55) / 0.3)
            g = lerp(g, G_HI, hs * 0.85)
        if (x, y) in rim and o[x, y][1] > o[x, y][0] + 12:      # blends into the green lip
            Bc, f = lip_blend(src, (x, y), inner)
            p[x, y] = lerp(Bc, g, f) + (p[x, y][3],)
        else:
            t = tval(o[x, y]) if (x, y) in rim else 1.0
            p[x, y] = lerp(FRINGE, g, t) + (p[x, y][3],)
    gy = y0 + round((y1 - y0) * 0.3)
    if gy in rows and rows[gy][1] - rows[gy][0] >= 5 and y1 - y0 >= 12:
        lo, hi = rows[gy]
        gx = lo + round((hi - lo) * 0.3)
        for dx, dy, a in [(0, 0, 1), (1, 0, .6), (-1, 0, .6), (0, 1, .6), (0, -1, .6),
                          (2, 0, .25), (-2, 0, .25), (0, 2, .25), (0, -2, .25)]:
            q = (gx + dx, gy + dy)
            if q in inner:
                c = p[q]
                p[q] = lerp(c, GLINT, a) + (c[3],)
    return inner, rim


ENGRAVE = (122, 52, 0)       # groove
ENGRAVE_LIP = (255, 236, 150)  # lit lower-right wall of the groove


def dollar_glyph(Hs=400):
    """High-res upright two-stroke dollar sign mask (L, cropped tight); sampled through a warp."""
    import math
    from PIL import ImageDraw
    Ws = int(Hs / 1.45)
    pad = Hs // 4
    im = Image.new('L', (Ws + 2 * pad, Hs + 2 * pad), 0)
    d = ImageDraw.Draw(im)
    sw = int(round(Hs * 0.085))
    rx, ry = Ws * 0.36, Hs * 0.2
    top_c = (pad + Ws / 2, pad + Hs * 0.32)
    bot_c = (pad + Ws / 2, pad + Hs * 0.70)
    pts = [(top_c[0] + rx * math.cos(math.radians(a)), top_c[1] + ry * math.sin(math.radians(a)))
           for a in range(-55, -271, -5)]
    pts += [(bot_c[0] + rx * math.cos(math.radians(a)), bot_c[1] + ry * math.sin(math.radians(a)))
            for a in range(-90, 126, 5)]
    d.line(pts, fill=255, width=sw, joint='curve')
    for fx in (0.32, 0.68):
        x = pad + Ws * fx
        d.line([(x, pad), (x, pad + Hs)], fill=255, width=int(sw * 0.75))
    return im.crop(im.getbbox())


def _smooth(vals, k=2):
    ys = sorted(vals)
    return {y: sum(vals[z] for z in ys if abs(z - y) <= k) / sum(1 for z in ys if abs(z - y) <= k) for y in ys}


def _lstsq(xs, ys, ws, deg):
    """Weighted polynomial fit (deg 1 or 2); returns coefficients c0..c_deg."""
    n = deg + 1
    A = [[sum(w * x ** (i + j) for x, w in zip(xs, ws)) for j in range(n)] for i in range(n)]
    b = [sum(w * y * x ** i for x, y, w in zip(xs, ys, ws)) for i in range(n)]
    for i in range(n):                       # Gauss-Jordan
        piv = A[i][i] or 1e-9
        for j in range(n):
            if j != i:
                f = A[j][i] / piv
                A[j] = [a - f * c for a, c in zip(A[j], A[i])]
                b[j] -= f * b[i]
    return [b[i] / (A[i][i] or 1e-9) for i in range(n)]


def tusk_frame(inner, up=None, axis=None):
    """Curved coordinate frame along a tusk. Slices the tusk across its PCA long axis (not pixel
    rows: rows cut a leaning horn obliquely and their midpoints bend the wrong way), fits one
    quadratic arc through the slice midpoints and a straight line through the half-widths.
    Returns (to_ab, centre, halfw): to_ab(x, y) -> (a, b) with a running tip -> base, or away from
    `up` (an (x, y) point the glyph's top should face, e.g. a broken tusk's cut face) when given.
    `axis` (dx, dy) overrides the PCA direction: a short stump's visible side is wider than it is
    long, so PCA would lay the glyph across the stump."""
    import math
    n = len(inner)
    mx = sum(q[0] for q in inner) / n
    my = sum(q[1] for q in inner) / n
    sxx = sum((q[0] - mx) ** 2 for q in inner) / n
    syy = sum((q[1] - my) ** 2 for q in inner) / n
    sxy = sum((q[0] - mx) * (q[1] - my) for q in inner) / n
    th = 0.5 * math.atan2(2 * sxy, sxx - syy) if axis is None else math.atan2(axis[1], axis[0])
    A = (math.cos(th), math.sin(th))

    def slices(Ax, Ay):
        sl = {}
        for x, y in inner:
            px, py = x + 0.5 - mx, y + 0.5 - my
            a, b = px * Ax + py * Ay, px * Ay - py * Ax
            k = int(math.floor(a))
            lo, hi = sl.get(k, (b, b))
            sl[k] = (min(lo, b - 0.5), max(hi, b + 0.5))
        return sl
    sl = slices(*A)
    ks = sorted(sl)
    q = max(1, len(ks) // 4)
    w_lo = sum(sl[k][1] - sl[k][0] for k in ks[:q]) / q
    w_hi = sum(sl[k][1] - sl[k][0] for k in ks[-q:]) / q
    flip = w_lo > w_hi                        # make a run from the narrow tip to the wide base
    if up is not None:
        flip = (up[0] - mx) * A[0] + (up[1] - my) * A[1] > 0
    if flip:
        A = (-A[0], -A[1])
        sl = slices(*A)
        ks = sorted(sl)
    Ax, Ay = A
    xs = [k + 0.5 for k in ks]
    mids = [(sl[k][0] + sl[k][1]) / 2 for k in ks]
    halfs = [(sl[k][1] - sl[k][0]) / 2 for k in ks]
    ws = [h for h in halfs]                   # wide, reliable slices count more
    c = _lstsq(xs, mids, ws, 2)
    l = _lstsq(xs, halfs, ws, 1)
    to_ab = lambda x, y: ((x - mx) * Ax + (y - my) * Ay, (x - mx) * Ay - (y - my) * Ax)
    centre = lambda a: (c[0] + c[1] * a + c[2] * a * a, c[1] + 2 * c[2] * a)
    halfw = lambda a: max(1.5, l[0] + l[1] * a)
    return to_ab, centre, halfw


def cent_glyph(Hs=400):
    """High-res upright cent sign: an open c with one vertical stroke running past it."""
    import math
    from PIL import ImageDraw
    Ws = int(Hs / 1.45)
    pad = Hs // 4
    im = Image.new('L', (Ws + 2 * pad, Hs + 2 * pad), 0)
    d = ImageDraw.Draw(im)
    sw = int(round(Hs * 0.1))
    cx, cy, rx, ry = pad + Ws * 0.5, pad + Hs * 0.5, Ws * 0.4, Hs * 0.3
    pts = [(cx + rx * math.cos(math.radians(a)), cy + ry * math.sin(math.radians(a))) for a in range(-50, -311, -5)]
    d.line(pts, fill=255, width=sw, joint='curve')
    x = pad + Ws * 0.55
    d.line([(x, pad), (x, pad + Hs)], fill=255, width=int(sw * 0.8))
    return im.crop(im.getbbox())


def tusk_cap(img, inner, seed, max_lum=160):
    """Flat cut face of a broken tusk: the dark olive ellipse around `seed` (flood over dark inner pixels)."""
    px = img.load()
    cap = {seed}
    st = [seed]
    while st:
        x, y = st.pop()
        for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if q in inner and q not in cap and lum(px[q]) < max_lum:
                cap.add(q)
                st.append(q)
    return cap


def engrave_dollar(target, inner, **kw):
    return engrave(target, inner, dollar_glyph(), **kw)


def engrave(target, inner, glyph, region=None, up=None, axis=None, height=None, fill=0.72, ss=4, lift=0.06):
    """Engrave `glyph` (an upright high-res L mask) so it bends with the tusk: its vertical axis
    follows the tusk's fitted centre arc (top toward the tip, or toward `up`), its horizontal axis
    is the perpendicular offset from that arc, and its width follows the tusk's taper at half
    strength (full taper bloats a $'s lower bowl into an 8). With `region`, the glyph is sized,
    placed and clipped inside that subset of the tusk (e.g. the side of a broken stump, away from
    its flat cut face)."""
    import math
    p = target.load()
    area = region if region is not None else inner
    edge = [q for q in area if any((q[0] + dx, q[1] + dy) not in area
                                   for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    best = max(area, key=lambda q: min((q[0] - e[0]) ** 2 + (q[1] - e[1]) ** 2 for e in edge))
    r = math.sqrt(min((best[0] - e[0]) ** 2 + (best[1] - e[1]) ** 2 for e in edge))
    h = height or max(11, int(round(r * 2.6)))
    to_ab, centre, halfw = tusk_frame(area, up, axis)
    inner = area
    amin = min(to_ab(x + .5, y + .5)[0] for x, y in inner)
    amax = max(to_ab(x + .5, y + .5)[0] for x, y in inner)
    h = min(h, (amax - amin) * 0.9)          # never longer than the area it sits in
    a0 = to_ab(best[0] + .5, best[1] + .5)[0] - h * lift
    aa = max(amin, min(a0 - h / 2, amax - h))
    ab = min(amax, aa + h)
    wm = halfw((aa + ab) / 2)
    wfit = lambda a: max(2.0, wm + 0.5 * (halfw(a) - wm))
    g = glyph
    gp = g.load()
    GW, GH = g.size

    def cov(x, y):
        tot = 0.0
        for j in range(ss):
            for i in range(ss):
                a, b = to_ab(x + (i + 0.5) / ss, y + (j + 0.5) / ss)
                if not aa <= a < ab:
                    continue
                cb, slope = centre(a)
                d = (b - cb) / math.hypot(1, slope)
                u = 0.5 + d / (2 * wfit(a) * fill)
                v = (a - aa) / (ab - aa)
                if 0 <= u < 1:
                    tot += gp[int(u * GW), min(GH - 1, int(v * GH))] / 255
        return tot / (ss * ss)

    covs = {q: cov(*q) for q in inner if abs(q[0] - best[0]) <= h and abs(q[1] - best[1]) <= h}
    for (x, y), c in covs.items():
        lip = max(0, covs.get((x - 1, y - 1), 0) - c)
        col = p[x, y]
        rgb = lerp(col, ENGRAVE_LIP, lip * 0.75)
        rgb = lerp(rgb, ENGRAVE, min(1, c * 1.1))
        p[x, y] = rgb + (col[3],)
    return best, h


if __name__ == '__main__':
    a = sys.argv[1:]
    opt = {}
    for k in ('--clean', '--out'):
        if k in a:
            i = a.index(k)
            opt[k] = a[i + 1]
            del a[i:i + 2]
    target = Image.open(a[0]).convert('RGBA')
    clean = Image.open(opt['--clean']).convert('RGBA') if '--clean' in opt else None
    m = gild(target, (int(a[1]), int(a[2])), clean)
    target.save(opt.get('--out', a[0]))
    print('gilded %d px' % len(m))
