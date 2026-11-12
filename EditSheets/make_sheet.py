"""Builds an edit sheet for a rig's body/face sprites, to be painted and later unloaded as <name><Suffix>.png.

    python3 EditSheets/make_sheet.py <Rig> <BodyPartsDir> <Suffix> [Name=StartSprite ...]

Writes EditSheets/<Rig>_<Suffix>.png, its .original baseline and a .json manifest (see the edit-sheets notes).
Gear (Headgear/MainHand/OffHand), Shadow, Empty, Core and Rage_Streak are left out, and so is every existing
variant (a sprite whose name is another sprite's name plus a capitalised word, e.g. BodyPirate).
Each slot starts from <name><Suffix>.png if it exists (orange frame, '*'), else from Name=StartSprite when given
(e.g. Body=BodyNaked, orange frame, '+'), else from the base sprite.
"""
import glob
import json
import os
import re
import sys

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
GROUPS = [('Body', ('Body', 'Snout', 'Nose', 'Tail', 'Blain', 'Snot', 'Tear', 'Damp', 'Beard')),
          ('Eye patch', ('EyePatch',)),
          ('Eyebrows', ('Eyebrow',)),
          ('Eyes', ('Eye', 'Lid', 'Pupil')),
          ('Mouths', ('Mouth', 'Tongue'))]
SKIP = ('Headgear', 'MainHand', 'OffHand', 'Shadow', 'Empty', 'Core', 'Rage')
MAXW, PAD, LAB, HDR, GAP = 1360, 12, 14, 34, 22


def build(rig, parts, sfx, starts):
    parts = os.path.join(ROOT, parts) if not os.path.isabs(parts) else parts
    names = sorted(os.path.basename(p)[:-4] for p in glob.glob(os.path.join(parts, '*.png')))
    pool = set(names)
    variant = re.compile(r'[A-Z][a-z]+$')

    def is_variant(n):
        m = variant.search(n)
        return bool(m) and m.start() > 0 and n[:m.start()] in pool and not n[:m.start()].endswith('_')

    names = [n for n in names if not n.startswith(SKIP) and not is_variant(n) and n not in starts.values()]
    place, heads, y = [], [], 0
    used = set()
    for title, prefixes in GROUPS:
        group = [n for n in names if n.split('_')[0] in prefixes and n not in used]
        if not group:
            continue
        used.update(group)
        heads.append(('%s (%s) - %s' % (rig, sfx, title), y))
        y += HDR
        x = PAD
        rowh = 0
        for n in group:
            mine = os.path.join(parts, n + sfx + '.png')
            base = Image.open(os.path.join(parts, n + '.png')).convert('RGBA')
            mark = ''
            if os.path.exists(mine):
                im, mark = Image.open(mine).convert('RGBA'), ' *'
            elif n in starts:
                src = Image.open(os.path.join(parts, starts[n] + '.png')).convert('RGBA')
                im = Image.new('RGBA', base.size, (0, 0, 0, 0))
                im.paste(src.crop((0, 0, min(src.width, base.width), min(src.height, base.height))), (0, 0))
                mark = ' +'
            else:
                im = base
            cell = max(im.width, 7 * len(n + mark) + 14) + GAP
            if x + cell > MAXW:
                x, y, rowh = PAD, y + rowh, 0
            place.append((n, im, mark, x, y + LAB + 4))
            x += cell
            rowh = max(rowh, im.height + LAB + 20)
        y += rowh
    left = [n for n in names if n not in used]
    sheet = Image.new('RGBA', (MAXW, y), (0, 0, 0, 0))
    d = ImageDraw.Draw(sheet)
    for t, hy in heads:
        d.text((PAD, hy + 10), t, fill=(40, 40, 40, 255))
    rel = os.path.relpath(parts, ROOT) + '/'
    man = {'suffix': sfx, 'sprites': []}
    for n, im, mark, x, y0 in place:
        sheet.paste(im, (x, y0))
        d.rectangle((x - 2, y0 - 2, x + im.width + 1, y0 + im.height + 1), outline=(210, 140, 40, 255) if mark else (160, 160, 160, 255))
        d.text((x - 2, y0 - LAB - 2), n + mark, fill=(90, 90, 90, 255))
        man['sprites'].append({'name': n, 'rig': rig, 'source_dir': rel, 'x': x, 'y': y0, 'w': im.width, 'h': im.height,
                               'out': n + sfx + '.png', 'started_from': {' *': 'variant', ' +': starts.get(n)}.get(mark, 'base')})
    boxes = [(s['x'] - 3, s['y'] - 3, s['x'] + s['w'] + 3, s['y'] + s['h'] + 3) for s in man['sprites']]
    overlaps = [(i, j) for i in range(len(boxes)) for j in range(i + 1, len(boxes))
                if boxes[i][0] < boxes[j][2] and boxes[j][0] < boxes[i][2] and boxes[i][1] < boxes[j][3] and boxes[j][1] < boxes[i][3]]
    assert not overlaps, overlaps
    out = os.path.join(ROOT, 'EditSheets', '%s_%s' % (rig, sfx))
    sheet.save(out + '.png')
    sheet.save(os.path.join(ROOT, 'EditSheets', '.%s_%s_original.png' % (rig, sfx)))
    with open(out + '.json', 'w') as f:
        json.dump(man, f, indent=2)
    print('%s: %d sprites, %dx%d' % (os.path.relpath(out + '.png', ROOT), len(place), sheet.width, sheet.height))
    if left:
        print('  not in any group:', left)


if __name__ == '__main__':
    build(sys.argv[1], sys.argv[2], sys.argv[3], dict(a.split('=', 1) for a in sys.argv[4:]))
