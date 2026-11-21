"""Uploads a Forge project into the game: bakes each slot's art + transform into a swap-only atlas (HR + LR), registers
the atlas on the rig's prefabs, writes the combatant's skills, equipment, class item, inventory, pig row and loca, then
gets the balancing container re-encoded. The combatant has its own inventory and no prefab of its own."""
import glob
import json
import math
import os
import re
import shutil
import subprocess
import time
import uuid

from PIL import Image

import gamedata as g

ASSETS = g.ASSETS
TEMPLATE_ATLAS = 'MoustachePig_Atlas_03'
EDITOR_LOG = os.path.expanduser('~/.config/unity3d/Editor.log')
CONTAINER = os.path.join(ASSETS, 'StreamingAssets', 'local', 'live_SerializedBalancingDataContainer_3.0.1.bytes')
BAKED = os.path.join(g.UPLOADS, 'Baked')
GEAR_TEXTURE = {'Headgear': '', 'Headgear_Back': '_Back', 'MainHand': '_MainHand', 'MainHand_Arrow': '_MainHand_Arrow',
                'OffHand': '_OffHand', 'OffHand_Back': '_OffHand_Back'}
PLACEHOLDER = {
    # A combo move only signs up with ComboMoveDirector; its combo recipes (combo_<ids> rows) do the work.
    'Combo': {'SkillParameters': {'no_focus': 1.0}, 'SkillTemplateType': 'ComboMove', 'AssetId': '', 'EffectDuration': 0, 'EffectType': 'None',
              'TargetAlreadyAffectedTargets': True, 'TargetSelfPossible': False},
    'Attack': {'SkillParameters': {'damage_in_percent': 100.0}, 'SkillTemplateType': 'SimpleAttackSkill', 'AssetId': 'Smash', 'EffectDuration': 0, 'EffectType': 'Curse',
               'TargetAlreadyAffectedTargets': True, 'TargetSelfPossible': False},
    'Support': {'SkillParameters': {'percent': 0.0}, 'SkillTemplateType': 'BlockDamageSkill', 'AssetId': 'Protect', 'EffectDuration': 1, 'EffectType': 'Blessing',
                'TargetAlreadyAffectedTargets': False, 'TargetSelfPossible': True},
    'Passive': {'SkillParameters': {'damage_in_percent': 0.0}, 'SkillTemplateType': 'PassiveReflect', 'AssetId': 'Passive', 'EffectDuration': -1, 'EffectType': 'Passive',
                'TargetAlreadyAffectedTargets': False, 'TargetSelfPossible': True},
}


def slugify(text):
    slug = re.sub(r'[^a-z0-9]+', '_', (text or '').lower()).strip('_')
    return slug or 'unnamed'


def art_id(project):
    """What this combatant's own art, items and abilities are named after. An existing game combatant uses
    <NameId>__e<revision> (see existing.py), so its copies never collide with a Forge combatant's."""
    if project.get('kind') == 'existing':
        return '%s__e%d' % (project['id'], (project.get('existing') or {}).get('rev', 0))
    return project['id']


def combatant_id(project):
    return project['id'] if project.get('kind') == 'existing' else 'pig_forge_' + project['id']


def asset_base(project):
    # Lowercase slug, then the variant as a capitalised last segment (Forge_x_Pirate, Forge_x_Regular): that is what
    # CharacterAssetController.VariantSuffix reads to swap X for X<Suffix>.
    return 'Forge_' + art_id(project) + variant_tag(project.get('variant') or g.DEFAULT_VARIANT.get(project.get('rig'), ''))


def variant_tag(variant):
    return '_' + variant.capitalize() if variant else ''


# ------------------------------------------------------------------------------ baking

def _layer_affine(layer):
    """(a, b, d, e, cx, cy): local point of pixel offset (u, v), v up, from the sprite's centre is
    (a*u + b*v + cx, d*u + e*v + cy), with the centre placed at (tx, ty) + -pivot * size."""
    pivot = layer.get('pivot', [0, 0])
    w, h = layer['w'], layer['h']
    rot = math.radians(layer.get('rot', 0))
    sx, sy, shear = layer.get('sx', 1), layer.get('sy', 1), layer.get('shear', 0)
    c, s = math.cos(rot), math.sin(rot)
    return (c * sx, (c * shear - s) * sy, s * sx, (s * shear + c) * sy, layer.get('tx', 0) - pivot[0] * w, layer.get('ty', 0) - pivot[1] * h)


def bake_layers(layers, source=None):
    """Bakes a slot's layers, bottom first, into one texture. Returns (image, pivot) for a quad with the same result under
    SmoothMoves' centre = -pivot * size."""
    source = source or g.sprite_image
    ss = 2  # supersample so rotated edges stay clean
    placed = []
    corners = []
    for layer in layers:
        image = source(layer['src'])
        w, h = image.size
        a, b, d, e, cx, cy = _layer_affine(dict(layer, w=w, h=h))
        placed.append((image.convert('RGBa'), w, h, a, b, d, e, cx, cy))
        corners += [(a * u + b * v + cx, d * u + e * v + cy) for u in (-w / 2, w / 2) for v in (-h / 2, h / 2)]
    if not placed:
        return Image.new('RGBA', (2, 2), (0, 0, 0, 0)), (0.0, 0.0)
    minx, maxx = min(p[0] for p in corners), max(p[0] for p in corners)
    miny, maxy = min(p[1] for p in corners), max(p[1] for p in corners)
    out_w, out_h = max(1, int(math.ceil(maxx - minx))), max(1, int(math.ceil(maxy - miny)))
    ox, oy = (minx + maxx) / 2, (miny + maxy) / 2
    canvas = Image.new('RGBA', (out_w * ss, out_h * ss), (0, 0, 0, 0))
    for image, w, h, a, b, d, e, cx, cy in placed:
        det = a * e - b * d
        if abs(det) < 1e-6:
            continue
        ia, ib, id_, ie = e / det, -b / det, -d / det, a / det

        def inverse(px, py):
            # output pixel (continuous) -> source pixel; PIL's transform takes the inverse map, y down
            x = px / ss - out_w / 2 + ox - cx
            y = out_h / 2 - py / ss + oy - cy
            return ia * x + ib * y + w / 2, h / 2 - (id_ * x + ie * y)
        x0, y0 = inverse(0, 0)
        x1, y1 = inverse(1, 0)
        x2, y2 = inverse(0, 1)
        big = image.transform(canvas.size, Image.AFFINE, (x1 - x0, x2 - x0, x0, y1 - y0, y2 - y0, y0), Image.BICUBIC)
        canvas.alpha_composite(big.convert('RGBA'))
    baked = canvas.convert('RGBa').resize((out_w, out_h), Image.BOX).convert('RGBA')
    return baked, (-ox / out_w, -oy / out_h)


def migrate_project(project):
    """Brings a saved project up to date: slots hold a stack of layers (they used to hold one sprite), and abilities get
    current number formulas from the game's skill templates."""
    slots = project.get('slots') or {}
    for key, slot in list(slots.items()):
        if 'layers' in slot:
            continue
        hidden = bool(slot.pop('hidden', False))
        link = slot.pop('link', None)
        layers = []
        if slot.get('src'):
            slot['uid'] = uuid.uuid4().hex[:8]
            if link:
                slot['pair'] = '|'.join(sorted((key, link)))
            layers.append(slot)
        slots[key] = {'layers': layers, 'hidden': hidden}
    project['slots'] = slots
    # A duplicate's skills the Forge couldn't show when it was made (combo moves before the Combo type) become abilities.
    keep = project.get('keepSkills') or []
    if keep and project.get('kind') != 'existing':
        catalog = {s['id']: s for s in g.skills_catalog()}
        for k in [k for k in keep if k['id'] in catalog]:
            s = catalog[k['id']]
            ability = {'uid': uuid.uuid4().hex[:8], 'type': s['type'], 'source': s['id'], 'name': s['name'], 'desc': s['desc'], 'color': s['color'],
                       'atlas': s['atlas'], 'icon': s['icon'], 'params': s['params'], 'values': s['values'], 'template': s['template'],
                       'duration': s['duration'], 'design': ''}
            if s['type'] == 'Combo':
                ability['comboId'] = (s.get('asset') or '').lower()
            project.setdefault('abilities', []).append(ability)
        project['keepSkills'] = [k for k in keep if k['id'] not in catalog]
    stats = project.setdefault('stats', {})
    stats.setdefault('perLevelHealth', 0)
    stats.setdefault('perLevelAttack', 0)
    for ability in project.get('abilities', []):
        row = read_row('SkillBalancingData', ability.get('nameId') or ability.get('source') or '')
        if row and (ability.get('source') or ability.get('written')):
            ability['template'] = row.get('SkillTemplateType')
            ability['params'] = row.get('SkillParameters')
            ability['duration'] = row.get('EffectDuration', 0)
            ability['values'] = g.template_values(row.get('SkillTemplateType', ''))
        # edited values (the Values section) that the game's row already has are no longer edits
        edits = {k: v for k, v in (ability.get('paramEdits') or {}).items() if (ability.get('params') or {}).get(k) != v}
        if edits:
            ability['paramEdits'] = edits
        else:
            ability.pop('paramEdits', None)
        if ability.get('durationEdit') is not None and ability.get('durationEdit') == ability.get('duration'):
            ability.pop('durationEdit', None)
    return project


# Fields only a job decides; everything else in the project may have been edited while the job ran.
JOB_FIELDS = ('guids', 'publishedAt', 'publishedRig', 'existing')
JOB_ABILITY_FIELDS = ('nameId', 'written', 'template', 'params', 'duration', 'values', 'origin')


def merge_job_result(current, result, texts=False):
    for key in JOB_FIELDS:
        if key in result:
            current[key] = result[key]
    by_uid = {a.get('uid'): a for a in current.get('abilities', [])}
    for ability in result.get('abilities', []):
        target = by_uid.get(ability.get('uid'))
        if target:
            for key in JOB_ABILITY_FIELDS + (('name', 'desc') if texts else ()):
                if key in ability:
                    target[key] = ability[key]
            # A recipe job decides only whether each recipe is written; its moves and wording stay as edited meanwhile.
            mine = {r.get('uid'): r for r in target.get('recipes') or []}
            for r in ability.get('recipes') or []:
                if r.get('uid') in mine:
                    for key in ('written', 'source'):
                        if key in r:
                            mine[r['uid']][key] = r[key]
    return current


def pack(sizes, pad=2):
    """Shelf packer, tallest first. Returns (side, {name: (x, y)}), top-left origin. side is a power of two."""
    order = sorted(sizes, key=lambda n: (-sizes[n][1], n))
    side = 128
    while side <= 4096:
        x = y = shelf = 0
        pos = {}
        fits = True
        for n in order:
            w, h = sizes[n]
            w2, h2 = w + (w % 2), h + (h % 2)
            if w2 > side:
                fits = False
                break
            if x + w2 > side:
                x, y, shelf = 0, y + shelf + pad, 0
            pos[n] = (x, y)
            x += w2 + pad
            shelf = max(shelf, h2)
        if fits and y + shelf <= side:
            return side, pos
        side *= 2
    raise ValueError('Baked art does not fit a 4096 atlas; scale some of it down')


def write_crlf(path, text):
    with open(path, 'w', newline='') as f:
        f.write(text.replace('\r\n', '\n').replace('\n', '\r\n'))


def read_any(path):
    return open(path, newline='', encoding='utf-8', errors='replace').read()


def gid(project, key):
    guids = project.setdefault('guids', {})
    if key not in guids:
        guids[key] = uuid.uuid4().hex
    return guids[key]


def write_atlas(project, entries, log):
    """entries: {texture name: (image, pivot)}. Writes source PNGs + metas, atlas textures, materials, atlas assets."""
    folder = os.path.join(BAKED, art_id(project))
    os.makedirs(folder, exist_ok=True)
    for old in glob.glob(os.path.join(folder, '*.png')):
        if os.path.basename(old)[:-4] not in entries:
            os.remove(old)
            if os.path.exists(old + '.meta'):
                os.remove(old + '.meta')
    src_guids = {}
    for name, (image, _) in entries.items():
        path = os.path.join(folder, name + '.png')
        image.save(path)
        src_guids[name] = gid(project, 'src:' + name)
        with open(path + '.meta', 'w', newline='') as f:
            f.write(g.new_meta(src_guids[name]))
    sizes = {n: im.size for n, (im, _) in entries.items()}
    side, pos = pack(sizes)
    atlas = 'Forge_%s_Atlas' % art_id(project)
    hr = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    lr = Image.new('RGBa', (side // 2, side // 2), (0, 0, 0, 0))
    for n, (im, _) in entries.items():
        x, y = pos[n]
        hr.alpha_composite(im, (x, y))
        w, h = im.size
        small = im.convert('RGBa').resize((max(1, math.ceil(w / 2)), max(1, math.ceil(h / 2))), Image.LANCZOS)
        lr.paste(small, (x // 2, y // 2))
    tex_dir = os.path.join(ASSETS, 'Texture2D')
    hr.save(os.path.join(tex_dir, atlas + '_HR.png'))
    lr.convert('RGBA').save(os.path.join(tex_dir, atlas + '_LR.png'))
    tex_meta = read_any(os.path.join(tex_dir, TEMPLATE_ATLAS + '_HR.png.meta'))
    for res in ('HR', 'LR'):
        write_crlf(os.path.join(tex_dir, '%s_%s.png.meta' % (atlas, res)), re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid(project, 'tex:' + res), tex_meta, count=1))
    mat_dir = os.path.join(ASSETS, 'Material')
    for mat, res, src in ((atlas + '_0', 'HR', TEMPLATE_ATLAS + '_0'), (atlas, 'LR', TEMPLATE_ATLAS)):
        text = read_any(os.path.join(mat_dir, src + '.mat')).replace('m_Name: ' + src, 'm_Name: ' + mat)
        text = re.sub(r'(m_Texture: \{fileID: 2800000, guid: )[0-9a-f]{32}', r'\g<1>' + gid(project, 'tex:' + res), text)
        write_crlf(os.path.join(mat_dir, mat + '.mat'), text)
        write_crlf(os.path.join(mat_dir, mat + '.mat.meta'), re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid(project, 'mat:' + res), read_any(os.path.join(mat_dir, src + '.mat.meta')), count=1))
    names = sorted(entries)
    mono = os.path.join(ASSETS, 'MonoBehaviour')
    rel_folder = 'Assets/' + os.path.relpath(folder, ASSETS).replace(os.sep, '/')
    for asset, res, scale, src in ((atlas, 'HR', 1, TEMPLATE_ATLAS), (atlas + '_low', 'LR', 2, TEMPLATE_ATLAS + '_low')):
        text = read_any(os.path.join(mono, src + '.asset')).replace('\r\n', '\n')
        head = text[:text.index('  uvs:\n')]
        tail = text[text.index('  lastBuildID:'):]
        head = re.sub(r'm_Name: .*', 'm_Name: ' + asset, head)
        head = re.sub(r'(material: \{fileID: 2100000, guid: )[0-9a-f]{32}', r'\g<1>' + gid(project, 'mat:' + res), head)
        size = side // scale
        body = '  uvs:\n'
        for n in names:
            x, y = pos[n]
            w, h = sizes[n]
            x, y, w, h = x // scale, y // scale, max(1, math.ceil(w / scale)), max(1, math.ceil(h / scale))
            body += '  - serializedVersion: 2\n    x: %s\n    y: %s\n    width: %s\n    height: %s\n' % (x / size, (size - y - h) / size, w / size, h / size)
        body += '  textureGUIDs:\n' + ''.join('  - %s\n' % src_guids[n] for n in names)
        body += '  textureSizes:\n' + ''.join('  - {x: %d, y: %d}\n' % sizes[n] for n in names)
        body += '  defaultPivotOffsets:\n' + ''.join('  - {x: %.7g, y: %.7g}\n' % tuple(entries[n][1]) for n in names)
        body += '  textureNames:\n' + ''.join('  - %s\n' % n for n in names)
        body += '  texturePaths:\n' + ''.join('  - %s/%s.png\n' % (rel_folder, n) for n in names)
        write_crlf(os.path.join(mono, asset + '.asset'), head + body + tail)
        write_crlf(os.path.join(mono, asset + '.asset.meta'), re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + gid(project, 'atlas:' + res), read_any(os.path.join(mono, src + '.asset.meta')), count=1))
    log('Baked %d textures into a %dx%d atlas (%s)' % (len(entries), side, side, atlas))


def _edit_prefab_atlases(path, guid, add):
    text = read_any(path)
    newline = '\r\n' if '\r\n' in text else '\n'
    line = '  - {fileID: 11400000, guid: %s, type: 2}%s' % (guid, newline)
    i = text.index('  textureAtlases:' + newline)
    j = text.index('  mAnimationClips:', i)
    present = line in text[i:j]
    if add == present:
        return False
    if add:
        text = text[:j] + line + text[j:]
    else:
        text = text[:i] + text[i:j].replace(line, '') + text[j:]
    with open(path, 'w', newline='', encoding='utf-8') as f:
        f.write(text)
    return True


def register_atlas(project, log):
    rig = g.rig_info(project['rig'])
    previous = project.get('publishedRig')
    if previous and previous != project['rig']:
        old = g.rig_info(previous)
        _edit_prefab_atlases(old['path'], gid(project, 'atlas:HR'), False)
        if old['low']:
            _edit_prefab_atlases(old['low'], gid(project, 'atlas:LR'), False)
        log('Removed the atlas from the previous base rig ' + previous)
    changed = _edit_prefab_atlases(rig['path'], gid(project, 'atlas:HR'), True)
    if rig['low']:
        changed = _edit_prefab_atlases(rig['low'], gid(project, 'atlas:LR'), True) or changed
    log('Registered the atlas on %s prefab%s' % (project['rig'], 's' if rig['low'] else '') if changed else 'Atlas already registered on ' + project['rig'])
    project['publishedRig'] = project['rig']


# ------------------------------------------------------------------------------ balancing rows (text level, so the rest of each file stays byte-identical)

def upsert_row(text, row):
    """Returns text with row added, or replacing the row with the same NameId. The rest stays byte-identical."""
    block = '  ' + json.dumps(row, indent=2, ensure_ascii=False).replace('\n', '\n  ')
    start = re.search(r'^  \{\n    "NameId": %s,' % re.escape(json.dumps(row['NameId'])), text, re.M)
    if start:
        end = text.index('\n  }', start.start()) + 4
        new = text[:start.start()] + block + text[end:]
    else:
        end = text.rindex('\n]')
        new = text[:end] + ',\n' + block + text[end:]
    json.loads(new)
    return new


def read_row(file_name, name_id):
    rows = json.load(open(os.path.join(g.BALANCING, file_name + '.json'), encoding='utf-8'))
    return next((r for r in rows if r['NameId'] == name_id), None)


def upsert_loca(texts):
    path = os.path.join(g.BALANCING, 'Loca', 'live_English.json')
    text = open(path, encoding='utf-8', newline='').read()
    for key, value in texts.items():
        line = '    %s: %s' % (json.dumps(key, ensure_ascii=False), json.dumps(value, ensure_ascii=False))
        m = re.search(r'^    %s: .*?(,?)$' % re.escape(json.dumps(key, ensure_ascii=False)), text, re.M)
        if m:
            text = text[:m.start()] + line + m.group(1) + text[m.end():]
        else:
            end = text.rindex('\n  }\n}')
            text = text[:end] + ',\n' + line + text[end:]
    json.loads(text)
    return path, text


def clip_hash(name):
    """24-bit FNV-1a of a clip name: the skill parameter anim_clip, matched by ForgeSkillAnimation.Hash in the game."""
    h = 2166136261
    for c in name:
        h = ((h ^ ord(c)) * 16777619) & 0xFFFFFFFF
    return h & 0xFFFFFF


# Ability fields the game reads as hashed skill parameters (ForgeSkillAnimation.cs): the clip it plays and the
# projectile it fires. No key on the ability: keep what the skill row already has; '' : the usual one.
NAMED_PARAMS = {'anim': 'anim_clip', 'projectile': 'anim_projectile'}


RANGED = {'ranged': 1.0, 'melee': 0.0}  # ability 'ranged' -> skill parameter anim_ranged; '' : the weapon decides


def _ranged_param(ability):
    return RANGED.get(ability.get('ranged') or '')


def _named_param(ability, field):
    return float(clip_hash(ability[field])) if ability.get(field) else None


def skill_name_id(project, ability):
    if ability.get('nameId'):
        return ability['nameId']
    kind = {'Attack': 'attack', 'Support': 'support', 'Passive': 'passive', 'Combo': 'combo'}[ability['type']]
    base = 'pig_%s_forge_%s_%s' % (kind, art_id(project), slugify(ability.get('name'))[:32])
    used = {a.get('nameId') for a in project['abilities']}
    name, n = base, 2
    while name in used:
        name, n = '%s_%d' % (base, n), n + 1
    return name


# ------------------------------------------------------------------------------ an ability's own attack effects

EFFECT_FIELDS = [('SpawnTiming', int), ('DelayInSeconds', float), ('Type', int), ('TargetCombatant', int), ('TargetAnchor', int), ('SoundId', str),
                 ('DoNotParentOnBone', bool), ('PrefabName', str), ('OnHitEffect', bool), ('OnHitPrefix', str), ('ShowEffectInvokerIcon', bool),
                 ('InterpolationTime', float), ('SlowMotionDuration', float), ('SlowMotionTimeScale', float), ('UseTargetBoneScale', bool),
                 ('UseTargetBoneRotation', bool), ('IgnoreScaling', bool), ('EffectTextIdent', str), ('HasText', bool), ('DoNotMirror', bool)]


def _effect_entry(e):
    """One forge effect ({timing, delay, target, anchor, prefab, doNotMirror, onHit, onHitPrefix, raw}) as a VisualEffect."""
    import effects
    raw = dict(e.get('raw') or {})
    inv = lambda table, v, d: next((k for k, name in table.items() if name == v), d)
    raw.update({'SpawnTiming': inv(effects.TIMINGS, e.get('timing'), 0), 'DelayInSeconds': e.get('delay', 0) or 0,
                'TargetCombatant': inv(effects.TARGETS, e.get('target'), 0), 'TargetAnchor': inv(effects.ANCHORS, e.get('anchor'), 0),
                'PrefabName': e.get('prefab', '') or '', 'DoNotMirror': bool(e.get('doNotMirror')), 'OnHitEffect': bool(e.get('onHit')),
                'OnHitPrefix': e.get('onHitPrefix', '') or ''})
    # the Forge's extras (ForgeEffectExtras.cs): text settings and after-images ride in EffectTextIdent, slow motion
    # is the game's own SlowMotion target
    turn = {}
    if e.get('turn'):
        turn['turn'] = float(e['turn'])
    for k in ('offsetX', 'offsetY'):
        if e.get(k):
            turn[k] = float(e[k])
    # a point on one of the combatant's bones it sits on (ForgeEffectExtras.AttachBone), in the bone's own space
    if e.get('attach'):
        turn['attach'] = e['attach']
        for k in ('attachX', 'attachY'):
            if e.get(k):
                turn[k] = float(e[k])
    if e.get('turnWith') and e.get('turnWith') != 'anchor':
        turn['turnWith'] = e['turnWith']
        if e['turnWith'] == 'bone':
            turn['bone'] = e.get('bone') or ''
    if e.get('text') is not None and raw.get('PrefabName') != 'Forge_AfterImages':
        raw['HasText'] = True
        raw['EffectTextIdent'] = 'forge:' + json.dumps(dict(e['text'], **turn), separators=(',', ':'))
    elif turn and raw.get('PrefabName') != 'Forge_AfterImages':
        word = str(raw.get('EffectTextIdent') or '').strip()
        if word and not word.startswith('forge:') and str(raw.get('HasText', '0')).strip() in ('1', 'True', 'true'):
            # a game word (txt_...) rides along: the game still shows it (BattleMgrBase reads Settings.word)
            raw['EffectTextIdent'] = 'forge:' + json.dumps(dict(turn, word=word), separators=(',', ':'))
        elif word.startswith('forge:') and '"word"' in word:
            try:
                old = json.loads(word[len('forge:'):]).get('word')
            except ValueError:
                old = None
            raw['EffectTextIdent'] = 'forge:' + json.dumps(dict(turn, word=old) if old else turn, separators=(',', ':'))
        else:
            raw['EffectTextIdent'] = 'forge:' + json.dumps(turn, separators=(',', ':'))
            raw['HasText'] = False
    if raw.get('PrefabName') == 'Forge_AfterImages':
        raw['EffectTextIdent'] = 'forge:' + json.dumps(e.get('after') or {}, separators=(',', ':'))
        raw['HasText'] = False
    if e.get('slow') is not None or e.get('target') == 'SlowMotion':
        sl = e.get('slow') or {}
        raw.update({'TargetCombatant': 7, 'SlowMotionTimeScale': sl.get('scale', 0.3), 'SlowMotionDuration': sl.get('duration', 0.5),
                    'InterpolationTime': sl.get('ease', 0.1), 'PrefabName': ''})
        # its own ease back to normal speed (BattleMgrBase.ChangeTimeScale reads it from the forge settings)
        if sl.get('easeOut') is not None and sl.get('easeOut') != sl.get('ease', 0.1):
            raw['EffectTextIdent'] = 'forge:' + json.dumps({'easeOut': float(sl['easeOut'])}, separators=(',', ':'))
        elif not str(raw.get('EffectTextIdent') or '').startswith('forge:{"text'):
            raw['EffectTextIdent'] = ''
    out = {}
    for key, kind in EFFECT_FIELDS:
        v = raw.get(key, '')
        if kind is bool:
            out[key] = v if isinstance(v, bool) else str(v).strip() in ('1', 'true', 'True')
        elif kind is str:
            out[key] = '' if v is None else str(v)
        else:
            try:
                out[key] = kind(float(v or 0))
            except ValueError:
                out[key] = kind(0)
    return out


def skill_effects_dir():
    return os.path.join(g.ASSETS, 'Resources', 'ForgeSkillEffects')


def _same_effects(a, b):
    return [_effect_entry(x) for x in a or []] == [_effect_entry(x) for x in b or []]


def write_skill_effects(name_id, effect_list, log):
    """Resources/ForgeSkillEffects/<name_id>.json (ForgeSkillEffects.cs), or removes it when effect_list is None."""
    folder = skill_effects_dir()
    path = os.path.join(folder, name_id + '.json')
    if effect_list is None:
        if os.path.exists(path):
            os.remove(path)
            if os.path.exists(path + '.meta'):
                os.remove(path + '.meta')
            log('Ability %s uses its skill\'s usual effects again' % name_id)
        return
    os.makedirs(folder, exist_ok=True)
    meta = folder + '.meta'
    if not os.path.exists(meta):
        open(meta, 'w').write('fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    json.dump({'BalancingId': name_id, 'VisualEffects': [_effect_entry(e) for e in effect_list]}, open(path, 'w', encoding='utf-8'), indent=1)
    if not os.path.exists(path + '.meta'):
        open(path + '.meta', 'w').write('fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    log('Wrote %d effect%s for %s' % (len(effect_list), '' if len(effect_list) == 1 else 's', name_id))


def skill_fx_of(skill_id, asset):
    """The effects a skill plays in the game: its own Forge list (ForgeSkillEffects.cs), else its AssetId's in Root.unity."""
    import effects
    own = os.path.join(skill_effects_dir(), skill_id + '.json') if skill_id else None
    if own and os.path.exists(own):
        return [dict({'raw': dict(e), 'timing': effects.TIMINGS.get(e['SpawnTiming'], 'Start'), 'delay': e['DelayInSeconds'],
                      'target': effects.TARGETS.get(e['TargetCombatant'], 'Origin'), 'anchor': effects.ANCHORS.get(e['TargetAnchor'], 'Bottom'),
                      'prefab': e['PrefabName'], 'doNotMirror': e['DoNotMirror'], 'onHit': e['OnHitEffect'], 'onHitPrefix': e['OnHitPrefix']},
                     **effects.extras({k: (int(v) if isinstance(v, bool) else v) for k, v in e.items()}))
                for e in json.load(open(own, encoding='utf-8'))['VisualEffects']]
    return effects.skill_effects(asset or '')


_PLACED = ('SpawnTiming', 'DelayInSeconds', 'TargetCombatant', 'TargetAnchor', 'PrefabName', 'OnHitEffect', 'OnHitPrefix', 'DoNotMirror')


def _compact(e):
    # only the raw fields the editor doesn't show and that aren't at their default (_effect_entry fills the rest)
    return dict(e, raw={k: v for k, v in (e.get('raw') or {}).items() if k not in _PLACED and str(v) not in ('', '0', 'False', '0.0')})


def all_skill_effects():
    """Every effect list, for the Abilities tab in one request: {'assets': {AssetId: [...]}, 'skills': {skill NameId: [...]}}
    (skills with their own Forge list)."""
    import effects
    effects.skill_effects('')
    assets = {k: [_compact(e) for e in v] for k, v in effects._settings[1].items()}
    own = {}
    folder = skill_effects_dir()
    if os.path.isdir(folder):
        for f in os.listdir(folder):
            if f.endswith('.json'):
                own[f[:-5]] = [_compact(e) for e in skill_fx_of(f[:-5], '')]
    return {'assets': assets, 'skills': own}


# ------------------------------------------------------------------------------ bones turning around another point

def _rest_local(clip, i):
    tr = (clip['tracks'][i] if i < len(clip['tracks']) else None) or {}
    first = lambda v, d: (v[0] if v and isinstance(v[0], list) else v) if v is not None else d
    pos, ang, sc = first(tr.get('p'), [0.0, 0.0]), tr.get('r', 0.0), first(tr.get('s'), [1.0, 1.0])
    ang = ang[0] if isinstance(ang, list) else ang
    return pos, ang, sc


def bone_pivots_dir():
    return os.path.join(g.ASSETS, 'Resources', 'ForgeBonePivots')


def write_bone_pivots(project, log):
    """Resources/ForgeBonePivots/Forge_<art id>.json for ForgeBonePivots.cs: each slot's rotation pivot (the Forge's
    S.project.bonePivots, in the slot's art space) as a point in its bone's space, with the bone's rest rotation and
    scale (Idle's first frame, as the preview). Removes the file when there are none. Returns whether it changed."""
    folder = bone_pivots_dir()
    path = os.path.join(folder, 'Forge_%s.json' % art_id(project))
    pivots = project.get('bonePivots') or {}
    entries = []
    if pivots:
        rig = g.bake_rig(project['rig'])
        rest = next((c for c in rig['clips'] if c['name'] == 'Idle'), rig['clips'][0])
        for b in rig['bones']:
            pv = pivots.get(b.get('slot') or '')
            if not pv or b.get('bone') is None or b['bone'] < 0:
                continue
            x, y = float(pv[0]), float(pv[1])
            sprite = b.get('sprite')
            if sprite is not None and rig['parents'][sprite] == b['bone']:
                (px, py), ang, (sx, sy) = _rest_local(rest, sprite)
                c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang))
                x, y = c * sx * x - s * sy * y + px, s * sx * x + c * sy * y + py
            _, ang, (sx, sy) = _rest_local(rest, b['bone'])
            entries.append({'bone': b['name'], 'x': round(x, 4), 'y': round(y, 4), 'restAngle': float(ang), 'restSx': float(sx), 'restSy': float(sy)})
    old = open(path, encoding='utf-8').read() if os.path.exists(path) else None
    if not entries:
        if old is not None:
            os.remove(path)
            if os.path.exists(path + '.meta'):
                os.remove(path + '.meta')
            log('Bones turn around their own origins again')
        return old is not None
    text = json.dumps({'bones': entries}, indent=1)
    if text == old:
        return False
    os.makedirs(folder, exist_ok=True)
    if not os.path.exists(folder + '.meta'):
        open(folder + '.meta', 'w').write('fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    open(path, 'w', encoding='utf-8').write(text)
    if not os.path.exists(path + '.meta'):
        open(path + '.meta', 'w').write('fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' % uuid.uuid4().hex)
    log('Rotation pivots: %s' % ', '.join(e['bone'] for e in entries))
    return True


def build_skills(project, log):
    """Decides each ability's skill row. An untouched existing skill is referenced as is; a changed one becomes this
    combatant's own copy, so other pigs using the skill aren't affected. New abilities start as a stand-in until Claude
    has written them."""
    rows, texts, ids = [], {}, []
    catalog = {s['id']: s for s in g.skills_catalog()}
    fx_of = lambda skill: skill_fx_of(skill['id'], skill.get('asset') or '')
    for ability in project['abilities']:
        typ = ability['type']
        source = catalog.get(ability.get('source')) if ability.get('source') else None
        default_color = g.SKILL_COLORS[typ]
        custom_color = ability.get('color', default_color).upper().lstrip('#')
        if source and not ability.get('nameId'):
            # No 'anim' key: the ability keeps whatever clip its skill already names.
            same_anim = all(f not in ability or _named_param(ability, f) == (source['params'] or {}).get(param) for f, param in NAMED_PARAMS.items())
            same_anim = same_anim and ('ranged' not in ability or _ranged_param(ability) == (source['params'] or {}).get('anim_ranged'))
            same_anim = same_anim and ('impactOnHit' not in ability or bool(ability['impactOnHit']) == ('anim_impact_on_hit' in (source['params'] or {})))
            # its own effects make it this combatant's copy too (the effects belong to the copy's NameId)
            same_fx = 'effects' not in ability or _same_effects(ability['effects'], fx_of(source))
            # edited values (damage %, turns...) make it this combatant's copy as well
            same_fx = same_fx and all((source['params'] or {}).get(k) == v for k, v in (ability.get('paramEdits') or {}).items())
            same_fx = same_fx and (ability.get('durationEdit') is None or ability['durationEdit'] == source.get('duration'))
            unchanged = same_fx and (ability.get('name') == source['name'] and ability.get('desc') == source['desc'] and custom_color == source['color']
                         and ability.get('icon') == source['icon'] and ability.get('atlas') == source['atlas'] and same_anim
                         and (typ != 'Combo' or (ability.get('comboId') or '') == (source.get('asset') or '').lower()))
            if unchanged:
                ids.append((source['id'], typ))
                continue
        name_id = ability['nameId'] = skill_name_id(project, ability)
        existing = read_row('SkillBalancingData', name_id)
        if existing:
            row = dict(existing)
        elif ability.get('row'):
            row = dict(ability['row'])  # came from an exported combatant, or an earlier copy of this ability
        elif source:
            row = dict(read_row('SkillBalancingData', source['id']))
        else:
            row = {'AssetId': '', 'SkillParameters': {}, 'SkillTemplateType': '', 'EffectDuration': 0, 'EffectType': 'Curse', 'TargetingBehavior': 'None',
                   'TargetAlreadyAffectedTargets': True, 'TargetSelfPossible': False, 'EffectIconAtlasId': '', 'EffectIconAssetId': '', 'TargetCulling': None}
            row.update(json.loads(json.dumps(PLACEHOLDER[typ])))
            if typ != 'Passive':
                log('Ability "%s" is a stand-in (%s) until Claude has written it' % (ability.get('name'), row['SkillTemplateType']))
            else:
                log('Ability "%s" is a stand-in with no effect until Claude has written it' % ability.get('name'))
        params = dict(row.get('SkillParameters') or {})
        for k, v in (ability.get('paramEdits') or {}).items():
            if v is None:
                params.pop(k, None)  # taken off (a basic value set back to 0)
            else:
                params[k] = float(v)
        if ability.get('durationEdit') is not None:
            row['EffectDuration'] = int(round(float(ability['durationEdit'])))
        params.pop('ui_color_rgb', None)
        if custom_color != default_color:
            params['ui_color_rgb'] = float(int(custom_color, 16))
        for f, param in NAMED_PARAMS.items():
            if f in ability:
                params.pop(param, None)
                if ability[f] and (f != 'projectile' or (typ == 'Attack' and ability.get('ranged') != 'melee')):
                    params[param] = _named_param(ability, f)
        if 'impactOnHit' in ability:
            # the projectile's hit plays the Impact effects (AttackSkillTemplate / SkillBattleDataBase.SpawnProjectile)
            params.pop('anim_impact_on_hit', None)
            if ability['impactOnHit'] and typ == 'Attack':
                params['anim_impact_on_hit'] = 1.0
        if 'ranged' in ability:
            params.pop('anim_ranged', None)
            if typ == 'Attack' and _ranged_param(ability) is not None:
                params['anim_ranged'] = _ranged_param(ability)
        if typ == 'Combo':
            # the combo id ComboMoveDirector matches recipes on
            row['AssetId'] = (ability.get('comboId') or slugify(ability.get('name'))).lower()
        row.update({'NameId': name_id, 'LocaId': name_id, 'SkillParameters': params, 'TargetType': typ, 'IconAssetId': ability.get('icon', ''), 'IconAtlasId': ability.get('atlas', '')})
        ordered = {k: row[k] for k in ('NameId', 'AssetId', 'LocaId', 'SkillParameters', 'SkillTemplateType', 'EffectDuration', 'TargetType', 'EffectType', 'TargetingBehavior',
                                       'TargetAlreadyAffectedTargets', 'TargetSelfPossible', 'IconAssetId', 'IconAtlasId', 'EffectIconAtlasId', 'EffectIconAssetId', 'TargetCulling') if k in row}
        rows.append(ordered)
        write_skill_effects(name_id, ability['effects'] if 'effects' in ability else None, log)
        texts[name_id + '_name'] = ability.get('name', '')
        texts[name_id + '_desc'] = ability.get('desc', '')
        ids.append((name_id, typ))
    return rows, texts, ids


def decision_combos(order, id_of, actives):
    """The game's SkillCombos from an Turn Plan (web/decisions.js). Only ticked trees go to the game: with
    one, its combos as they are; with more, each tree's combos start with "@tree<n>:<pct>" and the combatant picks one
    tree by pct at the start of a battle (ForgeDecisionTrees.cs). An order saved before there were tabs is one tree."""
    order = order or {}
    if 'trees' not in order:
        return tree_combos(order, id_of, actives)
    on = [t for t in order.get('trees') or [] if t.get('on')]
    if len(on) == 1:
        return tree_combos(on[0], id_of, actives)
    total = sum(max(0.0, float(t.get('pct') or 0)) for t in on)
    out = []
    for i, t in enumerate(on):
        share = 100.0 * max(0.0, float(t.get('pct') or 0)) / total if total > 0 else 100.0 / len(on)
        mark = '@tree%d:%s' % (i + 1, ('%.2f' % share).rstrip('0').rstrip('.'))
        out += [{'Percentage': c['Percentage'], 'ComboChain': [mark] + c['ComboChain']} for c in tree_combos(t, id_of, actives)]
    return out


def tree_combos(tree, id_of, actives):
    """One tree's combos: every path from the start is a combo, its chance the product of its branch shares. id_of maps
    a node's ref (an ability uid, or 'keep:<skill id>') to the skill NameId; steps whose ability is gone are skipped."""
    out = []
    if 'chains' in (tree or {}):
        # chains (web/decisions.js): a chain's moves always follow each other, then it takes one of its branches
        # a chain's percentage goes to the game as typed (the game takes each combo's share of their total); a
        # branch's chance is its share of its siblings' total
        def walk_seq(seqs, chain, share, top=False):
            total = sum(max(0.0, float(q.get('w') or 0)) for q in seqs)
            for q in seqs:
                w = max(0.0, float(q.get('w') or 0))
                sh = w if top else share * w / total if total > 0 else 0.0
                c = list(chain)
                for m in q.get('moves') or []:
                    ref = m.get('ref') or ''
                    sid = ref[5:] if ref.startswith('keep:') else id_of.get(ref)
                    if sid and sid in actives:
                        c.append(sid)
                if q.get('branches'):
                    walk_seq(q['branches'], c, sh)
                elif c:
                    out.append((sh, c))

        walk_seq(tree.get('chains') or [], [], 100.0, True)
        return [{'Percentage': round(pct, 2), 'ComboChain': chain} for pct, chain in out]

    # step by step (web/decisions.js): a first step's chance goes to the game as typed (the game takes each combo's
    # share of their total); a later step's is its share of its siblings' total. A '@decision' step takes one of its
    # options by share, and every path through it goes on with the decision's kids.
    def end(chain, s, after):
        for i, nxt in enumerate(after):
            if nxt:
                return walk(nxt, chain, s, False, after[i + 1:])
        if chain and len(out) < 4000:
            out.append((s, chain))

    def walk(nodes, chain, share, top, after):
        total = sum(max(0.0, float(n.get('w') or 0)) for n in nodes)
        for n in nodes:
            w = max(0.0, float(n.get('w') or 0))
            s = w if top else share * w / total if total > 0 else 0.0
            ref = n.get('ref') or ''
            if ref == '@stop':
                end(chain, s, after)
                continue
            if ref == '@decision':
                nxt = [n.get('kids') or []] + after
                if n.get('options'):
                    walk(n['options'], chain, s, False, nxt)
                else:
                    end(chain, s, nxt)
                continue
            sid = ref[5:] if ref.startswith('keep:') else id_of.get(ref)
            c = chain + [sid] if sid and sid in actives else chain
            if n.get('kids'):
                walk(n['kids'], c, s, False, after)
            else:
                end(c, s, after)

    walk((tree or {}).get('kids') or [], [], 100.0, True, [])
    return [{'Percentage': round(pct, 2), 'ComboChain': chain} for pct, chain in out]


def combos_for(ids):
    supports = [i for i, t in ids if t == 'Support']
    attacks = [i for i, t in ids if t in ('Attack', 'Combo')]  # a combo move is picked like an attack
    if attacks:
        share = round(100.0 / len(attacks), 2)
        return [{'Percentage': share, 'ComboChain': supports + [a]} for a in attacks]
    return [{'Percentage': 100.0, 'ComboChain': supports}] if supports else []


def build_items(project, entries):
    """The combatant's own inventory. The class item is always present: its asset name drives the headgear, the pirate
    variant and the feature swaps."""
    base = asset_base(project)
    pid = 'pig_forge_' + project['id']
    weapon = project.get('weapon') or {}
    template = read_row('EquipmentBalancingData', 'pig_weapon_brute_lassie_rooster_01')
    equipment = []
    content = {}

    def add_item(item_type, suffix, nameid):
        row = dict(template)
        row.update({'NameId': nameid, 'ItemType': item_type, 'AssetBaseId': base + suffix, 'DirectAssetAndLoca': True})
        if item_type == 'MainHandEquipment':
            row.update({'AnimationIndex': int(weapon.get('animationIndex', 1)), 'IsRanged': bool(weapon.get('isRanged')), 'ProjectileAssetId': weapon.get('projectile', '') if weapon.get('isRanged') else '',
                        'DirectProjectileAssetAndLoca': bool(weapon.get('isRanged')), 'HitEffectSuffix': weapon.get('hitEffect', row.get('HitEffectSuffix', ''))})
        equipment.append(row)
        content[nameid] = 1

    add_item('MainHandEquipment', '_MainHand', pid + '_mainhand')
    if base + '_OffHand' in entries or base + '_OffHand_Back' in entries:
        add_item('OffHandEquipment', '_OffHand', pid + '_offhand')
    class_row = dict(read_row('ClassItemBalancingData', 'headgear_brute_lassie'))
    class_row.update({'NameId': pid + '_headgear', 'AssetBaseId': base})
    content[class_row['NameId']] = 1
    inventory = {'NameId': pid + '_01', 'DefaultInventoryContent': content, 'InitializingLevel': 0}
    return equipment, class_row, inventory


# ------------------------------------------------------------------------------ encoding

def unity_editor():
    """The editor version this project uses, from Unity Hub's default install folder."""
    version = re.search(r'm_EditorVersion: (\S+)', read_any(os.path.join(g.ROOT, 'ProjectSettings', 'ProjectVersion.txt')))
    return os.path.expanduser('~/Unity/Hub/Editor/%s/Editor/Unity' % version.group(1))


def unity_running():
    return subprocess.run(['pgrep', '-x', 'Unity'], capture_output=True).returncode == 0


def _log_size():
    return os.path.getsize(EDITOR_LOG) if os.path.exists(EDITOR_LOG) else 0


def _rejection(since):
    """What Unity said if it refused a JSON file after this log position, else None."""
    with open(EDITOR_LOG, 'rb') as f:
        f.seek(since)
        text = f.read().decode('utf-8', 'replace')
    failed = re.search(r'Failed to reimport (?:[\w]+\.)*(\w+)! JSON is invalid!', text)
    if not failed:
        return None
    reason = re.search(r'^\w*Exception: (.*)$', text[failed.end():], re.M)
    return '%s: %s' % (failed.group(1), reason.group(1).strip() if reason else 'see the Unity console')


def _wait_log(pattern, since, timeout):
    end = time.time() + timeout
    while time.time() < end:
        if os.path.exists(EDITOR_LOG):
            with open(EDITOR_LOG, 'rb') as f:
                f.seek(since)
                if re.search(pattern.encode(), f.read()):
                    return True
            rejected = _rejection(since)
            if rejected:
                time.sleep(0.5)  # Unity logs the reason just after the failure line
                raise RuntimeError('Unity refused the balancing data, so the game still has the old version. Click through the dialog in Unity. ' + _rejection(since))
        time.sleep(0.5)
    return False


def write_in_place(path, text):
    # In-place write, not replace: BalancingDataContainerHelper's watcher only fires on the Changed event.
    with open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(text)


# What Unity logs once the encoded file is on disk: from the JSON watcher or from the Balancing Data menu.
CONTAINER_DONE = r'Serialized \d+ classes to the balancing container'
LOCA_DONE = r'Serialized live_English loca|Serialized \d+ locas'
WATCH_TIMEOUT = 20
MANUAL_TIMEOUT = 180


def _done_marker(cls):
    """What the editor log shows once a written file is encoded. The JSON watcher re-encodes just the changed class
    (\"Reimported ABH.Shared.BalancingData.<cls>\") and then writes the container; a full encode (the menu, or
    ForgeEncodeRequest) prints CONTAINER_DONE."""
    if cls == 'Loca':
        return LOCA_DONE
    return r'Reimported ABH\.Shared\.BalancingData\.%s in|%s' % (re.escape(cls), CONTAINER_DONE)


def _wait_written(path, before, timeout=10):
    """Until the file at path changed since `before` (mtime, size) and stopped changing."""
    end = time.time() + timeout
    last = None
    while time.time() < end:
        try:
            st = os.stat(path)
            now = (st.st_mtime, st.st_size)
        except OSError:
            now = None
        if now and now != before and now == last:
            return True
        last = now
        time.sleep(0.3)
    return False


def _stat(path):
    try:
        st = os.stat(path)
        return st.st_mtime, st.st_size
    except OSError:
        return None


LOCA_BYTES = os.path.join(ASSETS, 'StreamingAssets', 'local', 'live_English.bytes')


def encode(writes, log):
    """writes: [(class name or 'Loca', path, text)]."""
    if unity_running():
        request = os.path.join(g.ROOT, 'Temp', 'EquipmentPresetRefresh.request')
        if os.path.isdir(os.path.dirname(request)):
            open(request, 'w').close()
            log('Asked the open Unity editor to import the new assets')
        missed = {}
        for cls, path, text in writes:
            what = 'loca' if cls == 'Loca' else 'balancing'
            target = LOCA_BYTES if cls == 'Loca' else CONTAINER
            log('Encoding %s...' % cls)
            ok = False
            for attempt in range(2):  # the watcher can miss a write, for example during a script reload
                since, before = _log_size(), _stat(target)
                write_in_place(path, text)
                if _wait_log(_done_marker(cls), since, WATCH_TIMEOUT):
                    ok = _wait_written(target, before)
                    break
            if ok:
                log('Encoded ' + cls)
                time.sleep(1.5)  # the watcher can fire twice; the next file must not race its second write
            else:
                missed[what] = True
        if missed:
            _encode_in_editor(missed, log)
        return
    for _, path, text in writes:
        write_in_place(path, text)
    unity = unity_editor()
    if not os.path.exists(unity):
        log('WARNING: Unity is closed and not found at %s. The JSON is written, but the game reads the encoded container; open Unity and run Balancing Data > Encode.' % unity)
        return
    log('Unity is closed: encoding in batch mode (this imports the new assets too; it can take a few minutes)')
    os.makedirs(g.CACHE, exist_ok=True)
    loca_dir = os.path.join(ASSETS, 'StreamingAssets', 'local')
    keep = {p: open(p, 'rb').read() for p in glob.glob(os.path.join(loca_dir, 'live_*.bytes')) if not p.endswith(('live_English.bytes', os.path.basename(CONTAINER)))}
    for method in ('CreateNonEventContainerFromJsons', 'CreateLocaContainersFromJsons'):
        logfile = os.path.join(g.CACHE, 'unity_%s.log' % method)
        result = subprocess.run([unity, '-batchmode', '-quit', '-projectPath', g.ROOT, '-executeMethod', 'BalancingDataContainerHelper.' + method, '-logFile', logfile], capture_output=True)
        if result.returncode != 0:
            log('WARNING: Unity batch mode exited with %d; see %s' % (result.returncode, logfile))
            return
        log('Encoded with ' + method)
    # The loca encoder rewrites all languages; only English changed.
    for path, data in keep.items():
        with open(path, 'wb') as f:
            f.write(data)


def _encode_in_editor(missed, log):
    """The watcher missed some files: ask the open editor to run the encode menu commands (Assets/Editor/
    ForgeEncodeRequest.cs), else tell the designer which to run."""
    menus = {'balancing': 'Balancing Data > Encode > Balancing container', 'loca': 'Balancing Data > Loca > Serialize all loca'}
    marks = {'balancing': CONTAINER_DONE, 'loca': LOCA_DONE}
    loca_dir = os.path.join(ASSETS, 'StreamingAssets', 'local')
    # serializing all loca rewrites every language; only English changed
    keep = {p: open(p, 'rb').read() for p in glob.glob(os.path.join(loca_dir, 'live_*.bytes'))
            if not p.endswith(('live_English.bytes', os.path.basename(CONTAINER)))} if 'loca' in missed else {}
    since = _log_size()
    request = os.path.join(g.ROOT, 'Temp', 'ForgeEncode.request')
    try:
        with open(request, 'w') as f:
            f.write('\n'.join(missed))
        log('Unity\'s file watcher missed it: asked the editor to encode (%s)' % ', '.join(missed))
    except OSError:
        pass
    for what in missed:
        if _wait_log(marks[what], since, 60):
            log('Encoded the %s' % ('English text' if what == 'loca' else 'balancing data'))
            continue
        log('Unity did not encode the %s. Run %s now. Waiting up to %d s; you can close this window meanwhile.' % (what, menus[what], MANUAL_TIMEOUT))
        if _wait_log(marks[what], since, MANUAL_TIMEOUT):
            log('Encoded the %s' % what)
        else:
            log('WARNING: the %s was not encoded, so the game still has the old version. Run %s.' % (what, menus[what]))
    time.sleep(1)
    for path, data in keep.items():
        with open(path, 'wb') as f:
            f.write(data)


# ------------------------------------------------------------------------------ the whole upload

def validate(project):
    problems = []
    if not project.get('id') or not re.fullmatch(r'[a-z0-9_]+', project['id']):
        problems.append('The combatant id must use lowercase letters, digits and underscores')
    if not project.get('name', '').strip():
        problems.append('The combatant needs a name')
    if project.get('kind') != 'existing' and not any(a['type'] in ('Attack', 'Support') for a in project.get('abilities', [])):
        problems.append('The combatant needs at least one attack or support ability, or it can\'t act in battle')
    rig = json.loads(g.rig_json(project['rig']))
    known = {s['key'] for s in rig['slots']}
    for key in project.get('slots', {}):
        if key not in known:
            problems.append('%s has no %s slot' % (project['rig'], key))
    return problems


def publish(project, log):
    problems = validate(project)
    if problems:
        raise ValueError('; '.join(problems))
    base = asset_base(project)
    entries = {}
    names = {}
    for key, slot in project.get('slots', {}).items():
        name = names[key] = base + GEAR_TEXTURE[key] if key in GEAR_TEXTURE else '%s__%s' % (base, key)
        # eye beams and bells layers aren't art: beams.py / bells.py upload them on their own
        layers = [l for l in slot.get('layers', []) if not l.get('hidden') and l.get('kind') not in ('beams', 'bells')]
        if slot.get('hidden'):
            # A hidden feature gets a blank texture. Hidden equipment simply has none, and the game hides its bone.
            if key not in GEAR_TEXTURE:
                entries[name] = (Image.new('RGBA', (2, 2), (0, 0, 0, 0)), (0.0, 0.0))
        elif layers:
            entries[name] = bake_layers(layers)
    if not entries:
        entries[base + '__Unused'] = (Image.new('RGBA', (2, 2), (0, 0, 0, 0)), (0.0, 0.0))
    write_atlas(project, entries, log)
    register_atlas(project, log)
    write_bone_pivots(project, log)

    skill_rows, texts, ids = build_skills(project, log)
    import beams  # eye beams: Assets/Resources/HelmetBeams.json (HelmetBeams.cs)
    beams.write([base, 'Forge_' + art_id(project)], beams.project_entries(project, {k: n for k, n in names.items() if n in entries}, {a['uid']: i for a, (i, _) in zip(project['abilities'], ids)}, log), log)
    import bells  # bells: Assets/Resources/ScytheBellsConfig.json (ScytheBells.cs)
    bells.write([base, 'Forge_' + art_id(project)], bells.project_entries(project, {k: n for k, n in names.items() if n in entries}, {a['uid']: i for a, (i, _) in zip(project['abilities'], ids)}, log), log)
    pid = 'pig_forge_' + project['id']
    equipment, class_row, inventory = build_items(project, entries)
    stats = project.get('stats') or {}
    keep = project.get('keepSkills') or []
    actives = [i for i, t in ids if t != 'Passive'] + [k['id'] for k in keep if not k['passive']]
    passives = [i for i, t in ids if t == 'Passive'] + [k['id'] for k in keep if k['passive']]
    combos = combos_for(ids)
    if project.get('combos'):
        # A duplicate keeps the turn plan of the pig it was copied from while its abilities still match.
        import existing
        mapping = {a['source']: i for a, (i, _) in zip(project['abilities'], ids) if a.get('source')}
        planned = {i for c in project['combos'] for i in c.get('ComboChain', [])}
        added = [i for a, (i, t) in zip(project['abilities'], ids) if not a.get('source') and t != 'Passive']
        kept = existing.remap_combos(project['combos'], mapping, planned - set(mapping), added, actives)
        if kept is not None:
            combos = kept
    if project.get('decisionOrder') is not None:
        # its own Turn Plan (the Abilities tab)
        by_uid = {a['uid']: i for a, (i, _) in zip(project['abilities'], ids)}
        combos = decision_combos(project['decisionOrder'], by_uid, set(actives))
    pig = {'NameId': pid, 'AssetId': project['rig'], 'LocaId': pid, 'DefaultInventoryNameId': inventory['NameId'], 'BaseHealth': int(stats.get('hp', 100)), 'BaseAttack': int(stats.get('attack', 10)),
           'PerLevelHealth': int(stats.get('perLevelHealth', 0)), 'PerLevelAttack': int(stats.get('perLevelAttack', 0)), 'SkillNameIds': actives, 'SkillCombos': combos, 'SizeType': stats.get('sizeType', 'Medium'),
           'Faction': 'Pigs', 'LootTableDefeatBonus': stats.get('loot') or {}, 'SizeScale': float(stats.get('sizeScale', 1.0)), 'PigStrength': int(stats.get('strength', 5)),
           'PassiveSkillNameId': ','.join(passives), 'IgnoreDifficulty': False}
    texts[pid + '_name'] = project['name'].strip()

    stages = [('SkillBalancingData', skill_rows), ('EquipmentBalancingData', equipment), ('ClassItemBalancingData', [class_row]),
              ('InventoryBalancingData', [inventory]), ('PigBalancingData', [pig])]
    writes = []
    for file_name, rows in stages:
        if rows:
            path = os.path.join(g.BALANCING, file_name + '.json')
            text = open(path, encoding='utf-8', newline='').read()
            for row in rows:
                text = upsert_row(text, row)
            writes.append((file_name, path, text))
    loca_path, loca_text = upsert_loca(texts)
    writes.append(('Loca', loca_path, loca_text))
    log('Combatant id: %s, inventory: %s, abilities: %s' % (pid, inventory['NameId'], ', '.join(i for i, _ in ids)))
    encode(writes, log)
    import spring_config  # a spring drone's spring constant: Assets/Resources/SpringDrones.json
    spring_config.publish(pid, project, log)
    project['publishedAt'] = time.strftime('%Y-%m-%d %H:%M')
    log('Done. Place "%s" in a battle table to fight it.' % pid)
    return project


# ------------------------------------------------------------------------------ removing an uploaded combatant

def remove_row(text, name_id):
    """text without the row with this NameId (the rest byte-identical); unchanged if there is none."""
    start = re.search(r'^  \{\n    "NameId": %s,' % re.escape(json.dumps(name_id)), text, re.M)
    if not start:
        return text
    begin, end = start.start(), text.index('\n  }', start.start()) + 4
    if text.startswith(',\n', end):  # rows follow: drop it with its comma
        new = text[:begin] + text[end + 2:]
    elif text[:begin].endswith(',\n'):  # the last row: the one before it loses its comma
        new = text[:begin - 2] + text[end:]
    else:  # the only row
        new = text[:begin] + text[end + 1:]
    json.loads(new)
    return new


def remove_loca(keys):
    path = os.path.join(g.BALANCING, 'Loca', 'live_English.json')
    text = open(path, encoding='utf-8', newline='').read()
    for key in keys:
        m = re.search(r'^    %s: .*?(,?)\n' % re.escape(json.dumps(key, ensure_ascii=False)), text, re.M)
        if not m:
            continue
        text = text[:m.start()] + text[m.end():]
        if not m.group(1):  # it was the last entry: the one before it loses its comma
            before = text[:m.start()]
            if before.endswith(',\n'):
                text = before[:-2] + '\n' + text[m.start():]
    json.loads(text)
    return path, text


def references(name_ids, skip):
    """{NameId: [file, ...]} for the ids that some balancing data still names, outside the rows in skip."""
    found = {}
    files = glob.glob(os.path.join(g.BALANCING, '*.json')) + glob.glob(os.path.join(g.BALANCING, 'EventBalancingData', '*.json'))
    for path in files:
        try:
            rows = json.load(open(path, encoding='utf-8'))
        except (OSError, ValueError):
            continue
        if not isinstance(rows, list):
            continue
        for row in rows:
            if not isinstance(row, dict) or row.get('NameId') in skip:
                continue
            blob = json.dumps(row, ensure_ascii=False)
            for name_id in name_ids:
                if re.search(r'(?<![\w])%s(?![\w])' % re.escape(name_id), blob):
                    found.setdefault(name_id, []).append('%s (%s)' % (os.path.basename(path)[:-5], row.get('NameId')))
    return found


def unpublish(project, log):
    """Takes an uploaded Forge combatant out of the game: its pig, inventory, items, own abilities, text and atlas.
    Refuses when other balancing data (a battle table, another pig) still uses any of it."""
    if project.get('kind') == 'existing':
        raise ValueError('The game\'s own combatants can\'t be deleted here')
    art = art_id(project)
    pid = 'pig_forge_' + project['id']
    pig = read_row('PigBalancingData', pid)
    inventory = (pig or {}).get('DefaultInventoryNameId') or pid + '_01'
    items = [pid + '_mainhand', pid + '_offhand', pid + '_headgear']
    skills = [a['nameId'] for a in project.get('abilities', []) if a.get('nameId') and read_row('SkillBalancingData', a['nameId'])]
    own = {pid, inventory, *items, *skills}
    used = references([pid, inventory, *items], own)
    if used:
        raise ValueError('Not removed: the game still uses it. ' + '; '.join('%s in %s' % (k, ', '.join(v[:4])) for k, v in used.items()))
    # Abilities another combatant also uses stay.
    shared = references(skills, own)
    for sid in shared:
        log('Keeping ability %s: %s uses it too' % (sid, ', '.join(shared[sid][:3])))
    skills = [sid for sid in skills if sid not in shared]

    writes = []
    for file_name, ids in (('SkillBalancingData', skills), ('EquipmentBalancingData', items[:2]), ('ClassItemBalancingData', items[2:]),
                           ('InventoryBalancingData', [inventory]), ('PigBalancingData', [pid])):
        path = os.path.join(g.BALANCING, file_name + '.json')
        text = open(path, encoding='utf-8', newline='').read()
        new = text
        for name_id in ids:
            new = remove_row(new, name_id)
        if new != text:
            writes.append((file_name, path, new))
    texts = [pid + '_name'] + [sid + suffix for sid in skills for suffix in ('_name', '_desc')]
    loca_path, loca_text = remove_loca(texts)
    writes.append(('Loca', loca_path, loca_text))

    remove_atlas(project, art, log)
    import beams
    beams.write([asset_base(project), 'Forge_' + art], [], log)
    import bells
    bells.write([asset_base(project), 'Forge_' + art], [], log)
    log('Removing %s, its inventory, items, %d abilit%s and its text' % (pid, len(skills), 'y' if len(skills) == 1 else 'ies'))
    encode(writes, log)
    log('Removed from the game.')


def remove_atlas(project, art, log):
    """Takes the project's Forge_<art>_Atlas off its rig prefab, then deletes its files and baked art."""
    guids = project.get('guids') or {}
    rig_id = project.get('publishedRig') or project.get('rig')
    if guids.get('atlas:HR') and rig_id:
        try:
            rig = g.rig_info(rig_id)
            changed = _edit_prefab_atlases(rig['path'], guids['atlas:HR'], False)
            if rig['low'] and guids.get('atlas:LR'):
                changed = _edit_prefab_atlases(rig['low'], guids['atlas:LR'], False) or changed
            if changed:
                log('Took the atlas off the %s prefab' % rig_id)
        except StopIteration:
            pass
    atlas = 'Forge_%s_Atlas' % art
    removed = 0
    for folder, names in ((os.path.join(ASSETS, 'MonoBehaviour'), (atlas + '.asset', atlas + '_low.asset')),
                          (os.path.join(ASSETS, 'Material'), (atlas + '.mat', atlas + '_0.mat')),
                          (os.path.join(ASSETS, 'Texture2D'), (atlas + '_HR.png', atlas + '_LR.png'))):
        for name in names:
            for path in (os.path.join(folder, name), os.path.join(folder, name + '.meta')):
                if os.path.exists(path):
                    os.remove(path)
                    removed += 1
    baked = os.path.join(BAKED, art)
    if os.path.isdir(baked):
        shutil.rmtree(baked)
        removed += 1
    if os.path.exists(baked + '.meta'):
        os.remove(baked + '.meta')
    if removed:
        log('Deleted the atlas and baked art (%s)' % atlas)


# ------------------------------------------------------------------------------ abilities written by Claude

def claude_binary():
    found = shutil.which('claude')
    if found:
        return found
    candidates = sorted(glob.glob(os.path.expanduser('~/.vscode/extensions/anthropic.claude-code-*/resources/native-binary/claude')))
    return candidates[-1] if candidates else None


def combo_note(ability):
    if ability.get('type') != 'Combo':
        return ''
    cid = ability.get('comboId') or slugify(ability.get('name'))
    return f"""This is a COMBO MOVE (TargetType Combo). Keep its row on template ComboMove with AssetId "{cid}" (the combo id) and the no_focus parameter: in battle it only signs up, and at the end of the side's turn ComboMoveDirector resolves everyone who signed up into recipe rows named combo_<sorted combo ids joined with _> (combo_{cid} alone, combo_{cid}_<other id> with partners). Recipes are SkillBalancingData rows with TargetType Combo whose template subclasses ComboRecipe (read ComboRecipe.cs, ComboMove.cs and ComboSpin.cs first). Build the recipes the designer describes, with loca combo_<ids>_name and _desc. The move's own tooltip may use {{<recipe ids>.<param>}} and {{<recipe ids>.damage}}.

"""


def ability_prompt(project, ability):
    name_id = ability.get('nameId') or skill_name_id(project, ability)
    return f"""Create a battle ability for this game (decompiled Unity project, you are in its root).

Skill NameId: {name_id}
Type: {ability['type']}
Display name: {ability.get('name', '')}
Current tooltip text: {ability.get('desc', '')}
Starts from existing skill: {ability.get('source') or 'none'} (if set, copy that row under the new NameId; never change the original)
Combatant: {combatant_id(project)} "{project.get('name', '')}" on the {project['rig']} rig, {(project.get('stats') or {}).get('hp', '?')} HP, {(project.get('stats') or {}).get('attack', '?')} attack.

{combo_note(ability)}What the ability must do, in the designer's words:
{ability.get('design', '').strip()}

Do this:
1. In Assets/BalancingData/SkillBalancingData.json, add or update the row with NameId {name_id}. Prefer an existing SkillTemplateType in Assets/Scripts/Assembly-CSharp/ABH/GameDatas/Battle/Skills/ when one fits; otherwise write a new template there, matching the conventions of its neighbours. Keep LocaId, IconAssetId, IconAtlasId and any ui_color_rgb, anim_clip, anim_projectile or anim_ranged parameter as they are if the row exists; set LocaId to {name_id} and TargetType to {ability['type']} if not.
2. Set "{name_id}_desc" in Assets/BalancingData/Loca/live_English.json so the numbers use the template's {{value_N}} placeholders and the game's [ff0000]number[-] style. Keep the designer's wording otherwise. Also set "{name_id}_name".
   The tooltip must show the real number the ability uses. Damage is the computed amount, never a percent: "Deals [ff0000]{{value_1}}[-] damage", where the template's GetLocalizedDescription adds {{value_1}} as Convert.ToInt32(damage_in_percent / 100f * invoker.ModifiedAttack). The same goes for healing or any other amount based on a stat. Keep % only for things that really are percentages, such as a chance.
3. If you wrote or changed C#, keep the file's CRLF line endings and compile-check it.

Rules: edit the JSON in place with the Edit tool, keeping the rest of the file untouched. Do not encode the balancing container, do not edit prefabs or scenes, and don't commit: Combatant Forge encodes after you finish. Finish with two sentences saying what you built and anything the designer should test."""


def run_claude_ability(project, ability, log, register=None):
    binary = claude_binary()
    if not binary:
        raise ValueError('Claude Code was not found on PATH or in the VS Code extensions folder')
    ability['nameId'] = skill_name_id(project, ability)
    command = [binary, '-p', ability_prompt(project, ability), '--output-format', 'stream-json', '--verbose', '--permission-mode', 'acceptEdits',
               '--allowedTools', 'Read,Edit,Write,Grep,Glob,Bash(ls:*),Bash(grep:*),Bash(file:*)']
    log('Claude is writing "%s" (%s)' % (ability.get('name'), ability['nameId']))
    returncode, final = _stream_claude(command, log, register)
    row = read_row('SkillBalancingData', ability['nameId'])
    if returncode != 0 or not row or (final and final.get('is_error')):
        raise ValueError('Claude did not finish the ability (exit %s). Nothing was marked as written.' % returncode)
    return _finish_ability(project, ability, row, log)


def _stream_claude(command, log, register=None):
    """Runs Claude Code, logging what it says and which files it touches. Returns (exit code, final result event)."""
    proc = subprocess.Popen(command, cwd=g.ROOT, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if register:
        register(proc)
    final = None
    for line in proc.stdout:
        try:
            event = json.loads(line)
        except ValueError:
            continue
        if event.get('type') == 'assistant':
            for block in event.get('message', {}).get('content', []):
                if block.get('type') == 'text' and block['text'].strip():
                    log(block['text'].strip())
                elif block.get('type') == 'tool_use':
                    target = block.get('input', {}).get('file_path') or block.get('input', {}).get('pattern') or block.get('input', {}).get('command') or ''
                    log('  %s %s' % (block.get('name'), os.path.relpath(target, g.ROOT) if str(target).startswith('/') else target))
        elif event.get('type') == 'result':
            final = event
    proc.wait()
    return proc.returncode, final


def _finish_ability(project, ability, row, log):
    ability['written'] = True
    texts = g.loca_texts()
    ability['desc'] = texts.get(ability['nameId'] + '_desc', ability.get('desc', ''))
    ability['name'] = texts.get(ability['nameId'] + '_name', ability.get('name', ''))
    ability['template'] = row.get('SkillTemplateType')
    ability['params'] = row.get('SkillParameters')
    ability['duration'] = row.get('EffectDuration', 0)
    ability['values'] = g.template_values(row.get('SkillTemplateType', ''))
    ability.pop('paramEdits', None)  # Claude wrote its numbers anew
    ability.pop('durationEdit', None)
    log('Ability written with template %s. Press Upload to push it into the game.' % row.get('SkillTemplateType'))
    return project


# ------------------------------------------------------------------------------ combo recipes written by Claude

def recipe_id(ids):
    """The recipe row ComboMoveDirector looks for: combo_ + the combo ids sorted and joined with _."""
    return 'combo_' + '_'.join(sorted(i.lower() for i in ids))


def recipe_prompt(project, ability, recipe):
    rid = recipe_id(recipe['ids'])
    members = ', '.join(recipe['ids'])
    return f"""Create or update a COMBO RECIPE for this game (decompiled Unity project, you are in its root).

Recipe row NameId: {rid}
Combo moves in it (combo ids, a move's SkillBalancingData row has TargetType Combo, template ComboMove and its combo id as AssetId): {members}
It runs when exactly these moves signed up in the same side's turn ({'one pig on its own' if len(recipe['ids']) == 1 else str(len(recipe['ids'])) + ' pigs together'}). This recipe is for the combo move "{ability.get('name', '')}" (combo id {ability.get('comboId', '')}) of {combatant_id(project)} "{project.get('name', '')}" on the {project['rig']} rig.

What the combination must do, in the designer's words:
{recipe.get('design', '').strip()}

Do this:
1. Read Assets/Scripts/Assembly-CSharp/ABH/GameDatas/Battle/Skills/ComboRecipe.cs, ComboMove.cs and ComboSpin.cs first: they show how recipes perform (Perform(battle, comboers, runner)), share damage (damage_in_percent of the comboers' average ModifiedAttack) and stun.
2. In Assets/BalancingData/SkillBalancingData.json, add or update the row {rid}: TargetType Combo, a SkillTemplateType that subclasses ComboRecipe (write a new one in that folder if none fits, matching ComboSpin.cs), its numbers in SkillParameters, LocaId {rid}. Leave every other recipe and every combo move row as it is.
3. Set "{rid}_name" and "{rid}_desc" in Assets/BalancingData/Loca/live_English.json; the description may use {{param}} placeholders for the row's parameters.
4. If you wrote or changed C#, keep the file's CRLF line endings and compile-check it.

Rules: edit the JSON in place with the Edit tool, keeping the rest of the file untouched. Do not encode the balancing container, do not edit prefabs or scenes, and don't commit: Combatant Forge encodes on the next upload. Finish with two sentences saying what you built and what the designer should test."""


def run_claude_recipe(project, ability, recipe, log, register=None):
    binary = claude_binary()
    if not binary:
        raise ValueError('Claude Code was not found on PATH or in the VS Code extensions folder')
    if not recipe.get('ids'):
        raise ValueError('The recipe has no combo moves in it')
    rid = recipe_id(recipe['ids'])
    command = [binary, '-p', recipe_prompt(project, ability, recipe), '--output-format', 'stream-json', '--verbose', '--permission-mode', 'acceptEdits',
               '--allowedTools', 'Read,Edit,Write,Grep,Glob,Bash(ls:*),Bash(grep:*),Bash(file:*)']
    log('Claude is writing the recipe %s (%s)' % (rid, ' + '.join(recipe['ids'])))
    returncode, final = _stream_claude(command, log, register)
    row = read_row('SkillBalancingData', rid)
    if returncode != 0 or not row or (final and final.get('is_error')):
        raise ValueError('Claude did not finish the recipe (exit %s). Nothing was marked as written.' % returncode)
    recipe['written'] = True
    recipe['source'] = rid
    log('Recipe %s written with template %s. Upload or update the combatant to encode it.' % (rid, row.get('SkillTemplateType')))
    return project

