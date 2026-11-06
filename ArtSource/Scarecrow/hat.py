# Straw farmer hat as a real 3D object, pushed back on the head (brim underside toward the viewer)
import math, random
from paint import *
import shirt as S
from hay import hay_layer

R_IN, R_OUT = 37.0, 80.0
CROWN_H = 54.0
PITCH = math.radians(30)     # tipped back
ROLL = math.radians(28)      # crown leans to screen right
YAW = math.radians(-10)      # turned with the head (to the left)
HAT_C = (226, 112)           # screen pos of the head opening's centre
STRAW = hexc('#f4cf45')
STRAW_UNDER = hexc('#e2ac2c')
STRAW_LINE = hexc('#8a5a12')
BAND = hexc('#b8352b')


def xform(p):
    x, y, z = p
    # pitch (about x): top goes away from the viewer
    y, z = y * math.cos(PITCH) - z * math.sin(PITCH), y * math.sin(PITCH) + z * math.cos(PITCH)
    # roll (about z)
    x, y = x * math.cos(ROLL) - y * math.sin(ROLL), x * math.sin(ROLL) + y * math.cos(ROLL)
    # yaw (about y)
    x, z = x * math.cos(YAW) + z * math.sin(YAW), -x * math.sin(YAW) + z * math.cos(YAW)
    return (x, y, z)


_D0 = -S.to_view(0.0, 150.0, 0.0)[2]          # camera depth at head height (local units)
_PX = S.CAM['f'] / _D0                         # screen px per local unit there


def scr(p):
    """hat point -> screen, with the same perspective as the shirt camera"""
    x, y, z = xform(p)
    k = _D0 / (_D0 - z / _PX)
    return (HAT_C[0] + x * k, HAT_C[1] + y * k, z)


def brim_pt(r, th):
    t = (r - R_IN) / (R_OUT - R_IN)
    # brim curls up at the sides, dips a little front/back, slight wave from wear
    y = -7.0 * t ** 1.6 * (0.55 + 0.45 * math.cos(2 * th)) + 2.0 * t * math.sin(th * 3 + 0.8)
    rr = r * (1 + 0.025 * math.sin(th * 5 + 1.0) * t)
    return (rr * math.cos(th), y, rr * math.sin(th))


def crown_pt(h, th):
    """h in [0,1] up the side wall; >1 continues over the top to the centre"""
    if h <= 1:
        r = R_IN * (1 - 0.10 * h ** 1.5)
        pinch = 1 - 0.10 * h * max(0, math.sin(th)) ** 4   # pinched front
        return (r * math.cos(th) * pinch, -CROWN_H * h, r * math.sin(th) * pinch)
    k = min(1, h - 1)               # 0 at the rim of the top, 1 at centre
    r = R_IN * 0.90 * (1 - k)
    x, z = r * math.cos(th), r * math.sin(th)
    crease = 2.5 * math.exp(-(x / 9) ** 2) * (1 - k * 0.3)   # centre dent, front to back
    return (x, -CROWN_H - 3.5 * (1 - (1 - k) ** 2) + crease * k ** 0.5, z)


def nrm(f, a, b, ea, eb, outward):
    p0 = f(a, b)
    p1 = f(a + ea, b)
    p2 = f(a, b + eb)
    n = S.norm(S.cross(tuple(p1[i] - p0[i] for i in range(3)), tuple(p2[i] - p0[i] for i in range(3))))
    if sum(n[i] * outward[i] for i in range(3)) < 0:
        n = tuple(-v for v in n)
    return n


def straw_brim(r, th, under):
    # coiled braid: rings + diagonal plait ticks
    ring = (r - R_IN) % 3.4
    k = 1.0
    if ring < 0.55:
        k = 0.80
    tick = math.sin(th * r * 1.6 + (r // 3.4) * 1.7)
    k *= 1 + 0.07 * tick
    base = STRAW_UNDER if under else STRAW
    if R_IN + 2 < r < R_IN + 5.5 and not under:
        base = scale_rgb(STRAW, 0.9)
    return scale_rgb(base, k)


def straw_crown(h, th):
    if h <= 1:
        v = h * CROWN_H
        if 2.0 < v < 8.5:
            col = BAND
            if 2.0 < v < 2.8 or 7.7 < v < 8.5:
                col = scale_rgb(BAND, 0.7)
            return col
        ring = v % 3.2
        k = 0.80 if ring < 0.55 else 1.0
        k *= 1 + 0.07 * math.sin(th * 40 + (v // 3.2) * 1.7)
        return scale_rgb(STRAW, k)
    k = 1 - min(1, h - 1)
    ring = (k * 33) % 3.2
    kk = 0.80 if ring < 0.55 else 1.0
    return scale_rgb(STRAW, kk * (1 + 0.07 * math.sin(th * 30 * k + 1)))


def hat_quads():
    quads = []
    nth = 220
    # brim
    nr = 60
    for i in range(nr):
        ra = R_IN + (R_OUT - R_IN) * i / nr
        rb = R_IN + (R_OUT - R_IN) * (i + 1) / nr
        for j in range(nth):
            ta = 2 * math.pi * j / nth
            tb = 2 * math.pi * (j + 1) / nth
            pts = [scr(brim_pt(ra, ta)), scr(brim_pt(rb, ta)), scr(brim_pt(rb, tb)), scr(brim_pt(ra, tb))]
            n_top = xform(nrm(brim_pt, (ra + rb) / 2, (ta + tb) / 2, 0.3, 0.01, (0, -1, 0)))
            under = n_top[2] < 0
            n = tuple(-v for v in n_top) if under else n_top
            col = straw_brim((ra + rb) / 2, (ta + tb) / 2, under)
            ao = 1.0
            if under and (ra - R_IN) < 7:
                ao = 0.72 + 0.04 * (ra - R_IN)   # dark near the head opening
            q = S.quad(pts, col, n, ao)
            quads.append(q + ('brim',))
    # crown wall + top
    nh = 60
    for i in range(nh):
        ha = 2.0 * i / nh
        hb = 2.0 * (i + 1) / nh
        for j in range(nth):
            ta = 2 * math.pi * j / nth
            tb = 2 * math.pi * (j + 1) / nth
            hm, tm = (ha + hb) / 2, (ta + tb) / 2
            c0 = crown_pt(hm, tm)
            out = (c0[0], -0.3 if hm <= 1 else -1, c0[2])
            n = xform(nrm(crown_pt, hm, tm, 0.01, 0.01, out))
            if n[2] < -0.02:
                continue
            pts = [scr(crown_pt(ha, ta)), scr(crown_pt(hb, ta)), scr(crown_pt(hb, tb)), scr(crown_pt(ha, tb))]
            quads.append(S.quad(pts, straw_crown(hm, tm), n, 1.0) + ('crown',))
    return quads


def head_z(x, y):
    import head as HD
    dx, dy = x - HD.HC[0], y - HD.HC[1]
    if math.hypot(dx, dy) >= HD.head_R(math.atan2(dy, dx)):
        return -999
    return HD._z_at(dx, dy)


def head_depth(x, y):
    """approx. depth of the felt head's front surface at screen (x, y)"""
    hx, hy = 186, 145
    dx, dy = (x - hx) / 64, (y - hy) / 60
    d = 1 - dx * dx - dy * dy
    return 48 * math.sqrt(d) if d > 0 else -999


def hat_layers():
    quads = hat_quads()
    back, front = [], []
    for q in quads:
        z, pts = q[0], q[1]
        cx = sum(p[0] for p in pts) / 4
        cy = sum(p[1] for p in pts) / 4
        # hat origin sits ~on top of the head, 10px behind the face plane
        # in front only where the hat is nearer than the felt head's surface
        (front if z > head_z(cx, cy) + 2 else back).append(q[:6])
    # crown interior seen through the opening (behind the head)
    s_, c = new_surface()
    ring = [scr((R_IN * 0.98 * math.cos(t), 0.5, R_IN * 0.98 * math.sin(t)))[:2]
            for t in [2 * math.pi * i / 64 for i in range(64)]]
    c.set_source_rgba(*hexc('#6a4210'))
    poly(c, ring)
    c.fill()
    interior = to_pil(s_)
    res = []
    for qs in (back, front):
        img = S.shade_layers(*S.draw_quads(qs), lit_t=0.55, sh_t=-0.25, soft=0.8) if qs else blank()
        res.append(img)
    # one outline for the whole hat, split the same way
    whole = ImageChops.lighter(res[0].getchannel('A'), res[1].getchannel('A'))
    o = dilate(whole, 1.7)
    for k in (0, 1):
        L = blank()
        ring = mul(o, inv(res[k].getchannel('A')))
        if k == 1:
            # front layer only outlines its own edge
            ring = mul(sub(dilate(res[1].getchannel('A'), 1.7), res[1].getchannel('A')), o)
        paint(L, STRAW_LINE, ring if k == 1 else o)
        if k == 0:
            L.alpha_composite(interior)
        L.alpha_composite(res[k])
        res[k] = L
    # frayed straw ends poking out of the brim edge
    rnd = random.Random(21)
    strands = []
    for i in range(26):
        th = rnd.uniform(0, 2 * math.pi)
        a = scr(brim_pt(R_OUT - 1.5, th))
        b = scr(brim_pt(R_OUT + rnd.uniform(4, 9), th + rnd.uniform(-0.08, 0.08)))
        m = ((a[0] + b[0]) / 2 + rnd.uniform(-1, 1), (a[1] + b[1]) / 2 + rnd.uniform(-1, 1))
        strands.append(((a[0], a[1]), m, (b[0], b[1]), 1.0, a[2]))
    fb = [s[:4] for s in strands if s[4] < 0]
    ff = [s[:4] for s in strands if s[4] >= 0]
    if fb:
        L = hay_layer(fb, seed=5, under=False)
        L.alpha_composite(res[0])
        res[0] = L
    # the lone straw sticking up from the brim (sketch)
    a = scr(brim_pt(R_OUT - 6, math.radians(200)))
    ff.append(((a[0], a[1]), (a[0] - 1, a[1] - 9), (a[0] + 1.5, a[1] - 19), 1.5))
    if ff:
        res[1].alpha_composite(hay_layer(ff, seed=6, under=False))
    return res
