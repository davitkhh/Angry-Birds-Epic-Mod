# Felt minion-pig head (silhouette = the real MinionPig Body.png), button eyes, stitched features
import math, random
from paint import *
from hay import hay_layer, burst
import shirt as S

PIGS = '/home/davit/Downloads/3.0.1Decomp/Assets/content/4_Characters/'
BODY_PNG = PIGS + '2_Pigs/1_MinionPig/BodyParts/Body.png'
SNOUT_PNG = PIGS + '8_Base/1_MinionPig/BodyParts/Snout.png'

HX0, HY0 = 131, 84     # canvas position of Body.png's top-left
HSCALE = 1.06          # a touch bigger than a minion pig (stuffed head)

FELT = hexc('#3cb425')
FELT_LIGHT = hexc('#62cc45')
FELT_PALE = hexc('#a9d46a')
FELT_DARK = hexc('#22900f')
FELT_LINE = hexc('#1c5504')
FELT_RIM = hexc('#3e8078')
THREAD = hexc('#173f05')
SNOUT = hexc('#76c94a')
SNOUT_DARK = hexc('#4a9a28')
SNOUT_PALE = hexc('#cdee8e')


def bpt(x, y):
    """Body.png pixel coords -> canvas HR coords"""
    return (HX0 + x * HSCALE, HY0 + y * HSCALE)


def sprite_mask(path, x, y, scale, lo=100, hi=160, channel='A'):
    im = Image.open(path).convert('RGBA')
    w, h = im.size
    ch = im.getchannel(channel) if channel == 'A' else im.convert('L')
    big = ch.resize((int(w * scale * SS), int(h * scale * SS)), Image.BICUBIC)
    m = Image.new('L', (W * SS, H * SS), 0)
    m.paste(big, (int(x * SS), int(y * SS)))
    return levels(m, lo, hi)


def radial(mask, center, r, c_in, c_out):
    s, c = new_surface()
    g = cairo.RadialGradient(center[0], center[1], 0, center[0], center[1], r)
    g.add_color_stop_rgba(0, *c_in)
    g.add_color_stop_rgba(1, *c_out)
    c.set_source(g)
    c.paint()
    return clip_alpha(to_pil(s), mask)


def felt_fibres(mask, base, n, seed, length=(1.2, 3.0), width=0.28, alpha=0.30):
    rnd = random.Random(seed)
    s, c = new_surface()
    bb = mask.getbbox()
    x0, y0, x1, y1 = [v / SS for v in bb]
    for i in range(n):
        x, y = rnd.uniform(x0, x1), rnd.uniform(y0, y1)
        a = rnd.uniform(0, 2 * math.pi)
        L = rnd.uniform(*length)
        k = rnd.choice((1.25, 1.18, 0.82, 0.75))
        col = scale_rgb(base, k)
        c.set_source_rgba(col[0], col[1], col[2], alpha)
        c.set_line_width(width)
        c.move_to(x, y)
        c.curve_to(x + math.cos(a) * L * 0.4, y + math.sin(a) * L * 0.4 + rnd.uniform(-0.6, 0.6),
                   x + math.cos(a + 0.6) * L * 0.7, y + math.sin(a + 0.6) * L * 0.7,
                   x + math.cos(a + 0.3) * L, y + math.sin(a + 0.3) * L)
        c.stroke()
    return clip_alpha(to_pil(s), mask)


def mottle(img, mask, scale, amount, seed):
    n = noise(scale, 40, seed)
    light = levels(n, 140, 200)
    dark = inv(levels(n, 60, 116))
    paint(img, (1, 1, 0.9), mul(light, mask), amount)
    paint(img, (0, 0.1, 0), mul(dark, mask), amount)


def stitch_line(c, pts, color, w=0.75, dash=(2.0, 1.4), closed=False):
    c.save()
    c.set_source_rgba(*color)
    c.set_line_width(w)
    c.set_line_cap(cairo.LINE_CAP_ROUND)
    c.set_dash(list(dash))
    smooth(c, pts, closed=closed)
    c.stroke()
    c.restore()


def button(c, cx, cy, rx, ry, col, thread, holes=4, rot=0.0):
    def ell(sx, sy, k=1.0, n=40):
        pts = []
        for i in range(n):
            a = 2 * math.pi * i / n
            x, y = rx * k * math.cos(a), ry * k * math.sin(a)
            pts.append((cx + sx + x * math.cos(rot) - y * math.sin(rot), cy + sy + x * math.sin(rot) + y * math.cos(rot)))
        return pts
    # shadow on the felt
    c.set_source_rgba(0.02, 0.15, 0.0, 0.45)
    poly(c, ell(1.2, 1.8, 1.05))
    c.fill()
    # body + outline
    c.set_source_rgba(*hexc('#0c0c10'))
    poly(c, ell(0, 0, 1.12))
    c.fill()
    c.set_source_rgba(*col)
    poly(c, ell(0, 0, 1.0))
    c.fill()
    # raised rim: light on upper-left, dark lower-right; dished centre
    c.save()
    poly(c, ell(0, 0, 1.0))
    c.clip()
    g = cairo.LinearGradient(cx - rx, cy - ry, cx + rx, cy + ry)
    g.add_color_stop_rgba(0, 1, 1, 1, 0.38)
    g.add_color_stop_rgba(0.5, 1, 1, 1, 0.0)
    g.add_color_stop_rgba(1, 0, 0, 0, 0.35)
    c.set_source(g)
    c.paint()
    c.restore()
    c.set_source_rgba(*scale_rgb(col, 0.55))
    poly(c, ell(0, 0, 0.70))
    c.fill()
    c.save()
    poly(c, ell(0, 0, 0.70))
    c.clip()
    g = cairo.LinearGradient(cx - rx, cy - ry, cx + rx, cy + ry)
    g.add_color_stop_rgba(0, 0, 0, 0, 0.35)
    g.add_color_stop_rgba(1, 1, 1, 1, 0.18)
    c.set_source(g)
    c.paint()
    c.restore()
    # holes + crossed thread
    d = 0.30
    d = 0.36
    hs = [(-d, -d), (d, -d), (d, d), (-d, d)] if holes == 4 else [(-d, 0), (d, 0)]
    pts = [(cx + rx * hx * math.cos(rot) - ry * hy * math.sin(rot),
            cy + rx * hx * math.sin(rot) + ry * hy * math.cos(rot)) for hx, hy in hs]
    for p in pts[:4]:
        c.set_source_rgba(0.02, 0.02, 0.03, 1)
        c.arc(p[0], p[1], rx * 0.11, 0, 2 * math.pi)
        c.fill()
    pairs = [(0, 2), (1, 3), (0, 1), (1, 2), (2, 3), (3, 0)] if holes == 4 else [(0, 1)]
    if holes == 4:
        # spokes through the centre: the 'asterisk' sewing from the sketch
        pts = pts + [((pts[0][0] + pts[1][0]) / 2, (pts[0][1] + pts[1][1]) / 2),
                     ((pts[1][0] + pts[2][0]) / 2, (pts[1][1] + pts[2][1]) / 2),
                     ((pts[2][0] + pts[3][0]) / 2, (pts[2][1] + pts[3][1]) / 2),
                     ((pts[3][0] + pts[0][0]) / 2, (pts[3][1] + pts[0][1]) / 2)]
        pairs = [(0, 2), (1, 3), (4, 6), (5, 7)]
    for a, b in pairs:
        for w, col_ in ((1.6, hexc('#3a2a10')), (1.05, thread)):
            c.set_source_rgba(*col_)
            c.set_line_width(w)
            c.set_line_cap(cairo.LINE_CAP_ROUND)
            c.move_to(*pts[a])
            c.line_to(*pts[b])
            c.stroke()
    # thread highlight
    c.set_source_rgba(1, 1, 1, 0.5)
    c.set_line_width(0.35)
    c.move_to(pts[0][0] + 0.3, pts[0][1] - 0.2)
    c.line_to((pts[0][0] + pts[2 if holes == 4 else 1][0]) / 2 + 0.3, (pts[0][1] + pts[2 if holes == 4 else 1][1]) / 2 - 0.2)
    c.stroke()
    # glossy specular (like the pigs' pupil shine)
    hl = (cx - rx * 0.48, cy - ry * 0.52)
    c.set_source_rgba(1, 1, 1, 0.9)
    c.save()
    c.translate(*hl)
    c.rotate(-0.6)
    c.scale(rx * 0.30, ry * 0.16)
    c.arc(0, 0, 1, 0, 2 * math.pi)
    c.restore()
    c.fill()
    c.set_source_rgba(1, 1, 1, 0.75)
    c.arc(cx + rx * 0.45, cy + ry * 0.5, rx * 0.08, 0, 2 * math.pi)
    c.fill()


# ---- 3D felt head (fitted to the user's sketch; screen-space placement) ----
HC = (232.0, 127.0)       # centre of the head outline (screen)
HZ = 46.0                 # depth of the inflated felt head
HROLL = 0.0
CR_, SR_ = 1.0, 0.0
FAR_EYE = (225.0, 101.0)
NEAR_EYE = (252.5, 112.5)
SNOUT_C = (220.0, 123.5)
EYE_R = 8.6

# head silhouette traced from the sketch (screen HR), clockwise from the upper left
OUTLINE = [(186, 95), (196, 88), (210, 84.5), (225, 83), (240, 84), (255, 88), (268, 95), (280, 104),
           (287.5, 116), (290.5, 129), (290, 142), (286.5, 155), (280, 164), (270, 169.5), (258, 170.5),
           (246, 166.5), (233, 161.5), (221, 157), (209, 151.5), (198, 147), (188, 142.5), (180.5, 136.5),
           (176, 127.5), (175, 116.5), (178.5, 105)]


def _catmull(pts, per=24):
    out = []
    n = len(pts)
    for i in range(n):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[(i + 1) % n], pts[(i + 2) % n]
        for k in range(per):
            t = k / per
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * (2 * p1[j] + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2
                                    + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3) for j in range(2)))
    return out


NB = 1440
_R = [0.0] * NB
for _x, _y in _catmull(OUTLINE):
    _a = math.atan2(_y - HC[1], _x - HC[0]) % (2 * math.pi)
    _i = int(_a / (2 * math.pi) * NB) % NB
    _R[_i] = max(_R[_i], math.hypot(_x - HC[0], _y - HC[1]))
for _ in range(NB):  # fill gaps
    if all(_R):
        break
    _R = [r if r else (_R[i - 1] or _R[(i + 1) % NB]) for i, r in enumerate(_R)]
_R = [sum(_R[(i + k) % NB] for k in range(-6, 7)) / 13 for i in range(NB)]   # extra roundness
A0, A1 = math.radians(44), math.radians(166)   # gathered, scalloped bottom edge


def head_R(a):
    a %= 2 * math.pi
    f = a / (2 * math.pi) * NB
    i = int(f) % NB
    t = f - int(f)
    r = _R[i] * (1 - t) + _R[(i + 1) % NB] * t
    if A0 < a < A1:
        u = (a - A0) / (A1 - A0)
        env = math.sin(math.pi * u) ** 0.5
        r += 2.6 * env * abs(math.sin(7 * math.pi * u)) - 1.0 * env
    return r


def h_to_screen(x, y, z):
    return (HC[0] + x, HC[1] + y, z)


def h_from_screen(px, py):
    return (px - HC[0], py - HC[1])


def h_rotv(v):
    return v


def head_pt(a, ph):
    """a: angle around the face (screen), ph: 0 = front centre .. pi/2 = silhouette"""
    R = head_R(a)
    s = math.sin(ph)
    return (R * s * math.cos(a), R * s * math.sin(a), HZ * max(0.0, math.cos(ph)) ** 0.9)


def _z_at(x, y):
    a = math.atan2(y, x)
    s = min(0.985, math.hypot(x, y) / head_R(a))
    return HZ * math.cos(math.asin(s)) ** 0.9


def surface_at(px, py):
    """front surface point + normal (head-local) under a screen position"""
    x, y = h_from_screen(px, py)
    z = _z_at(x, y)
    e = 0.5
    dzx = (_z_at(x + e, y) - _z_at(x - e, y)) / (2 * e)
    dzy = (_z_at(x, y + e) - _z_at(x, y - e)) / (2 * e)
    return (x, y, z), S.norm((-dzx, -dzy, 1.0))


def felt_quads(pts3, col, n, sheen=1.0):
    ndl = sum(n[i] * S.LIGHT[i] for i in range(3))
    edge = 1 - max(0, n[2])
    teal = max(0.0, min(1.0, (n[0] - 0.55) / 0.3)) * max(0.0, 1 - abs(n[1]) * 1.6)
    z = sum(p[2] for p in pts3) / len(pts3)
    return (z, [(p[0], p[1]) for p in pts3], col, ndl, edge, teal)


def felt_shade(alb, light, edge, teal, base):
    """round, soft felt shading: broad gradient, soft terminator, darkened rim, no hard outline"""
    a = alb.getchannel('A')
    light = blur(light, 1.2)
    grad = light.point(lambda v: int(255 * max(0.0, min(1.0, 0.40 + 0.74 * (v / 255.0)))))
    out = ImageChops.multiply(alb, Image.merge('RGBA', (grad, grad, grad, Image.new('L', grad.size, 255))))
    paint(out, FELT_PALE, mul(levels(light, 205, 245), a), 0.45)
    paint(out, hexc('#0f5a12'), mul(inv(levels(light, 120, 165)), a), 0.42)
    paint(out, hexc('#0b3a02'), mul(levels(blur(edge, 0.8), 175, 252), a), 0.55)
    paint(out, FELT_RIM, mul(blur(teal, 1.5), a), 0.45)
    out.putalpha(a)
    return out


def head_layer():
    quads = []
    nth, nph = 240, 70
    for i in range(nph):
        pa = (math.pi / 2) * i / nph
        pb = (math.pi / 2) * (i + 1) / nph
        for j in range(nth):
            ta = 2 * math.pi * j / nth
            tb = 2 * math.pi * (j + 1) / nth
            P = [head_pt(ta, pa), head_pt(tb, pa), head_pt(tb, pb), head_pt(ta, pb)]
            c3 = [sum(p[k] for p in P) / 4 for k in range(3)]
            n = surface_at(c3[0] + HC[0], c3[1] + HC[1])[1] if i < nph - 2 else S.norm((c3[0] / 50, c3[1] / 50, 0.05))
            quads.append(felt_quads([h_to_screen(*p) for p in P], FELT, n))
    # snout: oval disc raised along the surface normal (the live pig's 3D snout)
    (sx, sy, sz), sn = surface_at(*SNOUT_C)
    u = S.norm((sn[2], 0, -sn[0]))
    v = S.cross(sn, u)
    SU, SV, DEPTH = 21.5, 16.0, 7.5
    def snout_pt(r, t, h):
        dome = 1.6 * (1 - r * r) if h >= 1 else 0
        rr = r * (1 - 0.10 * h * h)                          # rounded edge
        x = rr * SU * math.cos(t)
        y = rr * SV * math.sin(t)
        d = h * DEPTH + dome - 2.0
        return tuple((sx, sy, sz)[k] + u[k] * x + v[k] * y + sn[k] * d for k in range(3))
    def add(P, col):
        e1 = [P[1][k] - P[0][k] for k in range(3)]
        e2 = [P[3][k] - P[0][k] for k in range(3)]
        n = S.norm(S.cross(e1, e2))
        c3 = [sum(p[k] for p in P) / 4 for k in range(3)]
        if sum(n[k] * (c3[k] - (sx + sn[0] * 3, sy + sn[1] * 3, sz + sn[2] * 3)[k]) for k in range(3)) < 0:
            n = tuple(-q for q in n)
        if n[2] < -0.05:
            return
        q = felt_quads([h_to_screen(*p) for p in P], col, h_rotv(n))
        quads.append((q[0] + 6,) + q[1:])
    nt = 90
    for j in range(nt):
        ta, tb = 2 * math.pi * j / nt, 2 * math.pi * (j + 1) / nt
        for i in range(8):           # side wall
            ha, hb = i / 8, (i + 1) / 8
            add([snout_pt(1, ta, ha), snout_pt(1, tb, ha), snout_pt(1, tb, hb), snout_pt(1, ta, hb)], SNOUT)
        for i in range(14):          # domed face
            ra, rb = 1 - i / 14, 1 - (i + 1) / 14
            add([snout_pt(ra, ta, 1), snout_pt(ra, tb, 1), snout_pt(rb, tb, 1), snout_pt(rb, ta, 1)], SNOUT)
    alb, light, edge, teal = S.draw_quads(quads)
    img = felt_shade(alb, light, edge, teal, FELT)
    m = img.getchannel('A')
    mottle(img, m, 5, 0.09, 7)
    img.alpha_composite(felt_fibres(m, FELT, 4200, 1))

    s, c = new_surface()
    # faint top seam, the rest of the head stays seamless
    seam = [h_to_screen(*surface_at(p[0], p[1])[0]) for p in ((238, 86), (235, 92), (231, 98))]
    stitch_line(c, [(p[0], p[1]) for p in seam], THREAD, 0.7, (1.8, 1.5))
    # gathered bottom: puckers
    for k in range(7):
        t = A0 + (A1 - A0) * (k + 0.5) / 7 + (A1 - A0) / 14
        if t > A1 - 0.05:
            continue
        a = h_to_screen(*head_pt(t, math.pi / 2 * 0.985))
        b = h_to_screen(*head_pt(t + 0.03, math.pi / 2 * 0.80))
        c.set_source_rgba(*FELT_DARK[:3], 0.6)
        c.set_line_width(1.0)
        c.move_to(a[0], a[1])
        c.curve_to(a[0], a[1] - 2, b[0] - 1, b[1] + 2, b[0], b[1])
        c.stroke()
    img.alpha_composite(clip_alpha(to_pil(s), m))
    # very soft edge definition instead of an outline
    paint(img, hexc('#0b3a02'), mul(blur(sub(m, erode(m, 1.0)), 0.5), m), 0.35)

    # snout decorations: nostrils + blanket stitch on the face rim
    s, c = new_surface()
    def face(r, t, k=1.0):
        p = snout_pt(r, t, 1)
        return h_to_screen(*p)[:2]
    for cx_, cy_, rr in ((-0.42, -0.10, 0.27), (0.36, 0.28, 0.28)):
        pts = []
        for k in range(32):
            t = 2 * math.pi * k / 32
            x = cx_ + rr * math.cos(t) * 14.0 / 19.5
            y = cy_ + rr * math.sin(t)
            p3 = tuple((sx, sy, sz)[q] + u[q] * x * SU + v[q] * y * SV + sn[q] * (DEPTH + 1.6 * (1 - x * x - y * y) - 1.9) for q in range(3))
            pts.append(h_to_screen(*p3)[:2])
        c.set_source_rgba(*hexc('#123c02'))
        poly(c, pts)
        c.fill()
        c.save()
        poly(c, pts)
        c.clip()
        for k in range(-8, 9):
            c.set_source_rgba(0.32, 0.52, 0.18, 0.45)
            c.set_line_width(0.35)
            cxp = sum(p[0] for p in pts) / 32
            cyp = sum(p[1] for p in pts) / 32
            c.move_to(cxp + k * 0.8 - 2, cyp - 8)
            c.line_to(cxp + k * 0.8 + 2, cyp + 8)
            c.stroke()
        c.restore()
        c.set_source_rgba(1, 1, 1, 0.22)
        c.set_line_width(0.6)
        poly(c, [(x + 0.4, y + 0.6) for x, y in pts[2:12]])
        c.new_path()
    ring = [face(0.93, 2 * math.pi * k / 56) for k in range(56)]
    for k in range(0, 56):
        p = ring[k]
        q = face(0.78, 2 * math.pi * k / 56)
        c.set_source_rgba(*THREAD)
        c.set_line_width(0.5)
        c.move_to(*p)
        c.line_to(*q)
        c.stroke()
    stitch_line(c, ring, THREAD, 0.45, (100, 0), closed=True)
    img.alpha_composite(to_pil(s))

    # button eyes, foreshortened by the head's curvature
    s, c = new_surface()
    for (ex, ey), col, thr in ((FAR_EYE, hexc('#22201e'), hexc('#efe4c4')), (NEAR_EYE, hexc('#1d1d24'), hexc('#efe4c4'))):
        p, n = surface_at(ex, ey)
        ns = h_rotv(n)
        rot = math.atan2(ns[1], ns[0])
        rx = EYE_R * max(0.55, ns[2])
        c.save(); c.translate(ex + 1.6, ey + 2.2); c.rotate(rot); c.scale(rx * 1.15, EYE_R * 1.15)
        c.arc(0, 0, 1, 0, 2 * math.pi); c.restore()
        c.set_source_rgba(*FELT_DARK[:3], 0.7)
        c.fill()
        button(c, ex, ey, rx, EYE_R, col, thr, holes=4, rot=rot)
    img.alpha_composite(to_pil(s))
    return img, m


TAIL_PNG = PIGS + '2_Pigs/1_MinionPig/BodyParts/Tail.png'


def tail_layer():
    """felt curly tail, behind the head (game: Tail bone at the rear right)"""
    tx, ty = bpt(60 + 53 + 0.41 * 24 - 12, 110 - 26 - 9 + 1.4)
    m = sprite_mask(TAIL_PNG, tx, ty, HSCALE * 1.05)
    img = blank()
    paint(img, FELT, m)
    paint(img, FELT_DARK, crescent(m, -1.5, -2.2, 0.15))
    paint(img, FELT_PALE, crescent(m, 1.0, 1.2, 0.1), 0.7)
    img.alpha_composite(felt_fibres(m, FELT, 250, 4))
    paint(img, FELT_LINE, sub(m, erode(m, 1.6)))
    return img


def neck_hay():
    strands = []
    rnd = random.Random(12)
    for i in range(90):
        t = rnd.uniform(A0 - 0.1, A1 + 0.1)
        o = h_to_screen(*head_pt(t, math.pi / 2 * rnd.uniform(0.9, 0.97)))
        a = t + rnd.uniform(-0.3, 0.3)
        L = rnd.uniform(8, 15)
        p2 = (o[0] + math.cos(a) * L, o[1] + math.sin(a) * L)
        p1 = (o[0] + math.cos(a) * L * 0.5 + rnd.uniform(-1, 1), o[1] + math.sin(a) * L * 0.5)
        strands.append(((o[0], o[1]), p1, p2, rnd.uniform(1.3, 1.9)))
    return hay_layer(strands, seed=13)
