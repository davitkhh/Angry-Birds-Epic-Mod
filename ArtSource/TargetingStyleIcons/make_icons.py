#!/usr/bin/env python3
"""Options-strip icons for the battle targeting style toggle (OptionsMgr / TargetingStyle.cs).

  TargetingStyle_Drag.png      the battle ring (the CharacterControlHUD marker: a ring of dashes, seen tilted) under the
                               tutorial's dragging hand (Guide_Hand_Point)
  TargetingStyle_Slingshot.png a slingshot (mirrored, a small battle ring in the pouch, both ropes behind the arms) with
                               the tutorial's tap hand (Guide_Hand_Tap, mirrored) pointing 45 degrees up-right at the ring

Colours follow the strip's other icons (Sound_On, the gear, the voucher keyboard): white shapes, a brown (153,102,0)
outline and a solid brown drop shadow down-right, with negative space (fully transparent, so the yellow button shows)
for detail. The hands are cut from GenericElements.png and remapped: light glove -> white, its gray shading ->
negative space, its dark outline -> brown.

Everything is drawn at 4x and downsampled. Run from the project root:
    python3 ArtSource/TargetingStyleIcons/make_icons.py [preview.png]
"""
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ATLAS = os.path.join(ROOT, "Assets", "Texture2D", "GenericElements.png")
BUTTON_ATLAS = ATLAS
BUTTON_RECT = None  # looked up from GenericElements.prefab for the preview only
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "UI")

SIZE = 128          # output size (shown at 66 on the 84 button)
SS = 4              # supersampling
S = SIZE * SS

HAND_POINT = (349, 531, 96, 96)
HAND_TAP = (250, 617, 96, 96)

# slingshot icon: the direction the fingertip points (degrees counter-clockwise from pointing right; 45 = north-east)
# and the fingertip's distance from the ring's centre (icon pixels; the ring is ~14 x 10 plus its outline)
HAND_ANGLE = 45
RING_GAP = 22

FACE = (255, 255, 255, 255)
LINE = (153, 102, 0, 255)        # the strip icons' brown
HOLE = (255, 0, 255, 255)        # marker for negative space, removed in finish()

OUTLINE_W = 4                    # icon pixels
SHADOW = (4, 5)                  # icon pixels, down-right


def hand(rect, scale, mirror=False):
    """A guide hand recoloured into the strip style (white / negative space / brown)."""
    x, y, w, h = rect
    img = Image.open(ATLAS).convert("RGBA").crop((x, y, x + w, y + h))
    if mirror:
        img = img.transpose(Image.FLIP_LEFT_RIGHT)
    img = img.resize((int(w * scale * SS), int(h * scale * SS)), Image.LANCZOS)
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    src = img.load()
    dst = out.load()
    for yy in range(img.height):
        for xx in range(img.width):
            r, g, b, a = src[xx, yy]
            if a < 100:
                continue
            lum = (r + g + b) / 3.0
            if lum > 212:
                dst[xx, yy] = FACE
            elif lum > 125:
                dst[xx, yy] = HOLE
            else:
                dst[xx, yy] = LINE
    return out


def outlined(layer, width):
    """Brown outline around everything drawn on layer (dilated alpha)."""
    alpha = layer.split()[3].point(lambda v: 255 if v > 0 else 0)
    grown = alpha.filter(ImageFilter.MaxFilter(width * 2 + 1))
    out = Image.new("RGBA", layer.size, LINE)
    out.putalpha(grown)
    out.alpha_composite(layer)
    return out


def finish(canvas):
    """Drop shadow behind the drawing, then punch the negative space out."""
    # the hand's rotation blends the HOLE marker into its neighbours: mostly-marker pixels are negative space,
    # partly-marker pixels become brown so no pink fringe survives
    canvas = canvas.copy()
    px = canvas.load()
    hole = Image.new("L", canvas.size, 0)
    hp = hole.load()
    for y in range(canvas.height):
        for x in range(canvas.width):
            r, g, b, al = px[x, y]
            if al == 0:
                continue
            m = min(r, b) - g
            if m > 110:
                hp[x, y] = 255
                px[x, y] = LINE[:3] + (al,)
            elif m > 15:
                px[x, y] = LINE[:3] + (al,)
    a = canvas.split()[3]
    shadow = Image.new("RGBA", canvas.size, LINE)
    shadow_alpha = Image.new("L", canvas.size, 0)
    # the shadow is the whole silhouette (negative space included), so it stays solid brown
    shadow_alpha.paste(a.point(lambda v: 255 if v > 0 else 0), (SHADOW[0] * SS, SHADOW[1] * SS))
    shadow.putalpha(shadow_alpha)
    out = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    out.alpha_composite(shadow)
    drawing = canvas.copy()
    drawing.putalpha(ImageChops.multiply(a, ImageChops.invert(hole)))
    out.alpha_composite(drawing)
    # negative space shows the button even through the shadow
    out.putalpha(ImageChops.multiply(out.split()[3], ImageChops.invert(hole)))
    return out


def ellipse_pt(cx, cy, rx, ry, a):
    return (cx + rx * math.cos(a), cy + ry * math.sin(a))


def dashed_ring(size, cx, cy, rx, ry, thickness, dashes=16, fill=0.8):
    """The battle marker: a ring of white dashes, seen tilted (an ellipse)."""
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    step = 2 * math.pi / dashes
    for i in range(dashes):
        a0 = i * step + step * (1 - fill) / 2
        a1 = a0 + step * fill
        n = 10
        outer = [ellipse_pt(cx, cy, rx, ry, a0 + (a1 - a0) * k / n) for k in range(n + 1)]
        inner = [ellipse_pt(cx, cy, rx - thickness, ry - thickness * ry / rx, a1 - (a1 - a0) * k / n) for k in range(n + 1)]
        d.polygon(outer + inner, fill=FACE)
    return layer


def drag_icon():
    canvas = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ring = dashed_ring((S, S), cx=46 * SS, cy=58 * SS, rx=40 * SS, ry=27 * SS, thickness=12 * SS)
    canvas.alpha_composite(outlined(ring, OUTLINE_W * SS))
    h = hand(HAND_POINT, 0.80)
    # fingertip (top-left of the sprite) over the ring's right half, as if dragging it
    canvas.alpha_composite(outlined(h, 1 * SS), (54 * SS, 42 * SS))
    return finish(canvas)


def thick_line(d, p0, p1, width, colour):
    d.line([p0, p1], fill=colour, width=int(width))
    r = width / 2
    for p in (p0, p1):
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=colour)


def finger_axis(h):
    """Fingertip and the finger's pointing angle (degrees CCW from right) of the mirrored tap hand."""
    alpha = h.split()[3]
    right = alpha.getbbox()[2] - 1

    def run_mid(x, near):
        ys = [y for y in range(h.height) if alpha.getpixel((x, y)) > 128]
        runs = []
        for y in ys:
            if runs and y == runs[-1][1] + 1:
                runs[-1][1] = y
            else:
                runs.append([y, y])
        best = min(runs, key=lambda r: abs((r[0] + r[1]) / 2.0 - near))
        return (best[0] + best[1]) / 2.0

    tip_y = run_mid(right - 2 * SS, h.height / 2.0)
    # the finger's underside merges into the fist, so its direction is read off the top edge, away from the rounded tip

    def top(x):
        return next(y for y in range(h.height) if alpha.getpixel((x, y)) > 128)

    x0, x1 = right - 18 * SS, right - 6 * SS
    angle = math.degrees(math.atan2(-(top(x1) - top(x0)), x1 - x0))
    return (right, tip_y), angle


def slingshot_icon():
    # drawn on an oversized canvas (margin O) so the rotated hand can't be clipped; save() crops and fits it
    O = S // 2
    canvas = Image.new("RGBA", (S + 2 * O, S + 2 * O), (0, 0, 0, 0))
    u = SS
    # Y frame (seen from the side, leaning back like the sketch): handle, crotch, two prongs
    base = (76 * u, 118 * u)
    crotch = (72 * u, 76 * u)
    prong_l = (56 * u, 36 * u)
    prong_r = (96 * u, 42 * u)
    pouch = (112 * u, 62 * u)
    W = 2 * S
    sling = Image.new("RGBA", (W, S), (0, 0, 0, 0))
    # both ropes behind the frame's arms: thin brown lines
    ropes = ImageDraw.Draw(sling)
    thick_line(ropes, prong_l, pouch, 4 * u, LINE)
    thick_line(ropes, prong_r, pouch, 4 * u, LINE)
    frame = Image.new("RGBA", (W, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(frame)
    thick_line(d, base, crotch, 12 * u, FACE)
    thick_line(d, crotch, prong_l, 10 * u, FACE)
    thick_line(d, crotch, prong_r, 10 * u, FACE)
    sling.alpha_composite(outlined(frame, OUTLINE_W * SS))
    # a small battle ring sits in the pouch instead of a stone
    ring_layer = dashed_ring((W, S), cx=pouch[0], cy=pouch[1], rx=15 * u, ry=11 * u, thickness=6 * u, dashes=10)
    sling.alpha_composite(outlined(ring_layer, 3 * SS))
    # the whole slingshot is mirrored about its handle (the pouch ends up on the left); the hand is not
    flipped = sling.transpose(Image.FLIP_LEFT_RIGHT)
    flipped = flipped.crop((W - 2 * crotch[0] - O, 0, W, S)) if W - 2 * crotch[0] - O > 0 else flipped
    canvas.alpha_composite(flipped, (max(0, O + 2 * crotch[0] - W), O))
    ring = (2 * crotch[0] - pouch[0], pouch[1])

    h = outlined(hand(HAND_TAP, 0.62, mirror=True), 1 * SS)
    tip, finger_angle = finger_axis(h)
    # turn the finger to HAND_ANGLE and put the hand on that line below-left of the ring, fingertip short of it
    rot = HAND_ANGLE - finger_angle
    rotated = h.rotate(rot, resample=Image.BICUBIC, expand=True)
    a = math.radians(rot)
    dx, dy = tip[0] - h.width / 2.0, tip[1] - h.height / 2.0
    tip_r = (rotated.width / 2.0 + dx * math.cos(a) + dy * math.sin(a), rotated.height / 2.0 - dx * math.sin(a) + dy * math.cos(a))
    p = math.radians(HAND_ANGLE)
    direction = (math.cos(p), -math.sin(p))
    reach = RING_GAP * u
    target = (ring[0] - direction[0] * reach, ring[1] - direction[1] * reach)
    canvas.alpha_composite(rotated, (int(O + target[0] - tip_r[0]), int(O + target[1] - tip_r[1])))
    return finish(canvas)


def save(img, name):
    path = os.path.join(OUT_DIR, name)
    # centre the drawing on the button, shrinking it if it outgrew the icon
    box = img.getbbox()
    content = img.crop(box)
    if max(content.size) > S:
        k = S / float(max(content.size))
        content = content.resize((int(content.width * k), int(content.height * k)), Image.LANCZOS)
    centred = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    centred.alpha_composite(content, ((S - content.width) // 2, (S - content.height) // 2))
    centred.resize((SIZE, SIZE), Image.LANCZOS).save(path)
    print("wrote", path)


def preview(path):
    """Both icons on the strip's yellow round button (Button_Round_SubSmall), as in game (icon 66 on an 84 button)."""
    import re
    text = open(os.path.join(ROOT, "Assets", "Prefab", "GenericElements.prefab")).read()
    m = re.search(r"- name: Button_Round_SubSmall\n\s+x: (\d+)\n\s+y: (\d+)\n\s+width: (\d+)\n\s+height: (\d+)", text)
    x, y, w, h = map(int, m.groups())
    button = Image.open(ATLAS).convert("RGBA").crop((x, y, x + w, y + h))
    sheet = Image.new("RGBA", (2 * 100, 100), (60, 60, 60, 255))
    for i, name in enumerate(("TargetingStyle_Drag.png", "TargetingStyle_Slingshot.png")):
        sheet.alpha_composite(button, (i * 100 + 8, 8))
        icon = Image.open(os.path.join(OUT_DIR, name)).resize((66, 66), Image.LANCZOS)
        sheet.alpha_composite(icon, (i * 100 + 50 - 33, 50 - 33))
    sheet.resize((sheet.width * 3, sheet.height * 3), Image.LANCZOS).save(path)


if __name__ == "__main__":
    save(drag_icon(), "TargetingStyle_Drag.png")
    save(slingshot_icon(), "TargetingStyle_Slingshot.png")
    if len(sys.argv) > 1:
        preview(sys.argv[1])
