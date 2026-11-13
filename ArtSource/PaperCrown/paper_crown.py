#!/usr/bin/env python3
"""King Pig's crown, arts & crafts edition: construction paper, pom-poms,
buttons, a paper-plate rim and lots of sticky tape.

Same canvas (114x222) and silhouette layout as
Assets/content/4_Characters/2_Pigs/16_KingPig/BodyParts/Headgear.png, so it
can reuse that sprite's pivot.

usage: python3 paper_crown.py [out.png] [--preview preview.png]
"""
import math
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

W, H = 114, 222
SS = 4                      # supersampling
RW, RH = W * SS, H * SS

OUTLINE = (112, 58, 14, 255)
PAPER = (250, 206, 62)
PAPER_SHADE = (232, 166, 40)
PAPER_LIGHT = (255, 232, 140)
PAPER_BACK = (236, 148, 46)
PAPER_BACK_SHADE = (205, 115, 30)
BAND = (214, 52, 58)
BAND_SHADE = (170, 34, 46)
BAND_LIGHT = (240, 110, 104)
PLATE = (250, 248, 238)
PLATE_SHADE = (214, 208, 190)
HOLE = (92, 50, 18)
BUTTON = (64, 200, 214)
BUTTON_SHADE = (32, 140, 168)

rng = random.Random(7)


# vertical offset (sprite px) applied to whatever is being drawn: the parts above
# the red band are drawn with UP so the band can be made shorter without moving the
# tube / plate / pivot at the bottom
OFF = [0.0]
UP = 5.0


def s(p):
    return (p[0] * SS, (p[1] + OFF[0]) * SS)


def blank(mode="L", fill=0):
    return Image.new(mode, (RW, RH), fill)


def scissor_poly(pts, jitter=0.35, step=6.0):
    """Subdivide a polygon into short straight 'snips' with a little wobble."""
    out = []
    n = len(pts)
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        d = math.dist(a, b)
        k = max(1, int(d / step))
        for j in range(k):
            t = j / k
            x = a[0] + (b[0] - a[0]) * t
            y = a[1] + (b[1] - a[1]) * t
            if j:
                x += rng.uniform(-jitter, jitter)
                y += rng.uniform(-jitter, jitter)
            out.append((x, y))
    return out


def poly_mask(pts):
    m = blank()
    ImageDraw.Draw(m).polygon([s(p) for p in pts], fill=255)
    return m


def ellipse_mask(box):
    m = blank()
    ImageDraw.Draw(m).ellipse([s(box[:2]), s(box[2:])], fill=255)
    return m


def dilate(m, px):
    r = int(px * SS) | 1
    return m.filter(ImageFilter.MaxFilter(r if r % 2 else r + 1))


def solid(color):
    return Image.new("RGBA", (RW, RH), color if len(color) == 4 else color + (255,))


def paste(canvas, color, mask):
    canvas.paste(solid(color), (0, 0), mask)


def paper_noise(canvas, mask, strength=10, seed=1):
    """Speckles + a few fibres so it reads as construction paper."""
    noise = Image.effect_noise((RW // 2, RH // 2), 40 + seed).resize((RW, RH), Image.BILINEAR)
    k = strength / 255.0
    dark = noise.point(lambda v: int(max(0, 128 - v) * k * 4))
    light = noise.point(lambda v: int(max(0, v - 128) * k * 4))
    canvas.paste(solid((0, 0, 0, 255)), (0, 0), ImageChops.multiply(dark, mask))
    canvas.paste(solid((255, 255, 255, 255)), (0, 0), ImageChops.multiply(light, mask))
    d = ImageDraw.Draw(canvas)
    fib = blank()
    fd = ImageDraw.Draw(fib)
    for _ in range(60):
        x, y = rng.uniform(0, W), rng.uniform(0, H)
        a = rng.uniform(0, math.pi)
        L = rng.uniform(1.5, 4)
        fd.line([s((x, y)), s((x + math.cos(a) * L, y + math.sin(a) * L))], fill=255, width=SS // 2)
    fib = ImageChops.multiply(fib, mask)
    canvas.paste(solid((255, 255, 255, 60)), (0, 0), fib.point(lambda v: v * 50 // 255))
    del d


def clip(m, by):
    return ImageChops.multiply(m, by)


def outline_and_fill(canvas, mask, color, width=1.5):
    paste(canvas, OUTLINE, dilate(mask, width))
    paste(canvas, color, mask)


def pompom(canvas, c, r, color, dark, light, seed):
    g = random.Random(seed)
    body = blank()
    d = ImageDraw.Draw(body)
    # fuzzy silhouette: lots of little tufts around a core
    d.ellipse([s((c[0] - r * 0.82, c[1] - r * 0.82)), s((c[0] + r * 0.82, c[1] + r * 0.82))], fill=255)
    for _ in range(46):
        a = g.uniform(0, 2 * math.pi)
        rr = r * g.uniform(0.7, 0.93)
        tr = r * g.uniform(0.14, 0.24)
        x, y = c[0] + math.cos(a) * rr, c[1] + math.sin(a) * rr
        d.ellipse([s((x - tr, y - tr)), s((x + tr, y + tr))], fill=255)
    outline_and_fill(canvas, body, color, 1.2)
    # shading: lower-right dark, upper-left light, both as fuzzy blobs
    sh = blank()
    sd = ImageDraw.Draw(sh)
    sd.ellipse([s((c[0] - r * 0.4, c[1] - r * 0.3)), s((c[0] + r * 1.3, c[1] + r * 1.4))], fill=255)
    paste(canvas, dark, clip(sh, body.filter(ImageFilter.MinFilter(SS * 2 + 1))))
    li = blank()
    ld = ImageDraw.Draw(li)
    ld.ellipse([s((c[0] - r * 0.6, c[1] - r * 0.62)), s((c[0] + r * 0.05, c[1] - r * 0.05))], fill=255)
    paste(canvas, light, clip(li, body))
    # fibre ticks
    fd = ImageDraw.Draw(canvas)
    for _ in range(10):
        a = g.uniform(0, 2 * math.pi)
        rr = r * g.uniform(0.25, 0.7)
        x, y = c[0] + math.cos(a) * rr, c[1] + math.sin(a) * rr
        b = a + g.uniform(-0.6, 0.6)
        L = g.uniform(1.0, 2.0)
        col = dark if g.random() < 0.5 else light
        fd.line([s((x, y)), s((x + math.cos(b) * L, y + math.sin(b) * L))], fill=col + (255,), width=SS // 2 + 1)


def button(canvas, c, r, color, dark, holes=4):
    m = ellipse_mask((c[0] - r, c[1] - r, c[0] + r, c[1] + r))
    outline_and_fill(canvas, m, color, 1.0)
    rim = ellipse_mask((c[0] - r * 0.66, c[1] - r * 0.66, c[0] + r * 0.66, c[1] + r * 0.66))
    inner = ellipse_mask((c[0] - r * 0.5, c[1] - r * 0.5, c[0] + r * 0.5, c[1] + r * 0.5))
    paste(canvas, dark, ImageChops.subtract(rim, inner))
    d = ImageDraw.Draw(canvas)
    hr = r * 0.15
    offs = [(-0.22, -0.22), (0.22, -0.22), (-0.22, 0.22), (0.22, 0.22)] if holes == 4 else [(-0.22, 0), (0.22, 0)]
    for ox, oy in offs:
        x, y = c[0] + ox * r, c[1] + oy * r
        d.ellipse([s((x - hr, y - hr)), s((x + hr, y + hr))], fill=OUTLINE)
    d.arc([s((c[0] - r * 0.8, c[1] - r * 0.8)), s((c[0] + r * 0.8, c[1] + r * 0.8))], 200, 250, fill=(255, 255, 255, 230), width=SS)


def tape(canvas, c, angle_deg, length, width, seed):
    """Translucent sticky tape with torn, zig-zag ends."""
    g = random.Random(seed)
    a = math.radians(angle_deg)
    ux, uy = math.cos(a), math.sin(a)
    vx, vy = -uy, ux
    hl, hw = length / 2, width / 2

    def P(u, v):
        return (c[0] + ux * u + vx * v, c[1] + uy * u + vy * v)

    pts = [P(-hl, -hw), P(hl, -hw)]
    n = 5
    for i in range(1, n):  # torn right end
        v = -hw + width * i / n
        pts.append(P(hl + (g.uniform(0.3, 1.0) if i % 2 else -g.uniform(0.0, 0.5)), v))
    pts += [P(hl, hw), P(-hl, hw)]
    for i in range(n - 1, 0, -1):  # torn left end
        v = -hw + width * i / n
        pts.append(P(-hl - (g.uniform(0.3, 1.0) if i % 2 else -g.uniform(0.0, 0.5)), v))
    m = poly_mask(pts)
    layer = Image.new("RGBA", (RW, RH), (0, 0, 0, 0))
    paste(layer, (255, 253, 238, 165), m)
    # faint edge so it reads on light paper
    edge = ImageChops.subtract(m, m.filter(ImageFilter.MinFilter(SS + 1)))
    paste(layer, (140, 105, 55, 175), edge)
    # glossy streak
    gl = blank()
    ImageDraw.Draw(gl).line([s(P(-hl * 0.8, -hw * 0.35)), s(P(hl * 0.6, -hw * 0.35))], fill=255, width=SS)
    paste(layer, (255, 255, 255, 170), clip(gl, m))
    canvas.alpha_composite(layer)


def main():
    out = "Headgear_Paper.png"
    preview = None
    args = sys.argv[1:]
    if "--preview" in args:
        i = args.index("--preview")
        preview = args[i + 1]
        del args[i:i + 2]
    if args:
        out = args[0]

    cv = Image.new("RGBA", (RW, RH), (0, 0, 0, 0))

    OFF[0] = UP   # everything above the band sits lower by UP
    # ---- back spike (the inside of the paper shows: darker orange) ----
    back = poly_mask(scissor_poly([(51.5, 47), (57, 47), (63, 96), (47, 96)]))
    outline_and_fill(cv, back, PAPER_BACK)
    sh = poly_mask([(54.5, 47), (57, 47), (63, 96), (55, 96)])
    paste(cv, PAPER_BACK_SHADE, clip(sh, back))
    paper_noise(cv, back, 8, 2)

    # ---- front paper: five-pointed cut-out rolled into a cone ----
    # one continuous outline (body + spikes) with V-shaped notches, so the
    # cylinder runs straight up into the spikes without seams or steps.
    front_pts = [
        (40, 169), (33, 117), (27.5, 100), (23, 81), (17, 60), (7, 42),          # left edge, far-left spike
        (10, 39.5), (14, 39),                                                     # far-left tip
        (25, 60), (30, 75), (32.5, 80.5),                                         # notch 1
        (33, 60), (31, 30), (30, 16), (32.5, 14.5), (35.5, 15.5),                 # left spike + tip
        (41, 60), (47, 86), (51, 93), (54.5, 95.5), (58, 93), (61, 86),           # notch 2 (back spike shows)
        (67.5, 60), (78.5, 13), (81.5, 11.5), (84, 13.5),                         # right spike + tip
        (79, 60), (76.5, 82), (75.3, 88.5),                                       # notch 3
        (80, 75), (85, 60), (98.5, 42), (101.5, 40.5), (104.5, 43),               # far-right spike + tip
        (94, 60), (88, 75), (84, 86), (82, 100), (77, 116), (68, 169),            # right edge
    ]
    front = poly_mask(scissor_poly(front_pts, 0.25, 6))
    outline_and_fill(cv, front, PAPER)

    # cone shading: one shadow side, bounded by the glued seam and running up
    # through the middle of the right spike, so it's continuous into the spikes
    SEAM_CTRL = [(58, 168), (60.5, 130), (63, 100), (65.2, 88), (68.6, 75), (72.2, 60), (76.2, 40),
                 (79.4, 24), (81.3, 15), (81.8, 10)]
    facet = poly_mask([(81.5, 5), (115, 5), (115, 175), (58, 175)] + SEAM_CTRL + [(81.5, 5)])
    paste(cv, PAPER_SHADE, clip(facet, front))
    # light rim along every left-facing cut edge (where the paper faces the light)
    shifted = ImageChops.offset(front, int(2.6 * SS), 0)
    rim_l = ImageChops.subtract(front, shifted)
    paste(cv, PAPER_LIGHT, ImageChops.subtract(rim_l, facet))
    paste(cv, (242, 186, 64), clip(rim_l, facet))
    # soft shade in the bottom of each notch where the paper curves away
    for nx, ny in ((32.5, 80.5), (54.5, 95.5), (75.3, 88.5)):
        nm = poly_mask([(nx - 2.2, ny - 6), (nx + 2.2, ny - 6), (nx + 0.8, ny + 3.5), (nx - 0.8, ny + 3.5)])
        paste(cv, (238, 182, 52), ImageChops.subtract(clip(nm, front), facet))
    paper_noise(cv, front, 9, 3)

    d = ImageDraw.Draw(cv)
    # glued seam where the paper overlaps itself (with scissor-wobbly edge)
    # it runs the whole way up the cone and the right spike, to the tip under the pom-pom
    ctrl = SEAM_CTRL

    def catmull(pts, per=8):
        pts = [pts[0]] + pts + [pts[-1]]
        out = []
        for i in range(1, len(pts) - 2):
            p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
            for k in range(per):
                u = k / per
                out.append(tuple(0.5 * ((2 * p1[j]) + (-p0[j] + p2[j]) * u
                                        + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * u * u
                                        + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * u ** 3) for j in range(2)))
        out.append(pts[-2])
        return out

    seam = catmull(ctrl)
    nseg = len(seam) - 1
    for i in range(nseg):
        a, b = seam[i], seam[i + 1]
        taper = min(1.0, (nseg - i) / 10)            # thins out toward the tip
        d.line([s((a[0] + 1.1 * taper, a[1])), s((b[0] + 1.1 * taper, b[1]))], fill=PAPER_SHADE + (255,),
               width=max(1, int(SS * taper)))
    for i in range(nseg):
        a, b = seam[i], seam[i + 1]
        taper = min(1.0, (nseg - i) / 10)
        d.line([s(a), s(b)], fill=OUTLINE, width=max(2, int(SS * 1.1 * (0.45 + 0.55 * taper))))

    # crayon scribbles: a wonky star and some glitter-glue dots
    star_c, R = (49, 128), 7.5
    pts = []
    for i in range(10):
        r = R if i % 2 == 0 else R * 0.45
        a = -math.pi / 2 + i * math.pi / 5 + rng.uniform(-0.06, 0.06)
        pts.append(s((star_c[0] + math.cos(a) * r + rng.uniform(-0.4, 0.4), star_c[1] + math.sin(a) * r)))
    pts.append(pts[0])
    d.line(pts, fill=(226, 60, 60, 235), width=int(SS * 1.1), joint="curve")
    for _ in range(3):  # crayon fill strokes
        y = star_c[1] + rng.uniform(-2.5, 2.5)
        d.line([s((star_c[0] - 2.5, y)), s((star_c[0] + 2.5, y + rng.uniform(-1, 1)))], fill=(236, 90, 80, 200), width=SS)
    glitter = [(40, 104, (240, 244, 252)), (66, 110, (255, 226, 120)), (58, 142, (255, 236, 160)),
               (44, 152, (255, 150, 200)), (70, 132, (230, 236, 248)), (36, 98, (255, 214, 90)),
               (52, 112, (255, 255, 255)), (62, 158, (255, 255, 255)), (19, 64, (255, 226, 120)),
               (90, 60, (240, 244, 252)), (79, 40, (255, 255, 255))]
    for x, y, col in glitter:
        r = 1.2
        d.ellipse([s((x - r, y - r)), s((x + r, y + r))], fill=col + (255,))
        d.point([s((x - 0.4, y - 0.4))], fill=(255, 255, 255, 255))

    # ---- a strip of blue paper wrapped round the cone ----
    strip = clip(poly_mask(scissor_poly([(20, 99), (95, 96), (95, 106), (20, 109)], 0.3, 5)), dilate(front, 0.0))
    outline_and_fill(cv, strip, (110, 186, 236), 1.2)
    paste(cv, (70, 140, 206), clip(facet, strip))
    paste(cv, (170, 220, 250), clip(poly_mask([(20, 99.5), (95, 96.5), (95, 98.5), (20, 101.5)]), strip))
    paper_noise(cv, strip, 8, 7)
    d = ImageDraw.Draw(cv)
    for x in (37, 47, 57, 67, 76):  # crayon zig-zag on the strip
        d.line([s((x, 101.5)), s((x + 2.5, 104.5)), s((x + 5, 101.5))], fill=(255, 255, 255, 220), width=SS)

    # Seen from below (like the plate): every ring's edge is the near, lower half of
    # an ellipse, so horizontal edges sag in the middle.
    def arc(xl, xr, y, ry, n=24):
        cx, hw = (xl + xr) / 2, (xr - xl) / 2
        return [(cx + hw * (-1 + 2 * i / n), y + ry * math.sqrt(max(0.0, 1 - (-1 + 2 * i / n) ** 2)))
                for i in range(n + 1)]

    # ---- flared paper collar cupped round the cone, a skirt over the band's top ----
    # seen from below: face runs from the cone down to the FAR arc of its rim, and
    # inside the rim we look up at the collar's shaded underside.
    rcx, rcy, rrx, rry = 54.5, 165.0, 27.5, 6.0
    c_top = arc(38, 70.5, 151, 3.5)
    c_far = arc(rcx - rrx, rcx + rrx, rcy, -rry)
    collar_all = ImageChops.lighter(poly_mask(scissor_poly(c_top + c_far[::-1], 0.25, 3)),
                                    ellipse_mask((rcx - rrx, rcy - rry, rcx + rrx, rcy + rry)))
    rim = ellipse_mask((rcx - rrx, rcy - rry, rcx + rrx, rcy + rry))
    collar_face = ImageChops.subtract(collar_all, rim)
    outline_and_fill(cv, collar_all, (255, 222, 110), 1.3)
    paste(cv, (190, 128, 40), rim)                       # underside, in shadow
    paste(cv, (222, 160, 56), ImageChops.subtract(rim, ellipse_mask((rcx - rrx, rcy - rry + 2, rcx + rrx, rcy + rry + 2))))
    paste(cv, OUTLINE, ImageChops.subtract(dilate(collar_face, 0.9), collar_face))
    inner = collar_face.filter(ImageFilter.MinFilter(3))
    paste(cv, (255, 222, 110), inner)
    paste(cv, PAPER_SHADE, clip(poly_mask([(62, 143), (90, 143), (90, 178), (68, 178)]), inner))
    paper_noise(cv, collar_face, 8, 8)
    d = ImageDraw.Draw(cv)
    for k in (3, 7, 12, 17, 21):  # snipped tabs, radiating with the flare
        d.line([s(c_top[k]), s(c_far[k])], fill=(200, 140, 40, 255), width=SS // 2 + 1)

    OFF[0] = 0.0  # the band, tube and plate keep their places
    before_band = cv.copy()   # the collar is in front of the band; restored over it below
    # ---- band: red paper ring, seen from below ----
    # face = from the top rim's near arc down to the FAR arc of the bottom opening;
    # inside the opening we look up into the ring and see the tube pass through.
    x0, x1, n = 30, 79, 14
    ocx, ocy, orx, ory = 54.5, 193.0, 23.0, 5.0
    zz = []
    for i, (x, y) in enumerate(arc(x0 + 3, x1 - 3, 151.5, 5.0, n)):   # top edge is hidden up inside the cup
        zz.append((x, y + (0 if i % 2 else 1.6)))
    for i, (x, y) in reversed(list(enumerate(arc(ocx - orx, ocx + orx, ocy, ory, n)))):
        zz.append((x, y - (0 if i % 2 else 1.4)))
    band_all = poly_mask(zz)
    opening = ellipse_mask((ocx - orx, ocy - ory, ocx + orx, ocy + ory))
    band_face = ImageChops.subtract(band_all, opening)
    band_all = ImageChops.lighter(band_all, opening)
    outline_and_fill(cv, band_all, BAND)
    # inside of the ring: far inner wall dark, fading to the paper's paler back side near us
    paste(cv, (110, 22, 32), opening)
    paste(cv, (150, 40, 48), ImageChops.subtract(opening, ellipse_mask((ocx - orx, ocy - ory - 2.2, ocx + orx, ocy + ory - 2.2))))
    paste(cv, OUTLINE, ImageChops.subtract(dilate(band_face, 0.9), band_face.filter(ImageFilter.MaxFilter(1))))
    paste(cv, BAND, band_face.filter(ImageFilter.MinFilter(3)))
    paste(cv, BAND_SHADE, clip(poly_mask([(66, 150), (90, 150), (90, 200), (64, 200)]), band_face.filter(ImageFilter.MinFilter(3))))
    paste(cv, BAND_LIGHT, clip(poly_mask(arc(27, 82, 165.6 + UP, 6.0) + arc(27, 82, 168.2 + UP, 6.0)[::-1]), band_face.filter(ImageFilter.MinFilter(3))))
    # the part of the band up inside the collar sits in its shadow, darkest at the top
    up_inside = clip(band_face, rim)
    paste(cv, (128, 24, 36), up_inside)
    paste(cv, (150, 30, 42), clip(up_inside, poly_mask(arc(20, 90, 161.5 + UP, 6.0) + [(90, 178 + UP), (20, 178 + UP)])))
    paste(cv, (90, 16, 26), clip(up_inside, poly_mask([(20, 140)] + arc(20, 90, 159.0 + UP, 6.0) + [(90, 140)])))
    paper_noise(cv, band_face, 9, 4)
    # buttons ride a ring round the band: they sag in the middle, outer ones turn away
    bcx, bhw = 54.5, 24.5
    for bx, col, dk, h in [(38, BUTTON, BUTTON_SHADE, 4), (48.5, (250, 220, 70), (210, 160, 30), 2),
                           (60.5, BUTTON, BUTTON_SHADE, 4), (71, (250, 140, 200), (210, 90, 160), 2)]:
        u = (bx - bcx) / bhw
        by = 177.3 + 4.6 * math.sqrt(1 - u * u)
        squash = math.sqrt(1 - u * u) * 0.35 + 0.65
        lay = Image.new("RGBA", (RW, RH), (0, 0, 0, 0))
        button(lay, (bx, by), 4.0, col, dk, h)
        box = (int((bx - 7) * SS), int((by - 7) * SS), int((bx + 7) * SS), int((by + 7) * SS))
        crop = lay.crop(box)
        nw = max(1, int(crop.width * squash))
        crop = crop.resize((nw, crop.height), Image.LANCZOS)
        cv.alpha_composite(crop, (int(bx * SS - nw / 2), box[1]))

    # the band goes up *inside* the cup: its pinked top edge (and corners) must not
    # draw over the collar's face or the collar's lip outline
    OFF[0] = UP
    above_lip = poly_mask([(0, -UP), (114, -UP), (114, rcy)] + c_far[::-1] + [(0, rcy)])
    OFF[0] = 0.0
    cv.paste(before_band, (0, 0), ImageChops.lighter(dilate(collar_face, 0.9), above_lip))

    # ---- neck: a rolled paper tube, its bottom hidden behind the plate ----
    # the tube is seen through the band's bottom opening, so cut the band face away
    tube = poly_mask(scissor_poly([(41, 180), (68, 180), (67, 206), (42, 206)], 0.2, 6))
    tube = ImageChops.subtract(tube, band_face)
    outline_and_fill(cv, tube, PAPER, 1.3)
    paste(cv, PAPER_SHADE, clip(poly_mask([(60, 180), (90, 180), (90, 220), (58, 220)]), tube))
    paste(cv, PAPER_LIGHT, clip(poly_mask([(45, 186), (47.5, 186), (47, 213), (45, 213)]), tube))
    # shadow cast by the band onto the tube, following the opening's far arc
    paste(cv, (196, 128, 36), clip(poly_mask(arc(31.5, 77.5, 193, -5.0) + arc(31.5, 77.5, 196.5, -5.0)[::-1]), tube))
    paper_noise(cv, tube, 9, 6)
    d = ImageDraw.Draw(cv)
    d.line([s((61, 189.5)), s((60.5, 206))], fill=OUTLINE, width=SS)  # tube seam

    # ---- bottom rim: a paper plate seen from below (its underside) ----
    cx, cy, rx, ry = 53.75, 208.75, 29.5, 12.75   # bottom edge stays at 221.5 (canvas limit)
    plate = ellipse_mask((cx - rx, cy - ry, cx + rx, cy + ry))
    outline_and_fill(cv, plate, PLATE, 1.3)
    # underside: the near rim catches light, the far (upper) rim is in shade
    paste(cv, PLATE_SHADE, ImageChops.subtract(plate, ellipse_mask((cx - rx, cy - ry + 2.6, cx + rx, cy + ry + 2.6))))
    paper_noise(cv, plate, 6, 5)
    d = ImageDraw.Draw(cv)
    for i in range(34):  # fluted rim ridges
        a = 2 * math.pi * i / 34
        p0 = (cx + math.cos(a) * rx * 0.72, cy + math.sin(a) * ry * 0.72)
        p1 = (cx + math.cos(a) * rx * 0.94, cy + math.sin(a) * ry * 0.94)
        d.line([s(p0), s(p1)], fill=PLATE_SHADE + (255,), width=SS // 2 + 1)
    # the opening, looking up into the tube: dark, with the far inner wall lit
    hole = ellipse_mask((40.5, 204.5, 67.5, 215.5))
    paste(cv, OUTLINE, dilate(hole, 1.0))
    paste(cv, HOLE, hole)
    wall = ImageChops.subtract(hole, ellipse_mask((40.5, 207.3, 67.5, 218.3)))
    paste(cv, (200, 130, 40), wall)
    seam_in = clip(poly_mask([(48, 204), (49.5, 204), (49.5, 208), (48, 208)]), wall)
    paste(cv, (150, 85, 25), seam_in)



    OFF[0] = UP
    # ---- glitter: mostly gold/silver, unevenly spread but never clumped ----
    g = random.Random(42)
    glit = Image.new("RGBA", (RW, RH), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glit)

    def glit_col():
        r = g.random()
        if r < 0.55:   # golds, from deep to pale
            base = g.choice([(214, 146, 26), (255, 246, 190), (226, 160, 34), (255, 250, 214), (190, 124, 20)])
        elif r < 0.93:  # silver / white
            base = g.choice([(232, 236, 244), (250, 250, 255), (200, 206, 220)])
        else:          # the odd coloured fleck
            base = g.choice([(255, 150, 200), (150, 210, 250)])
        k = g.uniform(0.82, 1.08)
        return tuple(max(0, min(255, int(c * k))) for c in base)

    taken = set()

    def speck(x, y, alpha=255, gap=1):
        """one glitter fleck snapped to the sprite's pixel grid (rarely a 2-px chip).
        Flecks never touch each other, so nothing reads as a clump."""
        xi, yi = int(x), int(y + OFF[0])
        w = 2 if g.random() < 0.06 else 1
        h = 1
        for ox in range(-gap, w + gap):
            for oy in range(-gap, 1 + gap):
                if (xi + ox, yi + oy) in taken:
                    return False
        for ox in range(w):
            taken.add((xi + ox, yi))
        gd.rectangle([xi * SS, yi * SS, (xi + w) * SS - 1, (yi + h) * SS - 1], fill=glit_col() + (alpha,))
        return True

    def glue_line(path, width=2.8, base=(255, 236, 170, 120), density=4.2):
        # the glue swells and thins, and the glitter bunches up in places
        for i in range(len(path) - 1):
            wv = width * (0.55 + 0.75 * abs(math.sin(i * 0.37 + g.random() * 0.6)))
            gd.line([s(path[i]), s(path[i + 1])], fill=base, width=max(1, int(wv * SS)))
        L = sum(math.dist(path[i], path[i + 1]) for i in range(len(path) - 1))
        for _ in range(int(L * density)):
            k = g.randrange(len(path) - 1)
            a, b = path[k], path[k + 1]
            if g.random() > 0.6 + 0.4 * abs(math.sin(k * 0.29)):     # slightly uneven coverage
                continue
            u = g.random()
            x, y = a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u
            speck(x + g.gauss(0, width * 0.4), y + g.gauss(0, width * 0.4))

    # glitter-glue squiggles on the cone (wobbly, not quite parallel)
    glue_line([(36 + i * 0.8, 113.5 + 2.6 * math.sin(i * 0.43) + g.uniform(-0.3, 0.3)) for i in range(46)])
    glue_line([(42 + i * 0.6, 150 + 2.4 * math.sin(i * 0.33 + 1) + i * 0.03) for i in range(44)])
    glue_line([(52 + i * 0.5, 84 + 3.0 * math.sin(i * 0.5)) for i in range(18)], width=2.2)   # short dab
    # glitter glue along the collar's flared edge
    glue_line(c_far, width=2.3, density=3.6)
    # dust: thin, uneven scatter over all the paper (thicker toward the top)
    paper_px = ImageChops.lighter(clip(front, above_lip), collar_face).resize((W, H), Image.BILINEAR)
    hits = 0
    for _ in range(20000):
        if hits >= 600:
            break
        x, y = g.uniform(0, W), g.uniform(8, 172)
        # density drifts gently (more toward the top, a soft wave across), never bunches
        if paper_px.getpixel((int(x), int(y))) > 200 and \
                g.random() < (1.2 - y / 172) * (0.75 + 0.25 * math.sin(x * 0.21 + y * 0.09)):
            if speck(x, y, g.choice((255, 210, 160))):
                hits += 1
    # keep every fleck on the paper
    on_paper = ImageChops.lighter(ImageChops.lighter(clip(front, above_lip), collar_face), back)
    glit.putalpha(ImageChops.multiply(glit.getchannel("A"), on_paper))
    cv.alpha_composite(glit)
    # a few sparkle glints
    d = ImageDraw.Draw(cv)
    for gx, gy, r in ((21, 62, 2.9), (73, 47, 2.0), (67, 126, 3.2), (40, 147, 1.6), (62, 155, 2.0),
                      (33, 38, 1.5), (88, 70, 1.8), (46, 113, 2.4)):
        d.line([s((gx - r, gy)), s((gx + r, gy))], fill=(255, 252, 230, 235), width=SS // 2 + 1)
        d.line([s((gx, gy - r)), s((gx, gy + r))], fill=(255, 252, 230, 235), width=SS // 2 + 1)
        d.ellipse([s((gx - 0.6, gy - 0.6)), s((gx + 0.6, gy + 0.6))], fill=(255, 255, 255, 255))

    # ---- sticky tape holding it all together ----
    tape(cv, (24, 71), 62, 17, 7, 11)       # far-left spike was bent back
    tape(cv, (79, 34), -78, 14, 6.5, 12)    # right spike splint
    tape(cv, (60, 117), 4, 15, 6.5, 13)     # seam
    tape(cv, (59, 148), -6, 15, 6.5, 14)    # seam
    tape(cv, (33, 163), -55, 13, 6, 15)     # collar -> band
    OFF[0] = 0.0
    tape(cv, (74, 193), 62, 12, 5.5, 16)    # band -> plate
    tape(cv, (37, 201), -70, 10, 4.5, 17)    # tube -> plate rim
    OFF[0] = UP

    # ---- tip pom-poms ----
    poms = [((11.5, 36), 9.5, (232, 70, 120), (186, 40, 92), (255, 150, 186)),
            ((30, 10.5), 9.5, (80, 160, 236), (44, 106, 190), (160, 210, 255)),
            ((54.5, 42), 9.0, (120, 200, 80), (74, 150, 50), (190, 240, 150)),
            ((85.5, 9), 9.5, (170, 110, 220), (120, 70, 176), (214, 176, 250)),
            ((103.5, 34), 9.5, (250, 150, 50), (210, 100, 30), (255, 206, 130))]
    # back (middle) pom first so neighbours overlap it
    order = [2, 0, 1, 3, 4]
    for i in order:
        c, r, col, dk, li = poms[i]
        pompom(cv, c, r, col, dk, li, 100 + i)

    img = cv.resize((W, H), Image.LANCZOS)
    img.save(out)
    print("wrote", out)
    if preview:
        orig = Image.open("Assets/content/4_Characters/2_Pigs/16_KingPig/BodyParts/Headgear.png").convert("RGBA")
        sheet = Image.new("RGBA", (W * 2 + 10, H), (120, 160, 120, 255))
        sheet.alpha_composite(orig, (0, 0))
        sheet.alpha_composite(img, (W + 10, 0))
        sheet.resize((sheet.width * 3, sheet.height * 3), Image.NEAREST).save(preview)
        print("wrote", preview)


if __name__ == "__main__":
    main()
