"""Packs the fruiting Cactus Knight gear (cactus_fruit.py output, every growth stage: Fruit, Bud, Bloom) into the
swap-only PigKnight_Atlas_04 (HR) / _04_low (LR) and adds the atlases to PigKnight(_low).prefab.

Entries per stage <S> (texture name = item AssetBaseId [+ "_Back"]):
  Headgear_CactusKnight<S>       <- BodyParts/Headgear_CactusKnight<S>.png
  Headgear_CactusKnight<S>_Back  <- copy of Headgear_CactusKnight_Back.png (helmet inside, no fruit)
  MainHand_CactusKnight<S>       <- BodyParts/MainHand_CactusKnight<S>.png
  OffHand_CactusKnight<S>        <- BodyParts/OffHand_CactusKnight<S>.png
Pivots are the base sprites' (PigKnight_Atlas_01), corrected for the canvas padding in pads.json so the original
art stays at the same spot on the bone. GUIDs live in guids.json, so re-packing keeps every reference.
Run after cactus_fruit.py: python3 pack.py"""
import json
import math
import os
import re
import shutil
import uuid

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
BODYPARTS = 'Assets/content/4_Characters/2_Pigs/2_PigKnight/BodyParts'
BASE_ATLAS = 'Assets/Resources/Content/4_Characters/2_Pigs/2_PigKnight/PigKnight_Atlas_01.asset'
TEMPLATE = 'PigKnight_Atlas_03'
ATLAS = 'PigKnight_Atlas_04'
HR_SIZE = 1024
PAD = 2
STAGE_NAMES = ('Fruit', 'Bud', 'Bloom')
# atlas name: (source png, base sprite for the pivot, pads.json key or None)
ENTRIES = []
for _s in STAGE_NAMES:
    ENTRIES += [
        ('Headgear_CactusKnight' + _s, 'Headgear_CactusKnight' + _s, 'Headgear_CactusKnight', 'Headgear_CactusKnight' + _s),
        ('Headgear_CactusKnight' + _s + '_Back', 'Headgear_CactusKnight_Back', 'Headgear_CactusKnight_Back', None),
        ('MainHand_CactusKnight' + _s, 'MainHand_CactusKnight' + _s, 'MainHand_CactusKnight', 'MainHand_CactusKnight' + _s),
        ('OffHand_CactusKnight' + _s, 'OffHand_CactusKnight' + _s, 'OffHand_CactusKnight', 'OffHand_CactusKnight' + _s),
    ]


def rp(*p):
    return os.path.join(ROOT, *p)


def read(p):
    return open(rp(p), newline='').read()


def write_crlf(p, text):
    text = text.replace('\r\n', '\n').replace('\n', '\r\n')
    with open(rp(p), 'w', newline='') as f:
        f.write(text)


def guids():
    path = os.path.join(HERE, 'guids.json')
    g = json.load(open(path)) if os.path.exists(path) else {}

    def get(key):
        if key not in g:
            g[key] = uuid.uuid4().hex
            json.dump(g, open(path, 'w'), indent=1)
        return g[key]
    return get


def base_pivots():
    t = read(BASE_ATLAS)

    def lst(k):
        m = re.search(r'\n  ' + k + r':\r?\n((?:  - .*\r?\n)+)', t)
        return [l.strip()[2:] for l in m.group(1).splitlines()]
    out = {}
    for n, p, s in zip(lst('textureNames'), lst('defaultPivotOffsets'), lst('textureSizes')):
        px, py = map(float, re.findall(r'-?[\d.e-]+', p)[:2])
        sw, sh = map(int, re.findall(r'\d+', s)[:2])
        out[n] = ((px, py), (sw, sh))
    return out


def padded_pivot(pivot, size, pad):
    """SmoothMoves: bone = centre + pivot*size (y up). Keep the bone on the same original pixel."""
    (px, py), (w, h) = pivot, size
    bx, by = w / 2 + px * w, h / 2 - py * h          # bone in original px, y down
    W, H = w + pad['l'] + pad['r'], h + pad['t'] + pad['b']
    return ((bx + pad['l'] - W / 2) / W, -(by + pad['t'] - H / 2) / H)


def pack(sizes):
    """Shelf packer, tallest first. Returns {name: (x, y)} in HR px, top-left origin."""
    order = sorted(sizes, key=lambda n: -sizes[n][1])
    x = y = shelf = 0
    pos = {}
    for n in order:
        w, h = sizes[n]
        if x + w > HR_SIZE:
            x, y, shelf = 0, y + shelf + PAD, 0
        pos[n] = (x, y)
        x += w + PAD
        shelf = max(shelf, h)
    assert y + shelf <= HR_SIZE, 'atlas overflow'
    return pos


def main():
    gid = guids()
    pads = json.load(open(os.path.join(HERE, 'pads.json')))
    base = base_pivots()
    meta_template = read(BODYPARTS + '/Headgear_CactusKnight.png.meta')
    images, src_guids, pivots = {}, {}, {}
    for name, src, pivot_of, pad_key in ENTRIES:
        if src != name:
            shutil.copyfile(rp(BODYPARTS, src + '.png'), rp(BODYPARTS, name + '.png'))
        images[name] = Image.open(rp(BODYPARTS, name + '.png')).convert('RGBA')
        meta = rp(BODYPARTS, name + '.png.meta')
        if not os.path.exists(meta):
            write_crlf(BODYPARTS + '/' + name + '.png.meta',
                       re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid('src:' + name), meta_template, count=1))
        src_guids[name] = re.search(r'guid: ([0-9a-f]{32})', read(BODYPARTS + '/' + name + '.png.meta')).group(1)
        pivot, size = base[pivot_of]
        pad = pads.get(pad_key, {'l': 0, 't': 0, 'r': 0, 'b': 0}) if pad_key else {'l': 0, 't': 0, 'r': 0, 'b': 0}
        assert images[name].size == (size[0] + pad['l'] + pad['r'], size[1] + pad['t'] + pad['b']), name
        pivots[name] = padded_pivot(pivot, size, pad)
    sizes = {n: im.size for n, im in images.items()}
    pos = pack(sizes)
    hr = Image.new('RGBA', (HR_SIZE, HR_SIZE), (0, 0, 0, 0))
    lr = Image.new('RGBa', (HR_SIZE // 2, HR_SIZE // 2), (0, 0, 0, 0))
    for n, im in images.items():
        x, y = pos[n]
        hr.alpha_composite(im, (x, y))
        w, h = im.size
        small = im.convert('RGBa').resize((math.ceil(w / 2), math.ceil(h / 2)), Image.LANCZOS)
        lr.paste(small, (x // 2, y // 2))
    hr.save(rp('Assets/Texture2D', ATLAS + '_HR.png'))
    lr.convert('RGBA').save(rp('Assets/Texture2D', ATLAS + '_LR.png'))

    tex_meta = read('Assets/Texture2D/%s_HR.png.meta' % TEMPLATE)
    for res in ('HR', 'LR'):
        write_crlf('Assets/Texture2D/%s_%s.png.meta' % (ATLAS, res),
                   re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid('tex:' + res), tex_meta, count=1))
    for mat, res, src in ((ATLAS + '_0', 'HR', TEMPLATE + '_0'), (ATLAS, 'LR', TEMPLATE)):
        text = read('Assets/Material/%s.mat' % src)
        text = text.replace('m_Name: ' + src, 'm_Name: ' + mat)
        text = re.sub(r'(m_Texture: \{fileID: 2800000, guid: )[0-9a-f]{32}', r'\g<1>' + gid('tex:' + res), text)
        write_crlf('Assets/Material/%s.mat' % mat, text)
        mmeta = read('Assets/Material/%s.mat.meta' % src)
        write_crlf('Assets/Material/%s.mat.meta' % mat, re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid('mat:' + res), mmeta, count=1))

    names = [e[0] for e in ENTRIES]
    for asset, res, scale, src in ((ATLAS, 'HR', 1, TEMPLATE), (ATLAS + '_low', 'LR', 2, TEMPLATE + '_low')):
        text = read('Assets/MonoBehaviour/%s.asset' % src).replace('\r\n', '\n')
        head = text[:text.index('  uvs:\n')]
        tail = text[text.index('  lastBuildID:'):]
        head = re.sub(r'm_Name: .*', 'm_Name: ' + asset, head)
        head = re.sub(r'(material: \{fileID: 2100000, guid: )[0-9a-f]{32}', r'\g<1>' + gid('mat:' + res), head)
        size = HR_SIZE // scale
        body = '  uvs:\n'
        for n in names:
            x, y = pos[n]
            w, h = sizes[n]
            x, y, w, h = x // scale, y // scale, math.ceil(w / scale), math.ceil(h / scale)
            body += '  - serializedVersion: 2\n    x: %s\n    y: %s\n    width: %s\n    height: %s\n' % (
                x / size, (size - y - h) / size, w / size, h / size)
        body += '  textureGUIDs:\n' + ''.join('  - %s\n' % src_guids[n] for n in names)
        body += '  textureSizes:\n' + ''.join('  - {x: %d, y: %d}\n' % sizes[n] for n in names)
        body += '  defaultPivotOffsets:\n' + ''.join('  - {x: %.7g, y: %.7g}\n' % pivots[n] for n in names)
        body += '  textureNames:\n' + ''.join('  - %s\n' % n for n in names)
        body += '  texturePaths:\n' + ''.join('  - Assets/Content/4_Characters/2_Pigs/2_PigKnight/BodyParts/%s.png\n' % n for n in names)
        write_crlf('Assets/MonoBehaviour/%s.asset' % asset, head + body + tail)
        ameta = read('Assets/MonoBehaviour/%s.asset.meta' % src)
        write_crlf('Assets/MonoBehaviour/%s.asset.meta' % asset, re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid('atlas:' + res), ameta, count=1))

    for prefab, res in (('PigKnight', 'HR'), ('PigKnight_low', 'LR')):
        p = 'Assets/Resources/Prefabs/4_Characters/2_Pigs/%s.prefab' % prefab
        text = read(p)
        nl = '\r\n' if '\r\n' in text else '\n'
        ref = '  - {fileID: 11400000, guid: %s, type: 2}' % gid('atlas:' + res)
        if ref in text:
            continue
        i = text.index('  textureAtlases:' + nl)
        j = text.index('  mAnimationClips:', i)
        text = text[:j] + ref + nl + text[j:]
        with open(rp(p), 'w', newline='') as f:
            f.write(text)
    print('packed', {n: (pos[n], sizes[n], tuple(round(v, 4) for v in pivots[n])) for n in names})


if __name__ == '__main__':
    main()
