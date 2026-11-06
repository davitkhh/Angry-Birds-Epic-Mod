"""Rests the bucket on the pirate's head physically instead of where it was sketched.

The head is the PigPirate Body.png silhouette (body px, y down). Its top outline, ears and earring included, is the
support surface: they're drawn rigid, so the bucket can rest on them.

- Worn (upside down): the rim's left/right extremes both touch the outline, and the head pokes up inside the bucket.
  The walls must clear the head. Of the valid landings, the highest one is where a bucket lowered from above stops.
- Balanced (upright): the flat base lies along an upper-convex-hull edge of the outline (two supports). The centre of
  mass, raised by the water, has to sit above the span between the supports.
- Both: the tilt is limited by friction (wood on pig skin, mu ~0.5 -> about 27 deg). Placements that would slide or tip
  are moved toward the top of the head until they hold.
- Handle: falls under gravity about its lugs until it rests on the head (smooth dome, ears ignored, with depth) or
  against the bucket. This is the lowest potential energy that doesn't penetrate, on the side the variant asks for.
"""
import math
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
BODY = os.path.join(HERE, '..', '..', 'Assets', 'content', '4_Characters', '2_Pigs', '6_PigPirate', 'BodyParts', 'Body.png')
MAX_TILT = math.radians(27)
COM_UPRIGHT = 0.42  # centre of mass up the axis, water-filled bucket on its base
COM_WORN = 0.55  # empty bucket upside down: the heavier base end is up


def _load():
    im = Image.open(BODY).convert('RGBA')
    a = im.split()[3].load()
    px = im.load()
    W, H = im.size
    top, dome = {}, {}
    for x in range(W):
        t = next((y for y in range(H) if a[x, y] > 128), None)
        if t is not None:
            top[x] = t
        g = next((y for y in range(H) if a[x, y] > 128 and px[x, y][1] > px[x, y][0] + 30), None)
        if g is not None:
            dome[x] = g
    # smooth dome for the handle: drop the ears (x 104..152, 154..188) and interpolate across
    keep = {x: y for x, y in dome.items() if not (104 <= x <= 152 or 154 <= x <= 189)}
    return top, keep, W


TOP, DOME_PTS, BODY_W = _load()
_XS = sorted(TOP)
_DX = sorted(DOME_PTS)


def outline(x):
    """Top of the head (ears/earring included) at body-px x; +inf (no support) off the body."""
    if x < _XS[0] or x > _XS[-1]:
        return float('inf')
    x0 = int(math.floor(x))
    x1 = min(x0 + 1, _XS[-1])
    y0, y1 = TOP.get(x0, 999), TOP.get(x1, 999)
    return y0 + (y1 - y0) * (x - x0)


def _dome_exact(x):
    if x <= _DX[0]:
        return DOME_PTS[_DX[0]] + (_DX[0] - x) * 3
    if x >= _DX[-1]:
        return DOME_PTS[_DX[-1]] + (x - _DX[-1]) * 3
    for i in range(len(_DX) - 1):
        if _DX[i] <= x <= _DX[i + 1]:
            t = (x - _DX[i]) / (_DX[i + 1] - _DX[i])
            return DOME_PTS[_DX[i]] + (DOME_PTS[_DX[i + 1]] - DOME_PTS[_DX[i]]) * t


_DOME = {x: _dome_exact(x) for x in range(-100, 320)}


def dome(x):
    x0 = int(math.floor(x))
    if x0 < -100 or x0 >= 319:
        return _dome_exact(x)
    return _DOME[x0] + (_DOME[x0 + 1] - _DOME[x0]) * (x - x0)


def inside_head(x, y, z):
    """Smooth head volume: the dome outline, falling away with depth (front/back curvature ~90 px)."""
    return y > dome(x) + z * z / (2 * 90.0)


# --- worn (upside down) -------------------------------------------------------------------------------------------

def _walls_clear(xm, r, R, Rb, H):
    """Head outline inside the bucket's cross-section (rim chord -> base) for every x it spans."""
    c, s = math.cos(r), math.sin(r)
    cy = (outline(xm - R * c) + outline(xm + R * c)) / 2
    up = (s, -c)
    for i in range(41):
        t = i / 40
        # point along the head outline between the contacts
        x = xm - R * c + 2 * R * c * t
        y = outline(x)
        if y == float('inf'):
            continue
        # into bucket frame: u across the rim chord, v up the axis from the rim centre
        dx, dy = x - xm, y - cy
        u = dx * c + dy * s
        v = dx * up[0] + dy * up[1]
        if v < -0.5:
            continue  # below the rim: outside the bucket, fine
        if v > H:
            return False
        half = R + (Rb - R) * v / H
        if abs(u) > half + 0.5:
            return False
    return True


def rest_worn(xm, R, Rb, H):
    """Tilt r (rad, clockwise) and rim-centre y for the rim (half-width R) centred over xm, or None."""
    best = None
    prev = None
    for i in range(-240, 241):
        r = math.radians(i * 0.25)
        x1, x2 = xm - R * math.cos(r), xm + R * math.cos(r)
        y1, y2 = outline(x1), outline(x2)
        if math.isinf(y1) or math.isinf(y2):
            prev = None
            continue
        f = (y2 - y1) - 2 * R * math.sin(r)
        if prev is not None and (f == 0 or (f > 0) != (prev[1] > 0)):
            ry = (y1 + y2) / 2
            if _walls_clear(xm, r, R, Rb, H) and (best is None or ry < best[1]):
                best = (r, ry, x1, x2)
        prev = (r, f)
    return best


def settle_worn(x_target, rt, rb, H):
    R = rt * 1.02
    toward = 1 if x_target < 125 else -1  # slide toward the top of the head
    for step in range(0, 160):
        if True:
            xm = x_target + toward * step * 0.5
            res = rest_worn(xm, R, rb, H)
            if res is None:
                continue
            r, ry, x1, x2 = res
            com_x = xm + math.sin(r) * COM_WORN * H
            if abs(r) <= MAX_TILT and x1 + 2 < com_x < x2 - 2:
                return dict(xm=xm, roll=math.degrees(r), rim=(xm, ry), contacts=(x1, x2), com_x=com_x)
    raise RuntimeError('no stable worn placement near x=%s' % x_target)


# --- upright (balanced on its base) -------------------------------------------------------------------------------

def upper_hull():
    pts = [(x, -TOP[x]) for x in _XS]  # y up
    hull = []
    for p in pts:
        while len(hull) >= 2:
            (x1, y1), (x2, y2) = hull[-2], hull[-1]
            if (x2 - x1) * (p[1] - y1) - (y2 - y1) * (p[0] - x1) >= 0:
                hull.pop()
            else:
                break
        hull.append(p)
    return [(x, -y) for x, y in hull]


def settle_upright(x_target, rb, H):
    hull = upper_hull()
    options = []
    for (p, q) in zip(hull, hull[1:]):
        if q[0] - p[0] < 6:
            continue
        r = math.atan2(q[1] - p[1], q[0] - p[0])
        length = math.hypot(q[0] - p[0], q[1] - p[1])
        if abs(r) > MAX_TILT or length > 2 * rb:
            continue
        # base centre at distance d along the support line from p: the base has to reach both supports, and the
        # centre of mass has to sit over the span between them; as near the target as that allows
        best = None
        steps = 200
        for k in range(steps + 1):
            d = (length - rb) + (2 * rb - length) * k / steps
            if d < length - rb - 1e-6 or d > rb + 1e-6:
                continue
            bx = p[0] + d * math.cos(r)
            by = p[1] + d * math.sin(r)
            com_x = bx + math.sin(r) * COM_UPRIGHT * H
            if not (p[0] + 3 <= com_x <= q[0] - 3):
                continue
            if best is None or abs(com_x - x_target) < abs(best[2] - x_target):
                best = (bx, by, com_x)
        if best is None:
            continue
        bx, by, com_x = best
        options.append((abs(com_x - x_target), dict(roll=math.degrees(r), base=(bx, by), contacts=(p, q), com_x=com_x)))
    if not options:
        raise RuntimeError('no stable upright placement')
    return min(options, key=lambda o: o[0])[1]


# --- handle ---------------------------------------------------------------------------------------------------------

def settle_handle(bucket_cls, params, side):
    """Swing (deg) where the handle comes to rest. side +1 = drapes toward the viewer, -1 = away."""
    best = None
    for s10 in range(-1800, 1800, 10):
        sw = s10 / 10
        b = bucket_cls(**dict(params, swing=sw))
        pts = b.handle_points(48)
        apex = pts[len(pts) // 2]
        if (apex[2] > 0) != (side > 0):
            continue
        ok = True
        energy = 0.0
        for (sx, sy, Z, _front, ly) in pts[3:-3]:
            if inside_head(sx, sy, Z) or b.inside(sx, sy, Z):
                ok = False
                break
            energy += sy  # y down: larger = lower = less potential energy
        if ok and (best is None or energy > best[0]):
            best = (energy, sw)
    if best is None:
        raise RuntimeError('handle has nowhere to rest')
    return best[1]
