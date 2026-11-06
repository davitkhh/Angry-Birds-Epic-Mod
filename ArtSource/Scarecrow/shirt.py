# Shirt: torso + sleeves as tessellated 3D surfaces seen through a perspective camera
import math, random, cairo
from paint import *

# model pose + camera, fitted to the user's sketch by fit_camera.py
CAM = dict(yaw=40.0, roll=22.0, camx=0.0, camy=330.0, camz=420.0, pitch=8.0, f=330.0, u0=177.0, v0=294.0, hs=1.0)
HEM_L = 321                    # local y of the hem (roll pivot)
LIGHT = (-0.50, -0.62, 0.62)  # view space (x right, y down, z to viewer)
_l = math.sqrt(sum(v * v for v in LIGHT))
LIGHT = tuple(v / _l for v in LIGHT)
_C = {}


def set_cam(**kw):
    CAM.update(kw)
    th, ro, pi = math.radians(CAM['yaw']), math.radians(CAM['roll']), math.radians(CAM['pitch'])
    _C.update(ct=math.cos(th), st=math.sin(th), cr=math.cos(ro), sr=math.sin(ro), cp=math.cos(pi), sp=math.sin(pi))


import json as _json, os as _os
FIT = {}
_fp = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)), 'camera_fit.json')
if _os.path.exists(_fp):
    FIT = _json.load(open(_fp))
    CAM.update({k: FIT[k] for k in CAM if k in FIT})
set_cam()


def _orient(x, y, z):
    """model rotation (yaw, then roll in the picture plane) + camera pitch: vector -> view axes"""
    X, Y, Z = x * _C['ct'] - z * _C['st'], y, x * _C['st'] + z * _C['ct']
    X, Y = X * _C['cr'] - Y * _C['sr'], X * _C['sr'] + Y * _C['cr']
    # camera looking up by `pitch`: view y = dot(d, down_c), view z(toward viewer) = -dot(d, forward)
    Y, Z = Y * _C['cp'] - Z * _C['sp'], Z * _C['cp'] + Y * _C['sp']
    return (X, Y, Z)


def to_view(x, y, z):
    """local point -> view space (x right, y down, z toward viewer; camera at origin looking -z)"""
    X, Y, Z = x * _C['ct'] - z * _C['st'], (y - HEM_L) * CAM['hs'], x * _C['st'] + z * _C['ct']
    X, Y = X * _C['cr'] - Y * _C['sr'], X * _C['sr'] + Y * _C['cr']
    Y += HEM_L
    dx, dy, dz = X - CAM['camx'], Y - CAM['camy'], Z - CAM['camz']
    return (dx, dy * _C['cp'] - dz * _C['sp'], dz * _C['cp'] + dy * _C['sp'])


def proj(x, y, z):
    vx, vy, vz = to_view(x, y, z)
    d = -vz
    return (CAM['u0'] + CAM['f'] * vx / d, CAM['v0'] + CAM['f'] * vy / d, vz)


def rot(x, y, z):
    return _orient(x, y, z)


def persp(n, p_local):
    """rotate a view-oriented normal so that +z points from the surface point to the camera"""
    v = to_view(*p_local)
    vd = norm((-v[0], -v[1], -v[2]))
    # rotation taking vd to (0,0,1) (Rodrigues)
    ax = (vd[1], -vd[0], 0.0)
    s = math.hypot(ax[0], ax[1])
    c = vd[2]
    if s < 1e-6:
        return n
    k = (ax[0] / s, ax[1] / s, 0.0)
    kn = cross(k, n)
    kd = sum(k[i] * n[i] for i in range(3))
    return tuple(n[i] * c + kn[i] * s + k[i] * kd * (1 - c) for i in range(3))


def norm(v):
    l = math.sqrt(sum(a * a for a in v)) or 1
    return tuple(a / l for a in v)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


# ---------- plaid ----------
BASE = hexc('#4f86d0')
SETT = [  # (offset, width, colour) along one axis, period P
    (0.0, 10.0, hexc('#1f3570')),
    (10.0, 1.4, hexc('#2b4a8e')),
    (15.5, 4.0, hexc('#2f5aa6')),
    (22.0, 1.3, hexc('#dbe7f6')),
    (27.5, 1.0, hexc('#c8473c')),
]
P = 34.0


def sett(t):
    t %= P
    for off, w, col in SETT:
        if off <= t < off + w:
            return col
    return BASE


def plaid(u, v):
    a = sett(u)
    b = sett(v + 4)
    c = mix(mix(a, b, 0.5), BASE, 0.28)
    # twill: fine diagonal ribs
    k = 1.0 + 0.045 * math.sin((u + v) * 2 * math.pi / 2.2)
    return scale_rgb(c, k)


# ---------- shading ----------
LIT_TINT = hexc('#fff3cf')
SHADE_K = (0.58, 0.63, 0.80)
RIM = hexc('#7fd0e6')


def quad(pts, col, n, ao=1.0):
    ndl = sum(n[i] * LIGHT[i] for i in range(3))
    rim = 0.0
    if n[0] > 0.78 and n[2] < 0.6:
        rim = min(1, (n[0] - 0.78) / 0.12)
    z = sum(p[2] for p in pts) / 4
    return (z, [(p[0], p[1]) for p in pts], col, ndl, rim, ao)


def draw_quads(quads):
    """-> (albedo RGBA, light L, rim L, ao L) layers"""
    quads.sort(key=lambda q: q[0])
    layers = [new_surface() for _ in range(4)]
    for _, c in layers:
        c.set_antialias(cairo.ANTIALIAS_NONE)
    for z, pts, col, ndl, rim, ao in quads:
        vals = (col, ((ndl + 1) / 2,) * 3, (rim,) * 3, (ao,) * 3)
        for (s_, c), v in zip(layers, vals):
            c.set_source_rgba(v[0], v[1], v[2], 1)
            poly(c, pts)
            c.fill()
    imgs = [to_pil(s_) for s_, c in layers]
    return imgs[0], imgs[1].getchannel('R'), imgs[2].getchannel('R'), imgs[3].getchannel('R')


def shade_layers(alb, light, rim, ao, lit_t=0.66, sh_t=0.14, soft=1.0):
    """clean cel bands from a smoothed light map + soft gradient, rim and AO"""
    a = alb.getchannel('A')
    light = blur(light, soft)
    lt = int((lit_t + 1) * 127.5)
    st = int((sh_t + 1) * 127.5)
    lit = mul(levels(light, lt - 3, lt + 3), a)
    shd = mul(inv(levels(light, st - 3, st + 3)), a)
    out = alb.copy()
    grad = light.point(lambda v: int(255 * min(1, 0.86 + 0.16 * (v / 127.5 - 1) + 0.06)))
    out = ImageChops.multiply(out, Image.merge('RGBA', (grad, grad, grad, Image.new('L', grad.size, 255))))
    paint(out, LIT_TINT, lit, 0.24)
    sk = Image.new('RGBA', out.size, tuple(int(v * 255) for v in SHADE_K) + (255,))
    out = Image.composite(ImageChops.multiply(out, sk), out, shd)
    paint(out, RIM, blur(mul(rim, a), 0.6), 0.34)
    aob = blur(ao, 1.5)
    out = ImageChops.multiply(out, Image.merge('RGBA', (aob, aob, aob.point(lambda v: min(255, int(v * 1.06))), Image.new('L', aob.size, 255))))
    out.putalpha(a)
    return out


def lerp_profile(prof, y):
    for i in range(len(prof) - 1):
        y0, a0, b0 = prof[i]
        y1, a1, b1 = prof[i + 1]
        if y0 <= y <= y1:
            t = (y - y0) / (y1 - y0)
            t = t * t * (3 - 2 * t)
            return a0 + (a1 - a0) * t, b0 + (b1 - b0) * t
    return prof[-1][1], prof[-1][2]


# torso profile: (y, half-width a, half-depth b)
PROFILE = [
    (176, 22, 17), (180, 36, 25), (186, 46, 31), (196, 54, 36),
    (230, 58, 39), (270, 61, 41), (300, 63, 42), (322, 64, 43),
]
CINCH_Y = 2000   # no waist tie (A-line shirt)
NE = 3.2  # superellipse exponent (boxy front/side panels)


SCALE_XZ = (FIT.get('wx', 1.0), FIT.get('wz', 1.0))   # torso width / depth scale (from the camera fit)


def section(phi, y, lump=True):
    a, b = lerp_profile(PROFILE, y)
    a *= SCALE_XZ[0]
    b *= SCALE_XZ[1]
    s, c = math.sin(phi), math.cos(phi)
    x = a * math.copysign(abs(s) ** (2 / NE), s)
    z = b * math.copysign(abs(c) ** (2 / NE), c)
    if lump:
        r = math.hypot(x, z) or 1
        k = 1 + fold_lift(phi, y) / r
        x *= k
        z *= k
    return (x, y, z)


# fold ridges in (phi, y): (phi0, y0, phi1, y1, amplitude HR, width HR)
FOLDS = [
    (1.30, 204, 0.35, 262, 2.6, 5.5),    # near armpit tension fold
    (1.05, 214, 0.55, 300, 1.8, 4.5),
    (-1.25, 204, -0.40, 256, 2.4, 5.0),  # far armpit
    (-0.30, 196, -0.38, 306, 1.6, 6.0),  # front drape beside the placket
    (0.30, 236, 0.20, 312, 1.8, 5.0),
    (1.62, 214, 1.70, 310, 2.0, 6.0),    # side drape
    (0.90, 270, 1.20, 314, 1.6, 4.0),
]


def fold_lift(phi, y):
    lift = 0.0
    for p0, y0, p1, y1, amp, w in FOLDS:
        ax, ay = p0 * 45, y0
        bx, by = p1 * 45, y1
        px, py = phi * 45, y
        dx, dy = bx - ax, by - ay
        t = max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
        d = math.hypot(px - ax - dx * t, py - ay - dy * t)
        taper = math.sin(math.pi * t) ** 0.6
        lift += amp * taper * math.exp(-(d / w) ** 2)
    # gathers pulled in by the twine: vertical pleats above and below the cinch
    g = math.exp(-((y - CINCH_Y) / 11) ** 2)
    below = max(0, min(1, (y - CINCH_Y) / 24))
    lift += (2.4 * g + 2.2 * below) * math.sin(phi * 11 + 0.7 + 0.4 * math.sin(y * 0.2))
    ramp = max(0, (y - 296) / 26)
    lift += 1.8 * ramp * math.sin(phi * 8 + 0.7 + 0.3 * math.sin(y * 0.2))
    # chest/belly fullness from the hay stuffing
    lift += 2.0 * math.exp(-((y - 238) / 30) ** 2) * max(0, math.cos(phi)) ** 2
    return lift


def hem_y(phi):
    base = 321 + 3 * math.sin(phi * 2.3 + 0.4)
    # tears / jagged frayed edge
    j = ((phi * 37.0) % 1.0)
    tooth = 2.2 * abs(j - 0.5) * 2
    notch = 0
    for c, w, d in ((-0.35, 0.10, 7), (0.55, 0.07, 5), (1.15, 0.12, 6)):
        if abs(phi - c) < w:
            notch = max(notch, d * (1 - abs(phi - c) / w))
    return base - tooth - notch


ARC = {}


def arc_u(phi, y):
    """arc length from the front centre at height y (rounded y for caching)"""
    key = int(y)
    if key not in ARC:
        tab = []
        acc = 0
        prev = section(-math.pi, key, lump=False)
        n = 720
        for i in range(n + 1):
            ph = -math.pi + 2 * math.pi * i / n
            p = section(ph, key, lump=False)
            acc += math.hypot(p[0] - prev[0], p[2] - prev[2])
            tab.append(acc)
            prev = p
        mid = tab[n // 2]
        ARC[key] = [t - mid for t in tab]
    tab = ARC[key]
    i = (phi + math.pi) / (2 * math.pi) * 720
    i0 = max(0, min(719, int(i)))
    f = i - i0
    return tab[i0] * (1 - f) + tab[i0 + 1] * f


def phi_at(u, y):
    """inverse of arc_u: arc length from the front centre -> phi"""
    arc_u(0, y)
    tab = ARC[int(y)]
    lo, hi = 0, 720
    while hi - lo > 1:
        m = (lo + hi) // 2
        if tab[m] < u:
            lo = m
        else:
            hi = m
    f = (u - tab[lo]) / ((tab[hi] - tab[lo]) or 1)
    return -math.pi + 2 * math.pi * (lo + f) / 720


def uv_pt(u, y, lift=0.0):
    return surf_pt(phi_at(u, y), y, lift)


def uv_poly(pts, lift=0.0, step=1.5):
    """polyline in (u, y) surface coords -> densified screen points"""
    out = []
    for i in range(len(pts) - 1):
        (u0, y0), (u1, y1) = pts[i], pts[i + 1]
        n = max(1, int(math.hypot(u1 - u0, y1 - y0) / step))
        for k in range(n):
            t = k / n
            p = uv_pt(u0 + (u1 - u0) * t, y0 + (y1 - y0) * t, lift)
            out.append((p[0], p[1]))
    p = uv_pt(*pts[-1], lift)
    out.append((p[0], p[1]))
    return out


def uv_normal(u, y):
    return torso_normal(phi_at(u, y), y)


def surf_pt(phi, y, lift=0.0):
    """screen point on torso surface (+lift along normal)"""
    p = section(phi, y)
    if lift:
        n = torso_normal(phi, y, local=True)
        p = (p[0] + n[0] * lift, p[1] + n[1] * lift, p[2] + n[2] * lift)
    return proj(*p)


def torso_normal(phi, y, local=False):
    e = 0.01
    p0 = section(phi, y)
    pu = section(phi + e, y)
    pv = section(phi, y + 0.5)
    du = tuple(pu[i] - p0[i] for i in range(3))
    dv = tuple(pv[i] - p0[i] for i in range(3))
    n = norm(cross(dv, du))
    # make it point outward
    if n[0] * p0[0] + n[2] * p0[2] < 0:
        n = tuple(-v for v in n)
    return n if local else norm(persp(rot(*n), p0))


def torso_ao(phi, y):
    """darken under the collar, armpits and toward the hem"""
    ao = 1.0
    ao -= 0.10 * max(0, 1 - (y - 186) / 14) * max(0, math.cos(phi))
    ao -= 0.22 * math.exp(-((y - CINCH_Y - 3) / 5) ** 2)
    ao -= 0.10 * max(0, (y - 306) / 16)
    for side in (1, -1):  # armpits
        d = math.hypot((phi - side * 1.35) * 30, (y - 206) * 0.6)
        ao -= 0.18 * max(0, 1 - d / 20)
    return max(0.6, ao)


def torso_quads():
    quads = []
    dphi = 2 * math.pi / 300
    dy = 0.6
    y = PROFILE[0][0]
    while y < 330:
        phi = -math.pi
        while phi < math.pi:
            hy = hem_y(phi + dphi / 2)
            if y < hy:
                y1 = min(y + dy, hy)
                n = torso_normal(phi + dphi / 2, y + dy / 2)
                if n[2] > -0.02:
                    pts = [surf_pt(phi, y), surf_pt(phi + dphi, y),
                           surf_pt(phi + dphi, y1 + 0.05), surf_pt(phi, y1 + 0.05)]
                    u = arc_u(phi + dphi / 2, y)
                    quads.append(quad(pts, plaid(u, y), n, torso_ao(phi, y)))
            phi += dphi
        y += dy
    return quads


# ---------- sleeves ----------
class Sleeve:
    def __init__(self, start, d, length, sag, r0, r1, twist=0.0, seed=1):
        self.s = start          # local 3D start (inside the shoulder)
        self.d = norm(d)        # local direction
        self.L = length
        self.sag = sag
        self.r0, self.r1 = r0, r1
        self.twist = twist
        self.seed = seed
        # perpendicular frame
        up = (0, -1, 0)
        self.e1 = norm(cross(self.d, up))
        self.e2 = norm(cross(self.e1, self.d))

    def center(self, t):
        s, d, L = self.s, self.d, self.L
        sag = self.sag * 4 * t * (1 - t) * 0 + self.sag * t * t
        return (s[0] + d[0] * L * t, s[1] + d[1] * L * t + sag, s[2] + d[2] * L * t)

    def radius(self, t, psi):
        r = self.r0 + (self.r1 - self.r0) * t
        r *= 1 + 0.06 * math.sin(t * math.pi * 2.2 + 0.5)          # lumpy stuffing
        # bunched wrinkles around the elbow + soft crumples
        env = max(0, math.sin(math.pi * (t - 0.30) / 0.45)) if 0.30 < t < 0.75 else 0
        r *= 1 + 0.07 * env * max(0, math.sin(t * 46 + psi * 0.8 + self.seed)) ** 2
        r *= 1 + 0.035 * math.sin(psi * 2 + t * 7 + self.seed) * (0.4 + t)
        if t > 0.84:   # rolled cuff
            r *= 1.12 + 0.03 * math.sin(psi * 5)
        return r

    def point(self, t, psi, lift=0.0):
        c = self.center(t)
        r = self.radius(t, psi) + lift
        a = psi + self.twist * t
        ca, sa = math.cos(a), math.sin(a)
        return tuple(c[i] + (self.e1[i] * ca + self.e2[i] * sa) * r for i in range(3))

    def normal(self, t, psi):
        e = 0.01
        p0 = self.point(t, psi)
        p1 = self.point(t + e, psi)
        p2 = self.point(t, psi + e)
        n = norm(cross(tuple(p1[i] - p0[i] for i in range(3)), tuple(p2[i] - p0[i] for i in range(3))))
        c = self.center(t)
        if sum(n[i] * (p0[i] - c[i]) for i in range(3)) < 0:
            n = tuple(-v for v in n)
        return norm(persp(rot(*n), p0))

    def quads(self, t0=0.0, t1=1.0):
        quads = []
        nt, npsi = 140, 120
        circ = 2 * math.pi * (self.r0 + self.r1) / 2
        for i in range(nt):
            ta = t0 + (t1 - t0) * i / nt
            tb = t0 + (t1 - t0) * (i + 1) / nt
            for j in range(npsi):
                pa = 2 * math.pi * j / npsi
                pb = 2 * math.pi * (j + 1) / npsi
                n = self.normal((ta + tb) / 2, (pa + pb) / 2)
                if n[2] < -0.05:
                    continue
                pts = [proj(*self.point(ta, pa)), proj(*self.point(tb, pa)),
                       proj(*self.point(tb, pb)), proj(*self.point(ta, pb))]
                u = (pa + pb) / 2 / (2 * math.pi) * circ
                v = (ta + tb) / 2 * self.L
                col = plaid(v + 3, u)
                tm = (ta + tb) / 2
                ao = 1.0
                if tm < 0.12:      # shadow where it tucks into the shoulder
                    ao -= 0.25 * (1 - tm / 0.12)
                if 0.82 < tm < 0.86:  # cuff fold line
                    ao -= 0.22
                quads.append(quad(pts, col, n, ao))
        return quads

    def cuff_ring(self, t=1.0, lift=0.0, n=48):
        return [proj(*self.point(t, 2 * math.pi * k / n, lift)) for k in range(n)]
