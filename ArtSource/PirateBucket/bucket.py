"""Pirate bucket headgear generator (PigPirate rig).

Renders the wooden bucket from Props_01_18 (Prop_Bucket_01) as character equipment: a 3D frustum of wooden staves
with brass hoops and a teal steel wire handle, cel shaded with a dark outline. Four placements on the pirate's head,
fitted to the user's outline sketches (sketch_strokes.json, body pixel coords):
  1-3: worn upside down as a helmet, different tilts and yaw (handle hangs onto the head)
  4:   upright, full of water, balanced on top of the head
Each variant writes a front texture (Headgear bone) and a back texture (Headgear_Back bone, drawn behind the body)
on the same canvas, so both share one pivot.

Coordinates: "body px" = Body.png pixel space (210x186, y down). The Headgear bone sits at body px (85, 46).
Run: python3 bucket.py  -> out/
"""
import json
import math
import os
import random
import sys

import cairo
from PIL import Image, ImageChops, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'out')
SS = 4  # supersampling
BONE = (85.0, 46.0)  # Headgear bone in body px

WOOD_DARK = (0.18, 0.10, 0.09)
WOOD_MID = (0.32, 0.19, 0.14)
WOOD_LIGHT = (0.45, 0.28, 0.19)
WOOD_HI = (0.62, 0.42, 0.29)
INSIDE_DARK = (0.17, 0.10, 0.07)
INSIDE_MID = (0.30, 0.18, 0.12)
END_GRAIN = (0.66, 0.43, 0.33)
METAL_DARK = (0.16, 0.18, 0.21)
METAL = (0.46, 0.50, 0.54)
METAL_LIGHT = (0.70, 0.74, 0.78)
METAL_SPEC = (1.0, 1.0, 1.0)
WATER_DEEP = (0.10, 0.42, 0.66)
WATER = (0.24, 0.66, 0.86)
WATER_LIGHT = (0.70, 0.92, 0.98)
OUTLINE = (0.15, 0.08, 0.05)

LIGHT = (-0.45, 0.55, 0.70)
_l = math.sqrt(sum(c * c for c in LIGHT))
LIGHT = tuple(c / _l for c in LIGHT)

# Placements, in body px. center = screen position of the bucket's axis midpoint; roll = clockwise screen tilt (deg);
# elev = camera elevation (deg, >0 sees from above); yaw = bucket spin about its axis (moves the handle lugs);
# swing = handle angle about the lug axis (0 = handle straight out past the rim, +90 = toward the viewer).
# Where each placement starts: x = body px the bucket is put down over (physics.py then finds where it actually rests:
# centre, tilt, and the handle's swing); side = which way the handle drapes (+1 toward the viewer, -1 away).
# Walls flare outward (rb well under rt).
VARIANTS = {
    1: dict(inverted=True, x=158, side=1, rt=60, rb=38, H=98, elev=-9, yaw=14, staves=11, seed=11),
    2: dict(inverted=True, x=112, side=1, rt=62, rb=40, H=102, elev=-9, yaw=8, staves=11, seed=22),
    3: dict(inverted=True, x=76, side=-1, rt=60, rb=38, H=98, elev=-9, yaw=-28, staves=11, seed=33),
    4: dict(inverted=False, x=148, side=1, rt=61, rb=36, H=88, elev=20, yaw=22, staves=11, seed=44, water=0.80),
}


def adjustments():
    """Hand adjustments from the Unity placement editor: {placement: (dx, dy, rotation)}. dx/dy in body px (y up) are
    applied by pack.py as pivot shifts; rotation (deg, counter-clockwise like Unity) is turned about the Headgear bone
    and baked into the art here, so the water levels and the handle re-settles for the new tilt."""
    out = {}
    path = os.path.join(HERE, 'placement_offsets.txt')
    if os.path.exists(path):
        for line in open(path):
            f = line.split()
            if len(f) >= 3:
                out[int(f[0])] = (float(f[1]), float(f[2]), float(f[3]) if len(f) > 3 else 0.0)
    return out


def settle(params, adjust=(0.0, 0.0, 0.0)):
    """Bucket kwargs for a VARIANTS entry, resting where physics puts it (see physics.py), then turned by the hand
    adjustment's rotation."""
    import physics
    p = dict(params)
    x, side = p.pop('x'), p.pop('side')
    if p['inverted']:
        rest = physics.settle_worn(x, p['rt'], p['rb'], p['H'])
        px_, py_ = rest['rim']
    else:
        rest = physics.settle_upright(x, p['rb'], p['H'])
        px_, py_ = rest['base']
    p['roll'] = rest['roll']
    # put the rim centre (worn) / base centre (upright) on the resting point
    probe = Bucket(**dict(p, center=(0.0, 0.0), swing=0))
    ox, oy, _ = probe.P(0, p['H'] if p['inverted'] else 0, 0)
    p['center'] = (px_ - ox, py_ - oy)
    dx, dy, rot = adjust
    if rot:
        # turn the bucket about the bone counter-clockwise (screen y is down; roll is clockwise-positive)
        t = math.radians(rot)
        rx, ry = p['center'][0] - BONE[0], p['center'][1] - BONE[1]
        p['center'] = (BONE[0] + rx * math.cos(t) + ry * math.sin(t), BONE[1] - rx * math.sin(t) + ry * math.cos(t))
        p['roll'] -= rot
    # the handle settles where the bucket really ends up (dragged position included), then moves back with the art
    moved = dict(p, swing=0, center=(p['center'][0] + dx, p['center'][1] - dy))
    p['swing'] = physics.settle_handle(Bucket, moved, side)
    return p, rest


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def ramp(stops, t):
    t = max(0.0, min(1.0, t))
    for i in range(len(stops) - 1):
        t0, c0 = stops[i]
        t1, c1 = stops[i + 1]
        if t <= t1:
            return lerp(c0, c1, (t - t0) / (t1 - t0) if t1 > t0 else 0)
    return stops[-1][1]


class Bucket:
    def __init__(self, inverted, center, roll, rt, rb, H, elev, yaw, swing, staves, seed, water=None, _id=None, px=1.0):
        self.inv = inverted
        self.cx, self.cy = center
        self.rt, self.rb, self.H = rt, rb, H
        self.ce, self.se = math.cos(math.radians(elev)), math.sin(math.radians(elev))
        self.cr, self.sr = math.cos(math.radians(roll)), math.sin(math.radians(roll))
        self.yaw = math.radians(yaw)
        self.swing = math.radians(swing)
        self.n = staves
        self.water = water
        self.seed = seed
        self.px = px  # canvas units per body px (SS when supersampled)
        self._layout = None
        self.spill = None
        self._water_level = None

    # local: y along the axis from base (0) to rim (H); azimuth 90deg faces the viewer before yaw.
    def radius(self, y):
        return self.rb + (self.rt - self.rb) * y / self.H

    def rot(self, x, y, z):
        if self.inv:
            y, z = -y, -z
        # physical tilt (roll) in the world, then the camera's elevation; screen space: X right, Y up, Z to viewer
        X = x * self.cr + y * self.sr
        Y = -x * self.sr + y * self.cr
        return X, Y * self.ce - z * self.se, Y * self.se + z * self.ce

    def inside(self, sx, sy, Z):
        """Is screen point (sx, sy, depth Z) inside the bucket's walls?"""
        X, Y2 = sx - self.cx, -(sy - self.cy)
        Y, z = Y2 * self.ce + Z * self.se, -Y2 * self.se + Z * self.ce
        x = X * self.cr - Y * self.sr
        y = X * self.sr + Y * self.cr
        if self.inv:
            y, z = -y, -z
        y += self.H / 2
        return 0 < y < self.H and math.hypot(x, z) < self.radius(y)

    def world_up(self, x, y):
        """Height against gravity (ignores the camera elevation) of local point (x, y, *) on an upright bucket."""
        return -x * self.sr + (y - self.H / 2) * self.cr

    def P(self, x, y, z):
        X, Y, Z = self.rot(x, y - self.H / 2, z)
        return self.cx + X, self.cy - Y, Z

    def surf(self, a, y, dr=0.0):
        r = self.radius(y) + dr
        a += self.yaw
        return self.P(r * math.cos(a), y, r * math.sin(a))

    def normal(self, a, inward=False):
        a += self.yaw
        s = -(self.rt - self.rb) / self.H
        n = (math.cos(a), s, math.sin(a))
        if inward:
            n = tuple(-c for c in n)
        X, Y, Z = self.rot(*n)
        l = math.sqrt(X * X + Y * Y + Z * Z)
        return X / l, Y / l, Z / l

    def shade(self, n):
        return n[0] * LIGHT[0] + n[1] * LIGHT[1] + n[2] * LIGHT[2]

    def layout(self):
        """Hand-made looking staves: uneven widths, seams that lean, gaps, ragged/chipped ends, varied tones.
        Everything is a fraction of the bucket size, so the 1x and supersampled buckets match."""
        if self._layout is not None:
            return self._layout
        rng = random.Random(self.seed * 7 + 1)
        n = self.n
        widths = [rng.uniform(0.72, 1.3) for _ in range(n)]
        total = sum(widths)
        start = rng.uniform(0, 2 * math.pi)
        edges, acc = [], start
        for w in widths:
            edges.append(acc)
            acc += 2 * math.pi * w / total
        step = 2 * math.pi / n
        twists = [rng.uniform(-0.16, 0.16) * step for _ in range(n)]
        staves = []
        for i in range(n):
            top_l = rng.uniform(-0.03, 0.06)
            if rng.random() < 0.25:
                top_l += rng.uniform(0.05, 0.1)  # a long finger, like the sketch's open end
            top_r = top_l + rng.uniform(-0.05, 0.05)
            staves.append(dict(
                a0=edges[i], a1=edges[(i + 1) % n] + (2 * math.pi if i == n - 1 else 0),
                t0=twists[i], t1=twists[(i + 1) % n],
                gl=rng.uniform(0.02, 0.09) * step if rng.random() < 0.4 else 0.0,
                gr=rng.uniform(0.02, 0.09) * step if rng.random() < 0.4 else 0.0,
                top=(top_l, top_r), bot=(rng.uniform(-0.02, 0.01), rng.uniform(-0.02, 0.01)),
                chip=(rng.uniform(0.2, 0.8), rng.uniform(0.04, 0.08)) if rng.random() < 0.3 else None,
                tone=rng.uniform(-0.13, 0.13),
                tint=rng.choice([None, None, (0.36, 0.16, 0.14), (0.30, 0.23, 0.19)]),
                grain=[(rng.uniform(0.15, 0.85), rng.uniform(0.0, 0.4), rng.uniform(0.55, 1.0), rng.random() < 0.5)
                       for _ in range(3)],
                knot=(rng.uniform(0.3, 0.7), rng.uniform(0.25, 0.75)) if rng.random() < 0.35 else None,
                sheen=rng.uniform(0.3, 0.5)))
        self._layout = staves
        return staves

    # point on stave st at fraction u across (0 = left seam) and height y; gaps shrink the stave inside its span
    def stave_at(self, st, u, y, dr=0.0, gaps=True):
        f = y / self.H
        a0 = st['a0'] + st['t0'] * f + (st['gl'] * self.gap_open(st, 0, y) if gaps else 0.0)
        a1 = st['a1'] + st['t1'] * f - (st['gr'] * self.gap_open(st, 1, y) if gaps else 0.0)
        return self.surf(a0 + (a1 - a0) * u, y, dr)

    def seal(self, st, side):
        """Water bucket: the seams are tight up to a little above the waterline (else it would leak), and only open
        above it. Height where the gap on this side of the stave starts; 0 = gaps all the way down."""
        if self.inv or not self.water:
            return 0.0
        key = 'seal_r' if side else 'seal_l'
        if key not in st:
            a = st['a1'] + st['t1'] if side else st['a0'] + st['t0']
            st[key] = water_height(self, a) + 0.05 * self.H
        return st[key]

    def gap_open(self, st, side, y):
        s = self.seal(st, side)
        if s <= 0:
            return 1.0
        t = min(1.0, max(0.0, (y - s) / (0.06 * self.H)))
        return t * t * (3 - 2 * t)

    def stave_top(self, st, u):
        tl, tr = st['top']
        y = self.H * (1 + tl + (tr - tl) * u)
        if st['chip']:
            c, d = st['chip']
            y -= self.H * d * max(0.0, 1 - abs(u - c) / 0.14)
        return y

    def stave_bottom(self, st, u):
        bl, br = st['bot']
        return self.H * (bl + (br - bl) * u)

    def stave_outline(self, st, dr=0.0, gaps=True, y_from=None):
        sub = 8
        us = [j / sub for j in range(sub + 1)]
        if st['chip']:
            c = st['chip'][0]
            us = sorted(set(us + [c - 0.14, c, c + 0.14]))
            us = [u for u in us if 0 <= u <= 1]
        bottom = (lambda u: y_from) if y_from is not None else (lambda u: self.stave_bottom(st, u))
        pts = [self.stave_at(st, u, bottom(u), dr, gaps) for u in us]
        top = [self.stave_at(st, u, self.stave_top(st, u), dr, gaps) for u in reversed(us)]
        # sides
        side_r = [self.stave_at(st, 1.0, bottom(1.0) + (self.stave_top(st, 1.0) - bottom(1.0)) * j / 30, dr, gaps)
                  for j in range(1, 30)]
        side_l = [self.stave_at(st, 0.0, self.stave_top(st, 0.0) + (bottom(0.0) - self.stave_top(st, 0.0)) * j / 30, dr,
                                gaps) for j in range(1, 30)]
        return pts + side_r + top + side_l

    def stave_normal(self, st, inward=False):
        return self.normal((st['a0'] + st['a1']) / 2 + (st['t0'] + st['t1']) / 4, inward)

    # handle sample points: (screen x, y, depth Z, front-half?) from lug to lug
    def handle_points(self, samples=64):
        R = self.rt + 3.5 * self.px
        yl = self.H * 0.88
        u = (math.cos(self.yaw), 0.0, math.sin(self.yaw))
        nvec = (-math.sin(self.yaw), 0.0, math.cos(self.yaw))  # horizontal, faces the viewer at yaw 0
        k = 1.12
        a = (math.sin(self.swing) * nvec[0], math.cos(self.swing), math.sin(self.swing) * nvec[2])
        pts = []
        for i in range(samples + 1):
            t = math.pi * i / samples
            # lugs sit at the rim; the handle bows out to R*k from the lug axis
            p = [u[j] * R * math.cos(t) + a[j] * R * k * math.sin(t) for j in range(3)]
            p[1] += yl
            sx, sy, Z = self.P(*p)
            radial = (p[0], 0.0, p[2])
            rv = self.rot(*radial)
            pts.append((sx, sy, Z, rv[2] >= 0, p[1]))
        return pts

    def lugs(self):
        yl = self.H * 0.88
        out = []
        for a in (0.0, math.pi):
            n = self.normal(a)
            out.append((self.surf(a, yl, 2.0 * self.px), n))
        return out


def poly(ctx, pts):
    ctx.move_to(pts[0][0], pts[0][1])
    for p in pts[1:]:
        ctx.line_to(p[0], p[1])
    ctx.close_path()


def wood_color(st, k, inward=False):
    if inward:
        return ramp([(-1, INSIDE_DARK), (0.2, INSIDE_DARK), (1.0, INSIDE_MID)], k + st['tone'])
    col = ramp([(-1, WOOD_DARK), (-0.05, WOOD_DARK), (0.25, WOOD_MID), (0.65, WOOD_LIGHT), (1.0, WOOD_HI)], k + st['tone'])
    if st['tint']:
        col = lerp(col, st['tint'], 0.35)
    return col


def draw_staves(ctx, b):
    """Front-facing staves. The gaps between them stay open: the inside (Headgear_Back) or the head shows through."""
    staves = [st for st in b.layout() if b.stave_normal(st)[2] > 0.0]
    for st in staves:
        n = b.stave_normal(st)
        k = b.shade(n)
        col = wood_color(st, k)
        outline = b.stave_outline(st)
        poly(ctx, outline)
        ctx.save()
        ctx.clip_preserve()
        top = b.stave_at(st, 0.5, b.H)
        bot = b.stave_at(st, 0.5, 0)
        g = cairo.LinearGradient(bot[0], bot[1], top[0], top[1])
        g.add_color_stop_rgb(0, *col)
        g.add_color_stop_rgb(1, *lerp(col, WOOD_DARK, 0.25))
        ctx.set_source(g)
        ctx.fill()
        if k > 0.35:
            s = st['sheen']
            hp0 = b.stave_at(st, s, b.H * 0.1)
            hp1 = b.stave_at(st, s + 0.08, b.H * 0.92)
            ctx.set_source_rgba(*WOOD_HI, 0.35 * min(1.0, (k - 0.35) * 2.5))
            ctx.set_line_width(2.2 * SS)
            ctx.set_line_cap(cairo.LINE_CAP_ROUND)
            ctx.move_to(hp0[0], hp0[1])
            ctx.line_to(hp1[0], hp1[1])
            ctx.stroke()
        for s, f0, f1, light in st['grain']:
            # wavy grain line along the stave, like the prop's painted swirls
            ctx.set_source_rgba(*(lerp(col, WOOD_HI, 0.55) if light else lerp(col, WOOD_DARK, 0.65)), 0.85)
            ctx.set_line_width(0.75 * SS)
            steps = 14
            for j in range(steps + 1):
                f = f0 + (f1 - f0) * j / steps
                wob = 0.13 * math.sin(f * 9.0 + s * 20)
                g = b.stave_at(st, min(0.95, max(0.05, s + wob)), b.H * f)
                if j == 0:
                    ctx.move_to(g[0], g[1])
                else:
                    ctx.line_to(g[0], g[1])
            ctx.stroke()
        if st['knot']:
            kp = b.stave_at(st, st['knot'][0], b.H * st['knot'][1])
            ctx.save()
            ctx.translate(kp[0], kp[1])
            ctx.scale(1.4 * SS * max(0.4, n[2]), 2.2 * SS)
            ctx.arc(0, 0, 1, 0, 2 * math.pi)
            ctx.restore()
            ctx.set_source_rgba(*lerp(col, WOOD_DARK, 0.7), 0.9)
            ctx.set_line_width(0.7 * SS)
            ctx.stroke()
        ctx.restore()
        # every stave gets its own dark edge, so gaps and ragged ends read clearly
        poly(ctx, outline)
        ctx.set_source_rgb(*OUTLINE)
        ctx.set_line_width(1.0 * SS)
        ctx.set_line_join(cairo.LINE_JOIN_ROUND)
        ctx.stroke()


def metal_color(k):
    # banded, mirror-like steel: dark reflection band between the mid tone and the bright highlight
    return ramp([(-1, METAL_DARK), (0.0, METAL_DARK), (0.25, METAL), (0.5, lerp(METAL, METAL_DARK, 0.4)),
                 (0.72, METAL_LIGHT), (0.92, METAL_SPEC), (1.0, METAL_SPEC)], k)


def draw_hoop(ctx, b, y0, y1, rivets=True):
    """Shiny steel band around the bucket between heights y0..y1, protruding slightly."""
    segs = 72
    dr = 1.6 * b.px
    for i in range(segs):
        a0 = 2 * math.pi * i / segs
        a1 = 2 * math.pi * (i + 1) / segs
        n = b.normal((a0 + a1) / 2)
        if n[2] <= -0.02:
            continue
        pts = [b.surf(a0, y0, dr), b.surf(a1 + 0.004, y0, dr), b.surf(a1 + 0.004, y1, dr), b.surf(a0, y1, dr)]
        poly(ctx, pts)
        ctx.set_source_rgb(*metal_color(b.shade(n)))
        ctx.fill()
    # white glint along the top of the band on the lit side, a dark reflection low on it, dark edge lines
    for frac, color, width, alpha, lit in ((0.28, METAL_SPEC, 1.1, 0.95, 0.3), (0.72, METAL_DARK, 0.8, 0.5, -1.0),
                                           (0.0, OUTLINE, 0.9, 1.0, None), (1.0, OUTLINE, 0.9, 1.0, None)):
        y = y0 + (y1 - y0) * frac
        prev = None
        for i in range(segs + 1):
            a = 2 * math.pi * i / segs
            n = b.normal(a)
            vis = n[2] > 0.02 and (lit is None or b.shade(n) > lit)
            p = b.surf(a, y, dr)
            if vis and prev is not None:
                ctx.set_source_rgba(*color, alpha)
                ctx.set_line_width(width * SS)
                ctx.set_line_cap(cairo.LINE_CAP_ROUND)
                ctx.move_to(prev[0], prev[1])
                ctx.line_to(p[0], p[1])
                ctx.stroke()
            prev = p if vis else None
    if rivets:
        for st in b.layout()[::2]:
            a = (st['a0'] + st['a1']) / 2 + (st['t0'] + st['t1']) / 4
            n = b.normal(a)
            if n[2] < 0.25:
                continue
            p = b.surf(a, (y0 + y1) / 2, dr + 0.4 * b.px)
            rr = 1.25 * SS * max(0.55, n[2])
            ctx.arc(p[0], p[1], rr, 0, 2 * math.pi)
            ctx.set_source_rgb(*METAL_DARK)
            ctx.fill()
            ctx.arc(p[0] - rr * 0.3, p[1] - rr * 0.3, rr * 0.45, 0, 2 * math.pi)
            ctx.set_source_rgb(*METAL_SPEC)
            ctx.fill()


def ellipse_pts(b, y, dr, count=72):
    return [b.surf(2 * math.pi * i / count, y, dr) for i in range(count)]


def end_cap(b, st):
    """Top end grain of a stave (outer edge to inner edge along its ragged top)."""
    th = -3.0 * b.px
    sub = 8
    us = [j / sub for j in range(sub + 1)]
    outer = [b.stave_at(st, u, b.stave_top(st, u)) for u in us]
    inner = [b.stave_at(st, u, b.stave_top(st, u), th) for u in reversed(us)]
    return outer + inner


def draw_interior(ctx, b):
    """Inside of the bucket: the far staves' inner faces down to the bottom, plus their end grain when the open end
    is seen from above. Goes in the back texture, so the head covers it wherever the head is inside the bucket."""
    H = b.H
    th = -3.0 * b.px
    ctx.set_line_join(cairo.LINE_JOIN_ROUND)
    poly(ctx, ellipse_pts(b, 0.03 * H, th))
    ctx.set_source_rgb(*INSIDE_DARK)
    ctx.fill()
    back = [st for st in b.layout() if b.stave_normal(st, inward=True)[2] > 0.0]
    for st in back:
        k = b.shade(b.stave_normal(st, inward=True))
        poly(ctx, b.stave_outline(st, dr=th, gaps=False, y_from=0.03 * H))
        ctx.set_source_rgb(*wood_color(st, k, inward=True))
        ctx.fill_preserve()
        ctx.set_source_rgb(*INSIDE_DARK)
        ctx.set_line_width(0.9 * SS)
        ctx.stroke()
    if not b.inv:
        # below the waterline the far wall is seen through water (it shows through the front wall's gaps)
        for st in back:
            sub = 10
            us = [j / sub for j in range(sub + 1)]
            pts = [b.stave_at(st, u, 0.03 * H, th, gaps=False) for u in us]
            for u in reversed(us):
                a = st['a0'] + st['t0'] + (st['a1'] + st['t1'] - st['a0'] - st['t0']) * u
                pts.append(b.stave_at(st, u, min(water_height(b, a), b.stave_top(st, u)), th, gaps=False))
            poly(ctx, pts)
            ctx.set_source_rgba(*WATER_DEEP, 0.8)
            ctx.fill()
        for st in back:
            draw_cap(ctx, b, st)


def water_height(b, a):
    """Local height where the (world-level) water surface meets the inner wall at azimuth a. Sets b.spill."""
    th = -3.0 * b.px
    rin = lambda y: b.radius(y) + th
    if b._water_level is None:
        level = b.world_up(0, b.H * b.water)
        low_a, low = None, None
        for st in b.layout():
            for u in (0.0, 0.5, 1.0):
                aa = st['a0'] + (st['a1'] - st['a0']) * u + b.yaw
                y = b.stave_top(st, u)
                h = b.world_up(rin(y) * math.cos(aa), y)
                if low is None or h < low:
                    low, low_a = h, aa - b.yaw
        b.spill = None
        if level > low - 1.5 * b.px:
            level = low - 1.5 * b.px
            b.spill = low_a
        b._water_level = level
    lo, hi = 0.0, b.H * 1.2
    for _ in range(30):
        mid = (lo + hi) / 2
        if b.world_up(rin(mid) * math.cos(a + b.yaw), mid) < b._water_level:
            lo = mid
        else:
            hi = mid
    return lo


def draw_water(ctx, b):
    """Upright bucket: the water surface, level with the world (spills to the lowest stave top if tilted)."""
    th = -3.0 * b.px
    water = []
    for i in range(72):
        a = 2 * math.pi * i / 72
        water.append(b.surf(a, water_height(b, a), th - 0.2 * b.px))
    poly(ctx, water)
    xs = [p[0] for p in water]
    ys = [p[1] for p in water]
    g = cairo.LinearGradient(min(xs), min(ys), max(xs), max(ys))
    g.add_color_stop_rgb(0, *WATER_DEEP)
    g.add_color_stop_rgb(0.55, *WATER)
    g.add_color_stop_rgb(1, *lerp(WATER, WATER_LIGHT, 0.35))
    ctx.set_source(g)
    ctx.fill()
    cxw, cyw = sum(xs) / len(xs), sum(ys) / len(ys)
    w, h = (max(xs) - min(xs)) / 2, (max(ys) - min(ys)) / 2
    ctx.set_line_cap(cairo.LINE_CAP_ROUND)
    for (ox, oy, sc, al) in ((-0.15, -0.05, 0.55, 0.85), (0.25, 0.2, 0.35, 0.7), (-0.35, 0.3, 0.22, 0.6)):
        ctx.save()
        ctx.translate(cxw + ox * w, cyw + oy * h)
        ctx.scale(w * sc, h * sc)
        ctx.arc(0, 0, 1, math.radians(200), math.radians(320))
        ctx.restore()
        ctx.set_source_rgba(*WATER_LIGHT, al)
        ctx.set_line_width(1.3 * SS)
        ctx.stroke()


def draw_spill(ctx, b):
    """Water that slopped over the low lip running down the outside, with a drip at the bottom."""
    # start just round from the low lip toward the viewer, far enough that the trickle shows
    a = None
    for k in range(1, 40):
        for cand in (b.spill + 0.05 * k, b.spill - 0.05 * k):
            if b.normal(cand)[2] >= 0.2:
                a = cand
                break
        if a is not None:
            break
    if a is None:
        return
    rng = random.Random(5)
    pts = []
    steps = 16
    for j in range(steps + 1):
        f = 1.02 - 1.0 * j / steps
        pts.append(b.surf(a + 0.05 * math.sin(j * 1.3) + rng.uniform(-0.02, 0.02), b.H * f, 0.6 * b.px))
    ctx.set_line_cap(cairo.LINE_CAP_ROUND)
    ctx.set_line_join(cairo.LINE_JOIN_ROUND)
    for width, color, alpha in ((3.0, WATER_DEEP, 0.55), (1.8, WATER, 0.8), (0.7, WATER_LIGHT, 0.9)):
        ctx.set_line_width(width * SS)
        ctx.set_source_rgba(*color, alpha)
        ctx.move_to(pts[0][0], pts[0][1])
        for p in pts[1:]:
            ctx.line_to(p[0], p[1])
        ctx.stroke()
    x, y = pts[-1][0], pts[-1][1] + 2.5 * SS
    ctx.save()
    ctx.translate(x, y)
    ctx.move_to(0, -2.6 * SS)
    ctx.curve_to(1.6 * SS, -0.6 * SS, 1.6 * SS, 1.6 * SS, 0, 1.6 * SS)
    ctx.curve_to(-1.6 * SS, 1.6 * SS, -1.6 * SS, -0.6 * SS, 0, -2.6 * SS)
    ctx.restore()
    ctx.set_source_rgb(*WATER)
    ctx.fill_preserve()
    ctx.set_source_rgb(*OUTLINE)
    ctx.set_line_width(0.7 * SS)
    ctx.stroke()
    ctx.arc(x - 0.5 * SS, y - 0.2 * SS, 0.55 * SS, 0, 2 * math.pi)
    ctx.set_source_rgb(*WATER_LIGHT)
    ctx.fill()


def draw_cap(ctx, b, st):
    pts = end_cap(b, st)
    poly(ctx, pts)
    ctx.set_source_rgb(*lerp(END_GRAIN, WOOD_HI, 0.3 + st['tone']))
    ctx.fill_preserve()
    ctx.set_source_rgb(*OUTLINE)
    ctx.set_line_width(0.9 * SS)
    ctx.stroke()


def draw_handle_run(ctx, run):
    if len(run) < 2:
        return
    for width, color in ((5.4, OUTLINE), (3.4, METAL), (1.2, None)):
        if color is None:
            # darker underside of the wire
            ctx.set_line_width(width * SS)
            ctx.set_source_rgba(*METAL_DARK, 0.8)
            ctx.move_to(run[0][0] + 0.7 * SS, run[0][1] + 0.8 * SS)
            for p in run[1:]:
                ctx.line_to(p[0] + 0.7 * SS, p[1] + 0.8 * SS)
            ctx.stroke()
            continue
        ctx.set_line_width(width * SS)
        ctx.set_line_cap(cairo.LINE_CAP_ROUND)
        ctx.set_line_join(cairo.LINE_JOIN_ROUND)
        ctx.set_source_rgb(*color)
        ctx.move_to(run[0][0], run[0][1])
        for p in run[1:]:
            ctx.line_to(p[0], p[1])
        ctx.stroke()
    # light streak offset toward the light (up-left)
    ctx.set_line_width(1.2 * SS)
    ctx.set_source_rgba(*METAL_SPEC, 0.95)
    ctx.move_to(run[0][0] - 0.6 * SS, run[0][1] - 0.7 * SS)
    for p in run[1:]:
        ctx.line_to(p[0] - 0.6 * SS, p[1] - 0.7 * SS)
    ctx.stroke()


def draw_lug(ctx, p, n):
    x, y, _ = p
    r = 3.0 * SS
    ctx.save()
    ctx.translate(x, y)
    ctx.scale(max(0.45, abs(n[2])) if abs(n[0]) < 0.9 else 0.5, 1.0)
    ctx.arc(0, 0, r, 0, 2 * math.pi)
    ctx.restore()
    ctx.set_source_rgb(*OUTLINE)
    ctx.set_line_width(1.6 * SS)
    ctx.stroke_preserve()
    ctx.set_source_rgb(*METAL_LIGHT)
    ctx.fill()
    ctx.arc(x, y, 1.1 * SS, 0, 2 * math.pi)
    ctx.set_source_rgb(*METAL_DARK)
    ctx.fill()


def split_runs(pts, pred):
    runs, cur = [], []
    for p in pts:
        if pred(p):
            cur.append(p)
        else:
            if len(cur) > 1:
                runs.append(cur)
            cur = [p] if False else []
    if len(cur) > 1:
        runs.append(cur)
    return runs


def bounds(b, extra_pts=()):
    pts = []
    for y in (-0.03 * b.H, 1.16 * b.H):
        pts += ellipse_pts(b, y, 3)
    pts += [(p[0], p[1]) for p in b.handle_points()]
    pts += list(extra_pts)
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def to_image(surface):
    w, h = surface.get_width(), surface.get_height()
    data = bytes(surface.get_data())
    # cairo ARGB32 premultiplied BGRA -> PIL RGBa
    im = Image.frombuffer('RGBA', (w, h), data, 'raw', 'BGRa', surface.get_stride(), 1)
    return im.convert('RGBA')


def outline_ring(mask, width_px):
    """Dark outline just outside a silhouette mask (L image), as an RGBA layer."""
    m = mask.point(lambda v: 255 if v > 40 else 0)
    a = m
    for _ in range(int(round(width_px * SS))):
        a = a.filter(ImageFilter.MaxFilter(3))
    a = ImageChops.subtract(a, m).filter(ImageFilter.GaussianBlur(SS * 0.35))
    ring = Image.new('RGBA', mask.size, tuple(int(c * 255) for c in OUTLINE) + (0,))
    ring.putalpha(a)
    return ring


def outlined(im, width_px):
    """Dark silhouette outline under the art (dilated alpha)."""
    a = im.split()[3].point(lambda v: 255 if v > 40 else 0)
    for _ in range(int(round(width_px * SS))):
        a = a.filter(ImageFilter.MaxFilter(3))
    a = a.filter(ImageFilter.GaussianBlur(SS * 0.35))
    base = Image.new('RGBA', im.size, tuple(int(c * 255) for c in OUTLINE) + (0,))
    base.putalpha(a)
    base.alpha_composite(im)
    return base


def render(vid, params, box=None):
    """params: settled Bucket kwargs (see settle)."""
    b = Bucket(**params)
    x0, y0, x1, y1 = box or bounds(b)
    pad = 5
    ox, oy = math.floor(x0 - pad), math.floor(y0 - pad)
    W, Hh = math.ceil(x1 + pad) - ox, math.ceil(y1 + pad) - oy
    # rebuild in supersampled canvas coordinates
    p2 = dict(params)
    p2['center'] = ((params['center'][0] - ox) * SS, (params['center'][1] - oy) * SS)
    for k in ('rt', 'rb', 'H'):
        p2[k] = params[k] * SS
    p2['px'] = SS
    b = Bucket(**p2)

    def layer():
        s = cairo.ImageSurface(cairo.FORMAT_ARGB32, W * SS, Hh * SS)
        return s, cairo.Context(s)

    hp = b.handle_points()
    # handle segments in front of the bucket, and behind it (behind the head, or behind the far wall)
    front = split_runs(hp, lambda p: p[3])
    behind = split_runs(hp, lambda p: not p[3])

    # back texture (Headgear_Back, the body draws over it): handle parts behind, then the inside of a worn bucket (the
    # head is inside it). An upright bucket's inside can't have the head in front of it, so it stays in the front texture.
    sb, cb = layer()
    si, ci = layer()
    draw_interior(ci, b)
    inside_img = outlined(to_image(si), 1.5)
    sf, cf = layer()
    for run in behind:
        draw_handle_run(cb if b.inv else cf, run)
    back_img = to_image(sb)
    if b.inv:
        back_img.alpha_composite(inside_img)

    # front texture
    sbody, cbody = layer()
    if not b.inv:
        draw_water(cbody, b)
    draw_staves(cbody, b)
    # one band round the middle
    draw_hoop(cbody, b, 0.43 * b.H, 0.56 * b.H)
    if not b.inv and b.spill is not None:
        draw_spill(cbody, b)
    if not b.inv:
        for st in b.layout():
            if b.stave_normal(st, inward=True)[2] <= 0.0:
                draw_cap(cbody, b, st)
    # outline round the whole outer wall, gaps included (so the gaps stay open instead of filling with outline)
    sm, cm = layer()
    cm.set_source_rgb(1, 1, 1)
    for st in b.layout():
        if b.stave_normal(st)[2] > 0.0:
            poly(cm, b.stave_outline(st, gaps=False))
            cm.fill()
    body_img = to_image(sbody)
    mask = ImageChops.lighter(to_image(sm).split()[3], body_img.split()[3])
    front_img = to_image(sf)
    if not b.inv:
        front_img.alpha_composite(inside_img)
    front_img.alpha_composite(outline_ring(mask, 1.5))
    front_img.alpha_composite(body_img)
    sl, cl = layer()
    for (p, n) in b.lugs():
        if n[2] > -0.35:
            draw_lug(cl, p, n)
    for run in front:
        draw_handle_run(cl, run)
    front_img.alpha_composite(to_image(sl))

    def down(im):
        # premultiplied downsample
        pm = im.convert('RGBa').resize((W, Hh), Image.LANCZOS)
        return pm.convert('RGBA')

    front_img, back_img = down(front_img), down(back_img)
    meta = dict(origin=[ox, oy], size=[W, Hh])
    return front_img, back_img, meta


def pivot_offset(meta):
    ox, oy = meta['origin']
    w, h = meta['size']
    px, py = BONE[0] - ox, BONE[1] - oy
    return [(px - w / 2) / w, (h / 2 - py) / h]


def main():
    os.makedirs(OUT, exist_ok=True)
    manifest = {}
    adjust = adjustments()
    for vid, spec in VARIANTS.items():
        params, rest = settle(spec, adjust.get(vid, (0.0, 0.0, 0.0)))
        print('placement %d: tilt %.1f deg, contacts %s, centre of mass x %.1f, handle swing %.0f' % (
            vid, params['roll'], [tuple(round(v, 1) for v in (c if isinstance(c, tuple) else (c,))) for c in rest['contacts']],
            rest['com_x'], params['swing']))
        f, bk, meta = render(vid, params)
        name = 'Headgear_PirateBucket_%d' % vid
        f.save(os.path.join(OUT, name + '.png'))
        has_back = bk.getbbox() is not None
        back_path = os.path.join(OUT, name + '_Back.png')
        if has_back:
            bk.save(back_path)
        elif os.path.exists(back_path):
            os.remove(back_path)  # this placement no longer has a back layer
        meta['pivot'] = pivot_offset(meta)
        meta['back'] = has_back
        manifest[name] = meta
        print(name, meta)
    json.dump(manifest, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=1)
    # plain-text pivots for the Unity placement editor (Assets/Editor/PirateBucketPlacement.cs)
    with open(os.path.join(OUT, 'pivots.txt'), 'w') as f:
        for name, meta in sorted(manifest.items()):
            f.write('%s %d %d %.7g %.7g %d\n' % (name, meta['size'][0], meta['size'][1], meta['pivot'][0], meta['pivot'][1],
                                               1 if meta['back'] else 0))
    # bare pig render as the placement editor's reference, bone at render px (236, 266)
    from preview import bare_pig
    bare_pig().save(os.path.join(OUT, 'pig_reference.png'))


if __name__ == '__main__':
    main()
