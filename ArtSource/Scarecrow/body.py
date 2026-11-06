# Shirt body part: far sleeve, torso with details, near sleeve, hay
import math, random
from paint import *
import shirt as S
from hay import hay_layer, burst, HAY_LINE

NAVY = hexc('#15214a')
THREAD_DARK = hexc('#1b2b5c')
TWINE = hexc('#cfa862')
TWINE_DARK = hexc('#8e6a2c')
TWINE_LINE = hexc('#5a3c12')
BURLAP = hexc('#c9a46a')
RED_THREAD = hexc('#c23b2e')

_F = S.FIT
_b = math.radians(_F['beta'])
_ARM = (math.cos(_b), _F['delta'], -math.sin(_b))
NEAR = S.Sleeve((44 * _F['wx'], _F['shy'], 0), _ARM, _F['L'], 3, 19, _F['rc'] / 1.12, seed=1)
FAR = S.Sleeve((-44 * _F['wx'], _F['shy'], 0), tuple(-v for v in _ARM), _F['L'], 3, 19, _F['rc'] / 1.12, seed=2)


def pip(pt, poly_):
    x, y = pt
    inside = False
    n = len(poly_)
    for i in range(n):
        x1, y1 = poly_[i]
        x2, y2 = poly_[(i + 1) % n]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            inside = not inside
    return inside


def surface_patch(poly_uv, color_fn, lift, step=0.7, ao_fn=None):
    """quads of the torso surface inside a (u,y) polygon, raised by lift"""
    us = [p[0] for p in poly_uv]
    ys = [p[1] for p in poly_uv]
    quads = []
    y = min(ys)
    while y < max(ys):
        u = min(us)
        while u < max(us):
            if pip((u + step / 2, y + step / 2), poly_uv):
                pts = [S.uv_pt(u, y, lift), S.uv_pt(u + step, y, lift),
                       S.uv_pt(u + step, y + step + 0.05, lift), S.uv_pt(u, y + step + 0.05, lift)]
                n = S.uv_normal(u + step / 2, y + step / 2)
                ao = ao_fn(u, y) if ao_fn else 1.0
                quads.append(S.quad(pts, color_fn(u, y), n, ao))
            u += step
        y += step
    return quads


def shaded_patch(poly_uv, color_fn, lift, outline=0.9, ao_fn=None):
    q = surface_patch(poly_uv, color_fn, lift, ao_fn=ao_fn)
    img = S.shade_layers(*S.draw_quads(q), sh_t=0.2)
    return with_outline(img, NAVY, outline)


def burlap(u, y):
    k = 1 + 0.08 * math.sin(u * 2.6) * math.sin(y * 2.4) + 0.05 * math.sin((u - y) * 0.9)
    return scale_rgb(BURLAP, k)


def stroke_pts(c, pts, width, color, dash=None, cap=cairo.LINE_CAP_ROUND):
    c.save()
    c.set_source_rgba(*color)
    c.set_line_width(width)
    c.set_line_cap(cap)
    c.set_line_join(cairo.LINE_JOIN_ROUND)
    if dash:
        c.set_dash(dash)
    c.move_to(*pts[0])
    for p in pts[1:]:
        c.line_to(*p)
    c.stroke()
    c.restore()


def button(c, u, y, r=3.1, col=hexc('#efe2bd'), holes=4, lift=0.9):
    ring = [S.uv_pt(u + r * math.cos(a), y + r * math.sin(a), lift)[:2]
            for a in [i * 2 * math.pi / 28 for i in range(28)]]
    # drop shadow
    c.set_source_rgba(0.05, 0.08, 0.2, 0.35)
    poly(c, [(x + 0.7, yy + 0.9) for x, yy in ring])
    c.fill()
    c.set_source_rgba(*hexc('#4a3b22'))
    poly(c, ring)
    c.set_line_width(1.2)
    c.stroke_preserve()
    c.set_source_rgba(*col)
    c.fill()
    inner = [S.uv_pt(u + r * 0.62 * math.cos(a), y + r * 0.62 * math.sin(a), lift)[:2]
             for a in [i * 2 * math.pi / 20 for i in range(20)]]
    c.set_source_rgba(*scale_rgb(col, 0.82))
    poly(c, inner)
    c.fill()
    hs = [(-0.9, -0.9), (0.9, -0.9), (-0.9, 0.9), (0.9, 0.9)] if holes == 4 else [(-0.9, 0), (0.9, 0)]
    pts = [S.uv_pt(u + dx, y + dy, lift)[:2] for dx, dy in hs]
    if holes == 4:
        stroke_pts(c, [pts[0], pts[3]], 0.55, hexc('#2d3f7a'))
        stroke_pts(c, [pts[1], pts[2]], 0.55, hexc('#2d3f7a'))
    for p in pts:
        c.set_source_rgba(*hexc('#3a2c14'))
        c.arc(p[0], p[1], 0.42, 0, 2 * math.pi)
        c.fill()
    hl = S.uv_pt(u - r * 0.45, y - r * 0.5, lift)[:2]
    c.set_source_rgba(1, 1, 1, 0.85)
    c.arc(hl[0], hl[1], 0.55, 0, 2 * math.pi)
    c.fill()


def torso_layer():
    alb, light, rim, ao = S.draw_quads(S.torso_quads())
    torso = S.shade_layers(alb, light, rim, ao, sh_t=0.2)
    tmask = torso.getchannel('A')

    # --- details painted on the surface, clipped to the torso ---
    s, c = new_surface()
    side_u = lambda y: S.arc_u(math.pi / 2, y)
    # side seam groove + stitches
    for y0, y1 in ((200, 287), (298, 318)):
        pts = [(S.uv_pt(side_u(y), y)[0], S.uv_pt(side_u(y), y)[1]) for y in range(y0, y1)]
        stroke_pts(c, pts, 0.9, hexc('#14204a', 0.55))
        pts2 = [(S.uv_pt(side_u(y) - 1.6, y)[0], S.uv_pt(side_u(y) - 1.6, y)[1]) for y in range(y0, y1)]
        stroke_pts(c, pts2, 0.45, hexc('#bcd2f0', 0.6), dash=[1.3, 1.1])
    # placket: raised strip, edge, stitching
    for y0, y1 in ((183, 288), (297, 318)):
        strip = S.uv_poly([(-4.6, y0), (-4.6, y1)]) + S.uv_poly([(4.6, y1), (4.6, y0)])
        c.set_source_rgba(1, 1, 1, 0.07)
        poly(c, strip)
        c.fill()
        stroke_pts(c, S.uv_poly([(4.7, y0), (4.7, y1)], 0.3), 0.9, hexc('#101a40', 0.75))
        stroke_pts(c, S.uv_poly([(4.0, y0), (4.0, y1)], 0.3), 0.5, hexc('#cfe0f6', 0.45))
        stroke_pts(c, S.uv_poly([(-3.6, y0), (-3.6, y1)], 0.3), 0.42, hexc('#d8e6f8', 0.65), dash=[1.4, 1.0])
        stroke_pts(c, S.uv_poly([(2.9, y0), (2.9, y1)], 0.3), 0.42, hexc('#d8e6f8', 0.65), dash=[1.4, 1.0])
    # hole torn in the side, hay poking out (hole drawn here, hay later)
    hole = [S.uv_pt(48 + 4.2 * math.cos(a) * (1 + 0.35 * math.sin(a * 5)),
                    268 + 3.2 * math.sin(a) * (1 + 0.3 * math.cos(a * 4)), 0)[:2]
            for a in [i * 2 * math.pi / 26 for i in range(26)]]
    c.set_source_rgba(*hexc('#3b2a0c'))
    smooth(c, hole)
    c.fill()
    c.set_source_rgba(*hexc('#c99b3a'))
    smooth(c, [(x + 0.6, y + 0.9) for x, y in hole])
    c.fill()
    stroke_pts(c, hole + hole[:1], 0.8, NAVY)
    # frayed threads around the hole
    rnd = random.Random(5)
    for i in range(10):
        a = rnd.uniform(0, 2 * math.pi)
        p = S.uv_pt(48 + 4.6 * math.cos(a), 268 + 3.6 * math.sin(a), 0.2)
        q = (p[0] + math.cos(a) * rnd.uniform(1, 2.2), p[1] + math.sin(a) * rnd.uniform(1, 2.2) + 0.6)
        stroke_pts(c, [p[:2], q], 0.4, hexc('#d9e6f8', 0.8))
    det = clip_alpha(to_pil(s), tmask)
    torso.alpha_composite(det)

    # straw sprig poking out of the pocket (behind the pocket fabric)
    top_l = S.uv_pt(16, 207, 1)[:2]
    top_r = S.uv_pt(34, 207, 1)[:2]
    sprig = []
    rnd = random.Random(11)
    for i in range(7):
        t = rnd.uniform(0.15, 0.85)
        o = (top_l[0] + (top_r[0] - top_l[0]) * t, top_l[1] + (top_r[1] - top_l[1]) * t + 3)
        a = -math.pi / 2 + rnd.uniform(-0.55, 0.45)
        L = rnd.uniform(9, 17)
        sprig.append((o, (o[0] + math.cos(a) * L * 0.5 + 1, o[1] + math.sin(a) * L * 0.5),
                      (o[0] + math.cos(a) * L, o[1] + math.sin(a) * L), 1.25))
    torso.alpha_composite(hay_layer(sprig, seed=3))

    # pocket (fabric cut on the bias), with top hem
    pocket = [(14, 207), (35, 207), (35, 227), (24.5, 233), (14, 227)]
    bias = lambda u, y: S.plaid((u + y) * 0.7071 + 5, (u - y) * 0.7071)
    torso.alpha_composite(shaded_patch(pocket, bias, 1.0, 0.9,
                                       ao_fn=lambda u, y: 0.85 if y < 210 else 1.0))
    s, c = new_surface()
    stroke_pts(c, S.uv_poly([(14.8, 210.2), (34.2, 210.2)], 1.2), 0.45, hexc('#dbe7f6', 0.8), dash=[1.3, 1.0])
    stroke_pts(c, S.uv_poly([(15.4, 211), (15.4, 226.5), (24.5, 231.8), (33.6, 226.5), (33.6, 211)], 1.2),
               0.45, hexc('#dbe7f6', 0.8), dash=[1.3, 1.0])
    torso.alpha_composite(to_pil(s))

    # burlap patch on the far side, cross-stitched in red
    patch = [(-35, 255), (-14, 253.5), (-12.5, 275), (-34, 278)]
    torso.alpha_composite(shaded_patch(patch, burlap, 0.7, 0.8))
    s, c = new_surface()
    edge = patch + patch[:1]
    for i in range(4):
        (u0, y0), (u1, y1) = edge[i], edge[i + 1]
        L = math.hypot(u1 - u0, y1 - y0)
        n = int(L / 3.4)
        nx, ny = -(y1 - y0) / L, (u1 - u0) / L
        for k in range(n):
            t = (k + 0.5) / n
            u, y = u0 + (u1 - u0) * t, y0 + (y1 - y0) * t
            a = S.uv_pt(u - 1.0 + nx * 1.6, y - 1.0 + ny * 1.6, 1.0)[:2]
            b = S.uv_pt(u + 1.0 - nx * 1.6, y + 1.0 - ny * 1.6, 1.0)[:2]
            a2 = S.uv_pt(u + 1.0 + nx * 1.6, y - 1.0 + ny * 1.6, 1.0)[:2]
            b2 = S.uv_pt(u - 1.0 - nx * 1.6, y + 1.0 - ny * 1.6, 1.0)[:2]
            stroke_pts(c, [a, b], 0.55, RED_THREAD)
            stroke_pts(c, [a2, b2], 0.55, RED_THREAD)
    torso.alpha_composite(to_pil(s))

    # buttons (one missing: dangling thread)
    s, c = new_surface()
    for y in (199, 223, 247):
        button(c, 0.4, y)
    button(c, 0.4, 307)
    p = S.uv_pt(0.4, 271, 0.6)[:2]
    stroke_pts(c, [p, (p[0] + 1.5, p[1] + 2.5), (p[0] + 0.6, p[1] + 5.5)], 0.45, hexc('#efe2bd'))
    stroke_pts(c, [S.uv_pt(-0.6, 270.2, 0.5)[:2], S.uv_pt(1.4, 271.8, 0.5)[:2]], 0.45, hexc('#efe2bd'))
    torso.alpha_composite(to_pil(s))
    return torso


def twine_layer():
    s, c = new_surface()
    y = S.CINCH_Y
    pts = []
    for i in range(361):
        phi = -math.pi + 2 * math.pi * i / 360
        n = S.torso_normal(phi, y)
        if n[2] > -0.05:
            pts.append(S.surf_pt(phi, y, 1.6)[:2])
    # rope body
    stroke_pts(c, pts, 3.6, TWINE_LINE)
    stroke_pts(c, pts, 2.4, TWINE)
    stroke_pts(c, [(x, yy + 0.6) for x, yy in pts], 0.9, TWINE_DARK)
    # twist ticks
    for i in range(2, len(pts) - 2, 3):
        x, yy = pts[i]
        stroke_pts(c, [(x - 0.8, yy - 1.0), (x + 0.6, yy + 1.0)], 0.45, TWINE_LINE)
    # knot + dangling ends
    k = S.uv_pt(17, y, 2.0)[:2]
    for dx, L, sw in ((-1.5, 13, 0.4), (1.8, 10, -0.3)):
        a = (k[0] + dx, k[1] + 1)
        b = (k[0] + dx + sw * 6, k[1] + L * 0.6)
        e = (k[0] + dx + sw * 9 + 1, k[1] + L)
        c.set_source_rgba(*TWINE_LINE)
        strand(c, a, b, e, 2.6, 2.0)
        c.fill()
        c.set_source_rgba(*TWINE)
        strand(c, a, b, e, 1.6, 1.1)
        c.fill()
        for j in range(4):
            stroke_pts(c, [e, (e[0] + (j - 1.5) * 0.9, e[1] + 2.2 + j % 2)], 0.45, TWINE_DARK)
    c.set_source_rgba(*TWINE_LINE)
    c.arc(k[0], k[1], 2.9, 0, 2 * math.pi)
    c.fill()
    c.set_source_rgba(*TWINE)
    c.arc(k[0] - 0.2, k[1] - 0.2, 2.0, 0, 2 * math.pi)
    c.fill()
    stroke_pts(c, [(k[0] - 1.6, k[1] - 0.6), (k[0] + 1.2, k[1] + 1.1)], 0.5, TWINE_LINE)
    return to_pil(s)


def hem_threads():
    s, c = new_surface()
    rnd = random.Random(8)
    for i in range(70):
        phi = rnd.uniform(-math.pi * 0.95, math.pi * 0.6)
        if S.torso_normal(phi, 318)[2] < 0.05:
            continue
        hy = S.hem_y(phi)
        p = S.surf_pt(phi, hy - 0.3)[:2]
        L = rnd.uniform(1.0, 3.2)
        col = rnd.choice([hexc('#cfe0f6'), hexc('#2a4a90'), hexc('#1f3570'), hexc('#7aa2dc')])
        stroke_pts(c, [p, (p[0] + rnd.uniform(-0.8, 0.8), p[1] + L)], 0.42, col)
    return to_pil(s)


def collar_layers():
    """(back band drawn before the head's hay, flaps drawn on top of torso)"""
    # back collar stand
    s, c = new_surface()
    y = 177
    ring = [S.surf_pt(math.pi * (0.5 + i / 60.0), y)[:2] for i in range(61)]
    up = [(x, yy - 8) for x, yy in ring[::-1]]
    alb = [ring[0]] + ring + up
    c.set_source_rgba(*hexc('#3b67ad'))
    poly(c, ring + up)
    c.fill()
    c.set_source_rgba(*hexc('#2a4a8a'))
    poly(c, ring + [(x, yy - 3) for x, yy in ring[::-1]])
    c.fill()
    for i in range(0, 61, 1):
        pass
    stroke_pts(c, [(x, yy - 6) for x, yy in ring], 0.42, hexc('#d8e6f8', 0.6), dash=[1.3, 1.0])
    back = with_outline(to_pil(s), NAVY, 1.4)
    plaid_c = lambda u, y: S.plaid(u + 9, y * 1.0 + 3)
    left = shaded_patch([(-2, 182), (-23, 179.5), (-19, 201), (-5, 192)], plaid_c, 2.6, 1.2)
    right = shaded_patch([(2, 182), (25, 179.5), (21, 202), (5.5, 192)], plaid_c, 2.6, 1.2)
    s, c = new_surface()
    for poly_uv in ([(-4, 184), (-20, 182), (-17.5, 197)], [(4, 184), (22, 182), (19, 198)]):
        stroke_pts(c, S.uv_poly(poly_uv, 2.8), 0.42, hexc('#d8e6f8', 0.6), dash=[1.3, 1.0])
    # soft shadow under the flaps
    sh = blank()
    for L in (left, right):
        m = shift(L.getchannel('A'), 0.8, 1.6)
        paint(sh, hexc('#0b1430'), blur(m, 0.8), 0.45)
    flaps = blank()
    flaps.alpha_composite(sh)
    flaps.alpha_composite(left)
    flaps.alpha_composite(right)
    flaps.alpha_composite(to_pil(s))
    return back, flaps


def sleeve_layer(sl, seed, outline=True):
    alb, light, rim, ao = S.draw_quads(sl.quads())
    img = S.shade_layers(alb, light, rim, ao, sh_t=0.2)
    m = img.getchannel('A')
    if outline:
        img = with_outline(img, NAVY, 2.2)
    s, c = new_surface()
    # cuff seam stitches + shoulder seam
    ring = sl.cuff_ring(0.86, 0.3)
    vis = [p for k, p in enumerate(ring) if sl.normal(0.86, 2 * math.pi * k / len(ring))[2] > 0]
    ring2 = sl.cuff_ring(0.95, 0.3)
    vis2 = [p for k, p in enumerate(ring2) if sl.normal(0.95, 2 * math.pi * k / len(ring2))[2] > 0]
    for v in (vis, vis2):
        if v:
            v = sorted(v, key=lambda p: p[0])
            for p in v:
                c.set_source_rgba(*hexc('#d8e6f8', 0.55))
                c.arc(p[0], p[1], 0.3, 0, 2 * math.pi)
                c.fill()
    img.alpha_composite(clip_alpha(to_pil(s), dilate(m, 0.1)))
    cloth = img
    img = blank()
    # opening + hay
    end = [p[:2] for p in sl.cuff_ring(1.0, 0.0)]
    s, c = new_surface()
    c.set_source_rgba(*hexc('#2a1c0a'))
    poly(c, end)
    c.fill()
    c.set_source_rgba(*hexc('#8a6420'))
    poly(c, [((x - sum(e[0] for e in end) / len(end)) * 0.7 + sum(e[0] for e in end) / len(end),
              (y - sum(e[1] for e in end) / len(end)) * 0.7 + sum(e[1] for e in end) / len(end)) for x, y in end])
    c.fill()
    img.alpha_composite(with_outline(to_pil(s), NAVY, 1.0))
    a = S.proj(*sl.center(0.92))
    b = S.proj(*sl.center(1.0))
    ang = math.atan2(b[1] - a[1], b[0] - a[0])
    cx = sum(e[0] for e in end) / len(end)
    cy = sum(e[1] for e in end) / len(end)
    r = (max(e[0] for e in end) - min(e[0] for e in end)) * 0.32
    rr = (max(e[1] for e in end) - min(e[1] for e in end)) * 0.32
    strands = burst((cx, cy), ang, 0.85, 64, 44, 1.5, gravity=0.30, seed=seed, origin_jitter=(r, rr))
    img.alpha_composite(hay_layer(strands, seed=seed))
    return cloth, img, m


def body_layers():
    """returns list of (name, layer) back-to-front"""
    out = []
    # hay hanging out under the hem, around the pole (behind the shirt)
    hem_hay = []
    rnd = random.Random(77)
    for i in range(60):
        phi = rnd.uniform(-math.pi * 0.95, math.pi * 0.7)
        if S.torso_normal(phi, 318)[2] < 0.0:
            continue
        p = S.surf_pt(phi, S.hem_y(phi) - 4)[:2]
        a = math.pi / 2 + rnd.uniform(-0.35, 0.35)
        L = rnd.uniform(7, 16)
        hem_hay.append((p, (p[0] + math.cos(a) * L * 0.5 + rnd.uniform(-1, 1), p[1] + math.sin(a) * L * 0.5),
                        (p[0] + math.cos(a) * L, p[1] + math.sin(a) * L), rnd.uniform(1.2, 1.7)))
    back = []
    # seen slightly from below: the hem opening shows the hay stuffing inside
    ring = [S.surf_pt(-math.pi + 2 * math.pi * i / 180, S.hem_y(-math.pi + 2 * math.pi * i / 180) - 0.5)[:2]
            for i in range(180)]
    s, c = new_surface()
    c.set_source_rgba(*hexc('#6e4a14'))
    poly(c, ring)
    c.fill()
    inside = to_pil(s)
    rm = inside.getchannel('A')
    bb = [v / SS for v in rm.getbbox()]
    rnd = random.Random(15)
    stuff = []
    for i in range(140):
        x, y = rnd.uniform(bb[0], bb[2]), rnd.uniform(bb[1], bb[3])
        a_ = rnd.uniform(0, math.pi)
        L = rnd.uniform(5, 11)
        stuff.append(((x, y), (x + math.cos(a_) * L / 2, y + math.sin(a_) * L / 2 - 1),
                      (x + math.cos(a_) * L, y + math.sin(a_) * L * 0.4), rnd.uniform(1.1, 1.6)))
    inside.alpha_composite(clip_alpha(hay_layer(stuff, seed=16), rm))
    paint(inside, hexc('#2a1a05'), mul(blur(sub(rm, shift(rm, 0, 3)), 1.2), rm), 0.5)
    back.append(('hem_inside', with_outline(inside, NAVY, 1.2)))
    back.append(('hem_hay', hay_layer(hem_hay, seed=4)))
    fc, fx, fm = sleeve_layer(FAR, 31)
    fc.alpha_composite(fx)
    back.append(('far_sleeve', fc))
    back_collar, flaps = collar_layers()
    back.append(('collar_back', back_collar))

    front = []
    torso = torso_layer()
    torso.alpha_composite(hem_threads())
    nc, nx, nm = sleeve_layer(NEAR, 51, outline=False)
    # one outline around torso + near sleeve: no separating line between them
    union = ImageChops.lighter(torso.getchannel('A'), nm)
    ol = blank()
    paint(ol, NAVY, dilate(union, 2.2))
    front.append(('outline', ol))
    front.append(('torso', torso))
    p = S.uv_pt(48, 268, 0)[:2]
    front.append(('hole_hay', hay_layer(burst(p, 0.55, 0.6, 9, 13, 1.3, gravity=0.4, seed=40, origin_jitter=(2, 1.5)), seed=41)))
    front.append(('collar', flaps))
    front.append(('near_sleeve', nc))
    front.append(('near_hay', nx))
    return back, front


if __name__ == '__main__':
    import time
    t = time.time()
    img = blank()
    bk, fr = body_layers()
    for n, L in bk + fr:
        img.alpha_composite(L)
    print(time.time() - t)
    bg = Image.new('RGBA', img.size, (200, 205, 210, 255))
    bg.alpha_composite(img)
    downsample(bg, 2).save('t_body.png')
