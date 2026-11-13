"""Builds swap-only SmoothMoves atlases for texture variants (e.g. <name>Pirate).

A variant atlas holds every <name><Suffix>.png it is told about, shelf-packed into the
smallest power-of-two square. It is rebuilt from scratch on every run, so only use it
on atlases this script owns (never one the rig draws from directly: those have their
UVs baked into the prefab).

    python3 EditSheets/variant_atlas.py <Rig> <AtlasName> <BodyPartsDir> <Suffix> [name ...]

<Rig>         rig asset name, e.g. MoustachePig (its <Rig>_Atlas_01/_02 give sizes/pivots
              and the material template; <Rig>.prefab / _low get the atlas appended)
<AtlasName>   e.g. MoustachePig_Atlas_03 (created with fresh GUIDs if missing)
names         base texture names; default = every <name><Suffix>.png in BodyPartsDir
"""
import glob
import os
import re
import sys
import uuid

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
A = os.path.join(ROOT, 'Assets')
PAD = 2


def rd(p):
    with open(p, 'rb') as f:
        return f.read().decode('utf-8')


def nl_of(t):
    return '\r\n' if '\r\n' in t else '\n'


def wr(p, t, nl=None):
    """Writes t with the given line endings (default: as written). Unity re-saves YAML with LF, so files differ."""
    if nl:
        t = t.replace('\r\n', '\n').replace('\n', nl)
    with open(p, 'wb') as f:
        f.write(t.encode('utf-8'))


def lst(t, k):
    m = re.search(r'\n  ' + k + r':\r?\n((?:  - .*\r?\n)+)', t)
    return [l.strip()[2:] for l in m.group(1).splitlines()] if m else []


def find_asset(name):
    for p in [os.path.join(A, 'MonoBehaviour', name + '.asset')] + glob.glob(os.path.join(A, 'Resources', 'Content', '**', name + '.asset'), recursive=True):
        if os.path.exists(p):
            return p
    return None


def guid_of(p):
    return re.search(r'^guid: (\w+)', rd(p + '.meta'), re.M).group(1)


def path_of_guid(g, folders):
    for f in folders:
        for m in glob.glob(os.path.join(A, f, '*.meta')):
            if 'guid: ' + g in rd(m):
                return m[:-5]
    return None


def originals(rig):
    """name -> (size line, pivot line) from the rig's own atlases."""
    info = {}
    for n in (rig + '_Atlas_01', rig + '_Atlas_02', rig + '_Atlas_01_low'):
        p = find_asset(n)
        if not p:
            continue
        t = rd(p)
        for nm, s, o in zip(lst(t, 'textureNames'), lst(t, 'textureSizes'), lst(t, 'defaultPivotOffsets')):
            info.setdefault(nm, (s, o))
    return info


def shelf(sizes):
    """sizes: list of (w, h) -> (side, [(x, y)]) with even coordinates."""
    order = sorted(range(len(sizes)), key=lambda i: -sizes[i][1])
    side = 64
    while True:
        pos = [None] * len(sizes)
        x = y = rowh = 0
        ok = True
        for i in order:
            w, h = sizes[i]
            w += PAD + (w + PAD) % 2
            h += PAD + (h + PAD) % 2
            if x + w > side:
                x, y, rowh = 0, y + rowh, 0
            if y + h > side or w > side:
                ok = False
                break
            pos[i] = (x, y)
            x += w
            rowh = max(rowh, h)
        if ok:
            return side, pos
        side *= 2


def ensure_atlas(rig, atlas):
    """Create <atlas>(_low) asset/material/texture files if missing. Returns paths."""
    out = {}
    tmpl = {'HR': find_asset(rig + '_Atlas_01'), 'LR': find_asset(rig + '_Atlas_01_low')}
    for res, suffix in (('HR', ''), ('LR', '_low')):
        asset = os.path.join(A, 'MonoBehaviour', atlas + suffix + '.asset')
        tex = os.path.join(A, 'Texture2D', '%s_%s.png' % (atlas, res))
        mat = os.path.join(A, 'Material', atlas + ('_0' if res == 'HR' else '') + '.mat')
        if not os.path.exists(asset):
            t = rd(tmpl[res])
            src_mat = path_of_guid(re.search(r'material: \{fileID: 2100000, guid: (\w+)', t).group(1), ['Material'])
            src_tex_g = re.search(r'm_Texture: \{fileID: 2800000, guid: (\w+)', rd(src_mat)).group(1)
            src_tex = path_of_guid(src_tex_g, ['Texture2D'])
            Image.new('RGBA', (64, 64), (0, 0, 0, 0)).save(tex)
            wr(tex + '.meta', re.sub(r'^guid: \w+', 'guid: ' + uuid.uuid4().hex, rd(src_tex + '.meta'), count=1, flags=re.M))
            m = rd(src_mat).replace(src_tex_g, guid_of(tex))
            m = re.sub(r'm_Name: .*', 'm_Name: ' + os.path.basename(mat)[:-4], m, count=1)
            wr(mat, m)
            wr(mat + '.meta', re.sub(r'^guid: \w+', 'guid: ' + uuid.uuid4().hex, rd(src_mat + '.meta'), count=1, flags=re.M))
            head = t[:t.index('  uvs:')]
            head = re.sub(r'm_Name: .*', 'm_Name: ' + atlas + suffix, head, count=1)
            head = re.sub(r'material: \{fileID: 2100000, guid: \w+', 'material: {fileID: 2100000, guid: ' + guid_of(mat), head)
            tail = t[t.index('  lastBuildID:'):]
            wr(asset, head + '  uvs: []\r\n' + tail)
            wr(asset + '.meta', re.sub(r'^guid: \w+', 'guid: ' + uuid.uuid4().hex, rd(tmpl[res] + '.meta'), count=1, flags=re.M))
            print('created', os.path.relpath(asset, ROOT))
        out[res] = (asset, tex)
        # append to the rig prefab's textureAtlases
        prefab = os.path.join(A, 'Resources', 'Prefabs', '4_Characters', '2_Pigs', rig + suffix + '.prefab')
        text = rd(prefab)
        ref = '  - {fileID: 11400000, guid: %s, type: 2}' % guid_of(asset)
        if ref not in text:
            nl = nl_of(text)
            i = text.index('  textureAtlases:' + nl)
            j = text.index('  mAnimationClips:', i)
            wr(prefab, text[:j] + ref + nl + text[j:])
            print('added to', os.path.relpath(prefab, ROOT))
    return out


def build(rig, atlas, parts, sfx, names=None):
    parts = os.path.join(ROOT, parts) if not os.path.isabs(parts) else parts
    if not names:
        # only real variants: the base sprite must exist (skips e.g. Headgear_Pirate.png -> "Headgear_")
        names = sorted(n for n in (os.path.basename(p)[:-4 - len(sfx)] for p in glob.glob(os.path.join(parts, '*' + sfx + '.png')))
                       if os.path.exists(os.path.join(parts, n + '.png')))
    info = originals(rig)
    entries = []
    for n in names:
        src = os.path.join(parts, n + sfx + '.png')
        im = Image.open(src).convert('RGBA')
        size_line, piv = info[n]
        w, h = map(int, re.findall(r'-?\d+', size_line))
        if im.getchannel('A').getbbox() is None:
            # a blank variant (hides the part, e.g. EyePatchRegular): a tiny transparent cell is enough, since the
            # sprite's size and pivot come from textureSizes / defaultPivotOffsets, not the cell
            im = Image.new('RGBA', (2, 2), (0, 0, 0, 0))
        elif im.size != (w, h):  # pad/crop top-left to the original's size so the pivot stays valid
            c = Image.new('RGBA', (w, h), (0, 0, 0, 0))
            c.paste(im.crop((0, 0, min(w, im.width), min(h, im.height))), (0, 0))
            im = c
        rel = os.path.relpath(src, ROOT).replace('Assets/content/', 'Assets/Content/')
        entries.append((n + sfx, im, size_line, piv, guid_of(src), rel))
    paths = ensure_atlas(rig, atlas)
    side, pos = shelf([e[1].size for e in entries])
    for res, scale in (('HR', 1), ('LR', 2)):
        asset, tex = paths[res]
        W = side // scale
        sheet = Image.new('RGBA', (W, W), (0, 0, 0, 0))
        uvs, gu, sz, pv, nm, pa = [], [], [], [], [], []
        for (name, im, size_line, piv, g, rel), (x, y) in zip(entries, pos):
            b = im if scale == 1 else im.convert('RGBa').resize(((im.width + 1) // 2, (im.height + 1) // 2), Image.LANCZOS).convert('RGBA')
            px, py = x // scale, y // scale
            sheet.paste(b, (px, py))
            uvs.append('  - serializedVersion: 2\r\n    x: %.10g\r\n    y: %.10g\r\n    width: %.10g\r\n    height: %.10g\r\n' % (px / W, (W - py - b.height) / W, b.width / W, b.height / W))
            gu.append('  - %s\r\n' % g)
            sz.append('  - %s\r\n' % size_line)
            pv.append('  - %s\r\n' % piv)
            nm.append('  - %s\r\n' % name)
            pa.append('  - %s\r\n' % rel)
        sheet.save(tex)
        t = rd(asset)
        head = t[:t.index('  uvs:')]
        tail = t[t.index('  lastBuildID:'):]
        body = ('  uvs:\r\n' + ''.join(uvs) + '  textureGUIDs:\r\n' + ''.join(gu) + '  textureSizes:\r\n' + ''.join(sz)
                + '  defaultPivotOffsets:\r\n' + ''.join(pv) + '  textureNames:\r\n' + ''.join(nm) + '  texturePaths:\r\n' + ''.join(pa))
        wr(asset, head + body + tail, nl_of(t))
    print('%s: %d sprites, %dx%d HR' % (atlas, len(entries), side, side), [e[0] for e in entries])


if __name__ == '__main__':
    build(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5:])
