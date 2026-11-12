"""Reads the game's files for Combatant Forge: rigs (prefab + .anim clips + trigger frames), texture atlases,
equipment and feature sprites, skills, skill icons and loca. Nothing here writes to Assets/ except add_upload()."""
import base64
import glob
import io
import json
import math
import os
import re
import threading
import uuid

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
# The game project. The installed desktop app sets FORGE_ROOT; run from the project, it's two folders up.
ROOT = os.path.abspath(os.environ.get('FORGE_ROOT') or os.path.join(HERE, '..', '..'))
ASSETS = os.path.join(ROOT, 'Assets')
# Projects and caches stay with the game, wherever the app itself is installed.
DATA = os.path.join(ROOT, 'Tools', 'CombatantForge')
CACHE = os.path.join(DATA, '.cache')
CHARACTERS = os.path.join(ASSETS, 'Resources', 'Prefabs', '4_Characters')
UPLOADS = os.path.join(ASSETS, 'content', '4_Characters', '_Forge')
BALANCING = os.path.join(ASSETS, 'BalancingData')
META_TEMPLATE = os.path.join(ASSETS, 'content', '4_Characters', '2_Pigs', '1_MinionPig', 'BodyParts', 'Headgear_RogueRoyal.png.meta')
CACHE_VERSION = 11

RIG_DIRS = (('1_Birds', 'bird'), ('2_Pigs', 'pig'), ('8_Base', 'pig'))
BIRD_RIGS_ELSEWHERE = {'MightyEagle'}
GEAR_SLOTS = ('Headgear', 'Headgear_Back', 'MainHand', 'MainHand_Arrow', 'OffHand', 'OffHand_Back')
# Body-parented twins of gear bones (PigPirate Action_Attack_3 draws the gear on these): they belong to the gear slot and
# wear its art, like the game's CharacterAssetController.GearBodyTwins.
GEAR_BODY_TWINS = {'HeadgearBody': 'Headgear', 'HeadgearBody_Back': 'Headgear_Back', 'MainHandBody': 'MainHand'}
ICON_ATLASES = ('Skills_Pigs_01', 'Skills_Birds_01', 'Skills_Birds_02', 'Skills_Passive_01', 'Skills_Banner', 'GenericElements')
SKILL_COLORS = {'Attack': 'E55B5B', 'Support': '8CCB50', 'Passive': 'BEE8FE'}
UI_SPRITES = ('InfoOverlay_TL', 'InfoOverlay_T', 'InfoOverlay_TR', 'InfoOverlay_L', 'InfoOverlay_R', 'InfoOverlay_BL', 'InfoOverlay_B',
              'InfoOverlay_BR', 'Resource_XP', 'Character_Health_Small', 'InfoOverlay_HealthBar_Body', 'InfoOverlay_HealthBar_Fill')

_lock = threading.RLock()
_guid_index = None
_atlases = {}
_images = {}
_rig_list = None


def read_text(path):
    with open(path, encoding='utf-8', errors='replace', newline='') as f:
        return f.read().replace('\r\n', '\n')


def guid_index(rescan=False):
    """guid -> asset path, from every .meta under Assets/ (cached on disk, rescanned on a miss)."""
    global _guid_index
    with _lock:
        path = os.path.join(CACHE, 'guids.json')
        if _guid_index is None and not rescan and os.path.exists(path):
            _guid_index = json.load(open(path))
        if _guid_index is None or rescan:
            index = {}
            for folder, dirs, files in os.walk(ASSETS):
                for name in files:
                    if name.endswith('.meta'):
                        full = os.path.join(folder, name)
                        try:
                            with open(full, 'rb') as f:
                                m = re.search(rb'guid: ([0-9a-f]{32})', f.read(200))
                        except OSError:
                            continue
                        if m:
                            index[m.group(1).decode()] = os.path.relpath(full[:-5], ASSETS)
            _guid_index = index
            os.makedirs(CACHE, exist_ok=True)
            json.dump(index, open(path, 'w'))
        return _guid_index


def asset_path(guid):
    rel = guid_index().get(guid)
    if rel is None or not os.path.exists(os.path.join(ASSETS, rel)):
        rel = guid_index(rescan=True).get(guid)
    return os.path.join(ASSETS, rel) if rel else None


def section(text, key, stops):
    """The '  - x' list items of a top-level YAML list."""
    m = re.search(r'^  %s:(.*)\n' % key, text, re.M)
    if not m:
        return []
    out = []
    for line in text[m.end():].split('\n'):
        if not line.startswith('  - ') and not line.startswith('    '):
            break
        if line.startswith('  - '):
            out.append(line[4:].strip())
    return out


def parse_atlas(guid):
    """{name: {name, x, y, w, h (pixel rect, top-left origin), size, pivot}} plus texture path."""
    with _lock:
        if guid in _atlases:
            return _atlases[guid]
    path = asset_path(guid)
    if not path:
        return None
    text = read_text(path)
    if 'textureNames:' not in text:
        return None
    names = section(text, 'textureNames', ())
    guids = section(text, 'textureGUIDs', ())
    sizes = [tuple(float(v) for v in re.findall(r'-?[\d.]+(?:e-?\d+)?', s)) for s in section(text, 'textureSizes', ())]
    pivots = [tuple(float(v) for v in re.findall(r'-?[\d.]+(?:e-?\d+)?', s)) for s in section(text, 'defaultPivotOffsets', ())]
    rects = re.findall(r'x: (-?[\d.e-]+)\n    y: (-?[\d.e-]+)\n    width: (-?[\d.e-]+)\n    height: (-?[\d.e-]+)', text)
    material = re.search(r'material: \{fileID: \d+, guid: (\w+)', text)
    tex_path = None
    if material:
        mat_path = asset_path(material.group(1))
        if mat_path:
            t = re.search(r'm_Texture: \{fileID: \d+, guid: (\w+)', read_text(mat_path))
            tex_path = asset_path(t.group(1)) if t else None
    atlas = {'guid': guid, 'name': os.path.basename(path)[:-6], 'texture': tex_path, 'sprites': {}, 'byGuid': {}}
    tw = th = 0
    if tex_path and os.path.exists(tex_path):
        try:
            with Image.open(tex_path) as im:
                tw, th = im.size
        except Exception:
            tex_path = None
    for i, name in enumerate(names):
        if i >= len(rects):
            break
        x, y, w, h = (float(v) for v in rects[i])
        size = sizes[i] if i < len(sizes) else (w * tw, h * th)
        entry = {'name': name, 'atlas': guid, 'x': round(x * tw), 'y': round((1 - y - h) * th), 'w': round(w * tw), 'h': round(h * th),
                 'size': [size[0], size[1]], 'pivot': list(pivots[i]) if i < len(pivots) else [0, 0], 'guid': guids[i] if i < len(guids) else ''}
        atlas['sprites'][name] = entry
        if entry['guid']:
            atlas['byGuid'][entry['guid']] = name
    with _lock:
        _atlases[guid] = atlas
    return atlas


def _image(path):
    with _lock:
        im = _images.get(path)
        if im is None:
            im = Image.open(path).convert('RGBA')
            if len(_images) > 40:
                _images.clear()
            _images[path] = im
        return im


def sprite_image(key):
    """key = <atlas guid>:<texture name> | upload:<path under the uploads folder>. Returns an RGBA image at game size."""
    source, _, name = key.partition(':')
    if source == 'upload':
        path = os.path.normpath(os.path.join(UPLOADS, name))
        if not path.startswith(UPLOADS + os.sep):
            raise ValueError('bad upload path')
        return Image.open(path).convert('RGBA')
    atlas = parse_atlas(source)
    s = atlas['sprites'][name]
    im = _image(atlas['texture']).crop((s['x'], s['y'], s['x'] + s['w'], s['y'] + s['h']))
    w, h = int(round(s['size'][0])), int(round(s['size'][1]))
    if (w, h) != im.size and w > 0 and h > 0:
        im = im.convert('RGBa').resize((w, h), Image.LANCZOS).convert('RGBA')
    return im


def sprite_png(key):
    safe = re.sub(r'[^\w.-]', '_', key)
    path = os.path.join(CACHE, 'sprites', safe + '.png')
    if key.startswith('upload:') or not os.path.exists(path):
        buf = io.BytesIO()
        sprite_image(key).save(buf, 'PNG')
        if key.startswith('upload:'):
            return buf.getvalue()
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'wb') as f:
            f.write(buf.getvalue())
    return open(path, 'rb').read()


# ------------------------------------------------------------------------------ NGUI atlases (icons, overlay art)

_ngui = {}


def ngui_atlas(name):
    with _lock:
        if name in _ngui:
            return _ngui[name]
    path = os.path.join(ASSETS, 'Prefab', name + '.prefab')
    if not os.path.exists(path):
        return None
    text = read_text(path)
    sprites = {}
    for m in re.finditer(r'- name: (.+)\n\s+x: (\d+)\n\s+y: (\d+)\n\s+width: (\d+)\n\s+height: (\d+)', text):
        sprites[m.group(1).strip()] = tuple(int(v) for v in m.groups()[1:])
    mat = re.search(r'material: \{fileID: \d+, guid: (\w+)', text)
    tex = None
    if mat:
        mat_path = asset_path(mat.group(1))
        t = re.search(r'm_Texture: \{fileID: \d+, guid: (\w+)', read_text(mat_path)) if mat_path else None
        tex = asset_path(t.group(1)) if t else None
    result = {'sprites': sprites, 'texture': tex}
    with _lock:
        _ngui[name] = result
    return result


def ngui_png(atlas_name, sprite):
    atlas = ngui_atlas(atlas_name)
    x, y, w, h = atlas['sprites'][sprite]
    buf = io.BytesIO()
    _image(atlas['texture']).crop((x, y, x + w, y + h)).save(buf, 'PNG')
    return buf.getvalue()


def icon_catalog():
    out = []
    for name in ICON_ATLASES:
        atlas = ngui_atlas(name)
        if not atlas or not atlas['texture']:
            continue
        names = sorted(n for n, r in atlas['sprites'].items() if name != 'GenericElements' or (40 <= r[2] <= 130 and 40 <= r[3] <= 130))
        out.append({'atlas': name, 'icons': names})
    return out


# ------------------------------------------------------------------------------ rigs

def rig_list():
    global _rig_list
    if _rig_list is None:
        rigs = []
        for folder, side in RIG_DIRS:
            for path in sorted(glob.glob(os.path.join(CHARACTERS, folder, '*.prefab'))):
                name = os.path.basename(path)[:-7]
                if name.endswith('_low'):
                    continue
                with open(path, encoding='utf-8', errors='replace') as f:
                    if 'mBoneSource:' not in f.read():
                        continue
                low = path[:-7] + '_low.prefab'
                rigs.append({'id': name, 'side': 'bird' if name in BIRD_RIGS_ELSEWHERE else side, 'path': path,
                             'low': low if os.path.exists(low) else None})
        rigs.sort(key=lambda r: (r['side'], r['id']))
        _rig_list = rigs
    return _rig_list


def rig_info(rig_id):
    return next(r for r in rig_list() if r['id'] == rig_id)


def rig_atlases(rig_id):
    text = read_text(rig_info(rig_id)['path'])
    i = text.index('  textureAtlases:')
    j = text.index('  mAnimationClips:', i)
    out = []
    for g in re.findall(r'guid: (\w+)', text[i:j]):
        atlas = parse_atlas(g)
        if atlas and not atlas['name'].startswith('Forge_'):
            out.append(atlas)
    return out


NUM = r'(-?[\d.]+(?:[eE][-+]?\d+)?|-?Infinity|NaN)'


def _vec(s):
    return [float(v) for v in re.findall(NUM, re.sub(r'[xyzw]:', '', s))]


def parse_anim(path):
    """{bone path: {'p': keys, 'r': keys, 's': keys}}; key = (time, value, inSlope, outSlope) of vectors."""
    text = read_text(path)
    tracks = {}
    length = 0.0
    for label, kind in (('m_RotationCurves', 'r'), ('m_PositionCurves', 'p'), ('m_ScaleCurves', 's')):
        i = text.find('  %s:' % label)
        if i < 0:
            continue
        m = re.compile(r'^  m_\w+:', re.M).search(text, i + 5)
        block = text[i:m.start() if m else len(text)]
        for curve in block.split('  - curve:')[1:]:
            pm = re.search(r'^\s+path: (.*)$', curve, re.M)
            if not pm:
                continue
            keys = []
            for k in re.finditer(r'time: ' + NUM + r'\n\s+value: (\{.*\})\n\s+inSlope: (\{.*\})\n\s+outSlope: (\{.*\})', curve):
                t = float(k.group(1))
                keys.append((t, _vec(k.group(2)), _vec(k.group(3)), _vec(k.group(4))))
                length = max(length, t)
            if keys:
                tracks.setdefault(pm.group(1).strip(), {})[kind] = keys
    return tracks, length


def eval_curve(keys, t):
    """Unity hermite evaluation, per component. Infinite slopes step."""
    if t <= keys[0][0]:
        return list(keys[0][1])
    if t >= keys[-1][0]:
        return list(keys[-1][1])
    lo, hi = 0, len(keys) - 1
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if keys[mid][0] <= t:
            lo = mid
        else:
            hi = mid
    t0, v0, _, out0 = keys[lo]
    t1, v1, in1, _ = keys[hi]
    d = t1 - t0
    u = (t - t0) / d if d > 0 else 0
    result = []
    for c in range(len(v0)):
        m0, m1 = out0[c], in1[c]
        if math.isinf(m0) or math.isinf(m1) or math.isnan(m0) or math.isnan(m1):
            result.append(v0[c])
            continue
        a = 2 * u ** 3 - 3 * u ** 2 + 1
        b = u ** 3 - 2 * u ** 2 + u
        e = -2 * u ** 3 + 3 * u ** 2
        f = u ** 3 - u ** 2
        result.append(a * v0[c] + b * d * m0 + e * v1[c] + f * d * m1)
    return result


def _compact(rows):
    return rows[0] if all(r == rows[0] for r in rows) else rows


def bake_rig(rig_id):
    """The rig for the browser: transform paths, sampled clips, per-bone texture timelines."""
    rig = rig_info(rig_id)
    text = read_text(rig['path'])
    src_i = text.index('  mBoneSource:')
    bones = {}
    for m in re.finditer(r'  - boneNodeIndex: (\d+)\n    boneDataIndex: \d+\n    boneName: (.+)\n', text[src_i:]):
        bones[int(m.group(1))] = m.group(2).strip()

    trig_i = text.index('  triggerFrames:')
    atlas_i = text.index('  textureAtlases:', trig_i)
    clips_i = text.index('  mAnimationClips:', atlas_i)
    clip_meta = re.findall(r'^  - animationName: (.+)\n    fps: ([\d.]+)\n    wrapMode: (\d+)', text[clips_i:], re.M)

    # transform curves, by clip name
    anim_i = text.index('  m_Animations:')
    anim_guids = re.findall(r'guid: (\w+)', text[anim_i:text.index('\n  m_', anim_i + 20)])
    anims = {}
    for g in anim_guids:
        path = asset_path(g)
        if path and path.endswith('.anim'):
            anims[os.path.basename(path)[:-5]] = parse_anim(path)

    # Texture events per clip per bone node. SmoothMoves' TriggerFrameBone.TRIGGER_EVENT_TYPE: 0 UserTrigger, 1 HideTexture,
    # 2 ChangeMaterial, 3 ChangeDepth, 4 ChangeTexture, 5 ChangePivot, 6 ChangeCollider. A record only carries the changes
    # its events name, so each bone's state is stepped through its clip's frames in order.
    textures = {}
    timelines = {}
    tags = {}
    clip_events = {}
    field = re.compile(r'^\s+(pivotOffset|upperLeft|bottomRight|textureGUID|depth|triggerEventTypes|boneNodeIndex|atlas): ?(.*)$', re.M)
    frames = []
    for block in text[trig_i:atlas_i].split('  - clipIndex: ')[1:]:
        clip = int(block[:block.index('\n')])
        frame = int(re.search(r'frame: (-?\d+)', block).group(1))
        frames.append((clip, frame, block))
    frames.sort(key=lambda f: (f[0], f[1]))
    states = {}
    for clip, frame, block in frames:
        for rec in block.split('    - userTriggerTag:')[1:]:
            f = dict(field.findall(rec))
            raw = f.get('triggerEventTypes', '')
            events = {int(raw[k:k + 8][:2], 16) for k in range(0, len(raw), 8)}
            tag = rec.split('\n', 1)[0].strip()
            if 0 in events and tag:
                tags.setdefault(clip, set()).add(tag)
                clip_events.setdefault(clip, set()).add((frame, tag))
            if 'boneNodeIndex' not in f or not events & {1, 3, 4, 5}:
                continue
            node = int(f['boneNodeIndex'])
            st = states.setdefault((clip, node), {'key': None, 'depth': 0, 'quad': None, 'hidden': True})
            if 4 in events:
                guid = f.get('textureGUID', '').strip()
                am = re.search(r'guid: (\w+)', f.get('atlas', ''))
                atlas = parse_atlas(am.group(1)) if am else None
                name = atlas['byGuid'].get(guid) if atlas else None
                if name:
                    st['key'] = '%s:%s' % (atlas['guid'], name)
                    s = atlas['sprites'][name]
                    textures[st['key']] = {'name': name, 'w': s['size'][0], 'h': s['size'][1], 'pivot': s['pivot']}
                    st['hidden'] = False
                else:
                    st['key'] = None
            if 4 in events or 5 in events:
                ul, br = _vec(f['upperLeft']), _vec(f['bottomRight'])
                st['quad'] = [round(ul[0], 2), round(ul[1], 2), round(br[0], 2), round(br[1], 2)]
            if 3 in events:
                st['depth'] = int(f.get('depth', 0))
            if 1 in events:
                st['hidden'] = True
            if st['hidden'] or not st['key'] or not st['quad']:
                row = [frame, None, 0]
            else:
                row = [frame, st['key'], st['depth']] + st['quad']
            rows = timelines.setdefault(clip, {}).setdefault(node, [])
            if rows and rows[-1][0] == frame:
                rows[-1] = row
            else:
                rows.append(row)

    # In a clip where a gear twin (HeadgearBody, MainHandBody) shows art, its gear bones are hidden: the twin takes their
    # place, like CharacterAssetController.HideGearBonesUnderTwins (SmoothMoves ignores the per-clip visible flag).
    node_of = {name: node for node, name in bones.items()}
    for twin in {t for t in GEAR_BODY_TWINS if not t.endswith('_Back')}:
        if twin not in node_of:
            continue
        gear_nodes = [node_of[g] for t, g in GEAR_BODY_TWINS.items() if t in (twin, twin + '_Back') and g in node_of]
        for clip, per_node in timelines.items():
            if any(r[1] for r in per_node.get(node_of[twin], ())):
                for node in gear_nodes:
                    if node in per_node:
                        per_node[node] = [[r[0], None, 0] for r in per_node[node]]

    # transform tree
    all_paths = set()
    for tracks, _ in anims.values():
        all_paths.update(tracks)
    paths = sorted(all_paths, key=lambda p: (p.count('/'), p))
    index = {p: i for i, p in enumerate(paths)}
    parents = [index.get(p.rpartition('/')[0], -1) for p in paths]
    by_leaf = {p.rpartition('/')[2]: p for p in paths}

    bone_rows = []
    for node, name in sorted(bones.items()):
        bone_path = by_leaf.get(name)
        sprite_path = by_leaf.get(name + '_Sprite')
        slot = GEAR_BODY_TWINS.get(name) or re.sub(r'_[ABC]$', '', name)
        bone_rows.append({'node': node, 'name': name, 'slot': slot, 'bone': index.get(bone_path, -1), 'sprite': index.get(sprite_path, index.get(bone_path, -1))})

    clips = []
    for ci, (name, fps, wrap) in enumerate(clip_meta):
        name = name.strip()
        tracks, length = anims.get(name, ({}, 0.0))
        steps = max(1, int(math.ceil(length)))
        out_tracks = {}
        for path, kinds in tracks.items():
            row = {}
            if 'p' in kinds:
                row['p'] = _compact([[round(v, 2) for v in eval_curve(kinds['p'], t)[:2]] for t in range(steps + 1)])
            if 'r' in kinds:
                angles = []
                for t in range(steps + 1):
                    q = eval_curve(kinds['r'], t)
                    angles.append(round(math.degrees(2 * math.atan2(q[2], q[3])), 2))
                row['r'] = _compact(angles)
            if 's' in kinds:
                row['s'] = _compact([[round(v, 3) for v in eval_curve(kinds['s'], t)[:2]] for t in range(steps + 1)])
            out_tracks[index[path]] = row
        clips.append({'name': name, 'fps': float(fps), 'wrap': int(wrap), 'length': length, 'steps': steps, 'tracks': out_tracks,
                      'tex': {str(k): v for k, v in timelines.get(ci, {}).items()}, 'tags': sorted(tags.get(ci, ())),
                      'events': sorted([f, t] for f, t in clip_events.get(ci, ()))})

    # which bones can hold art
    with_art = {}
    for clip in clips:
        for node, rows in clip['tex'].items():
            for r in rows:
                if r[1]:
                    with_art.setdefault(int(node), set()).add(r[1])
    slots = {}
    for b in bone_rows:
        if b['node'] in with_art:
            s = slots.setdefault(b['slot'], {'key': b['slot'], 'kind': 'gear' if b['slot'] in GEAR_SLOTS else 'feature', 'bones': [], 'textures': set()})
            s['bones'].append(b['name'])
            s['textures'].update(with_art[b['node']])
    order = {k: i for i, k in enumerate(GEAR_SLOTS)}
    slot_rows = sorted(slots.values(), key=lambda s: (order.get(s['key'], 99), s['key']))
    for s in slot_rows:
        s['textures'] = sorted(s['textures'])

    # texture variants: every texture X with an X<Suffix> (Pirate, Regular, ...) in one of the rig's atlases. The game
    # swaps them in for a class item named after the suffix (CharacterAssetController.VariantSuffix).
    atlases = rig_atlases(rig_id)
    variants = {'pirate': {}}
    for key, tex in list(textures.items()):
        for suffix in rig_variant_suffixes(rig_id):
            for atlas in atlases:
                s = atlas['sprites'].get(tex['name'] + suffix)
                if s:
                    vkey = '%s:%s' % (atlas['guid'], s['name'])
                    textures[vkey] = {'name': s['name'], 'w': s['size'][0], 'h': s['size'][1], 'pivot': s['pivot']}
                    variants.setdefault(suffix.lower(), {})[key] = vkey
                    break
    # A rig whose game pigs always wear the variant (every PigPirate has a pirate hat) shows it as its default look
    # ('base') and doesn't offer it as a separate variant.
    default = DEFAULT_VARIANT.get(rig_id)
    base = variants.pop(default, {}) if default else {}
    looks = rig_looks(rig_id)
    for name, look in looks.items():
        variants.setdefault(name, {})
        # The pod's rest height, as SpringDrone reads it from the fresh prefab (the Base clip holds the prefab pose).
        body = paths.index('Root/Body') if 'Root/Body' in paths else -1
        rest_clip = next((c for c in clips if c['name'] == 'Base'), None) or next((c for c in clips if c['name'] == 'Idle'), clips[0])
        p = _track_value(rest_clip['tracks'].get(body, rest_clip['tracks'].get(str(body), {})).get('p')) if body >= 0 else None
        look['bodyRestY'] = float(p[1]) if p else 80.0
        look['bodyRestX'] = float(p[0]) if p else 0.0
        bottom = paths.index('Root/Bottom') if 'Root/Bottom' in paths else -1
        q = _track_value(rest_clip['tracks'].get(bottom, rest_clip['tracks'].get(str(bottom), {})).get('p')) if bottom >= 0 else None
        look['bottomRestY'] = float(q[1]) if q else look['bodyRestY'] + look['mount'][1]
    centre = re.search(r'  BodyCenter: \{fileID: (\d+)\}', text)
    body = [0.0, 45.0]
    if centre and ('&%s\n' % centre.group(1)) in text:
        block = text[text.index('&%s\n' % centre.group(1)):]
        pos = re.search(r'm_LocalPosition: \{x: ([-\d.e]+), y: ([-\d.e]+)', block)
        if pos:
            body = [float(pos.group(1)), float(pos.group(2))]
    return {'id': rig_id, 'side': rig['side'], 'bodyCenter': body, 'paths': paths, 'parents': parents, 'bones': bone_rows, 'slots': slot_rows,
            'clips': clips, 'textures': textures, 'variants': variants, 'base': base, 'looks': looks}


def rig_mtime(rig_id):
    """Newest of the rig prefab and its atlases (assets + textures): a repacked variant atlas must refresh the caches."""
    paths = [rig_info(rig_id)['path']]
    if rig_id in LOOK_RIGS:
        paths.append(SPRING_SOURCE)
    for atlas in rig_atlases(rig_id):
        paths += [asset_path(atlas['guid']), atlas['texture']]
    return max(os.path.getmtime(p) for p in paths if p and os.path.exists(p))


def rig_json(rig_id):
    path = os.path.join(CACHE, 'rigs', '%s.%d.json' % (rig_id, CACHE_VERSION))
    if os.path.exists(path) and os.path.getmtime(path) >= rig_mtime(rig_id):
        return open(path, 'rb').read()
    data = json.dumps(bake_rig(rig_id), separators=(',', ':')).encode()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(data)
    return data


def rig_catalog():
    out = []
    for r in rig_list():
        variants = [s.lower() for s in rig_variant_suffixes(r['id']) if s.lower() != DEFAULT_VARIANT.get(r['id'])] + list(LOOK_RIGS.get(r['id'], ()))
        out.append({'id': r['id'], 'side': r['side'], 'pirate': 'pirate' in variants, 'variants': variants})
    return out


VARIANT_SUFFIX = re.compile(r'[A-Z][a-z]+$')
# Rigs whose default look is a texture variant: every game PigPirate wears a pirate hat, so the game always swaps in
# its XPirate sprites. Their Forge combatants are published with that variant's tag (publish.asset_base).
DEFAULT_VARIANT = {'PigPirate': 'pirate'}
# Looks that change how a rig moves rather than its textures. A Forge combatant with one is published as Forge_<id>_<Look>,
# which the game recognises (SpringDrone.IsSpringDrone); game pigs listed in the look's 'pigs' always have it.
LOOK_RIGS = {'PigDrone': ('spring',)}
SPRING_SOURCE = os.path.join(ASSETS, 'Scripts', 'Assembly-CSharp', 'SpringDrone.cs')


def spring_look():
    """The spring drone's settings, read from the game's SpringDrone.cs so the preview moves like the game."""
    text = read_text(SPRING_SOURCE)
    look = {m.group(1): float(m.group(2)) for m in re.finditer(r'public (?:float|int) (\w+) = (-?[\d.]+)f?;', text)}

    def strings(field):
        m = re.search(field + r'[^{;]*\{([^}]*)\}', text)
        return re.findall(r'"([^"]+)"', m.group(1)) if m else []
    mount = re.search(r'MountPoint = new Vector2\((-?[\d.]+)f, (-?[\d.]+)f\)', text)
    ground = re.search(r'const float GroundY = (-?[\d.]+)f', text)
    look.update({'kind': 'spring', 'pigs': strings('s_PigIds'), 'pod': ['Root/' + b for b in strings('s_PodBones')], 'hide': strings('s_HiddenBones'),
                 'hover': strings('s_HoverClips'), 'limp': strings('s_LimpClips'),
                 'mount': [float(mount.group(1)), float(mount.group(2))] if mount else [0.0, -40.0],
                 'ground': float(ground.group(1)) if ground else 5.0, 'bands': {}})
    # Cel-shading bands per part: Band(width, offset, r, g, b), outline first.
    for name in ('FrontWire', 'BackWire', 'PlateBands'):
        m = re.search(r'Band\[\] ' + name + r'\s*=\s*\{(.*?)\};', text, re.S)
        rows = re.findall(r'new Band\((-?[\d.]+)f, (-?[\d.]+)f, (\d+), (\d+), (\d+)\)', m.group(1)) if m else []
        look['bands'][name] = [[float(w), float(o), [int(r), int(g_), int(b)]] for w, o, r, g_, b in rows]
    return look


def rig_looks(rig_id):
    return {name: spring_look() for name in LOOK_RIGS.get(rig_id, ())}


def look_of_pig(rig_id, name_id):
    """The look a game pig always has (the spring drones), or ''."""
    for name, look in rig_looks(rig_id).items():
        if name_id in look.get('pigs', ()):
            return name
    return ''


def _track_value(track, frame=0):
    if track is None:
        return None
    return track[min(frame, len(track) - 1)] if isinstance(track[0], list) else track


def look_pose(rig, look, clip, world, height=None):
    """Moves the pod bones in world (rig-space affines, one per path) by the bounce, as SpringDrone.LateUpdate does.
    height: the mount's height above the ground; None = settled on the spring. Returns the spring (top, axis, length)."""
    paths = rig['paths']
    body = paths.index('Root/Body') if 'Root/Body' in paths else -1
    if body < 0:
        return None
    if height is None:
        height = look['RestLength'] - look['StaticSag']
    rest_height = look['bodyRestY'] + look['mount'][1] - look['ground']
    hovering = clip['name'] in look['hover']
    hover_x = (world[body][4] - look['bodyRestX']) if hovering else 0.0
    hover_y = (world[body][5] - look['bodyRestY']) if hovering else 0.0
    offset = height - rest_height - hover_y
    for i, path in enumerate(paths):
        if any(path == pod or path.startswith(pod + '/') for pod in look['pod']):
            m = world[i]
            world[i] = (m[0], m[1], m[2], m[3], m[4] - hover_x, m[5] + offset)
    m = world[body]
    top = (m[0] * look['mount'][0] + m[2] * look['mount'][1] + m[4], m[1] * look['mount'][0] + m[3] * look['mount'][1] + m[5])
    # The spring hangs straight down from its pivot; planted, its foot is on the ground below.
    length = min(look['RestLength'], top[1] - look['ground'])
    return top, (0.0, -1.0), max(length, look['SolidLength'])


def look_hides(look, path):
    return bool(look) and any(name in path.split('/') for name in look.get('hide', ()))


def spring_quads(look, top, axis, length):
    """[(4 corners, (r, g, b))] in rig space, back to front, as SpringDrone.BuildMesh builds its mesh."""
    ax, ay = axis
    sx, sy = -ay, ax
    turns = int(look['Coils'])
    count = turns * 20
    r = look['CoilRadius']
    wire = look['WireWidth']
    end = wire * 0.6
    pitched = max(1, turns - 1) / max(1, turns)
    pts = []
    for i in range(count + 1):
        t = i / count
        a = t * turns * math.pi * 2
        # closed last turn: a flat ring on the ground (SpringDrone.BuildMesh)
        ring = max(end, length - end - r * 0.22)
        along = end + (ring - end) * min(1.0, t / pitched) + math.sin(a) * r * 0.22
        c = math.cos(a) * r
        pts.append(((top[0] + ax * along + sx * c, top[1] + ay * along + sy * c), math.sin(a)))
    out = []

    def segment(a, b, thickness, band):
        dx, dy = b[0] - a[0], b[1] - a[1]
        n = math.hypot(dx, dy)
        if n < 1e-4:
            return
        dx, dy = dx / n, dy / n
        ux, uy = -dy, dx
        if uy < 0:
            ux, uy = -ux, -uy
        width = thickness * band[0]
        shx, shy = ux * thickness * band[1], uy * thickness * band[1]
        ex, ey = dx * width / 2, dy * width / 2
        nx, ny = ux * width / 2, uy * width / 2
        out.append(([(a[0] - ex - nx + shx, a[1] - ey - ny + shy), (a[0] - ex + nx + shx, a[1] - ey + ny + shy),
                     (b[0] + ex + nx + shx, b[1] + ey + ny + shy), (b[0] + ex - nx + shx, b[1] + ey - ny + shy)], tuple(band[2])))

    for front, name in ((False, 'BackWire'), (True, 'FrontWire')):
        for band in look['bands'][name]:
            for i in range(count):
                if ((pts[i][1] + pts[i + 1][1]) / 2 >= 0) == front:
                    segment(pts[i][0], pts[i + 1][0], wire, band)

    # mounting pad under the pod, bands from the outline inward, up toward the pod
    for w, o, color in look['bands'].get('PlateBands', []):
        half_len, half_thick = r + 6, 5
        half = half_len - (1 - w) * half_thick
        thick = half_thick * w
        x, y = top[0] - ax * 2 * half_thick * o, top[1] - ay * 2 * half_thick * o
        out.append(([(x - sx * half - ax * thick, y - sy * half - ay * thick), (x + sx * half - ax * thick, y + sy * half - ay * thick),
                     (x + sx * half + ax * thick, y + sy * half + ay * thick), (x - sx * half + ax * thick, y - sy * half + ay * thick)], tuple(color)))

    return out


def rig_variant_suffixes(rig_id):
    """Suffixes S such that some feature sprite X in the rig's atlases has an XS twin: ['Pirate', 'Regular', ...],
    Pirate first. Gear (Headgear/MainHand/OffHand) never counts: Headgear_Pirate is a hat, not a variant."""
    # A variant sprite lives in a swap-only atlas, apart from its base; that rules out ordinary pairs drawn in one
    # atlas, like Eyebrow_Left_Curved / Eyebrow_Left_CurvedPointed.
    atlases = rig_atlases(rig_id)
    names = set()
    for atlas in atlases:
        names.update(atlas['sprites'])
    found = set()
    for atlas in atlases:
        for name in atlas['sprites']:
            if name.startswith(('Headgear', 'MainHand', 'OffHand')):
                continue
            m = VARIANT_SUFFIX.search(name)
            base = name[:m.start()] if m else ''
            if base and not base.endswith('_') and base in names and base not in atlas['sprites']:
                found.add(m.group(0))
    return sorted(found, key=lambda s: (s != 'Pirate', s))


def rig_icon(rig_id, variant='', slots=None, path=None):
    """A portrait of the rig in its first Idle pose. Bare for the base picker; with slots ({slot: {layers, hidden}}, as in
    a project) it wears that art, for a combatant's tile."""
    path = path or os.path.join(CACHE, 'icons', '%s_%s.%d.png' % (rig_id, variant or 'base', CACHE_VERSION))
    if os.path.exists(path) and os.path.getmtime(path) >= rig_mtime(rig_id):
        return open(path, 'rb').read()
    slots = slots or {}
    rig = json.loads(rig_json(rig_id))
    clip = next((c for c in rig['clips'] if c['name'] == 'Idle' and c['tex']), None) or next(c for c in rig['clips'] if c['tex'])
    tracks = {int(k): v for k, v in clip['tracks'].items()}
    world = []

    def first(track, default):
        if track is None:
            return default
        return track[0] if isinstance(track[0], list) else track
    for i, parent in enumerate(rig['parents']):
        t = tracks.get(i, {})
        px, py = first(t.get('p'), [0, 0])
        rot = t.get('r', 0)
        angle = math.radians(rot[0] if isinstance(rot, list) else rot)
        sx, sy = first(t.get('s'), [1, 1])
        c, sn = math.cos(angle), math.sin(angle)
        local = (c * sx, sn * sx, -sn * sy, c * sy, px, py)
        if parent >= 0:
            m = world[parent]
            local = (m[0] * local[0] + m[2] * local[1], m[1] * local[0] + m[3] * local[1], m[0] * local[2] + m[2] * local[3], m[1] * local[2] + m[3] * local[3],
                     m[0] * local[4] + m[2] * local[5] + m[4], m[1] * local[4] + m[3] * local[5] + m[5])
        world.append(local)
    pirate = rig['variants'].get(variant, {}) if variant else rig.get('base', {})  # the chosen texture variant, or the default look's
    look = rig.get('looks', {}).get(variant) if variant else None
    spring = look_pose(rig, look, clip, world) if look else None
    # Each part: (depth, order, sprite key, w, h, L, m). L maps a source pixel (top-left origin) to bone space.
    parts = []
    for bone in rig['bones']:
        rows = clip['tex'].get(str(bone['node'])) or []
        row = rows[0] if rows else None
        if bone['sprite'] < 0 or not row or not row[1] or bone['slot'] == 'Shadow':
            continue
        if look_hides(look, rig['paths'][bone['sprite']]):
            continue
        m = world[bone['sprite']]
        slot = slots.get(bone['slot'])
        if slot and slot.get('hidden'):
            continue
        layers = [l for l in (slot or {}).get('layers', []) if not l.get('hidden')]
        if layers:
            for seq, l in enumerate(layers):
                rot = math.radians(l.get('rot', 0))
                c, sn = math.cos(rot), math.sin(rot)
                sx, sy, k = l.get('sx', 1), l.get('sy', 1), l.get('shear', 0)
                a, b, d, e = c * sx, (c * k - sn) * sy, sn * sx, (sn * k + c) * sy
                w, h = l['w'], l['h']
                cx, cy = l.get('tx', 0) - l['pivot'][0] * w, l.get('ty', 0) - l['pivot'][1] * h
                # pixel (px, py) -> offset from the centre (px - w/2, h/2 - py) -> bone space
                L = (a, -b, cx - a * w / 2 + b * h / 2, d, -e, cy - d * w / 2 + e * h / 2)
                parts.append((row[2], -bone['node'], seq, l['src'], w, h, L, m))
            continue
        if bone['slot'] in GEAR_SLOTS:
            continue
        key = pirate.get(row[1], row[1])
        x0, y0, x1, y1 = row[3], row[4], row[5], row[6]
        if key != row[1]:
            tex = rig['textures'][key]
            cx, cy = -tex['pivot'][0] * tex['w'], -tex['pivot'][1] * tex['h']
            x0, x1, y0, y1 = cx - tex['w'] / 2, cx + tex['w'] / 2, cy + tex['h'] / 2, cy - tex['h'] / 2
        parts.append((row[2], -bone['node'], 0, key, None, None, (x0, x1, y0, y1), m))
    sized = []
    for depth, order, seq, key, w, h, L, m in parts:
        try:
            im = sprite_image(key).convert('RGBa')
        except (KeyError, OSError, TypeError):
            continue
        iw, ih = im.size
        if w is None:
            x0, x1, y0, y1 = L
            kx, ky = (x1 - x0) / iw, (y1 - y0) / ih
            L = (kx, 0, x0, 0, ky, y0)
        else:
            # the layer's size may differ from the image's: stretch to it
            L = (L[0] * w / iw, L[1] * h / ih, L[2], L[3] * w / iw, L[4] * h / ih, L[5])
        sized.append((depth, order, seq, im, L, m))
    sized.sort(key=lambda r: (-r[0], r[1], r[2]))
    points = []
    for _, _, _, im, L, m in sized:
        iw, ih = im.size
        for px, py in ((0, 0), (iw, 0), (0, ih), (iw, ih)):
            x, y = L[0] * px + L[1] * py + L[2], L[3] * px + L[4] * py + L[5]
            points.append((m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]))
    quads = spring_quads(look, *spring) if spring else []
    for corners, _ in quads:
        points.extend(corners)
    minx, maxx = min(p[0] for p in points), max(p[0] for p in points)
    miny, maxy = min(p[1] for p in points), max(p[1] for p in points)
    size = 192
    scale = (size - 12) / max(maxx - minx, maxy - miny, 1)
    ox, oy = size / 2 - (minx + maxx) / 2 * scale, size / 2 + (miny + maxy) / 2 * scale
    out = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    if quads:
        # Behind the rig, like the game's spring mesh. Drawn 4x and scaled down for smooth edges.
        k = 4
        big = Image.new('RGBA', (size * k, size * k), (0, 0, 0, 0))
        pen = ImageDraw.Draw(big)
        for corners, color in quads:
            pen.polygon([((ox + x * scale) * k, (oy - y * scale) * k) for x, y in corners], fill=color + (255,))
        out.alpha_composite(big.resize((size, size), Image.LANCZOS))
    for _, _, _, im, L, m in sized:
        # source pixel -> bone space (L) -> world (m) -> output pixel (scale, y down)
        wa, wb, wc = m[0] * L[0] + m[2] * L[3], m[0] * L[1] + m[2] * L[4], m[0] * L[2] + m[2] * L[5] + m[4]
        wd, we, wf = m[1] * L[0] + m[3] * L[3], m[1] * L[1] + m[3] * L[4], m[1] * L[2] + m[3] * L[5] + m[5]
        a, b, c = scale * wa, scale * wb, ox + scale * wc
        d, e, f = -scale * wd, -scale * we, oy - scale * wf
        det = a * e - b * d
        if abs(det) < 1e-9:
            continue
        ia, ib, id_, ie = e / det, -b / det, -d / det, a / det
        layer = im.transform((size, size), Image.AFFINE, (ia, ib, -(ia * c + ib * f), id_, ie, -(id_ * c + ie * f)), Image.BICUBIC)
        out.alpha_composite(layer.convert('RGBA'))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    out.save(path)
    return open(path, 'rb').read()


# ------------------------------------------------------------------------------ library

GEAR_PREFIX = (('Headgear', 'Headgear'), ('MainHand', 'MainHand'), ('OffHand', 'OffHand'))
SKIP = re.compile(r'^(Empty|FX|Rays|Wave|Hull|Mast|Sail)', re.I)


def _categorize(name):
    for prefix, cat in GEAR_PREFIX:
        if name.startswith(prefix):
            return cat
    return 'Feature'


def _feature_group(name):
    return re.split(r'[_0-9]', name)[0]


def weapon_defaults():
    """AssetBaseId -> how the game's equipment using that art attacks."""
    rows = json.load(open(os.path.join(BALANCING, 'EquipmentBalancingData.json'), encoding='utf-8'))
    out = {}
    for r in rows:
        out.setdefault(r.get('AssetBaseId'), {'animationIndex': r.get('AnimationIndex', 1), 'isRanged': r.get('IsRanged', False),
                                              'projectile': r.get('ProjectileAssetId', ''), 'directProjectile': r.get('DirectProjectileAssetAndLoca', False),
                                              'hitEffect': r.get('HitEffectSuffix', '')})
    return out


def library():
    """Sprites grouped into items: <name> plus its <name>_Back and <name>_Arrow, split by side."""
    weapons = weapon_defaults()
    items = {}
    groups = []
    for rig in rig_list():
        for atlas in rig_atlases(rig['id']):
            groups.append((rig['side'], rig['id'], atlas))
    seen = set()
    sprites = {}
    for side, rig_id, atlas in groups:
        for name, s in atlas['sprites'].items():
            if SKIP.match(name) or s['w'] <= 1 or s['h'] <= 1:
                continue
            ident = (side, name, s['w'], s['h'])
            if ident in seen:
                continue
            seen.add(ident)
            sprites.setdefault(side, {}).setdefault(name, []).append((rig_id, atlas['guid'], s))
    for side, by_name in sprites.items():
        for name, variants in by_name.items():
            for rig_id, atlas_guid, s in variants:
                front = name
                part = 'front'
                if name.endswith('_Back'):
                    front, part = name[:-5], 'back'
                elif name.endswith('_Arrow'):
                    front, part = name[:-6], 'arrow'
                if part != 'front' and front not in by_name:
                    part, front = 'front', name
                ident = (side, rig_id, front)
                sprite = {'key': '%s:%s' % (atlas_guid, name), 'name': name, 'w': s['size'][0], 'h': s['size'][1], 'pivot': s['pivot']}
                item = items.get(ident)
                if item is None:
                    item = items[ident] = {'id': '%s/%s/%s' % (side, rig_id, front), 'side': side, 'rig': rig_id, 'name': front, 'cat': _categorize(front),
                                           'group': _feature_group(front), 'parts': {}}
                item['parts'][part] = sprite
    result = []
    for item in items.values():
        if 'front' not in item['parts']:
            only = next(iter(item['parts'].values()))
            item['parts'] = {'front': only}
        base = re.sub(r'_\d\d$', '', item['name'])
        if item['cat'] == 'MainHand' or item['cat'] == 'OffHand':
            w = weapons.get(item['name']) or weapons.get(base)
            if w:
                w = dict(w)
                suffix = re.search(r'_(\d\d)$', item['name'])
                if w['isRanged'] and not w['directProjectile'] and suffix and w['projectile']:
                    w['projectile'] += '_' + suffix.group(1)
                item['weapon'] = w
        result.append(item)
    result.extend(uploaded_items())
    result.sort(key=lambda i: (i['side'], i['cat'], i['name']))
    return result


def uploaded_items():
    out = {}
    for path in glob.glob(os.path.join(UPLOADS, '*', '*', '*.png')):
        rel = os.path.relpath(path, UPLOADS)
        side_dir, cat, file = rel.split(os.sep)
        if side_dir not in ('Birds', 'Pigs'):
            continue
        side = 'bird' if side_dir == 'Birds' else 'pig'
        name = file[:-4]
        part = 'back' if name.endswith('_Back') else 'front'
        front = name[:-5] if part == 'back' else name
        with Image.open(path) as im:
            w, h = im.size
        item = out.setdefault((side, cat, front), {'id': 'upload/%s/%s/%s' % (side, cat, front), 'side': side, 'rig': 'Uploaded', 'name': front, 'cat': cat,
                                                   'group': 'Uploaded', 'uploaded': True, 'parts': {}})
        item['parts'][part] = {'key': 'upload:' + rel, 'name': name, 'w': w, 'h': h, 'pivot': [0, 0]}
    result = []
    for item in out.values():
        if 'front' not in item['parts']:
            item['parts'] = {'front': item['parts']['back']}
        result.append(item)
    return result


def new_meta(guid):
    text = open(META_TEMPLATE, newline='').read()
    return re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + guid, text, count=1)


def add_upload(side, cat, filename, data_b64):
    """Writes an uploaded PNG plus .meta into the game's content folder. Returns the file's path under Assets."""
    if side not in ('bird', 'pig') or cat not in ('Headgear', 'MainHand', 'OffHand', 'Feature'):
        raise ValueError('bad side or category')
    stem = re.sub(r'\.png$', '', os.path.basename(filename), flags=re.I)
    stem = re.sub(r'[^A-Za-z0-9_-]+', '_', stem).strip('_')
    if not stem:
        raise ValueError('bad file name')
    if not stem.startswith(('Headgear', 'MainHand', 'OffHand')) and cat != 'Feature':
        stem = cat + '_' + stem
    raw = base64.b64decode(data_b64.split(',', 1)[-1])
    im = Image.open(io.BytesIO(raw)).convert('RGBA')
    folder = os.path.join(UPLOADS, 'Birds' if side == 'bird' else 'Pigs', cat)
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, stem + '.png')
    im.save(path)
    meta = path + '.meta'
    if not os.path.exists(meta):
        with open(meta, 'w', newline='') as f:
            f.write(new_meta(uuid.uuid4().hex))
    return os.path.relpath(path, ASSETS)


# ------------------------------------------------------------------------------ skills and text

def loca_texts():
    return json.load(open(os.path.join(BALANCING, 'Loca', 'live_English.json'), encoding='utf-8'))['Texts']


_template_values = {}
_template_sources = {}
SKILL_DIRS = (os.path.join(ASSETS, 'Scripts', 'Assembly-CSharp', 'ABH', 'GameDatas', 'Battle', 'Skills'),
              os.path.join(ASSETS, 'Scripts', 'Assembly-CSharp', 'ABH', 'GameDatas', 'Battle'))
EXPR_OK = re.compile(r'^(?:\s|\d+(?:\.\d+)?|[-+*/(),?:<>=!|]|\b[AHLDF]\b|Math\.(?:max|min|ceil|floor)|p\("\w+"\))+$')
# Values no formula can give. A refdamage_percent is a share of a reference damage that depends on the player's level.
# The charge and DoT entries are placeholders game text uses but the template never fills in, so the game prints them raw.
SOURCE_FILES = {'IgnoreDamageSummonCombatant': 'IgnoreDamageAndSummonCombatant'}
VALUE_OVERRIDES = {'IgnoreDamage': {'{value_1}': {'expr': 'p("refdamage_percent")', 'percent': True}},
                   'SimpleAttackSkill': {'{value_3}': {'expr': 'p("charge")'}},
                   'ReduceRageAttackSkill': {'{value_2}': {'expr': 'p("charge")'}},
                   'AttackAndApplyDotSkill': {'{value_6}': {'expr': 'p("dot_damage_in_percent") / 100 * A'}},
                   'IgnoreDamageSummonCombatant': {'{value_1}': {'expr': 'p("refdamage_percent")', 'percent': True}}}


def _template_source(name):
    if name not in _template_sources:
        text = None
        for folder in SKILL_DIRS:
            path = os.path.join(folder, SOURCE_FILES.get(name, name) + '.cs')
            if os.path.exists(path):
                text = read_text(path)
                break
        _template_sources[name] = text
    return _template_sources[name]


def _method_body(text, signature):
    m = re.search(signature, text)
    if not m:
        return None
    i = text.find('{', m.end())
    depth, j = 0, i
    while j < len(text):
        if text[j] == '{':
            depth += 1
        elif text[j] == '}':
            depth -= 1
            if depth == 0:
                return text[i + 1:j]
        j += 1
    return None


def _to_expr(code, env):
    """C# expression to the small formula language the preview evaluates, or None if it isn't plain arithmetic."""
    e = code.strip()
    e = re.sub(r'\bstring\.Empty\s*\+\s*', '', e)
    e = re.sub(r'\bDIContainerInfrastructure\.GetFormatProvider\(\)\.GetBattleStats(?:Fractional)?Format\b', '', e)
    for unity, js in (('Mathf.Max', 'Math.max'), ('Mathf.Min', 'Math.min'), ('Mathf.CeilToInt', 'Math.ceil'), ('Mathf.FloorToInt', 'Math.floor')):
        e = e.replace(unity, js)
    e = re.sub(r'\.ToString\((?:"[^"]*")?\)', '', e)
    e = re.sub(r'\b(?:System\.)?(?:Convert\.ToInt32|Mathf\.RoundToInt|Mathf\.Round)\b', '', e)
    e = re.sub(r'\((?:float|int)\)', '', e)
    e = re.sub(r'(?:base\.)?Model\.Balancing\.EffectDuration', 'D', e)
    e = re.sub(r'(?:base\.)?Model\.SkillParameters\["(\w+)"\]', r'p("\1")', e)
    e = re.sub(r'\binvoker\.ModifiedAttack\b|\bGetShieldAttack\(invoker\)|\binvoker\.CombatantView\b', 'A', e)
    e = re.sub(r'\binvoker\.ModifiedHealth\b', 'H', e)
    e = re.sub(r'\binvoker\.CharacterModel\.Level\b', 'L', e)
    e = re.sub(r'(\d)f\b', r'\1', e)
    for _ in range(4):
        e = re.sub(r'(?<!["\w])([A-Za-z_]\w*)\b(?!["\(])', lambda m: '(%s)' % env[m.group(1)] if m.group(1) in env else m.group(1), e)
    return e if EXPR_OK.match(e) else None


def _without_delegates(code):
    # What a skill does in battle, whose local variables mean nothing to its description.
    while True:
        m = re.search(r'delegate\s*\([^)]*\)\s*\{', code)
        if not m:
            return code
        depth, j = 1, m.end()
        while j < len(code) and depth:
            depth += {'{': 1, '}': -1}.get(code[j], 0)
            j += 1
        code = code[:m.start()] + code[j:]


def _conditional(new, old, code, at):
    # Inside a block a value is only set sometimes, usually when its parameter exists, so it keeps the one it had otherwise.
    if not old or new == old or re.fullmatch(r'[\d.\s()]+', old) or code.count('{', 0, at) == code.count('}', 0, at):
        return new
    return '(%s) || (%s)' % (new, old)


def template_values(template):
    """{'{value_N}': {'expr': formula}} from the template's GetLocalizedDescription and Init, following base classes.
    A formula uses p("param"), A (attack), H (health), L (level), D (effect duration) and F (the first parameter's value,
    which summon skills read their amount from). Preview only."""
    if template in _template_values:
        return _template_values[template]
    chain, name = [], template
    while name and _template_source(name) and name not in chain:
        chain.append(name)
        m = re.search(r'class\s+%s\s*:\s*(\w+)' % re.escape(name), _template_source(name))
        name = m.group(1) if m else None
    env = {'m_BuffDuration': 'D'}
    for name in reversed(chain):
        text = _template_source(name)
        init = _without_delegates(_method_body(text, r'void\s+Init\(SkillGameData\s+\w+\)') or '')
        for m in re.finditer(r'TryGetValue\("?(\w+)"?,\s*out\s+(?:var\s+|float\s+)?(\w+)\)|(?<![\w.])(?:var\s+|float\s+)?(\w+)\s*([-+*/]?)=\s*([^;=\n]+);', init):
            if m.group(1):
                name, expr = m.group(2), 'F' if m.group(1) == 'm_TableKey' else 'p("%s")' % m.group(1)
            elif m.group(4):
                name, expr = m.group(3), _to_expr('%s %s (%s)' % (m.group(3), m.group(4), m.group(5)), env)
            else:
                name, expr = m.group(3), _to_expr(m.group(5), env)
                # A plain number after a parameter read is the fallback for when the parameter is absent.
                if expr and name in env and re.fullmatch(r'[\d.\s()]+', expr):
                    expr = None
            if expr:
                env[name] = _conditional(expr, env.get(name), init, m.start())
    result = {}
    # Base class first, so a template that only adds to its base's description still gets the base's values.
    for name in reversed(chain):
        body = _method_body(_template_source(name), r'string\s+GetLocalizedDescription\(ICombatant\s+\w+\)')
        if not body:
            continue
        local = dict(env)
        for m in re.finditer(r'TryGetValue\("(\w+)",\s*out\s+(?:var\s+|float\s+)?(\w+)\)|(?:var|float|int)\s+(\w+)\s*=\s*([^;=\n]+);|Add\("\{value_(\d+)\}",\s*(.*?)\);', body):
            if m.group(1):
                local[m.group(2)] = 'p("%s")' % m.group(1)
            elif m.group(3):
                expr = _to_expr(m.group(4), local)
                if expr:
                    local[m.group(3)] = expr
            else:
                expr = _to_expr(m.group(6), local)
                if expr:
                    key = '{value_%s}' % m.group(5)
                    result[key] = {'expr': _conditional(expr, (result.get(key) or {}).get('expr'), body, m.start())}
    for name in chain:
        result.update(VALUE_OVERRIDES.get(name, {}))
    _template_values[template] = result
    return result


def skills_catalog():
    rows = json.load(open(os.path.join(BALANCING, 'SkillBalancingData.json'), encoding='utf-8'))
    texts = loca_texts()
    out = []
    for r in rows:
        if r.get('TargetType') not in SKILL_COLORS:
            continue
        loca = r.get('LocaId') or r['NameId']
        params = r.get('SkillParameters') or {}
        color = SKILL_COLORS[r['TargetType']]
        if 'ui_color_rgb' in params:
            color = '%06X' % int(params['ui_color_rgb'])
        out.append({'id': r['NameId'], 'name': texts.get(loca + '_name', r['NameId']), 'desc': texts.get(loca + '_desc', ''), 'type': r['TargetType'],
                    'template': r.get('SkillTemplateType', ''), 'params': params, 'atlas': r.get('IconAtlasId', ''), 'icon': r.get('IconAssetId', ''),
                    'color': color, 'duration': r.get('EffectDuration', 0), 'values': template_values(r.get('SkillTemplateType', ''))})
    return out


_projectiles = (None, [])


def projectile_catalog():
    """Projectile prefab names the game can fire: CharacterHighResAndEquipmentAssetProvider's entries under
    4_Projectiles in Root.unity (DIContainerInfrastructure.ProjectileAssetProvider)."""
    global _projectiles
    path = os.path.join(ASSETS, 'Scenes', 'Root.unity')
    stamp = os.path.getmtime(path)
    if _projectiles[0] != stamp:
        text = read_text(path)
        names = sorted(set(re.findall(r'- NameId: (\S+)\n    AssetLoadingType: \d+\n    Path: [^\n]*4_Projectiles/', text)))
        _projectiles = (stamp, names)
    return _projectiles[1]


def projectile_icon(name):
    """The projectile's picture for its library tile: its first CHMeshSprite (SmoothMoves or NGUI atlas), else the
    texture of its particle material (the barf is particles only). PNG bytes."""
    if name not in projectile_catalog():
        raise KeyError(name)
    path = os.path.join(CACHE, 'icons', 'projectile_%s.%d.png' % (name, CACHE_VERSION))
    if os.path.exists(path):
        return open(path, 'rb').read()
    text = read_text(os.path.join(ASSETS, 'Resources', 'Prefabs', '4_Characters', '4_Projectiles', name + '.prefab'))
    image = None
    for m in re.finditer(r'm_SmoothMovesAtlas: \{fileID: \d+(?:, guid: (\w+))?[^}]*\}\n  m_NguiAtlas: \{fileID: \d+(?:, guid: (\w+))?[^}]*\}\n  m_AtlasType: (\d)\n  m_SpriteName: (.*)\n', text):
        sm, ngui, kind, sprite = m.group(1), m.group(2), m.group(3), m.group(4).strip()
        try:
            if kind == '2' and sm:
                image = sprite_image('%s:%s' % (sm, sprite))
            elif kind == '1' and ngui:
                atlas_path = asset_path(ngui)
                if atlas_path:
                    image = Image.open(io.BytesIO(ngui_png(os.path.basename(atlas_path)[:-7], sprite))).convert('RGBA')
        except (KeyError, TypeError, OSError):
            image = None
        if image:
            break
    if image is None:
        for guid in re.findall(r'm_Materials:\n  - \{fileID: \d+, guid: (\w+)', text):
            mat = asset_path(guid)
            t = re.search(r'm_Texture: \{fileID: \d+, guid: (\w+)', read_text(mat)) if mat else None
            tex = asset_path(t.group(1)) if t else None
            if tex and tex.lower().endswith(('.png', '.tga', '.psd', '.jpg')):
                try:
                    image = Image.open(tex).convert('RGBA')
                    break
                except OSError:
                    continue
    if image is None:
        image = Image.new('RGBA', (2, 2), (0, 0, 0, 0))
    image.thumbnail((160, 160))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    image.save(path)
    return open(path, 'rb').read()


def _projectile_image(text):
    """(image, width, height, GameObject fileID) of the projectile's first drawable sprite, else its particle texture."""
    for m in re.finditer(r'--- !u!114 &(\d+)\nMonoBehaviour:\n(.*?)(?=\n--- |\Z)', text, re.S):
        body = m.group(2)
        sm = re.search(r'm_SmoothMovesAtlas: \{fileID: \d+(?:, guid: (\w+))?', body)
        ng = re.search(r'm_NguiAtlas: \{fileID: \d+(?:, guid: (\w+))?', body)
        kind = re.search(r'm_AtlasType: (\d)', body)
        sprite = re.search(r'm_SpriteName: (.*)', body)
        if not (sprite and kind):
            continue
        go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1)
        w = float(re.search(r'm_Width: ([-\d.]+)', body).group(1))
        h = float(re.search(r'm_Height: ([-\d.]+)', body).group(1))
        try:
            if kind.group(1) == '2' and sm and sm.group(1):
                return sprite_image('%s:%s' % (sm.group(1), sprite.group(1).strip())), w, h, go
            if kind.group(1) == '1' and ng and ng.group(1):
                atlas_path = asset_path(ng.group(1))
                if atlas_path:
                    data = ngui_png(os.path.basename(atlas_path)[:-7], sprite.group(1).strip())
                    return Image.open(io.BytesIO(data)).convert('RGBA'), w, h, go
        except (KeyError, TypeError, OSError):
            continue
    for m in re.finditer(r'--- !u!\d+ &\d+\n\w+:\n(.*?)(?=\n--- |\Z)', text, re.S):
        body = m.group(1)
        mats = re.search(r'm_Materials:\n  - \{fileID: \d+, guid: (\w+)', body)
        go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
        if not (mats and go):
            continue
        mat = asset_path(mats.group(1))
        t = re.search(r'm_Texture: \{fileID: \d+, guid: (\w+)', read_text(mat)) if mat else None
        tex = asset_path(t.group(1)) if t else None
        if tex and tex.lower().endswith(('.png', '.tga', '.jpg')):
            try:
                image = Image.open(tex).convert('RGBA')
            except OSError:
                continue
            return image, 80.0, 80.0 * image.height / max(1, image.width), go.group(1)
    return None, 0, 0, None


def _projectile_prefab(name):
    if name not in projectile_catalog():
        raise KeyError(name)
    return read_text(os.path.join(ASSETS, 'Resources', 'Prefabs', '4_Characters', '4_Projectiles', name + '.prefab'))


def projectile_art(name):
    """Full-size PNG of the projectile, for the preview shot."""
    image = _projectile_image(_projectile_prefab(name))[0] or Image.new('RGBA', (2, 2), (0, 0, 0, 0))
    buf = io.BytesIO()
    image.save(buf, 'PNG')
    return buf.getvalue()


def _curve_keys(block):
    num = r'([-\d.eE+]+|-?Infinity)'
    return [[float(a), float(b), float(c), float(d)] for a, b, c, d in
            re.findall(r'time: %s\n\s+value: %s\n\s+inSlope: %s\n\s+outSlope: %s' % (num, num, num, num), block)]


def projectile_info(name):
    """How the game flies the projectile (CHMotionTween): duration, the curve per axis, and where its sprite sits
    relative to the moving object. SkillBattleDataBase.SpawnProjectile starts it at the MainHand bone and ends it at the
    target's BodyCenter; the X and Y curves swap when the target is lower."""
    text = _projectile_prefab(name)
    transforms = {}
    for m in re.finditer(r'--- !u!4 &(\d+)\nTransform:\n(.*?)(?=\n--- |\Z)', text, re.S):
        body = m.group(2)
        go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1)
        rot = [float(v) for v in re.search(r'm_LocalRotation: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+), w: ([-\d.eE+]+)\}', body).groups()]
        pos = [float(v) for v in re.search(r'm_LocalPosition: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+)', body).groups()]
        scale = [float(v) for v in re.search(r'm_LocalScale: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+)', body).groups()]
        father = re.search(r'm_Father: \{fileID: (\d+)\}', body).group(1)
        transforms[go] = {'id': m.group(1), 'pos': pos, 'angle': 2 * math.atan2(rot[2], rot[3]), 'scale': scale, 'father': father}
    by_id = {t['id']: go for go, t in transforms.items()}
    tween_go, duration, curves, single = None, 0.2, {}, []
    for m in re.finditer(r'--- !u!114 &(\d+)\nMonoBehaviour:\n(.*?)(?=\n--- |\Z)', text, re.S):
        body = m.group(2)
        if 'm_AnimationCurvesPerAxis:' not in body:
            continue
        tween_go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1)
        d = re.search(r'm_DurationInSeconds: ([-\d.eE+]+)', body)
        duration = float(d.group(1)) if d else 0.2
        head, _, per_axis = body.partition('m_AnimationCurvesPerAxis:')
        single = _curve_keys(head.partition('m_AnimationCurve:')[2])
        for axis, chunk in re.findall(r'- Axis: (\d)\n(.*?)(?=\n  - Axis: |\Z)', per_axis, re.S):
            curves['xyz'[int(axis)]] = _curve_keys(chunk)
        break
    image, w, h, sprite_go = _projectile_image(text)

    def matrix(t):
        c, s = math.cos(t['angle']), math.sin(t['angle'])
        return [c * t['scale'][0], s * t['scale'][0], -s * t['scale'][1], c * t['scale'][1], t['pos'][0], t['pos'][1]]

    def mul(a, b):
        return [a[0] * b[0] + a[2] * b[1], a[1] * b[0] + a[3] * b[1], a[0] * b[2] + a[2] * b[3], a[1] * b[2] + a[3] * b[3],
                a[0] * b[4] + a[2] * b[5] + a[4], a[1] * b[4] + a[3] * b[5] + a[5]]
    local = [1, 0, 0, 1, 0, 0]
    go = sprite_go
    while go and go in transforms and go != tween_go:
        local = mul(matrix(transforms[go]), local)
        go = by_id.get(transforms[go]['father'])
    return {'name': name, 'w': w, 'h': h, 'local': [round(v, 4) for v in local], 'duration': duration, 'curves': curves, 'curve': single,
            'image': '/projectileart/' + name}


def power_levels():
    """Level to HealthModifier %, which sets a battle pig's health and attack (PigGameData.ApplyDifficultyOnHealth)."""
    rows = json.load(open(os.path.join(BALANCING, 'PowerLevelBalancingData.json'), encoding='utf-8'))
    return {int(r['NameId'].split('_')[1]): r.get('HealthModifier', 0) for r in rows}


def template_stats(rig_id):
    """Sensible starting stats: the first pig that uses this rig."""
    rows = json.load(open(os.path.join(BALANCING, 'PigBalancingData.json'), encoding='utf-8'))
    match = next((r for r in rows if r.get('AssetId') == rig_id), None) or next(r for r in rows if r['NameId'] == 'pig_brute_lassie')
    return {'hp': match.get('BaseHealth', 100), 'attack': match.get('BaseAttack', 10), 'sizeType': match.get('SizeType', 'Medium'),
            'sizeScale': match.get('SizeScale', 1.0), 'strength': match.get('PigStrength', 5), 'perLevelHealth': match.get('PerLevelHealth', 0),
            'perLevelAttack': match.get('PerLevelAttack', 0), 'loot': match.get('LootTableDefeatBonus') or {}}
