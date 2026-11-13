"""Gold chain looped through the hole of the PigPirate tail's curl: it droops into free air below the tail
and comes back up to the same point in the hole (Gangster variant). The first link passes behind the curl's inner strand.

Palette = the approved gold-tooth recipe (ArtSource/GoldTeeth/GOLD_TOOTH_STYLE.md): lit #FFE254 top-left,
body #FAB61E, shade #C4700A bottom-right, dark warm-brown outline. Links alternate face-on rings and
edge-on bars, rendered with supersampling so edges get the game's soft anti-aliasing.

Run: python3 -B gold_chain.py [Tail.png] [--out out.png]   (default: writes out/ previews only)
"""
import argparse
import math
import os

from PIL import Image, ImageChops, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
TAIL = os.path.join(HERE, '..', '..', 'Assets', 'content', '4_Characters', '2_Pigs', '6_PigPirate', 'BodyParts', 'Tail.png')

LIT = (255, 226, 84)
BODY = (250, 182, 30)
SHADE = (196, 112, 10)
OUTLINE = (92, 40, 6)
SS = 12

# anchors (sprite px, y down): inside the curl's hole and on the tip's end
A = (17.4, 9.6)
# The loop's path after leaving A (closed Catmull-Rom spline back to A): down past the hole, hanging under the
# tail (its bottom edge is y ~23 there), out around the tip's end, back up resting on top of the tip, into the hole.
LOOP = [(19.6, 16.0), (23.0, 22.0), (27.6, 25.3), (32.6, 25.0), (35.2, 21.6), (34.2, 17.7), (30.2, 15.4),
        (25.4, 12.9)]
# Layering (user-marked):
#  * the last BACK_LINKS links (the return side climbing from the tip back into the hole) pass BEHIND the whole tail, i.e.
#    behind the curl's flesh right of the hole;
#  * the curl's inner strand (dark line art in STRAND_BOX) is in front of the other links only down to the box's
#    bottom row, so the first ring threads through the hole but the chain is in front of the strand's lower end.
#  (Putting the tail tip in front was tried and reverted.)
BACK_LINKS = 4
STRAND_BOX = (16, 7, 20, 12)


def front_mask(tail):
    """L mask (255 = tail pixel drawn over the chain)."""
    W, H = tail.size
    tp = tail.load()
    m = Image.new('L', tail.size, 0)
    mp = m.load()
    x0, y0, x1, y1 = STRAND_BOX
    for y in range(y0, y1):
        xs = [x for x in range(x0, x1) if tp[x, y][3] > 200 and sum(tp[x, y][:3]) < 150]
        if xs:
            for x in xs:
                mp[x, y] = 255
    return m
LINK_LEN = 3.1          # spacing between link centers
RING = (2.75, 2.15)      # face-on link half-length, half-width (outer, incl. outline)
BAR = (2.45, 1.25)       # edge-on link


def bez(t, p0, p1, p2):
    return tuple((1 - t) ** 2 * p0[i] + 2 * (1 - t) * t * p1[i] + t * t * p2[i] for i in range(2))


def loop_path(n=1600):
    """Closed Catmull-Rom spline through A and LOOP, starting and ending at A."""
    P = [A] + LOOP
    m = len(P)
    pts = []
    for i in range(m):
        p0, p1, p2, p3 = P[i - 1], P[i], P[(i + 1) % m], P[(i + 2) % m]
        for k in range(n // m):
            t = k / (n // m)
            t2, t3 = t * t, t * t * t
            pts.append(tuple(0.5 * (2 * p1[j] + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2
                                   + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3) for j in range(2)))
    pts.append(A)
    return pts


def chain_points():
    pts = loop_path()
    total = sum(math.dist(p, q) for p, q in zip(pts, pts[1:]))
    n = max(2, round(total / LINK_LEN))
    step = total / n
    out, acc, target = [pts[0]], 0.0, step
    for p, q in zip(pts, pts[1:]):
        d = math.dist(p, q)
        while acc + d >= target and len(out) < n:
            f = (target - acc) / d
            out.append((p[0] + (q[0] - p[0]) * f, p[1] + (q[1] - p[1]) * f))
            target += step
        acc += d
    out.append(pts[0])   # closes the loop back at the hole
    centers = []
    for p, q in zip(out, out[1:]):
        centers.append(((p[0] + q[0]) / 2, (p[1] + q[1]) / 2, math.atan2(q[1] - p[1], q[0] - p[0])))
    return centers


def ellipse_poly(cx, cy, a, b, ang, n=48):
    ca, sa = math.cos(ang), math.sin(ang)
    return [((cx + a * math.cos(t) * ca - b * math.sin(t) * sa) * SS,
             (cy + a * math.cos(t) * sa + b * math.sin(t) * ca) * SS) for t in (2 * math.pi * i / n for i in range(n))]


def gold_at(x, y, cx, cy, r):
    k = 0.55 * ((x - cx) / r + 1) / 2 + 0.45 * ((y - cy) / r + 1) / 2
    k = max(0.0, min(1.0, k))
    if k < 0.5:
        t, c0, c1 = k / 0.5, LIT, BODY
    else:
        t, c0, c1 = (k - 0.5) / 0.5, BODY, SHADE
    return tuple(round(c0[i] + (c1[i] - c0[i]) * t) for i in range(3))


def render(size, keep=lambda idx, n: True):
    W, H = size
    big = Image.new('RGBA', (W * SS, H * SS), (0, 0, 0, 0))
    links = chain_points()
    # draw back-to-front so each link overlaps the previous one
    for idx, (cx, cy, ang) in enumerate(links):
        if not keep(idx, len(links)):
            continue
        face = idx % 2 == 0
        a, b = RING if face else BAR
        lay = Image.new('RGBA', big.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(lay)
        d.polygon(ellipse_poly(cx, cy, a, b, ang), fill=OUTLINE + (255,))
        inner = ellipse_poly(cx, cy, a - 0.55, b - 0.55, ang)
        # gold fill, shaded per pixel
        mask = Image.new('L', big.size, 0)
        ImageDraw.Draw(mask).polygon(inner, fill=255)
        bb = mask.getbbox()
        if bb:
            px = lay.load(); mp = mask.load()
            r = max(a, b)
            for yy in range(bb[1], bb[3]):
                for xx in range(bb[0], bb[2]):
                    if mp[xx, yy]:
                        px[xx, yy] = gold_at(xx / SS, yy / SS, cx, cy, r) + (255,)
        if face:  # the hole of a face-on ring
            d.polygon(ellipse_poly(cx, cy, a - 1.55, b - 1.45, ang), fill=SHADE + (255,))
        # small highlight on the upper-left of each link
        hx, hy = cx - 0.45, cy - 0.55
        d.ellipse(((hx - .35) * SS, (hy - .35) * SS, (hx + .35) * SS, (hy + .35) * SS), fill=(255, 252, 210, 255))
        big = Image.alpha_composite(big, lay)
    return big.resize((W, H), Image.LANCZOS)


def paste_layer(tail, result):
    """Layer L with  L over tail == result  (pasting it over the sheet reproduces the chained tail exactly)."""
    W, H = tail.size
    L = Image.new('RGBA', tail.size, (0, 0, 0, 0))
    lp, tp, rp = L.load(), tail.load(), result.load()
    for y in range(H):
        for x in range(W):
            t, r = tp[x, y], rp[x, y]
            if t == r or r[3] == 0:
                continue
            ta, ra = t[3] / 255, r[3] / 255
            la = 1.0 if ta >= 1 else max(0.0, min(1.0, (ra - ta) / (1 - ta)))
            if la <= 0:   # alpha unchanged: a near-opaque tail pixel recoloured by the chain, or a rounding-level change
                if ta < 0.99:
                    continue
                la = 1.0
            rgb = tuple(max(0, min(255, round((ra * r[i] - ta * (1 - la) * t[i]) / la))) for i in range(3))
            lp[x, y] = rgb + (round(la * 255),)
    return L


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('tail', nargs='?', default=TAIL)
    ap.add_argument('--out')
    args = ap.parse_args()
    tail = Image.open(args.tail).convert('RGBA')
    back = render(tail.size, lambda i, n: i >= n - BACK_LINKS)
    chain = render(tail.size, lambda i, n: i < n - BACK_LINKS)
    os.makedirs(os.path.join(HERE, 'out'), exist_ok=True)
    chain.save(os.path.join(HERE, 'out', 'gold_chain_overlay.png'))
    front = front_mask(tail)
    over = Image.alpha_composite(Image.alpha_composite(back, tail), chain)
    under = Image.alpha_composite(Image.alpha_composite(back, chain), tail)
    result = Image.composite(under, over, front)
    # keep the source's hidden RGB on fully transparent pixels
    result = Image.composite(tail, result, Image.eval(result.getchannel('A'), lambda v: 255 if v == 0 else 0))
    vis = paste_layer(tail, result)
    vis.save(os.path.join(HERE, 'out', 'gold_chain_overlay.png'))
    result.save(args.out or os.path.join(HERE, 'out', 'TailGangster.png'))
    # comparison preview: original | chained, 12x nearest on a checkerboard
    z = 12
    W, H = tail.size
    cmp_ = Image.new('RGBA', (W * z * 2 + z, H * z), (0, 0, 0, 0))
    chk = Image.new('RGBA', cmp_.size)
    cd = ImageDraw.Draw(chk)
    for y in range(0, cmp_.size[1], 24):
        for x in range(0, cmp_.size[0], 24):
            cd.rectangle((x, y, x + 23, y + 23), fill=(204, 204, 204, 255) if (x // 24 + y // 24) % 2 else (240, 240, 240, 255))
    cmp_.paste(tail.resize((W * z, H * z), Image.NEAREST), (0, 0))
    cmp_.paste(result.resize((W * z, H * z), Image.NEAREST), (W * z + z, 0))
    Image.alpha_composite(chk, cmp_).save(os.path.join(HERE, 'out', 'compare.png'))


if __name__ == '__main__':
    main()
