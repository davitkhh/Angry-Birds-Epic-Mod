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


def asset_base(project):
    # Lowercase slug: only the pirate variant may contain "Pirate", which is what SetPirateVariants looks for.
    return 'Forge_' + project['id'] + ('_Pirate' if project.get('variant') == 'pirate' else '')


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
    return project


# Fields only a job decides; everything else in the project may have been edited while the job ran.
JOB_FIELDS = ('guids', 'publishedAt', 'publishedRig')
JOB_ABILITY_FIELDS = ('nameId', 'written', 'template', 'params', 'duration', 'values')


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
    folder = os.path.join(BAKED, project['id'])
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
    atlas = 'Forge_%s_Atlas' % project['id']
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


def skill_name_id(project, ability):
    if ability.get('nameId'):
        return ability['nameId']
    kind = {'Attack': 'attack', 'Support': 'support', 'Passive': 'passive'}[ability['type']]
    base = 'pig_%s_forge_%s_%s' % (kind, project['id'], slugify(ability.get('name'))[:32])
    used = {a.get('nameId') for a in project['abilities']}
    name, n = base, 2
    while name in used:
        name, n = '%s_%d' % (base, n), n + 1
    return name


def build_skills(project, log):
    """Decides each ability's skill row. An untouched existing skill is referenced as is; a changed one becomes this
    combatant's own copy, so other pigs using the skill aren't affected. New abilities start as a stand-in until Claude
    has written them."""
    rows, texts, ids = [], {}, []
    catalog = {s['id']: s for s in g.skills_catalog()}
    for ability in project['abilities']:
        typ = ability['type']
        source = catalog.get(ability.get('source')) if ability.get('source') else None
        default_color = g.SKILL_COLORS[typ]
        custom_color = ability.get('color', default_color).upper().lstrip('#')
        if source and not ability.get('nameId'):
            unchanged = (ability.get('name') == source['name'] and ability.get('desc') == source['desc'] and custom_color == source['color']
                         and ability.get('icon') == source['icon'] and ability.get('atlas') == source['atlas'])
            if unchanged:
                ids.append((source['id'], typ))
                continue
        name_id = ability['nameId'] = skill_name_id(project, ability)
        existing = read_row('SkillBalancingData', name_id)
        if existing:
            row = dict(existing)
        elif source:
            row = dict(read_row('SkillBalancingData', source['id']))
        elif ability.get('row'):
            row = dict(ability['row'])  # came from an exported combatant
        else:
            row = {'AssetId': '', 'SkillParameters': {}, 'SkillTemplateType': '', 'EffectDuration': 0, 'EffectType': 'Curse', 'TargetingBehavior': 'None',
                   'TargetAlreadyAffectedTargets': True, 'TargetSelfPossible': False, 'EffectIconAtlasId': '', 'EffectIconAssetId': '', 'TargetCulling': None}
            row.update(json.loads(json.dumps(PLACEHOLDER[typ])))
            if typ != 'Passive':
                log('Ability "%s" is a stand-in (%s) until Claude has written it' % (ability.get('name'), row['SkillTemplateType']))
            else:
                log('Ability "%s" is a stand-in with no effect until Claude has written it' % ability.get('name'))
        params = dict(row.get('SkillParameters') or {})
        params.pop('ui_color_rgb', None)
        if custom_color != default_color:
            params['ui_color_rgb'] = float(int(custom_color, 16))
        row.update({'NameId': name_id, 'LocaId': name_id, 'SkillParameters': params, 'TargetType': typ, 'IconAssetId': ability.get('icon', ''), 'IconAtlasId': ability.get('atlas', '')})
        ordered = {k: row[k] for k in ('NameId', 'AssetId', 'LocaId', 'SkillParameters', 'SkillTemplateType', 'EffectDuration', 'TargetType', 'EffectType', 'TargetingBehavior',
                                       'TargetAlreadyAffectedTargets', 'TargetSelfPossible', 'IconAssetId', 'IconAtlasId', 'EffectIconAtlasId', 'EffectIconAssetId', 'TargetCulling') if k in row}
        rows.append(ordered)
        texts[name_id + '_name'] = ability.get('name', '')
        texts[name_id + '_desc'] = ability.get('desc', '')
        ids.append((name_id, typ))
    return rows, texts, ids


def combos_for(ids):
    supports = [i for i, t in ids if t == 'Support']
    attacks = [i for i, t in ids if t == 'Attack']
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


def _wait_log(pattern, since, timeout):
    end = time.time() + timeout
    while time.time() < end:
        if os.path.exists(EDITOR_LOG):
            with open(EDITOR_LOG, 'rb') as f:
                f.seek(since)
                if re.search(pattern.encode(), f.read()):
                    return True
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


def encode(writes, log):
    """writes: [(class name or 'Loca', path, text)]."""
    if unity_running():
        request = os.path.join(g.ROOT, 'Temp', 'EquipmentPresetRefresh.request')
        if os.path.isdir(os.path.dirname(request)):
            open(request, 'w').close()
            log('Asked the open Unity editor to import the new assets')
        missed = {}
        settled = {}  # log position once each kind's last file had its chance; a menu encode after it covers all of them
        for cls, path, text in writes:
            what = 'English text' if cls == 'Loca' else 'balancing data'
            done = LOCA_DONE if cls == 'Loca' else CONTAINER_DONE
            log('Encoding %s...' % cls)
            since = _log_size()
            write_in_place(path, text)
            if not _wait_log(done, since, WATCH_TIMEOUT):
                # The watcher can miss a write, for example during a script reload.
                since = _log_size()
                write_in_place(path, text)
                if not _wait_log(done, since, WATCH_TIMEOUT):
                    missed[what] = done
                    settled[what] = _log_size()
                    continue
            log('Encoded ' + cls)
            time.sleep(1.5)  # the watcher can fire twice; the next file must not race its second write
            settled[what] = _log_size()
        menus = {'balancing data': 'Balancing Data > Encode > Balancing container', 'English text': 'Balancing Data > Loca > Serialize all loca'}
        for what, done in missed.items():
            if not _wait_log(done, settled[what], 0.6):
                log('Unity did not encode the %s by itself. Run %s now. Waiting up to %d s; you can close this window meanwhile.' % (what, menus[what], MANUAL_TIMEOUT))
                if not _wait_log(done, settled[what], MANUAL_TIMEOUT):
                    log('WARNING: the %s was not encoded, so the game still has the old version. Run %s.' % (what, menus[what]))
                    continue
            log('Encoded the %s' % what)
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


# ------------------------------------------------------------------------------ the whole upload

def validate(project):
    problems = []
    if not project.get('id') or not re.fullmatch(r'[a-z0-9_]+', project['id']):
        problems.append('The combatant id must use lowercase letters, digits and underscores')
    if not project.get('name', '').strip():
        problems.append('The combatant needs a name')
    if not any(a['type'] in ('Attack', 'Support') for a in project.get('abilities', [])):
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
    for key, slot in project.get('slots', {}).items():
        name = base + GEAR_TEXTURE[key] if key in GEAR_TEXTURE else '%s__%s' % (base, key)
        layers = [l for l in slot.get('layers', []) if not l.get('hidden')]
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

    skill_rows, texts, ids = build_skills(project, log)
    pid = 'pig_forge_' + project['id']
    equipment, class_row, inventory = build_items(project, entries)
    stats = project.get('stats') or {}
    pig = {'NameId': pid, 'AssetId': project['rig'], 'LocaId': pid, 'DefaultInventoryNameId': inventory['NameId'], 'BaseHealth': int(stats.get('hp', 100)), 'BaseAttack': int(stats.get('attack', 10)),
           'PerLevelHealth': float(stats.get('perLevelHealth', 0)), 'PerLevelAttack': float(stats.get('perLevelAttack', 0)), 'SkillNameIds': [i for i, t in ids if t != 'Passive'], 'SkillCombos': combos_for(ids), 'SizeType': stats.get('sizeType', 'Medium'),
           'Faction': 'Pigs', 'LootTableDefeatBonus': stats.get('loot') or {}, 'SizeScale': float(stats.get('sizeScale', 1.0)), 'PigStrength': int(stats.get('strength', 5)),
           'PassiveSkillNameId': ','.join(i for i, t in ids if t == 'Passive'), 'IgnoreDifficulty': False}
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
    project['publishedAt'] = time.strftime('%Y-%m-%d %H:%M')
    log('Done. Place "%s" in a battle table to fight it.' % pid)
    return project


# ------------------------------------------------------------------------------ abilities written by Claude

def claude_binary():
    found = shutil.which('claude')
    if found:
        return found
    candidates = sorted(glob.glob(os.path.expanduser('~/.vscode/extensions/anthropic.claude-code-*/resources/native-binary/claude')))
    return candidates[-1] if candidates else None


def ability_prompt(project, ability):
    name_id = ability.get('nameId') or skill_name_id(project, ability)
    return f"""Create a battle ability for this game (decompiled Unity project, you are in its root).

Skill NameId: {name_id}
Type: {ability['type']}
Display name: {ability.get('name', '')}
Current tooltip text: {ability.get('desc', '')}
Starts from existing skill: {ability.get('source') or 'none'} (if set, copy that row under the new NameId; never change the original)
Combatant: pig_forge_{project['id']} "{project.get('name', '')}" on the {project['rig']} rig, {(project.get('stats') or {}).get('hp', '?')} HP, {(project.get('stats') or {}).get('attack', '?')} attack.

What the ability must do, in the designer's words:
{ability.get('design', '').strip()}

Do this:
1. In Assets/BalancingData/SkillBalancingData.json, add or update the row with NameId {name_id}. Prefer an existing SkillTemplateType in Assets/Scripts/Assembly-CSharp/ABH/GameDatas/Battle/Skills/ when one fits; otherwise write a new template there, matching the conventions of its neighbours. Keep LocaId, IconAssetId, IconAtlasId and any ui_color_rgb parameter as they are if the row exists; set LocaId to {name_id} and TargetType to {ability['type']} if not.
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
    row = read_row('SkillBalancingData', ability['nameId'])
    if proc.returncode != 0 or not row or (final and final.get('is_error')):
        raise ValueError('Claude did not finish the ability (exit %s). Nothing was marked as written.' % proc.returncode)
    ability['written'] = True
    texts = g.loca_texts()
    ability['desc'] = texts.get(ability['nameId'] + '_desc', ability.get('desc', ''))
    ability['name'] = texts.get(ability['nameId'] + '_name', ability.get('name', ''))
    ability['template'] = row.get('SkillTemplateType')
    ability['params'] = row.get('SkillParameters')
    ability['duration'] = row.get('EffectDuration', 0)
    ability['values'] = g.template_values(row.get('SkillTemplateType', ''))
    log('Ability written with template %s. Press Upload to push it into the game.' % row.get('SkillTemplateType'))
    return project
