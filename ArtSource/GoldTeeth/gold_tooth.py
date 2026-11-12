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
