"""Rough-outline sheet of bucket yaw (spin) variants for each placement: the spin mostly shows in where the handle
lugs sit and which way the handle hangs. Solid brown = in front, dashed = behind the bucket/head.
-> out/rotations.png (rows = placements 1-4, columns = yaw 0..330)"""
import math
import os

import cairo
from PIL import Image, ImageDraw, ImageFilter

import bucket as bk
from preview import BODY_TO_RENDER, bare_pig

YAWS = list(range(0, 360, 30))
TILE = (360, 300)
CROP = (60, 80)  # render px of the tile's top-left
SCALE = 2  # draw at 2x then halve


def outline_tile(pig, params, yaw):
    import physics
    spec = dict(params, yaw=yaw)
    spec.pop('_id', None)
    p, _ = bk.settle(spec)
    s = SCALE
    p['center'] = ((p['center'][0] + BODY_TO_RENDER[0] - CROP[0]) * s, (p['center'][1] + BODY_TO_RENDER[1] - CROP[1]) * s)
    for k in ('rt', 'rb', 'H'):
        p[k] = p[k] * s
    b = bk.Bucket(**p)
    W, H = TILE[0] * s, TILE[1] * s
    surf = cairo.ImageSurface(cairo.FORMAT_ARGB32, W, H)
    ctx = cairo.Context(surf)
    # bucket silhouette: union of the visible stave quads and the rim/base discs
    mask_s = cairo.ImageSurface(cairo.FORMAT_ARGB32, W, H)
    m = cairo.Context(mask_s)
    m.set_source_rgb(1, 1, 1)
    for i in range(48):
        a0, a1 = 2 * math.pi * i / 48, 2 * math.pi * (i + 1) / 48
        bk.poly(m, [b.surf(a0, 0), b.surf(a1, 0), b.surf(a1, b.H), b.surf(a0, b.H)])
        m.fill()
    for y in (0, b.H):
        bk.poly(m, bk.ellipse_pts(b, y, 0))
        m.fill()
    mask = bk.to_image(mask_s).split()[3]
    edge = Image.eval(mask.filter(ImageFilter.MaxFilter(5)), lambda v: v)
    edge = Image.composite(Image.new('L', mask.size, 255), Image.new('L', mask.size, 0), edge.point(lambda v: 255 if v > 128 else 0))
    edge = Image.eval(Image.merge('L', [edge]), lambda v: v)
    from PIL import ImageChops
    edge = ImageChops.subtract(edge, mask.point(lambda v: 255 if v > 128 else 0))
    # visible rim / base arcs (front halves) and the open top for the upright bucket
    ctx.set_source_rgb(0.47, 0.16, 0.0)
    ctx.set_line_width(1.6 * s)
    for y in (b.H,) if b.inv else (0, b.H):
        prev = None
        for i in range(73):
            a = 2 * math.pi * i / 72
            pt = b.surf(a, y, 0)
            vis = b.normal(a)[2] > 0 or (not b.inv and y == b.H)
            if vis and prev:
                ctx.move_to(*prev[:2])
                ctx.line_to(*pt[:2])
                ctx.stroke()
            prev = pt if vis else None
    # lugs and handle
    for (pt, n) in b.lugs():
        ctx.arc(pt[0], pt[1], 2.6 * s, 0, 2 * math.pi)
        if n[2] > -0.2:
            ctx.set_source_rgb(0.0, 0.45, 0.45)
            ctx.fill()
        else:
            ctx.set_source_rgba(0.0, 0.45, 0.45, 0.4)
            ctx.set_line_width(1.0 * s)
            ctx.stroke()
    hp = b.handle_points()
    for i in range(len(hp) - 1):
        a, c = hp[i], hp[i + 1]
        front = a[3] and c[3]
        if front:
            ctx.set_source_rgb(0.0, 0.45, 0.45)
            ctx.set_dash([])
            ctx.set_line_width(2.2 * s)
        else:
            ctx.set_source_rgba(0.0, 0.45, 0.45, 0.6)
            ctx.set_dash([3 * s, 3 * s])
            ctx.set_line_width(1.4 * s)
        ctx.move_to(a[0], a[1])
        ctx.line_to(c[0], c[1])
        ctx.stroke()
    ctx.set_dash([])
    lines = bk.to_image(surf)
    tile = Image.new('RGBA', (W, H), (232, 232, 232, 255))
    faded = pig.crop((CROP[0], CROP[1], CROP[0] + TILE[0], CROP[1] + TILE[1])).resize((W, H), Image.LANCZOS)
    faded.putalpha(faded.split()[3].point(lambda v: v * 45 // 100))
    tile.alpha_composite(faded)
    brown = Image.new('RGBA', (W, H), (120, 40, 0, 255))
    brown.putalpha(edge)
    tile.alpha_composite(brown)
    tile.alpha_composite(lines)
    d = ImageDraw.Draw(tile)
    d.text((8 * s, 6 * s), 'V%d  yaw %d' % (params['_id'], yaw), fill=(40, 40, 40, 255))
    return tile.resize(TILE, Image.LANCZOS)


def main():
    pig = bare_pig()
    cols = len(YAWS)
    sheet = Image.new('RGBA', (TILE[0] * cols // 2, TILE[1] * 4 // 2), (200, 200, 200, 255))
    for r, (vid, params) in enumerate(sorted(bk.VARIANTS.items())):
        for c, yaw in enumerate(YAWS):
            t = outline_tile(pig, dict(params, _id=vid), yaw)
            sheet.alpha_composite(t.resize((TILE[0] // 2, TILE[1] // 2), Image.LANCZOS), (c * TILE[0] // 2, r * TILE[1] // 2))
    sheet.save(os.path.join(bk.OUT, 'rotations.png'))


if __name__ == '__main__':
    main()
