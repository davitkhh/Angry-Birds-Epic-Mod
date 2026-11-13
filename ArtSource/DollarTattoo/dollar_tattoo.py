"""Dollar-sign tattoo for the PigPirate body, drawn to replace the anchor tattoo on its lower right.

Matches the anchor in Assets/content/4_Characters/2_Pigs/6_PigPirate/BodyParts/Body.png (210x186):
  * flat dark-green ink (~#165203), no outline, ~4-5 px strokes,
  * anti-aliased edges at full sprite resolution (a 2x2-blocked render was tried; it looked chunkier than the anchor),
  * same footprint (~25 x 41 px, x 173..197, y 112..152) and the same lean (top tilted toward the body edge),
  * the anchor's roundness: bent along arcs concentric with the body outline, narrowing toward the edge.
The $ is an S with two full-height vertical lines through it.

Outputs (out/):
  dollar_tattoo.png        RGBA overlay on a full 210x186 canvas, already in place over the anchor
  dollar_tattoo_crop.png   the same overlay cropped to its bounds (offset printed)
  dollar_tattoo_3x*.png    ink only, rendered at 3x (full canvas 630x558, and cropped)
  Body_no_anchor.png       Body.png with the anchor painted out (directional fill along the body's shading bands)
  Body_dollar.png          Body_no_anchor.png + the dollar tattoo
  compare.png              original | dollar, 3x nearest, on a checkerboard
Run: python3 dollar_tattoo.py   (only needs Pillow)
"""
import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
BODY = os.path.join(HERE, '..', '..', 'Assets', 'content', '4_Characters', '2_Pigs', '6_PigPirate', 'BodyParts',
                    'Body.png')
OUT = os.path.join(HERE, 'out')

INK = (22, 82, 3)

# ---- glyph (surface units = body px, y down, origin at glyph center) ----
S_HEIGHT = 32.0        # S cap height (outer edge to outer edge)
S_WIDTH = 24.0         # S outer width (narrower closes the counters at this size)
STROKE = 4.4           # S stroke width
BAR_STROKE = 2.8       # vertical lines
BAR_GAP = 8.5          # center-to-center distance of the two lines
BAR_OVERHANG = 4.5     # how far the lines stick out above/below the S
# ---- placement / curvature ----
CENTER = (186.0, 132.5)  # where the glyph center lands on Body.png
# The body's right outline next to the tattoo is close to a circle (least-squares fit of Body.png's right edge,
# rows 95..181: max error 4 px). The glyph is wrapped around it like the anchor's arms: its vertical axis follows
# arcs concentric with the outline, and it narrows slightly toward the edge.
EDGE_C = (148.4, 117.4)  # fitted outline circle center
EDGE_R = 60.2            # fitted outline circle radius
BEND = 1.0               # 1 = arcs exactly concentric with the outline, 0 = straight
TILT_DEG = -8.0          # extra lean on top of following the outline (negative = more upright)
WRAP = 0.4               # radial foreshortening toward the edge: 0 = none, 1 = full spherical
SS = 8                   # supersampling per pixel


def s_path(n=220):
    """Centerline of the S: upper bowl counter-clockwise from the top-right terminal around the left,
    lower bowl clockwise around the right to the bottom-left terminal."""
    h = S_HEIGHT - STROKE
    rx = (S_WIDTH - STROKE) / 2.0
    ru = h * 0.24   # upper bowl a touch smaller than the lower one
    rl = h * 0.26
    top = -h / 2.0
    cu = top + ru
    cl = cu + ru + rl
    pts = []
    for i in range(n + 1):  # upper: -25deg -> -270deg (y down: -90 is the top)
        a = math.radians(-25 - 245 * i / n)
        pts.append((rx * 0.96 * math.cos(a), cu + ru * math.sin(a)))
    for i in range(n + 1):  # lower: -90deg -> 155deg
        a = math.radians(-90 + 245 * i / n)
        pts.append((rx * math.cos(a), cl + rl * math.sin(a)))
    return pts


def glyph_mask(scale):
    """Upright $ coverage at `scale` px per surface unit. Returns (image, origin_px)."""
    half_w = S_WIDTH / 2 + 4
    half_h = S_HEIGHT / 2 + BAR_OVERHANG + 4
    w, h = int(2 * half_w * scale), int(2 * half_h * scale)
    img = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(img)
    ox, oy = w / 2.0, h / 2.0
    r = STROKE * scale / 2
    for x, y in s_path():
        px, py = ox + x * scale, oy + y * scale
        d.ellipse((px - r, py - r, px + r, py + r), fill=255)
    bw = BAR_STROKE * scale / 2
    y0 = oy - (S_HEIGHT / 2 + BAR_OVERHANG) * scale
    y1 = oy + (S_HEIGHT / 2 + BAR_OVERHANG) * scale
    for sx in (-BAR_GAP / 2, BAR_GAP / 2):
        cx = ox + sx * scale
        d.rectangle((cx - bw, y0 + bw, cx + bw, y1 - bw), fill=255)
        d.ellipse((cx - bw, y0, cx + bw, y0 + 2 * bw), fill=255)  # slightly rounded ends, like the anchor's
        d.ellipse((cx - bw, y1 - 2 * bw, cx + bw, y1), fill=255)
    return img, (ox, oy)


def screen_to_surface(X, Y):
    """Inverse of the wrap: body px -> upright glyph units (x toward the body edge, y down along the outline)."""
    ex, ey = EDGE_C
    nx, ny = CENTER[0] - ex, CENTER[1] - ey
    r0 = math.hypot(nx, ny)
    nx, ny = nx / r0, ny / r0                      # outward normal at the glyph center
    if BEND > 1e-3:
        rb = r0 / BEND                             # bend circle radius through the glyph center
        bx, by = CENTER[0] - nx * rb, CENTER[1] - ny * rb
        dx, dy = X - bx, Y - by
        r = math.hypot(dx, dy)
        dth = math.atan2(nx * dy - ny * dx, nx * dx + ny * dy)  # +: clockwise (y down) = down along the outline
        u_flat, v = r - rb, rb * dth
    else:
        dx, dy = X - CENTER[0], Y - CENTER[1]
        u_flat, v = dx * nx + dy * ny, -dx * ny + dy * nx
    # foreshortening: the body is round, so equal surface steps get shorter toward the outline
    s = max(-0.999, min(0.999, (r0 + u_flat) / EDGE_R))
    s0 = r0 / EDGE_R
    u_sph = EDGE_R * math.cos(math.asin(s0)) * (math.asin(s) - math.asin(s0))
    u = WRAP * u_sph + (1 - WRAP) * u_flat
    t = math.radians(TILT_DEG)
    return u * math.cos(t) + v * math.sin(t), -u * math.sin(t) + v * math.cos(t)


def render_overlay(size, k=1):
    """Anti-aliased coverage of the warped glyph on the body canvas, `k` output px per body px.
    Returns an RGBA overlay of size*k."""
    scale = 6.0
    mask, (ox, oy) = glyph_mask(scale)
    mp = mask.load()
    mw, mh = mask.size
    W, H = size[0] * k, size[1] * k
    out = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    op = out.load()
    for y in range(max(0, int((CENTER[1] - 30) * k)), min(H, int((CENTER[1] + 30) * k))):
        for x in range(max(0, int((CENTER[0] - 26) * k)), min(W, int((CENTER[0] + 26) * k))):
            acc = 0
            for j in range(SS):
                for i in range(SS):
                    u, v = screen_to_surface((x + (i + 0.5) / SS) / k, (y + (j + 0.5) / SS) / k)
                    mx, my = int(ox + u * scale), int(oy + v * scale)
                    if 0 <= mx < mw and 0 <= my < mh:
                        acc += mp[mx, my]
            a = acc / (SS * SS * 255.0)
            if a > 0.1:
                op[x, y] = INK + (int(round(min(1.0, a * 1.1) * 255)),)
    return out


def anchor_mask(body):
    """The anchor: dark-ink pixels connected to its shaft, grown by one pixel for its anti-aliased rim."""
    px = body.load()
    W, H = body.size

    def ink(x, y):
        r, g, b, a = px[x, y]
        return a > 128 and g < 0x66 and r < 0x2a

    seed = (186, 126)
    assert ink(*seed), 'anchor not where expected - Body.png changed?'
    seen = {seed}
    stack = [seed]
    while stack:
        x, y = stack.pop()
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                n = (x + dx, y + dy)
                if n not in seen and 165 <= n[0] < 200 and 105 <= n[1] < 158 and ink(*n):
                    seen.add(n)
                    stack.append(n)
    grown = set(seen)
    for x, y in seen:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                n = (x + dx, y + dy)
                r, g, b, a = px[n]
                if a > 128 and g < 0x80:  # rim pixels darker than the shading band
                    grown.add(n)
    return grown


def paint_out(body, mask):
    """Fill each masked pixel from the nearest clean pixels along the body's shading bands (parallel to the
    outline here), so the bright/dark band edge stays hard instead of being smeared."""
    out = body.copy()
    src = body.load()
    op = out.load()
    d = (-0.57, 0.82)  # band direction around the anchor (follows the outline from (205,134) to (176,176))
    for (x, y) in mask:
        best = None
        for sgn in (1, -1):
            for k in range(1, 40):
                qx, qy = int(round(x + sgn * d[0] * k)), int(round(y + sgn * d[1] * k))
                if (qx, qy) not in mask and src[qx, qy][3] == 255:
                    if best is None or k < best[0]:
                        best = (k, src[qx, qy])
                    break
        if best:
            op[x, y] = best[1]
    return out


def checker(size, cell=8):
    img = Image.new('RGBA', size, (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    for y in range(0, size[1], cell):
        for x in range(0, size[0], cell):
            if (x // cell + y // cell) % 2:
                d.rectangle((x, y, x + cell - 1, y + cell - 1), fill=(204, 204, 204, 255))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    body = Image.open(BODY).convert('RGBA')
    overlay = render_overlay(body.size)
    overlay.save(os.path.join(OUT, 'dollar_tattoo.png'))
    bbox = overlay.getbbox()
    overlay.crop(bbox).save(os.path.join(OUT, 'dollar_tattoo_crop.png'))
    # ink only, rendered (not upscaled) at 3x, about the scale of the in-game screenshots
    big = render_overlay(body.size, 3)
    big.save(os.path.join(OUT, 'dollar_tattoo_3x.png'))
    big.crop(big.getbbox()).save(os.path.join(OUT, 'dollar_tattoo_3x_crop.png'))

    clean = paint_out(body, anchor_mask(body))
    clean.save(os.path.join(OUT, 'Body_no_anchor.png'))
    # keep the body's own alpha (the glyph never leaves the silhouette, but stay safe at the edge)
    result = Image.alpha_composite(clean, overlay)
    result.putalpha(clean.getchannel('A'))
    result.save(os.path.join(OUT, 'Body_dollar.png'))

    k = 3
    W, H = body.size
    cmp_img = Image.new('RGBA', (W * k * 2 + 12, H * k), (255, 255, 255, 255))
    for i, im in enumerate((body, result)):
        big = im.resize((W * k, H * k), Image.NEAREST)
        bg = checker(big.size)
        bg.alpha_composite(big)
        cmp_img.paste(bg, (i * (W * k + 12), 0))
    cmp_img.convert('RGB').save(os.path.join(OUT, 'compare.png'))
    print('overlay bbox', bbox, 'size', (bbox[2] - bbox[0], bbox[3] - bbox[1]))


if __name__ == '__main__':
    main()
