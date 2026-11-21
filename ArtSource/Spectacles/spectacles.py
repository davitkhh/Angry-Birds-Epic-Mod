"""Semi-rimless round reading spectacles fitted to the PigPirate face (Idle pose, 3/4 view facing left).

Frame: gold wire in the approved gold-tooth palette (ArtSource/GoldTeeth/GOLD_TOOTH_STYLE.md) with the game's
dark warm outline. Half-moon readers: each lens is the BOTTOM half of the old round lens, and the gold rim wraps the
whole D shape (flat top edge + lower arc); the eye peeks over the flat top. Bridge, arm and hinge attach at the
D's two corners. Lenses: faint white tint + diagonal sheen + a white glint, like ProfessorPig's Glasses.

Built as a symmetrical 3D frame projected into the 3/4 view (see "3D model" below).
Geometry is in reference-render px (512x512 probe framing, body px + BODY_TO_RENDER (151,220)):
  near eye (visible, yellow) x 280-303, y 311-338      -> near lens, round, sits a little low (reading style)
  far eye (under the patch, yellow) x 186-203, y 304-330 -> far lens, foreshortened ellipse
The reference is rendered with the EyePatch bone hidden (preview only; the game rig is unchanged).
  Snout.png placed at (193,308) is IN FRONT of the far lens / bridge end: those go in the _Far layer (kept whole).

Run: python3 -B spectacles.py   -> three textures on one canvas (out/origin.txt), plus previews:
     out/Glasses_PirateReading.png       front, above the snout
     out/Glasses_PirateReading_Far.png   far side, behind the snout (above the eyes)
     out/Glasses_PirateReading_Back.png  behind the body: tip hooks curling round the back of the head
"""
import math
import os

import cairo
from PIL import Image, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, '..', '..')
OUT = os.path.join(HERE, 'out')
PARTS = os.path.join(ROOT, 'Assets', 'content', '4_Characters', '2_Pigs', '6_PigPirate', 'BodyParts')
# PigPirate Idle with the EyePatch bone hidden (PirateHeadgearProbe, request content 'EyePatch'; render 0 via
# PirateBucket/preview.bare_pig). Same framing as PirateBucket/out/pig_reference.png.
REF = os.path.join(HERE, 'out', 'pig_reference_nopatch.png')
SNOUT_AT = (193, 308)

SS = 8  # supersampling

# gold (GOLD_TOOTH_STYLE.md)
LIT = (255, 226, 84)
BODY = (250, 182, 30)
SHADE = (196, 112, 10)
SHINE = (255, 252, 210)
OUTLINE = (70, 24, 8)

# --- 3D model -----------------------------------------------------------------------------------------------------
# The glasses are ONE symmetrical 3D frame (glasses space: x = across, + toward the pig's near/visible side, y = up,
# z = forward out of the face; origin = midway between the lens centres on the flat top line). Both lenses are the
# same half-disc of radius R; each is wrapped back by WRAP about its own vertical axis (face form). The frame is then
# yawed by YAW (head turned toward screen-left) and rolled by ROLL (the face's tilt: near eye lower) and projected
# orthographically. All on-screen differences between the lenses come from that pose, not from the model.
R = 15.6            # lens radius (px)
WRAP = 17.5         # deg, each lens turned back at its outer edge
YAW = 32.5          # deg
ROLL = 4.42         # deg, = slope between the two eyes
# lens top-centre targets (reading position: just under the eye centres) fix origin, spacing and roll
NEAR_TOP = (292.0, 328.5)
FAR_TOP = (195.0, 321.0)
ORIGIN = ((NEAR_TOP[0] + FAR_TOP[0]) / 2, (NEAR_TOP[1] + FAR_TOP[1]) / 2)
HALF_SPAN = math.hypot(NEAR_TOP[0] - FAR_TOP[0], NEAR_TOP[1] - FAR_TOP[1]) / 2 / math.cos(math.radians(YAW))
BRIDGE_H = 27.0     # bridge control height above the top line (arches over the snout top, y~309 on screen)
BRIDGE_Z = 10.0     # bridge pushed forward of the lens plane
EAR_Y = 1.5         # temples run back almost level with the hinges
EAR_INSET = 1.2     # temple end lands this far inside the body silhouette, then the tip hooks over it
BRIDGE_FRONT_X = 214.0   # screen x: bridge is in front of the snout right of this, behind it (masked) left of it
RIM_W = 2.1         # gold wire thickness
LINE_W = 1.0        # dark outline each side of the wire
TIP_W = 3.0
TIP = (92, 50, 24)
TIP_LIT = (150, 92, 48)
TIP_LINE = (40, 14, 4)


def project(P):
    x, y, z = P
    yw, rl = math.radians(YAW), math.radians(ROLL)
    X = x * math.cos(yw) - z * math.sin(yw)
    Y = -y
    return (ORIGIN[0] + X * math.cos(rl) - Y * math.sin(rl), ORIGIN[1] + X * math.sin(rl) + Y * math.cos(rl))


def lens_frame(side):
    """side +1 = near, -1 = far. Lens point (u outward, v up) -> 3D."""
    w = math.radians(WRAP)
    return lambda u, v: (side * (HALF_SPAN + u * math.cos(w)), v, -u * math.sin(w))


def lens_ellipse(side):
    """Exact screen ellipse of the lens circle (orthographic: yaw squashes x, roll rotates)."""
    c = project(lens_frame(side)(0, 0))
    return dict(c=c, rx=R * math.cos(math.radians(YAW - side * WRAP)), ry=R, tilt=ROLL)


NEAR = lens_ellipse(+1)
FAR = lens_ellipse(-1)


def bez3(pts, t):
    return tuple((1 - t) ** 3 * pts[0][i] + 3 * (1 - t) ** 2 * t * pts[1][i] + 3 * (1 - t) * t * t * pts[2][i]
                 + t ** 3 * pts[3][i] for i in range(3))


def proj_curve(pts3, n=40):
    return [project(bez3(pts3, i / n)) for i in range(n + 1)]


def bridge3():
    ni = lens_frame(+1)(-R, 0)
    fi = lens_frame(-1)(-R, 0)
    return [ni, (ni[0] * 0.55, BRIDGE_H, ni[2] + BRIDGE_Z), (fi[0] * 0.55, BRIDGE_H, fi[2] + BRIDGE_Z), fi]


def temple3(side, ear):
    h = lens_frame(side)(R, 0)
    e = (side * ear[0], EAR_Y, ear[1])
    return [h, (h[0], h[1], h[2] + (e[2] - h[2]) * 0.4), (e[0] - side * 4.0, e[1], e[2] + 10.0), e]


def tip3(side, ear):
    t = temple3(side, ear)
    e = t[3]
    # Mirror-symmetric in 3D: past the ear the hook goes OUTWARD (side * x) more than back (-z), then curls down
    # hugging the outside of the body. Under the yaw, outward shows as cos(YAW) and back as sin(YAW) toward
    # screen-right, so the near hook clears the right edge by ~6.7 px and the far one the left edge by ~3.5 px
    # (foreshortened, same 3D shape). The part past the silhouette crossing is in the behind-body layer.
    return [bez3(t, TIP_START), (e[0] + side * 2.5, e[1] + 0.5, e[2] - 1.5), (e[0] + side * 7.0, e[1] - 6.0, e[2] - 3.0),
            (e[0] + side * 4.5, e[1] - 17.0, e[2] - 4.0)]


TIP_START = 0.68    # temple parameter where the tip sleeve begins; the gold temple stops just under it
TEMPLE_END = TIP_START + 0.02


def tip_split(side, ear, edges, n=60):
    """The whole tip as ONE projected polyline plus a cut (point, tangent) where it crosses the body silhouette:
    before the cut it lies on the side of the head (lens layer), after it curls round behind (behind-body layer).
    Both layers draw the same full stroke clipped to opposite half-planes, so the halves meet with no caps or
    outline between them. A hook that never leaves the silhouette is cut where it reaches furthest out."""
    proj = [project(bez3(tip3(side, ear), i / n)) for i in range(n + 1)]

    def out(q):
        e = edges.get(round(q[1]))
        if not e:
            return 0.0
        return (q[0] - e[1]) if side > 0 else (e[0] - q[0])   # > 0 = outside the silhouette

    k = next((i for i, q in enumerate(proj) if out(q) > 0.0), None)
    if k is None:
        k = max(range(len(proj)), key=lambda i: out(proj[i]))
    k = max(1, min(n - 1, k))
    tx, ty = proj[k + 1][0] - proj[k - 1][0], proj[k + 1][1] - proj[k - 1][1]
    tl = math.hypot(tx, ty) or 1.0
    return proj, proj[k], (tx / tl, ty / tl)


def clip_half(ctx, point, tangent, keep_after, overlap=0.35):
    """Clip to the half-plane before (keep_after=False) or after the line through point perpendicular to tangent."""
    tx, ty = tangent
    nx, ny = -ty, tx
    sgn = 1.0 if keep_after else -1.0
    ox, oy = point[0] - sgn * tx * overlap, point[1] - sgn * ty * overlap
    big = 400.0
    ctx.move_to(ox + nx * big, oy + ny * big)
    ctx.line_to(ox - nx * big, oy - ny * big)
    ctx.line_to(ox - nx * big + sgn * tx * big, oy - ny * big + sgn * ty * big)
    ctx.line_to(ox + nx * big + sgn * tx * big, oy + ny * big + sgn * ty * big)
    ctx.close_path()
    ctx.clip()


def tip_half(ctx, side, ears, edges, after):
    pts, point, tangent = tip_split(side, ears, edges)
    ctx.save()
    clip_half(ctx, point, tangent, after)
    tip(ctx, pts)
    ctx.restore()


def temple_pts(side, ears):
    """Gold temple from the hinge to just under the start of the tip sleeve (no wire past the sleeve)."""
    t3 = temple3(side, ears)
    return [project(bez3(t3, TEMPLE_END * i / 40)) for i in range(41)]


def silhouette_edges(ref):
    """Per-row left/right edge of the pig body (fishbone weapon excluded by colour)."""
    px = ref.load()
    edges = {}
    for y in range(260, 360):
        xs = [x for x in range(120, 400) if px[x, y][3] > 128 and (px[x, y][1] > px[x, y][0] + 10 or sum(px[x, y][:3]) < 200)]
        if xs:
            edges[y] = (xs[0], max(x for x in range(300, 400) if px[x, y][3] > 128))
    return edges


def solve_ears(edges):
    """Symmetric ear point (X, Z) whose two projections land EAR_INSET inside the silhouette on both sides."""
    best = None
    for X10 in range(600, 1600, 5):
        for Z10 in range(-1500, 0, 5):
            X, Z = X10 / 10, Z10 / 10
            pn = project((X, EAR_Y, Z))
            pf = project((-X, EAR_Y, Z))
            en, ef = edges.get(round(pn[1])), edges.get(round(pf[1]))
            if not en or not ef:
                continue
            err = (pn[0] - (en[1] - EAR_INSET)) ** 2 + (pf[0] - (ef[0] + EAR_INSET)) ** 2
            if best is None or err < best[0]:
                best = (err, X, Z)
    return best[1], best[2]


def ell(p, a, t=0.0):
    """Point on a lens ellipse at angle a (deg); t = radial offset in px."""
    tr = math.radians(p['tilt'])
    ar = math.radians(a)
    x = (p['rx'] + t) * math.cos(ar)
    y = (p['ry'] + t) * math.sin(ar)
    return (p['c'][0] + x * math.cos(tr) - y * math.sin(tr), p['c'][1] + x * math.sin(tr) + y * math.cos(tr))


def ell_path(ctx, p, a0, a1, t=0.0, n=96, move=True):
    for i in range(n + 1):
        pt = ell(p, a0 + (a1 - a0) * i / n, t)
        (ctx.move_to if (i == 0 and move) else ctx.line_to)(*pt)


def rgb(ctx, c, a=1.0):
    ctx.set_source_rgba(c[0] / 255, c[1] / 255, c[2] / 255, a)


def gold_grad(x0, y0, x1, y1):
    g = cairo.LinearGradient(x0, y0, x1, y1)
    for k, c in ((0.0, LIT), (0.45, BODY), (0.62, BODY), (1.0, SHADE)):
        g.add_color_stop_rgb(k, *(v / 255 for v in c))
    return g


def wire(ctx, build, grad, shine=True):
    """Stroke a gold wire: dark outline, gold body, thin top-left shine."""
    ctx.set_line_cap(cairo.LINE_CAP_ROUND)
    ctx.set_line_join(cairo.LINE_JOIN_ROUND)
    build()
    rgb(ctx, OUTLINE)
    ctx.set_line_width(RIM_W + 2 * LINE_W)
    ctx.stroke()
    build()
    ctx.set_source(grad)
    ctx.set_line_width(RIM_W)
    ctx.stroke()
    if shine:
        ctx.save()
        ctx.translate(-0.45, -0.55)
        build()
        rgb(ctx, SHINE, 0.75)
        ctx.set_line_width(RIM_W * 0.32)
        ctx.stroke()
        ctx.restore()


def half(ctx, p):
    """Half-moon lens outline: lower arc (0 -> 90 -> 180 deg, y down) closed by the flat top edge."""
    ell_path(ctx, p, 0, 180)
    ctx.close_path()


def lens(ctx, p):
    cx, cy = p['c']
    # glass tint
    half(ctx, p)
    g = cairo.LinearGradient(cx - p['rx'], cy, cx + p['rx'], cy + p['ry'])
    g.add_color_stop_rgba(0.0, 1, 1, 1, 0.34)
    g.add_color_stop_rgba(0.55, 0.92, 0.98, 1, 0.16)
    g.add_color_stop_rgba(1.0, 0.85, 0.95, 1, 0.26)
    ctx.set_source(g)
    ctx.fill()
    # diagonal sheen band (clipped to the lens)
    ctx.save()
    half(ctx, p)
    ctx.clip()
    ctx.translate(cx, cy)
    ctx.rotate(math.radians(-38))
    rgb(ctx, (255, 255, 255), 0.30)
    ctx.rectangle(-p['rx'] * 0.10, -40, p['rx'] * 0.26, 80)
    ctx.fill()
    rgb(ctx, (255, 255, 255), 0.18)
    ctx.rectangle(p['rx'] * 0.26, -40, p['rx'] * 0.10, 80)
    ctx.fill()
    ctx.restore()
    # glint dot top-left, just under the flat top (ProfessorPig style)
    gx, gy = ell(p, 158, -p['rx'] * 0.42)
    r = max(1.5, min(p['rx'], p['ry']) * 0.17)
    rgb(ctx, (255, 255, 255), 0.95)
    ctx.arc(gx, gy, r, 0, 2 * math.pi)
    ctx.fill()


def rim(ctx, p):
    cx, cy = p['c']
    wire(ctx, lambda: half(ctx, p), gold_grad(cx - p['rx'], cy - p['ry'] * 0.2, cx + p['rx'] * 0.7, cy + p['ry']))


def poly(ctx, pts):
    ctx.move_to(*pts[0])
    for q in pts[1:]:
        ctx.line_to(*q)


def grad_for(pts):
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    return gold_grad(min(xs), min(ys), max(xs), max(ys) + 4)


def tip(ctx, pts):
    """Temple tip (earpiece): dark tortoise sleeve, thicker than the wire, rounded end, small top-left highlight."""
    ctx.set_line_cap(cairo.LINE_CAP_ROUND)
    ctx.set_line_join(cairo.LINE_JOIN_ROUND)
    for col, wdt, off in ((TIP_LINE, TIP_W + 2 * LINE_W, 0), (TIP, TIP_W, 0), (TIP_LIT, TIP_W * 0.3, 1)):
        ctx.save()
        if off:
            ctx.translate(-0.5, -0.6)
        poly(ctx, pts)
        rgb(ctx, col, 0.9 if off else 1.0)
        ctx.set_line_width(wdt)
        ctx.stroke()
        ctx.restore()


def hinge(ctx, p, a):
    hx, hy = ell(p, a)
    rgb(ctx, OUTLINE)
    ctx.arc(hx, hy - 0.3, 2.1, 0, 2 * math.pi)
    ctx.fill()
    rgb(ctx, BODY)
    ctx.arc(hx, hy - 0.3, 1.15, 0, 2 * math.pi)
    ctx.fill()


def render(layer, ears, edges):
    """layer: 'front' -> above the snout: bridge over the snout, near lens, temple, tip sleeve.
    'far' -> behind the snout, above the eyes: far lens, temple, tip sleeve, the bridge's descent (all complete).
    'back' -> behind the body: both tips from where they cross the silhouette (curling round the head)."""
    W, H = 512, 512
    s = cairo.ImageSurface(cairo.FORMAT_ARGB32, W * SS, H * SS)
    ctx = cairo.Context(s)
    ctx.scale(SS, SS)
    bridge = proj_curve(bridge3())
    if layer == 'back':
        for side in (-1, +1):
            tip_half(ctx, side, ears, edges, after=True)
    elif layer == 'far':
        arm = temple_pts(-1, ears)
        lens(ctx, FAR)
        wire(ctx, lambda: poly(ctx, arm), grad_for(arm))
        tip_half(ctx, -1, ears, edges, after=False)
        rim(ctx, FAR)
        hinge(ctx, FAR, 180)
        ctx.save()
        ctx.rectangle(0, 0, BRIDGE_FRONT_X + 1.0, H)   # 1 px overlap with the front layer's part, no seam
        ctx.clip()
        wire(ctx, lambda: poly(ctx, bridge), grad_for(bridge))
        ctx.restore()
    else:
        ctx.save()
        ctx.rectangle(BRIDGE_FRONT_X, 0, W, H)
        ctx.clip()
        wire(ctx, lambda: poly(ctx, bridge), grad_for(bridge))
        ctx.restore()
        arm = temple_pts(+1, ears)
        lens(ctx, NEAR)
        wire(ctx, lambda: poly(ctx, arm), grad_for(arm))
        tip_half(ctx, +1, ears, edges, after=False)
        rim(ctx, NEAR)
        hinge(ctx, NEAR, 0)
    return to_image(s).resize((W, H), Image.LANCZOS)


def to_image(surface):
    w, h = surface.get_width(), surface.get_height()
    im = Image.frombuffer('RGBA', (w, h), bytes(surface.get_data()), 'raw', 'BGRa', 0, 1)
    return im.copy()


def main():
    os.makedirs(OUT, exist_ok=True)
    ref = Image.open(REF).convert('RGBA')
    edges = silhouette_edges(ref)
    ears = solve_ears(edges)
    print('ear point (|x|, z) = (%.1f, %.1f); near lens rx %.1f, far lens rx %.1f, half span %.1f'
          % (ears[0], ears[1], NEAR['rx'], FAR['rx'], HALF_SPAN))
    behind = render('back', ears, edges)
    back = render('far', ears, edges)
    front = render('front', ears, edges)
    # all three textures share one canvas (same crop box -> same pivot), like Headgear_X / Headgear_X_Back
    full = behind.copy()
    full.alpha_composite(back)
    full.alpha_composite(front)
    box = full.getbbox()
    box = (box[0] - 1, box[1] - 1, box[2] + 1, box[3] + 1)
    front.crop(box).save(os.path.join(OUT, 'Glasses_PirateReading.png'))
    back.crop(box).save(os.path.join(OUT, 'Glasses_PirateReading_Far.png'))
    behind.crop(box).save(os.path.join(OUT, 'Glasses_PirateReading_Back.png'))
    with open(os.path.join(OUT, 'origin.txt'), 'w') as f:
        f.write('both layers: top-left in reference px %d %d, size %d %d\n' % (box[0], box[1], box[2] - box[0], box[3] - box[1]))
        f.write('Glasses_PirateReading: above Snout\n')
        f.write('Glasses_PirateReading_Far: behind Snout, above Eye/Pupil/Lid\n')
        f.write('Glasses_PirateReading_Back: behind Body (tip hooks round the back of the head)\n')

    # preview stack: behind-body layer -> reference (body + snout baked in) -> far layer -> snout again -> front layer
    snout = Image.new('RGBA', ref.size, (0, 0, 0, 0))
    snout.paste(Image.open(os.path.join(PARTS, 'Snout.png')).convert('RGBA'), SNOUT_AT)

    def dressed(bg, with_snout=True):
        t = Image.new('RGBA', ref.size, bg)
        t.alpha_composite(behind)
        t.alpha_composite(ref)
        t.alpha_composite(back)
        if with_snout:
            t.alpha_composite(snout)
        t.alpha_composite(front)
        return t

    plain = Image.new('RGBA', ref.size, (255, 255, 255, 255))
    plain.alpha_composite(ref)
    tiles = [plain, dressed((255, 255, 255, 255)), dressed((178, 178, 178, 255)), dressed((255, 255, 255, 255), False)]
    tiles = [t.crop((140, 220, 380, 400)) for t in tiles]
    w, h = tiles[0].size
    sheet = Image.new('RGBA', (w * len(tiles), h))
    for i, t in enumerate(tiles):
        sheet.alpha_composite(t, (i * w, 0))
    sheet.save(os.path.join(OUT, 'preview.png'))
    sheet.resize((sheet.width * 3, sheet.height * 3), Image.LANCZOS).save(os.path.join(OUT, 'preview_3x.png'))
    dressed((255, 255, 255, 255)).crop((120, 190, 400, 420)).save(os.path.join(OUT, 'preview_full.png'))
    # layers on their own (4x, green): back (behind body) | far (behind snout) | front | combined
    layers = [behind.crop(box), back.crop(box), front.crop(box), full.crop(box)]
    lw, lh = layers[0].size
    strip = Image.new('RGBA', (lw, lh * 4 + 12), (255, 255, 255, 255))
    for i, im in enumerate(layers):
        tile = Image.new('RGBA', (lw, lh), (60, 170, 40, 255))
        tile.alpha_composite(im)
        strip.alpha_composite(tile, (0, i * (lh + 4)))
    strip.resize((strip.width * 4, strip.height * 4), Image.NEAREST).save(os.path.join(OUT, 'layers_4x.png'))


if __name__ == '__main__':
    main()
