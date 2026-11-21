"""Shield upgrade badge for McCool's paired shop tile: the GenericElements green UpgradeArrow with a cel-shaded hammer
laid diagonally across it (head upper right, handle down left), after the user's sketch.
Output: Assets/Resources/UI/ShieldUpgradeBadge.png (128x128). Run from the project root."""
import math, re
from PIL import Image, ImageDraw

S = 4                      # supersampling
SIZE = 128
OUT = 'Assets/Resources/UI/ShieldUpgradeBadge.png'

def atlas_sprite(prefab, texture, name):
    t = open(prefab).read()
    m = re.search(r'  - name: ' + name + r'\n    x: (\d+)\n    y: (\d+)\n    width: (\d+)\n    height: (\d+)', t)
    x, y, w, h = map(int, m.groups())
    return Image.open(texture).convert('RGBA').crop((x, y, x + w, y + h))

def poly(cx, cy, length, width, angle, along0=0.0):
    """Rectangle from along0..length along `angle` (radians, 0 = right, y down), centred on its axis."""
    ux, uy = math.cos(angle), math.sin(angle)
    px, py = -uy, ux
    pts = []
    for a, b in ((along0, -width / 2), (length, -width / 2), (length, width / 2), (along0, width / 2)):
        pts.append(((cx + ux * a + px * b) * S, (cy + uy * a + py * b) * S))
    return pts

def outlined(draw, pts, fill, outline, ow):
    draw.polygon(pts, fill=outline)
    # shrink towards the centroid for the fill
    cx = sum(p[0] for p in pts) / len(pts); cy = sum(p[1] for p in pts) / len(pts)
    inner = []
    for x, y in pts:
        dx, dy = x - cx, y - cy
        d = math.hypot(dx, dy)
        inner.append((x - dx / d * ow * S * 1.35, y - dy / d * ow * S * 1.35))
    draw.polygon(inner, fill=fill)
    return inner

def main():
    canvas = Image.new('RGBA', (SIZE * S, SIZE * S), (0, 0, 0, 0))
    arrow = atlas_sprite('Assets/Prefab/GenericElements_Fill.prefab', 'Assets/Texture2D/GenericElements.png', 'UpgradeArrow')
    arrow = arrow.resize((int(arrow.width * 1.55 * S), int(arrow.height * 1.55 * S)), Image.LANCZOS)
    canvas.alpha_composite(arrow, (int(6 * S), int(22 * S)))

    hammer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(hammer)
    angle = math.radians(-50)            # handle direction: from the bottom-left end up to the head
    hx, hy = 36, 116                     # handle end
    handle_len = 78
    ux, uy = math.cos(angle), math.sin(angle)
    # handle: wood, dark outline, lighter top edge
    outlined(d, poly(hx, hy, handle_len, 12, angle), (150, 92, 44, 255), (52, 30, 14, 255), 2.2)
    d.line([((hx + ux * 4 - uy * -2.5) * S, (hy + uy * 4 + ux * -2.5) * S), ((hx + ux * (handle_len - 6) - uy * -2.5) * S, (hy + uy * (handle_len - 6) + ux * -2.5) * S)], fill=(196, 132, 72, 255), width=int(2.4 * S))
    # head: steel block across the handle tip
    tx, ty = hx + ux * handle_len, hy + uy * handle_len
    head_angle = angle + math.pi / 2
    hux, huy = math.cos(head_angle), math.sin(head_angle)
    start = (tx - hux * 22, ty - huy * 22)
    inner = outlined(d, poly(start[0], start[1], 44, 24, head_angle), (118, 124, 136, 255), (28, 30, 36, 255), 2.4)
    # cel shading: lit half towards the handle top side, dark face strip on the striking end
    lit = poly(start[0] + ux * 3, start[1] + uy * 3, 40, 8, head_angle, 2)
    d.polygon(lit, fill=(176, 182, 194, 255))
    face = poly(start[0] + hux * 36, start[1] + huy * 36, 6, 19, head_angle)
    d.polygon(face, fill=(82, 86, 96, 255))
    # handle collar under the head
    cx0, cy0 = tx - ux * 15, ty - uy * 15
    outlined(d, poly(cx0, cy0, 8, 16, angle), (96, 100, 110, 255), (28, 30, 36, 255), 1.6)
    canvas.alpha_composite(hammer)
    canvas.resize((SIZE, SIZE), Image.LANCZOS).save(OUT)
    print('wrote', OUT)

if __name__ == '__main__':
    main()
