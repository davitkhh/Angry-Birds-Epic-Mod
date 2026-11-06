# Assemble the scarecrow: parts, composite, previews
import math, random, sys, os
from paint import *
import shirt as S
import body, head, hat
from hay import hay_layer

WOOD = hexc('#a06c3a')
WOOD_LIGHT = hexc('#c99560')
WOOD_DARK = hexc('#6b4220')
WOOD_LINE = hexc('#3d220c')
DIRT = hexc('#8a6440')
PIVOT = (164, 372)   # sway pivot (where the pole enters the ground)


# pole outline traced from the sketch (HR): slightly narrowing then flaring into the ground
POLE_L = [(156.5, 250), (155.5, 300), (154.0, 320), (152.6, 340), (151.4, 356), (150.0, 372)]
POLE_R = [(173.0, 250), (174.0, 295), (172.6, 318), (172.8, 338), (174.6, 356), (177.6, 372)]


def pole_layer():
    top, bot = 250, PIVOT[1]
    outline_pts = POLE_L + [(177.6, bot + 4), (150.0, bot + 4)] + POLE_R[::-1]
    m = mask_from(lambda c: (smooth(c, POLE_L, closed=False), c.line_to(150.0, bot + 4), c.line_to(177.6, bot + 4),
                             [c.line_to(*p) for p in POLE_R[::-1]], c.close_path(), c.fill()))
    cut = mask_from(lambda c: (c.rectangle(0, 0, W, bot), c.fill()))
    m = mul(m, cut)
    x = (POLE_L[-1][0] + POLE_R[-1][0]) / 2
    img = blank()
    paint(img, WOOD, m)
    paint(img, WOOD_LIGHT, crescent(m, 6.5, 0, 0.3), 0.9)
    paint(img, hexc('#f0c890'), crescent(m, 1.8, 0, 0.15), 0.6)
    paint(img, WOOD_DARK, crescent(m, -5.5, 0, 0.3))
    paint(img, hexc('#5a8a8a'), crescent(m, -1.5, 0, 0.15), 0.3)
    s, c = new_surface()
    rnd = random.Random(2)
    for i in range(22):
        f = rnd.uniform(0.08, 0.92)
        y0 = rnd.uniform(top, bot - 20)
        L = rnd.uniform(18, 60)
        c.set_source_rgba(*WOOD_DARK[:3], 0.7)
        c.set_line_width(rnd.uniform(0.35, 0.8))
        for k in range(9):
            yy = y0 + L * k / 8
            lx = POLE_L[0][0] + (POLE_L[-1][0] - POLE_L[0][0]) * (yy - top) / (bot - top)
            rx = POLE_R[0][0] + (POLE_R[-1][0] - POLE_R[0][0]) * (yy - top) / (bot - top)
            px = lx + (rx - lx) * f + 0.6 * math.sin(yy * 0.15 + i)
            (c.move_to if k == 0 else c.line_to)(px, yy)
        c.stroke()
    for kx, ky in ((159.5, 336), (168.5, 312)):
        c.set_source_rgba(*WOOD_LINE)
        c.save(); c.translate(kx, ky); c.scale(2.6, 3.8); c.arc(0, 0, 1, 0, 2 * math.pi); c.restore(); c.fill()
        c.set_source_rgba(*WOOD_DARK)
        c.save(); c.translate(kx + 0.3, ky + 0.3); c.scale(1.6, 2.6); c.arc(0, 0, 1, 0, 2 * math.pi); c.restore(); c.fill()
        c.set_source_rgba(*WOOD_LIGHT[:3], 0.8)
        c.set_line_width(0.5)
        c.save(); c.translate(kx, ky); c.scale(3.6, 5.6); c.arc(0, 0, 1, math.pi * 0.7, math.pi * 1.4); c.restore(); c.stroke()
    # damp, dirt-stained where it goes into the ground + clinging dirt specks
    g = cairo.LinearGradient(0, bot - 22, 0, bot)
    g.add_color_stop_rgba(0, 0.22, 0.13, 0.05, 0)
    g.add_color_stop_rgba(1, 0.22, 0.13, 0.05, 0.85)
    c.set_source(g)
    c.rectangle(140, bot - 22, 50, 22)
    c.fill()
    for i in range(18):
        c.set_source_rgba(*hexc(rnd.choice(['#5a3c1e', '#7a5530', '#3e2812'])))
        c.arc(rnd.uniform(150, 178), bot - rnd.uniform(0, 9) ** 1.3, rnd.uniform(0.4, 1.0), 0, 2 * math.pi)
        c.fill()
    img.alpha_composite(clip_alpha(to_pil(s), m))
    # outline on the sides only: the bottom continues into the dirt
    o = blank()
    paint(o, WOOD_LINE, mul(dilate(m, 2.0), cut))
    o.alpha_composite(img)
    return o


def shadow_layer():
    img = blank()
    sm = mask_from(lambda c: (c.save(), c.translate(PIVOT[0] + 6, PIVOT[1] + 1), c.scale(62, 9),
                              c.arc(0, 0, 1, 0, 2 * math.pi), c.restore(), c.fill()))
    paint(img, (0, 0, 0), blur(sm, 2.5), 0.28)
    return img


def ground_layer():
    """static: shadow + dirt mound with grass (not part of the sway)"""
    img = blank()
    sm = mask_from(lambda c: (c.save(), c.translate(PIVOT[0] + 4, PIVOT[1] + 6), c.scale(78, 12),
                              c.arc(0, 0, 1, 0, 2 * math.pi), c.restore(), c.fill()))
    paint(img, (0, 0, 0), blur(sm, 1.5), 0.33)
    pts = [(PIVOT[0] - 34, PIVOT[1] + 9), (PIVOT[0] - 22, PIVOT[1] + 1), (PIVOT[0] - 8, PIVOT[1] - 4),
           (PIVOT[0] + 10, PIVOT[1] - 4), (PIVOT[0] + 25, PIVOT[1] + 1), (PIVOT[0] + 37, PIVOT[1] + 9)]
    mound = mask_from(lambda c: (smooth(c, pts + [(PIVOT[0], PIVOT[1] + 13)]), c.fill()))
    d = blank()
    paint(d, DIRT, mound)
    paint(d, hexc('#b08458'), crescent(mound, 0, 3, 0.2), 0.9)
    paint(d, hexc('#5e4128'), crescent(mound, 0, -3, 0.2))
    s, c = new_surface()
    rnd = random.Random(4)
    for i in range(14):
        px, py = PIVOT[0] + rnd.uniform(-28, 28), PIVOT[1] + rnd.uniform(0, 9)
        c.set_source_rgba(*hexc(rnd.choice(['#6a4a2c', '#a88a6a', '#4e3620'])))
        c.arc(px, py, rnd.uniform(0.6, 1.4), 0, 2 * math.pi)
        c.fill()
    d.alpha_composite(clip_alpha(to_pil(s), mound))
    img.alpha_composite(with_outline(d, hexc('#3a2410'), 1.6))
    return img


def grass_layer():
    s, c = new_surface()
    rnd = random.Random(9)
    for cx_ in (PIVOT[0] - 30, PIVOT[0] - 14, PIVOT[0] + 18, PIVOT[0] + 33):
        for i in range(7):
            o = (cx_ + rnd.uniform(-4, 4), PIVOT[1] + 9 + rnd.uniform(-2, 1))
            a = -math.pi / 2 + rnd.uniform(-0.7, 0.7)
            L = rnd.uniform(6, 12)
            p2 = (o[0] + math.cos(a) * L, o[1] + math.sin(a) * L)
            p1 = (o[0] + math.cos(a) * L * 0.5, o[1] + math.sin(a) * L * 0.5 - 1)
            c.set_source_rgba(*hexc('#1c5504'))
            strand(c, o, p1, p2, 3.0, 0.6)
            c.fill()
            c.set_source_rgba(*hexc(rnd.choice(['#3fae22', '#5cc23a', '#2c9a18'])))
            strand(c, o, p1, p2, 1.8, 0.2)
            c.fill()
    return to_pil(s)


def build():
    parts = {}
    parts['shadow'] = shadow_layer()
    parts['pole'] = pole_layer()
    bk, fr = body.body_layers()
    for name, ls in (('body_back', bk), ('body', fr)):
        b = blank()
        for n, L in ls:
            b.alpha_composite(L)
        parts[name] = b
    parts['ruff'] = head.neck_hay()
    hb, hf = hat.hat_layers()
    parts['hat_back'] = hb
    h, hm = head.head_layer()
    sh = mul(blur(shift(hf.getchannel('A'), 2.0, 4.0), 1.5), hm)
    paint(h, hexc('#0a3300'), sh, 0.35)
    parts['head'] = h
    parts['hat_front'] = hf
    return parts


ORDER = ['shadow', 'hat_back', 'body_back', 'pole', 'body', 'ruff', 'head', 'hat_front']
HEADS = ('head', 'hat_back', 'hat_front', 'ruff')


def composite(parts, angle=0.0, lag=0.0):
    out = blank()
    for k in ORDER:
        L = parts[k]
        if k != 'shadow' and (angle or lag):
            a = angle + (lag if k in HEADS else 0)
            L = L.rotate(a, resample=Image.BICUBIC, center=(PIVOT[0] * SS, PIVOT[1] * SS))
        out.alpha_composite(L)
    return out


if __name__ == '__main__':
    import pickle
    os.makedirs('out', exist_ok=True)
    parts = build()
    for k, L in parts.items():
        L.save('out/_ss_%s.png' % k)
    img = composite(parts)
    downsample(img, 1).save('out/scarecrow_hr.png')
    downsample(img, 2).save('out/scarecrow_2x.png')
    bg = Image.new('RGBA', img.size, (196, 214, 170, 255))
    bg.alpha_composite(img)
    downsample(bg, 2).save('out/preview.png')
