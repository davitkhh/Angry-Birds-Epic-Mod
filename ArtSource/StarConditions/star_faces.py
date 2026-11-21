#!/usr/bin/env python3
"""Bakes a star condition icon onto the front face of each star sprite it is shown on, in that star's perspective.

The front face of a star sprite is its fully lit area (red channel 255). The frontal centre result star is the
reference: the icon is centred on its face. For every other star an affine map (rotation, scale, shear) from the
reference face to that star's face is searched by mask overlap, and the icon is warped with it. Each output has the
star sprite's own size, so in Unity it is drawn as a texture exactly over the star widget.

Writes Assets/Resources/UI/<Icon>_On_<Sprite>.png. Run from the project root:
  python3 ArtSource/StarConditions/star_faces.py
"""
import importlib.util
import math
import os
import re
from PIL import Image, ImageChops, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = 'Assets/Resources/UI'
SS = 4                      # supersampling for the warp
ICON_WIDTH = 0.60           # icon width as a share of the reference face's width

STARS = [
    # (atlas prefab, atlas texture, sprite)
    ('Assets/Prefab/BattleElements.prefab', 'Assets/Texture2D/BattleElements.png', 'ResultStar_Center'),
    ('Assets/Prefab/BattleElements.prefab', 'Assets/Texture2D/BattleElements.png', 'ResultStar_Left'),
    ('Assets/Prefab/BattleElements.prefab', 'Assets/Texture2D/BattleElements.png', 'ResultStar_Right'),
    ('Assets/Prefab/GenericElements.prefab', 'Assets/Texture2D/GenericElements.png', 'BattleStars'),
]
REFERENCE = 'ResultStar_Center'


def sprite(prefab, texture, name):
    t = open(prefab).read()
    m = re.search(r'name: ' + name + r'\n\s+x: (\d+)\n\s+y: (\d+)\n\s+width: (\d+)\n\s+height: (\d+)', t)
    x, y, w, h = map(int, m.groups())
    return Image.open(texture).convert('RGBA').crop((x, y, x + w, y + h))


def face_mask(img):
    r, g, b, a = img.split()
    lit = r.point(lambda v: 255 if v >= 250 else 0)
    solid = a.point(lambda v: 255 if v > 200 else 0)
    yellow = b.point(lambda v: 255 if v < 235 else 0)   # not the white of a tile highlight
    m = ImageChops.multiply(ImageChops.multiply(lit, solid), yellow)
    # keep the biggest blob only: close small gaps, then drop specks
    return m.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(3))


def stats(mask):
    w, h = mask.size
    px = mask.load()
    n = sx = sy = 0
    xs, ys = [], []
    for y in range(h):
        for x in range(w):
            if px[x, y]:
                n += 1; sx += x; sy += y; xs.append(x); ys.append(y)
    return n, (sx / n, sy / n), (min(xs), min(ys), max(xs) + 1, max(ys) + 1)


def matrix(theta, sx, sy, sh):
    c, s = math.cos(theta), math.sin(theta)
    # A = R(theta) @ [[sx, sh], [0, sy]]
    return (c * sx, c * sh - s * sy, s * sx, s * sh + c * sy)


def inverse(a, b, c, d):
    det = a * d - b * c
    return (d / det, -b / det, -c / det, a / det)


def warp(img, A, c_from, c_to, size, scale=1, resample=Image.NEAREST):
    """Forward map p = c_to + A (q - c_from), all in pixels of the unscaled images times `scale`."""
    ia, ib, ic, id_ = inverse(*A)
    fx, fy = c_from[0] * scale, c_from[1] * scale
    tx, ty = c_to[0] * scale, c_to[1] * scale
    coeffs = (ia, ib, fx - ia * tx - ib * ty, ic, id_, fy - ic * tx - id_ * ty)
    return img.transform((size[0] * scale, size[1] * scale), Image.AFFINE, coeffs, resample=resample)


def overlap(ref_mask, ref_c, tgt_mask, tgt_c, params):
    w = warp(ref_mask, matrix(*params), ref_c, tgt_c, tgt_mask.size)
    inter = ImageChops.multiply(w, tgt_mask).histogram()[255]
    union = ImageChops.lighter(w, tgt_mask).histogram()[255]
    return inter / max(1, union)


def fit(ref_mask, ref_stats, tgt_mask, tgt_stats):
    s0 = math.sqrt(tgt_stats[0] / ref_stats[0])
    best = (0.0, s0, s0, 0.0)
    best_score = overlap(ref_mask, ref_stats[1], tgt_mask, tgt_stats[1], best)
    steps = (math.radians(6), 0.08 * s0, 0.08 * s0, 0.08)
    for _ in range(7):
        improved = True
        while improved:
            improved = False
            for i in range(4):
                for sign in (-1, 1):
                    trial = list(best)
                    trial[i] += sign * steps[i]
                    score = overlap(ref_mask, ref_stats[1], tgt_mask, tgt_stats[1], tuple(trial))
                    if score > best_score:
                        best, best_score, improved = tuple(trial), score, True
        steps = tuple(s * 0.5 for s in steps)
    return best, best_score


def load_icon_module():
    spec = importlib.util.spec_from_file_location('only_x_birds', os.path.join(HERE, 'only_x_birds.py'))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def main():
    icons = {}
    mod = load_icon_module()
    mod.W, mod.H = 1024, 640                     # hi-res master for warping
    icons['StarReq_OnlyXBirds'] = (mod.render(), mod.with_rim)

    sprites = {name: sprite(p, t, name) for p, t, name in STARS}
    masks = {name: face_mask(img) for name, img in sprites.items()}
    st = {name: stats(m) for name, m in masks.items()}
    ref = st[REFERENCE]
    ref_bbox = ref[2]
    face_w = ref_bbox[2] - ref_bbox[0]

    for icon_name, (master, rim) in icons.items():
        # icon placed on the reference face, in reference pixels times SS
        rw, rh = sprites[REFERENCE].size
        placed = Image.new('RGBA', (rw * SS, rh * SS), (0, 0, 0, 0))
        iw = int(face_w * ICON_WIDTH * SS)
        ih = int(iw * master.height / master.width)
        icon = master.resize((iw, ih), Image.LANCZOS)
        cx, cy = ref[1]
        placed.alpha_composite(icon, (int(cx * SS - iw / 2), int(cy * SS - ih / 2)))
        for name, img in sprites.items():
            if name == REFERENCE:
                params, score = (0.0, 1.0, 1.0, 0.0), 1.0
            else:
                params, score = fit(masks[REFERENCE], ref, masks[name], st[name])
            A = matrix(*params)
            out = warp(placed, A, ref[1], st[name][1], img.size, SS, Image.BICUBIC).resize(img.size, Image.LANCZOS)
            out = rim(out)
            path = os.path.join(OUT, '%s_On_%s.png' % (icon_name, name))
            out.save(path)
            print('%-18s rot %5.1f sx %.3f sy %.3f shear %+.3f  overlap %.3f -> %s' % (
                name, math.degrees(params[0]), params[1], params[2], params[3], score, path))


if __name__ == '__main__':
    main()
