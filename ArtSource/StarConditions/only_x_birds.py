#!/usr/bin/env python3
"""Star condition icon "Enter the battle with only X birds".

An original, simple round-bird silhouette (solid white) in front of two dashed white outlines of the same bird.
Writes:
  Assets/Resources/UI/StarReq_OnlyXBirds.png       plain, for the tap-and-hold overlay
  Assets/Resources/UI/StarReq_OnlyXBirds_Star.png  with a dark soft rim, readable on top of a yellow star
Run from the project root: python3 ArtSource/StarConditions/only_x_birds.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

SS = 4                      # supersampling
W, H = 256, 160             # output size
WHITE = (240, 240, 240, 255)


def chaikin(pts, iterations, keep=()):
    """Corner-cutting smoothing of a closed polygon; indices in keep stay sharp."""
    pts = [(p, i in keep) for i, p in enumerate(pts)]
    for _ in range(iterations):
        out = []
        n = len(pts)
        for i in range(n):
            (p, sharp), (q, _) = pts[i], pts[(i + 1) % n]
            if sharp:
                out.append((p, True))
            else:
                out.append(((p[0] * 0.75 + q[0] * 0.25, p[1] * 0.75 + q[1] * 0.25), False))
            if not pts[(i + 1) % n][1]:
                out.append(((p[0] * 0.25 + q[0] * 0.75, p[1] * 0.25 + q[1] * 0.75), False))
        pts = out
    return [p for p, _ in pts]


def bird_outline(cx, cy, r):
    """Closed polygon of an original round bird (image coordinates, y down), facing right."""
    def body(a, k=1.0):
        return (cx + r * k * math.cos(math.radians(a)), cy - r * 0.94 * k * math.sin(math.radians(a)))

    def rel(x, y):
        return (cx + r * x, cy - r * y)

    pts, keep = [], []
    # tail: two rounded feathers sticking out to the left
    pts += [body(200), rel(-1.30, -0.30), rel(-1.42, -0.12), rel(-1.12, -0.06),
            rel(-1.40, 0.10), rel(-1.28, 0.24), body(166)]
    # lower body, from the bottom left round to the beak
    for a in range(-160, -20, 10):
        pts.append(body(a))
    # beak: a chunky wedge, tip kept sharp
    pts += [body(-20), rel(1.18, -0.30)]
    keep.append(len(pts)); pts.append(rel(1.52, -0.08))
    pts += [rel(1.14, 0.12), body(14)]
    # upper body up to the crest
    for a in range(30, 96, 12):
        pts.append(body(a))
    # crest: two rounded tufts leaning back
    pts += [body(96), rel(0.04, 1.30), rel(-0.18, 1.42), rel(-0.24, 1.10),
            rel(-0.44, 1.30), rel(-0.62, 1.30), rel(-0.50, 0.86), body(122)]
    for a in range(134, 166, 12):
        pts.append(body(a))
    return chaikin(pts, 3, keep=set(keep))


def dashed(draw, pts, dash, gap, width, color):
    pts = pts + [pts[0]]
    on, left = True, dash
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        seg = math.hypot(x1 - x0, y1 - y0)
        t = 0.0
        while t < seg:
            step = min(left, seg - t)
            if on:
                a = (x0 + (x1 - x0) * t / seg, y0 + (y1 - y0) * t / seg)
                b = (x0 + (x1 - x0) * (t + step) / seg, y0 + (y1 - y0) * (t + step) / seg)
                draw.line([a, b], fill=color, width=width)
                r = width / 2
                for p in (a, b):
                    draw.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=color)
            t += step
            left -= step
            if left <= 1e-6:
                on = not on
                left = dash if on else gap


def render():
    w, h = W * SS, H * SS
    k = W / 256.0               # sizes below are for the 256 px wide icon
    r = 40 * SS * k
    front = bird_outline(w * 0.50, h * 0.66, r)
    ghosts = [bird_outline(w * 0.25, h * 0.40, r * 0.92), bird_outline(w * 0.74, h * 0.36, r * 0.92)]

    ghost_layer = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    gd = ImageDraw.Draw(ghost_layer)
    for g in ghosts:
        dashed(gd, g, dash=10 * SS * k, gap=6 * SS * k, width=int(4.2 * SS * k), color=WHITE)
    # keep a clear gap between the dashed outlines and the solid bird in front
    cut = Image.new('L', (w, h), 0)
    cd = ImageDraw.Draw(cut)
    cd.polygon(front, fill=255)
    cd.line(front + [front[0]], fill=255, width=int(9 * SS * k), joint='curve')
    ghost_layer.putalpha(Image.composite(Image.new('L', (w, h), 0), ghost_layer.getchannel('A'), cut))

    img = ghost_layer
    fd = ImageDraw.Draw(img)
    fd.polygon(front, fill=WHITE)
    img = img.resize((W, H), Image.LANCZOS)
    return img


def with_rim(img):
    a = img.getchannel('A')
    rim = a.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(1.6))
    shadow = Image.new('RGBA', img.size, (70, 40, 10, 0))
    shadow.putalpha(rim.point(lambda v: int(v * 0.85)))
    out = Image.new('RGBA', img.size, (0, 0, 0, 0))
    out.alpha_composite(shadow)
    out.alpha_composite(img)
    return out


if __name__ == '__main__':
    os.makedirs('Assets/Resources/UI', exist_ok=True)
    icon = render()
    icon.save('Assets/Resources/UI/StarReq_OnlyXBirds.png')
    with_rim(icon).save('Assets/Resources/UI/StarReq_OnlyXBirds_Star.png')
    print('written')
