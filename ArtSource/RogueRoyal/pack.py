"""Packs the royal rogue headgear (MinionPig BodyParts/Headgear_RogueRoyal.png, a recolour of Headgear_RogueBoss) into
the swap-only MinionPig_Atlas_04 (HR) / _04_low (LR) and adds the atlases to MinionPig(_low).prefab. The back layer
is Headgear_RogueBoss_Back, copied as Headgear_RogueRoyal_Back. Pivots are the RogueBoss ones from MinionPig_Atlas_02.
GUIDs are kept in guids.json so re-packing after art changes keeps every reference. Run: python3 pack.py"""
import json
import math
import os
import re
import shutil
import uuid

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
BODYPARTS = 'Assets/content/4_Characters/2_Pigs/1_MinionPig/BodyParts'
ATLAS = 'MinionPig_Atlas_04'
HR_SIZE = 256
PAD = 2
# name: (source png in BODYPARTS, pivot)
ENTRIES = [
    ('Headgear_RogueRoyal', 'Headgear_RogueRoyal', (-0.145, -0.4)),
    ('Headgear_RogueRoyal_Back', 'Headgear_RogueBoss_Back', (-0.15, -0.15)),
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
    images, src_guids = {}, {}
    for name, src, _ in ENTRIES:
        src_png = rp(BODYPARTS, src + '.png')
        images[name] = Image.open(src_png).convert('RGBA')
        if name == src:
            # existing source texture: reference its own GUID
            src_guids[name] = re.search(r'guid: ([0-9a-f]{32})', read(BODYPARTS + '/' + src + '.png.meta')).group(1)
        else:
            shutil.copyfile(src_png, rp(BODYPARTS, name + '.png'))
            meta = re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid('src:' + name), read(BODYPARTS + '/' + src + '.png.meta'), count=1)
            write_crlf(BODYPARTS + '/' + name + '.png.meta', meta)
            src_guids[name] = gid('src:' + name)
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

    # atlas textures, materials, atlas assets (templates: the swap-only MoustachePig_Atlas_03)
    tex_meta = read('Assets/Texture2D/MoustachePig_Atlas_03_HR.png.meta')
    for res in ('HR', 'LR'):
        write_crlf('Assets/Texture2D/%s_%s.png.meta' % (ATLAS, res),
                   re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid('tex:' + res), tex_meta, count=1))
    for mat, res, src in ((ATLAS + '_0', 'HR', 'MoustachePig_Atlas_03_0'), (ATLAS, 'LR', 'MoustachePig_Atlas_03')):
        text = read('Assets/Material/%s.mat' % src)
        text = text.replace('m_Name: ' + src, 'm_Name: ' + mat)
        text = re.sub(r'(m_Texture: \{fileID: 2800000, guid: )[0-9a-f]{32}', r'\g<1>' + gid('tex:' + res), text)
        write_crlf('Assets/Material/%s.mat' % mat, text)
        mmeta = read('Assets/Material/%s.mat.meta' % src)
        write_crlf('Assets/Material/%s.mat.meta' % mat, re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid('mat:' + res), mmeta, count=1))

    names = [n for n, _, _ in ENTRIES]
    pivots = {n: p for n, _, p in ENTRIES}
    for asset, res, scale, src in ((ATLAS, 'HR', 1, 'MoustachePig_Atlas_03'), (ATLAS + '_low', 'LR', 2, 'MoustachePig_Atlas_03_low')):
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
        body += '  texturePaths:\n' + ''.join('  - Assets/Content/4_Characters/2_Pigs/1_MinionPig/BodyParts/%s.png\n' % n for n in names)
        write_crlf('Assets/MonoBehaviour/%s.asset' % asset, head + body + tail)
        ameta = read('Assets/MonoBehaviour/%s.asset.meta' % src)
        write_crlf('Assets/MonoBehaviour/%s.asset.meta' % asset, re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid('atlas:' + res), ameta, count=1))

    # add the atlases to the rig prefabs' textureAtlases
    for prefab, res in (('MinionPig', 'HR'), ('MinionPig_low', 'LR')):
        p = 'Assets/Resources/Prefabs/4_Characters/8_Base/%s.prefab' % prefab
        text = read(p)
        ref = '  - {fileID: 11400000, guid: %s, type: 2}' % gid('atlas:' + res)
        if ref in text:
            continue
        i = text.index('  textureAtlases:\r\n')
        j = text.index('  mAnimationClips:', i)
        text = text[:j] + ref + '\r\n' + text[j:]
        with open(rp(p), 'w', newline='') as f:
            f.write(text)
    print('packed', {n: pos[n] for n in names})


if __name__ == '__main__':
    main()
