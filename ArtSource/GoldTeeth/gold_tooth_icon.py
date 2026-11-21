#!/usr/bin/env python3
"""Gold tooth inventory icon (120x120, same frame as the Consumables atlas icons).

A molar is modelled as an SDF and ray-marched in gold_tooth_icon.c (compiled on the
fly with gcc; this machine has no numpy). It is cel-shaded with the approved gold
palette (GOLD_TOOTH_STYLE.md): top-left light, lit/body/shade bands, pale shine,
white glint and a warm dark-brown outline. Rendered supersampled, then downscaled.

Orientation: the tooth's long axis (roots -> crown) points --angle degrees
(45 = north-east) on screen and is tilted --tilt degrees toward the camera, so the
crown's chewing surface shows.

Usage: python3 -B gold_tooth_icon.py [--size 120] [--tilt 30] [--angle 45] [--out PATH]
"""
import argparse, math, os, subprocess, tempfile
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))


def sparkle(img, cx, cy, arm, diag=0.0, width=0.75):
    """Game-style sparkle (cf. ShinySand / MetalBar icons): four long, thin, tapered
    white rays with a bright core and a faint warm glow; optional shorter diagonal
    rays (diag = their length relative to arm). Drawn over transparency too."""
    px = img.load()
    w, h = img.size
    R = int(arm) + 2
    for y in range(max(0, int(cy) - R), min(h, int(cy) + R + 1)):
        for x in range(max(0, int(cx) - R), min(w, int(cx) + R + 1)):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            r = math.hypot(dx, dy)
            s = 0.0
            for (ax, ay, L) in ((1, 0, arm), (0, 1, arm), (0.7071, 0.7071, arm * diag), (0.7071, -0.7071, arm * diag)):
                if L <= 0:
                    continue
                along = abs(dx * ax + dy * ay)
                perp = abs(-dx * ay + dy * ax)
                if along >= L:
                    continue
                t = 1 - along / L
                wid = width * (0.35 + 0.65 * t) + 0.9 * t ** 6
                s = max(s, t ** 1.4 * math.exp(-(perp / wid) ** 2))
            s = max(s, math.exp(-(r / (arm * 0.12 + 0.6)) ** 2))
            glow = 0.30 * math.exp(-(r / (arm * 0.35)) ** 2)
            r0, g0, b0, a0 = px[x, y]
            a0 /= 255
            # glow: warm pale gold, then the white sparkle on top ("over" compositing)
            for col, al in (((255, 236, 150), glow), ((255, 255, 255), min(1.0, s * 1.15))):
                if al <= 0.003:
                    continue
                na = al + a0 * (1 - al)
                r0 = (col[0] * al + r0 * a0 * (1 - al)) / na
                g0 = (col[1] * al + g0 * a0 * (1 - al)) / na
                b0 = (col[2] * al + b0 * a0 * (1 - al)) / na
                a0 = na
            px[x, y] = (round(r0), round(g0), round(b0), round(a0 * 255))
    return img


def silhouette_points(img):
    """North-east-most and west-most opaque pixels (spots for the secondary sparkles)."""
    px = img.load()
    w, h = img.size
    ne = we = None
    for y in range(h):
        for x in range(w):
            if px[x, y][3] > 200:
                if ne is None or x - y > ne[0] - ne[1]:
                    ne = (x, y)
                if we is None or x < we[0]:
                    we = (x, y)
    return ne, we


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--size', type=int, default=120)
    ap.add_argument('--tilt', type=float, default=30)
    ap.add_argument('--angle', type=float, default=45)
    ap.add_argument('--outline', type=float, default=3.8, help="max outline width (shadow side) in final px")
    ap.add_argument('--margin', type=float, default=4, help='transparent margin in final px')
    ap.add_argument('--ss', type=int, default=8, help='supersampling factor')
    ap.add_argument('--glint', default='auto', help='hero sparkle "x,y", "auto" or "none" (none = no sparkles)')
    ap.add_argument('--out', default=os.path.join(HERE, 'out', 'icon', 'GoldTooth.png'))
    a = ap.parse_args()

    with tempfile.TemporaryDirectory() as tmp:
        exe = os.path.join(tmp, 'render')
        subprocess.run(['gcc', '-O2', '-o', exe, os.path.join(HERE, 'gold_tooth_icon.c'), '-lm'], check=True)
        raw = os.path.join(tmp, 'out.raw')
        res = subprocess.run([exe, raw, str(a.size), str(a.tilt), str(a.angle), str(a.outline), str(a.ss),
                              str(a.margin)], check=True, capture_output=True, text=True).stdout
        n = a.size * a.ss
        with open(raw, 'rb') as fh:
            big = Image.frombytes('RGBA', (n, n), fh.read())
    # premultiplied downscale so the outline doesn't bleed black into the AA edge
    img = big.convert('RGBa').resize((a.size, a.size), Image.LANCZOS).convert('RGBA')
    if a.glint != 'none':
        if a.glint == 'auto':
            _, gx, gy = res.split()
            gx, gy = float(gx), float(gy)
        else:
            gx, gy = map(float, a.glint.split(','))
        ne, we = silhouette_points(img)
        k = a.size / 120
        sparkle(img, gx, gy, 15 * k, diag=0.4)                         # hero glint on the crown shine
        sparkle(img, ne[0] - 2 * k, ne[1] + 3 * k, 9 * k, diag=0.0)    # on the NE cusp edge
        sparkle(img, we[0] + 4 * k, we[1] + 1 * k, 6 * k, diag=0.0)    # on the root tip
        sparkle(img, ne[0] - 14 * k, ne[1] - 4 * k, 3.5 * k, width=0.6)  # tiny ones in the air
        sparkle(img, gx - 15 * k, gy + 24 * k, 3 * k, width=0.6)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    img.save(a.out)
    print(a.out)


if __name__ == '__main__':
    main()
