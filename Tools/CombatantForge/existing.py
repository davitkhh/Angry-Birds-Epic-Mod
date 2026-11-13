"""The game's own combatants (PigBalancingData rows): view them, edit them, write the edits back, and duplicate them.

Variants are pigs whose NameId extends another pig's NameId (pig_x_bird3 is a variant of pig_x). Editing a pig also
edits its ticked variants: each variant gets the same change on top of its own differences.

Rows other pigs still use (inventories, items, skills, names) are never changed in place. The receiving pigs get
their own copies, named after the edited pig: <nameId>__e<rev>. The revision goes up when a pig outside the
receiving set (say, a variant that was unticked) still uses the current copies, so it keeps them as they are.
"""
import copy
import glob
import hashlib
import json
import os
import re
import time

import gamedata as g
import publish as p

DIR = None  # set by forge.py: <projects>/existing
GROUPS = {'class': ('Headgear', 'Headgear_Back'), 'main': ('MainHand', 'MainHand_Arrow'), 'off': ('OffHand', 'OffHand_Back')}
ITEM_TYPE = {'main': 'MainHandEquipment', 'off': 'OffHandEquipment'}
RATIO_STATS = ('hp', 'attack', 'sizeScale')
COPY_STATS = ('sizeType', 'strength', 'perLevelHealth', 'perLevelAttack', 'loot')
WEAPON_FIELDS = ('animationIndex', 'isRanged', 'projectile', 'hitEffect', 'directProjectile')


# ------------------------------------------------------------------------------ game tables

def _table(name):
    return {r['NameId']: r for r in json.load(open(os.path.join(g.BALANCING, name + '.json'), encoding='utf-8'))}


class Game:
    def __init__(self):
        self.pigs = _table('PigBalancingData')
        self.inventories = _table('InventoryBalancingData')
        self.equipment = _table('EquipmentBalancingData')
        self.classes = _table('ClassItemBalancingData')
        self.skills = _table('SkillBalancingData')
        self.catalog = {s['id']: s for s in g.skills_catalog()}
        self.texts = g.loca_texts()
        self.rigs = {r['id'] for r in g.rig_list()}
        self.parent = parents(self.pigs)

    def name(self, row):
        return self.texts.get(row.get('LocaId', '') + '_name') or row['NameId']

    def content(self, row):
        inv = self.inventories.get(row.get('DefaultInventoryNameId') or '')
        return dict((inv or {}).get('DefaultInventoryContent') or {})

    def items(self, row):
        """{'class' | 'main' | 'off': item row} from the pig's inventory."""
        out = {}
        for item_id in self.content(row):
            kind = self.kind_of(item_id)
            if kind and kind not in out:
                out[kind] = self.classes.get(item_id) or self.equipment.get(item_id)
        return out

    def kind_of(self, item_id):
        if item_id in self.classes:
            return 'class'
        eq = self.equipment.get(item_id)
        if eq:
            return {'MainHandEquipment': 'main', 'OffHandEquipment': 'off'}.get(eq.get('ItemType'))
        return None

    def skill_ids(self, row):
        actives = list(row.get('SkillNameIds') or [])
        passives = [x for x in (row.get('PassiveSkillNameId') or '').split(',') if x]
        return actives, passives

    def descendants(self, name_id):
        kids = {}
        for child, par in self.parent.items():
            kids.setdefault(par, []).append(child)
        out, todo = [], sorted(kids.get(name_id, []))
        while todo:
            c = todo.pop(0)
            out.append(c)
            todo[0:0] = sorted(kids.get(c, []))
        return out


_cached = (None, None)
TABLES = ('PigBalancingData', 'InventoryBalancingData', 'EquipmentBalancingData', 'ClassItemBalancingData', 'SkillBalancingData', os.path.join('Loca', 'live_English'))


def game():
    """A Game shared between requests until one of its files changes (the list and its tiles)."""
    global _cached
    stamp = tuple(os.path.getmtime(os.path.join(g.BALANCING, t + '.json')) for t in TABLES)
    if _cached[0] != stamp:
        _cached = (stamp, Game())
    return _cached[1]


def icon(name_id):
    """The pig's tile picture: its rig wearing its own equipment, in the first Idle pose."""
    G = game()
    row = G.pigs[name_id]
    if row['AssetId'] not in G.rigs:
        raise ValueError('no Forge rig for ' + row['AssetId'])
    slots, variant = _art(G, row)
    # Forge art is rebaked under the same texture names, so its atlas's age is part of the look.
    look = hashlib.sha1(json.dumps([slots, variant, forge_art_stamp(G, row)], sort_keys=True).encode()).hexdigest()[:10]
    path = os.path.join(g.CACHE, 'icons', 'pig_%s_%s_%s.%d.png' % (row['AssetId'], name_id, look, g.CACHE_VERSION))
    return g.rig_icon(row['AssetId'], variant, slots, path)


def forge_art_stamp(G, row):
    """When the pig wears Forge-made art: the newest Forge atlas's time, so a rebake shows. Else 0."""
    if not any((item or {}).get('AssetBaseId', '').startswith('Forge_') for item in G.items(row).values()):
        return 0
    stamps = [os.path.getmtime(p) for p in glob.glob(os.path.join(g.ASSETS, 'Texture2D', 'Forge_*_Atlas_HR.png'))]
    return round(max(stamps), 1) if stamps else 0


def look_version(G, row):
    """Changes whenever the pig's tile picture can: its rig, its gear rows and the Forge art they use."""
    inv = G.inventories.get(row.get('DefaultInventoryNameId') or '')
    items = sorted((G.items(row) or {}).items())
    return hashlib.sha1(json.dumps([row.get('AssetId'), inv, items, forge_art_stamp(G, row)], sort_keys=True, default=str).encode()).hexdigest()[:10]


def parents(pigs):
    """child NameId -> nearest pig whose NameId it extends."""
    out = {}
    for c in pigs:
        parts = c.split('_')
        for k in range(len(parts) - 1, 1, -1):
            par = '_'.join(parts[:k])
            if par in pigs:
                out[c] = par
                break
    return out


def root_of(name_id, parent):
    while name_id in parent:
        name_id = parent[name_id]
    return name_id


# ------------------------------------------------------------------------------ saved projects and ticks

def project_path(name_id):
    if not re.fullmatch(r'[a-z0-9_]+', name_id or ''):
        raise ValueError('bad combatant id')
    return os.path.join(DIR, name_id + '.json')


def load_saved(name_id):
    try:
        return json.load(open(project_path(name_id), encoding='utf-8'))
    except FileNotFoundError:
        return None


def save(project):
    os.makedirs(DIR, exist_ok=True)
    path = project_path(project['id'])
    with open(path + '.tmp', 'w', encoding='utf-8') as f:
        json.dump(project, f, indent=1, ensure_ascii=False)
    os.replace(path + '.tmp', path)


def _ticks_path():
    return os.path.join(DIR, '_unticked.json')


def unticked():
    """{edited pig: [variants that don't receive its edits]}"""
    try:
        return json.load(open(_ticks_path(), encoding='utf-8'))
    except (OSError, ValueError):
        return {}


def set_tick(parent, variant, on):
    data = unticked()
    off = set(data.get(parent, []))
    if on:
        off.discard(variant)
    else:
        off.add(variant)
    if off:
        data[parent] = sorted(off)
    else:
        data.pop(parent, None)
    os.makedirs(DIR, exist_ok=True)
    with open(_ticks_path(), 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=1)
    return data


# ------------------------------------------------------------------------------ a pig as a Forge project

_sprites = {}


def rig_sprites(rig_id):
    """Texture name -> sprite, from every atlas on the rig's prefab, the Forge ones included."""
    if rig_id in _sprites:
        return _sprites[rig_id]
    text = g.read_text(g.rig_info(rig_id)['path'])
    i = text.index('  textureAtlases:')
    j = text.index('  mAnimationClips:', i)
    out = {}
    for guid in re.findall(r'guid: (\w+)', text[i:j]):
        atlas = g.parse_atlas(guid)
        if atlas:
            for name, s in atlas['sprites'].items():
                out.setdefault(name, s)
    _sprites[rig_id] = out
    return out


def forget_art(rig_id, atlas_guids):
    """After an atlas is rewritten: drop what was read from it before."""
    _sprites.pop(rig_id, None)
    icons = os.path.join(g.CACHE, 'icons')
    if os.path.isdir(icons):
        for name in os.listdir(icons):
            if name.startswith('pig_%s_' % rig_id):
                os.remove(os.path.join(icons, name))
    for guid in atlas_guids:
        g._atlases.pop(guid, None)
        folder = os.path.join(g.CACHE, 'sprites')
        if os.path.isdir(folder):
            for name in os.listdir(folder):
                if name.startswith(guid + '_'):
                    os.remove(os.path.join(folder, name))


def _layer(key, name, s, item):
    return {'uid': 'g' + hashlib.md5(key.encode()).hexdigest()[:7], 'src': '%s:%s' % (s['atlas'], name), 'name': name, 'item': item,
            'w': s['size'][0], 'h': s['size'][1], 'pivot': list(s['pivot']), 'tx': 0, 'ty': 0, 'rot': 0, 'sx': 1, 'sy': 1, 'shear': 0}


def _art(G, row):
    """(slots, variant) as the game draws this pig."""
    rig_id = row['AssetId']
    if rig_id not in G.rigs:
        return {}, ''
    rig = json.loads(g.rig_json(rig_id))
    kinds = {s['key']: s['kind'] for s in rig['slots']}
    sprites = rig_sprites(rig_id)
    items = G.items(row)
    slots = {}

    def put(key, name, item, pair=None):
        if key not in kinds:
            return False
        s = sprites.get(name)
        if not s:
            return False
        if kinds[key] == 'feature' and s['size'][0] <= 2 and s['size'][1] <= 2:
            slots[key] = {'layers': [], 'hidden': True}  # a blanked feature
            return True
        layer = _layer(key, name, s, item)
        if pair:
            layer['pair'] = pair
        slots[key] = {'layers': [layer], 'hidden': False, 'active': layer['uid']}
        return True

    variant = ''
    cls = items.get('class')
    if cls and cls.get('AssetBaseId'):
        base = cls['AssetBaseId'].replace('_Ancient', '')
        front = base if base in sprites else base + '_1' if base + '_1' in sprites else base
        pair = 'g_class' if front + '_Back' in sprites else None
        put('Headgear', front, base, pair)
        put('Headgear_Back', front + '_Back', base, pair)
        for key, kind in kinds.items():
            if kind == 'feature':
                put(key, base + '__' + key, base)
        if ('Pirate' in base or 'RedBeard' in base) and rig['variants'].get('pirate'):
            variant = 'pirate'
        elif base.startswith('Forge_') and base.rsplit('_', 1)[-1][:1].isupper():
            # Forge_x_Regular etc. (CharacterAssetController.VariantSuffix)
            tail = base.rsplit('_', 1)[-1].lower()
            if rig['variants'].get(tail) or tail in rig.get('looks', {}):
                variant = tail
    # Pigs the game always draws with a look (SpringDrone's pig list).
    variant = g.look_of_pig(rig_id, row['NameId']) or variant
    for group in ('main', 'off'):
        item = items.get(group)
        if item and item.get('AssetBaseId'):
            base = item['AssetBaseId']
            front, back = GROUPS[group]
            pair = 'g_' + group if group == 'off' and base + '_Back' in sprites else None
            put(front, base, base, pair)
            put(back, base + ('_Arrow' if group == 'main' else '_Back'), base, pair)
    return slots, variant


def _weapon(G, row):
    main = G.items(row).get('main') or {}
    return {'animationIndex': main.get('AnimationIndex', 1), 'isRanged': bool(main.get('IsRanged', False)), 'projectile': main.get('ProjectileAssetId', '') or '',
            'hitEffect': main.get('HitEffectSuffix', '') or '', 'directProjectile': bool(main.get('DirectProjectileAssetAndLoca', False))}


def _stats(row):
    return {'hp': row.get('BaseHealth', 100), 'attack': row.get('BaseAttack', 10), 'sizeType': row.get('SizeType', 'Medium'), 'sizeScale': row.get('SizeScale', 1.0),
            'strength': row.get('PigStrength', 5), 'perLevelHealth': row.get('PerLevelHealth', 0), 'perLevelAttack': row.get('PerLevelAttack', 0),
            'loot': copy.deepcopy(row.get('LootTableDefeatBonus') or {})}


def _ability(s, uid):
    return {'uid': uid, 'origin': s['id'], 'type': s['type'], 'source': s['id'], 'name': s['name'], 'desc': s['desc'], 'color': s['color'], 'atlas': s['atlas'],
            'icon': s['icon'], 'params': s['params'], 'values': s['values'], 'template': s['template'], 'duration': s['duration'], 'design': '',
            **({'comboId': (s.get('asset') or '').lower()} if s['type'] == 'Combo' else {})}


def _abilities(G, row):
    """(abilities, skills the Forge can't show, kept as they are)."""
    actives, passives = G.skill_ids(row)
    out, keep = [], []
    for i, sid in enumerate(actives + passives):
        s = G.catalog.get(sid)
        if s:
            out.append(_ability(s, 'a%d_%s' % (i, hashlib.md5(sid.encode()).hexdigest()[:5])))
        else:
            keep.append({'id': sid, 'passive': i >= len(actives)})
    return out, keep


def fingerprint(G, name_id):
    row = G.pigs[name_id]
    inv = G.inventories.get(row.get('DefaultInventoryNameId') or '')
    items = [G.classes.get(i) or G.equipment.get(i) for i in G.content(row)]
    actives, passives = G.skill_ids(row)
    skills = [G.skills.get(s) for s in actives + passives]
    texts = [G.texts.get(s + '_name') for s in actives + passives] + [G.texts.get(s + '_desc') for s in actives + passives]
    blob = json.dumps([row, inv, items, skills, texts, G.name(row)], sort_keys=True, ensure_ascii=False)
    return hashlib.sha1(blob.encode()).hexdigest()


SNAPSHOT_FIELDS = ('name', 'rig', 'variant', 'slots', 'weapon', 'stats', 'abilities')


def snapshot(project):
    return copy.deepcopy({k: project.get(k) for k in SNAPSHOT_FIELDS})


def state(G, name_id):
    """The pig as the game has it now, as an existing-combatant project."""
    row = G.pigs[name_id]
    slots, variant = _art(G, row)
    abilities, keep = _abilities(G, row)
    project = {'kind': 'existing', 'id': name_id, 'name': G.name(row), 'rig': row['AssetId'], 'variant': variant, 'slots': slots, 'abilities': abilities,
               'weapon': _weapon(G, row), 'stats': _stats(row), 'statsEdited': True,
               'existing': {'nameId': name_id, 'fingerprint': fingerprint(G, name_id), 'rev': 0, 'keepSkills': keep, 'combos': copy.deepcopy(row.get('SkillCombos') or [])}}
    project['existing']['original'] = snapshot(project)
    return project


def _promote_kept(saved, fresh):
    """Skills saved as kept-as-is because the Forge couldn't show them then (combo moves before the Combo type) become
    abilities again, at their place in the game's order, in both the edits and the 'as the game has it' snapshot."""
    ex = saved.get('existing') or {}
    keep = ex.get('keepSkills') or []
    known = {a['origin']: a for a in fresh['abilities']}
    promote = [k['id'] for k in keep if k['id'] in known]
    if not promote:
        return saved
    order = [a['origin'] for a in fresh['abilities']]
    for target in (saved['abilities'], (ex.get('original') or {}).get('abilities')):
        if target is None:
            continue
        for sid in promote:
            if any(a.get('origin') == sid for a in target):
                continue
            # after the last ability that comes before it in the game's order
            before = set(order[:order.index(sid)])
            at = max((i + 1 for i, a in enumerate(target) if a.get('origin') in before), default=0)
            target.insert(at, copy.deepcopy(known[sid]))
    ex['keepSkills'] = [k for k in keep if k['id'] not in promote]
    return saved


def open_project(name_id):
    """The saved edits if the game still matches what they were made on, else the pig as the game has it now."""
    G = Game()
    if name_id not in G.pigs:
        raise ValueError('The game has no combatant ' + name_id)
    if G.pigs[name_id]['AssetId'] not in G.rigs:
        raise ValueError('The Forge can\'t draw the %s base yet, so %s can\'t be opened' % (G.pigs[name_id]['AssetId'], name_id))
    saved = load_saved(name_id)
    fresh = state(G, name_id)
    if saved and saved.get('existing', {}).get('fingerprint') == fresh['existing']['fingerprint']:
        return p.migrate_project(_promote_kept(saved, fresh)), None
    note = None
    if saved:
        # Keep what names its copies and atlas, so a rebuild reuses them.
        for key in ('guids', 'publishedAt', 'publishedRig'):
            if key in saved:
                fresh[key] = saved[key]
        fresh['existing']['rev'] = saved.get('existing', {}).get('rev', 0)
        if snapshot(saved) != saved.get('existing', {}).get('original'):
            note = 'The game\'s data for this combatant changed since you last edited it, so it was reloaded and your unsaved edits were dropped'
    return fresh, note


def catalog():
    G = game()
    saved = {}
    if os.path.isdir(DIR):
        for name in os.listdir(DIR):
            if name.endswith('.json') and not name.startswith('_'):
                try:
                    d = json.load(open(os.path.join(DIR, name), encoding='utf-8'))
                    saved[d['id']] = d.get('publishedAt')
                except (OSError, ValueError, KeyError):
                    pass
    out = []
    mine = added()
    for name_id, row in G.pigs.items():
        cls = G.items(row).get('class') or {}
        base = cls.get('AssetBaseId', '')
        parent = G.parent.get(name_id)
        depth = 0
        x = name_id
        while x in G.parent:
            x, depth = G.parent[x], depth + 1
        out.append({'id': name_id, 'name': G.name(row), 'rig': row['AssetId'], 'pirate': 'Pirate' in base or 'RedBeard' in base,
                    'hp': row.get('BaseHealth'), 'attack': row.get('BaseAttack'), 'parent': parent, 'root': root_of(name_id, G.parent), 'depth': depth,
                    'supported': row['AssetId'] in G.rigs, 'updatedAt': saved.get(name_id), 'look': look_version(G, row),
                    'deletable': name_id in mine})
    return {'pigs': out, 'unticked': unticked()}


# ------------------------------------------------------------------------------ adding a variant

def variant_presets():
    """[{suffix, hp, attack, scale, count}]: the median stat ratios of the game's variants per suffix (bird3 0.83 health,
    boss 2.17, ...), for suffixes used at least twice, most used first. birdN suffixes are always listed."""
    import statistics
    G = game()
    found = {}
    for child, parent in G.parent.items():
        a, b = G.pigs[child], G.pigs[parent]
        if not (b.get('BaseHealth') and b.get('BaseAttack')):
            continue
        r = found.setdefault(child[len(parent) + 1:], ([], [], []))
        r[0].append(a.get('BaseHealth', 0) / b['BaseHealth'])
        r[1].append(a.get('BaseAttack', 0) / b['BaseAttack'])
        r[2].append((a.get('SizeScale') or 1) / (b.get('SizeScale') or 1))
    out = [{'suffix': s, 'hp': round(statistics.median(h), 2), 'attack': round(statistics.median(at), 2), 'scale': round(statistics.median(sc), 2), 'count': len(h)}
           for s, (h, at, sc) in found.items() if len(h) >= 2 or re.fullmatch(r'bird\d+', s)]
    return sorted(out, key=lambda x: (not re.fullmatch(r'bird\d+', x['suffix']), -x['count'], x['suffix']))


def add_variant(parent_id, suffix, log, ratios=None):
    """A new pig <parent>_<suffix>, a copy of the parent's row as the game has it now (same name, inventory and
    abilities), so it shows as the parent's variant and gets its ticked edits. ratios {hp, attack, scale} scale its
    stats from the parent's. Returns its NameId."""
    suffix = (suffix or '').strip().lower()
    if not re.fullmatch(r'[a-z0-9][a-z0-9_]*', suffix):
        raise ValueError('Use lowercase letters, digits and underscores for the variant')
    G = Game()
    if parent_id not in G.pigs:
        raise ValueError('The game has no combatant ' + parent_id)
    name_id = '%s_%s' % (parent_id, suffix)
    if name_id in G.pigs:
        raise ValueError(name_id + ' already exists')
    row = copy.deepcopy(G.pigs[parent_id])
    row['NameId'] = name_id
    ratios = ratios or {}
    for key, column, kind in (('hp', 'BaseHealth', int), ('attack', 'BaseAttack', int), ('scale', 'SizeScale', float)):
        r = float(ratios.get(key, 1) or 1)
        if r <= 0:
            raise ValueError('Ratios must be above 0')
        if abs(r - 1) > 1e-9 and row.get(column) is not None:
            value = row[column] * r
            row[column] = max(1, int(round(value))) if kind is int else round(value, 3)
            log('%s: %s x %g = %s' % (column, G.pigs[parent_id][column], r, row[column]))
    path = os.path.join(g.BALANCING, 'PigBalancingData.json')
    text = p.upsert_row(open(path, encoding='utf-8', newline='').read(), row)
    log('Adding %s, a copy of %s' % (name_id, parent_id))
    p.encode([('PigBalancingData', path, text)], log)
    _set_added(name_id, True)
    log('Done. Open it to make it different; edits to %s reach it while it is ticked.' % parent_id)
    return name_id


# ------------------------------------------------------------------------------ deleting an added variant

def _added_path():
    return os.path.join(DIR, '_added.json')


def added():
    """NameIds of the variants the Forge added (only those can be deleted: the game's own pigs stay)."""
    try:
        return set(json.load(open(_added_path(), encoding='utf-8')))
    except (OSError, ValueError):
        return set()


def _set_added(name_id, on):
    ids = added()
    (ids.add if on else ids.discard)(name_id)
    os.makedirs(DIR, exist_ok=True)
    with open(_added_path(), 'w', encoding='utf-8') as f:
        json.dump(sorted(ids), f, indent=1)


def _code_references(name_id):
    """C# files that name this pig in a string (scripted battles, registries, ...)."""
    out = []
    for path in glob.glob(os.path.join(g.ASSETS, 'Scripts', '**', '*.cs'), recursive=True):
        try:
            if '"%s"' % name_id in open(path, encoding='utf-8', errors='replace').read():
                out.append(os.path.basename(path))
        except OSError:
            pass
    return out


def delete_variant(name_id, log):
    """Takes a variant the Forge added out of the game: its pig row, plus the Forge-made rows only it used (its own
    inventory, items, abilities and name copies, and its atlas). Refuses while a battle, another row, the code or a
    variant of its own still uses it."""
    G = Game()
    if name_id not in added():
        raise ValueError('Only variants added in the Forge can be deleted; %s is one of the game\'s own combatants' % name_id)
    if name_id not in G.pigs:
        log('%s is not in the game any more' % name_id)
    else:
        kids = G.descendants(name_id)
        if kids:
            raise ValueError('Not deleted: delete its own variants first (%s)' % ', '.join(kids))
        used = p.references([name_id], {name_id})
        code = _code_references(name_id)
        if used or code:
            raise ValueError('Not deleted: the game still uses it in ' + ', '.join((used.get(name_id) or [])[:6] + code[:3]))
        row = G.pigs[name_id]
        gone = {name_id}
        forge = lambda i: bool(i) and 'forge_' in i
        unused = lambda i: not p.references([i], gone).get(i)

        # What only it used, among the Forge-made rows: those go with it.
        inv = row.get('DefaultInventoryNameId') or ''
        actives, passives = G.skill_ids(row)
        skills = [s for s in actives + passives if forge(s) and s in G.skills and unused(s)]
        gone.update(skills)
        invs = [inv] if forge(inv) and inv in G.inventories and unused(inv) else []
        gone.update(invs)
        items = [i for i in ((G.inventories[inv].get('DefaultInventoryContent') or {}) if invs else {})
                 if forge(i) and (i in G.equipment or i in G.classes) and unused(i)]
        gone.update(items)
        loca = row.get('LocaId') or ''
        texts = [loca + '_name'] if forge(loca) and not any(r.get('LocaId') == loca for n, r in G.pigs.items() if n != name_id) else []
        texts += [s + x for s in skills for x in ('_name', '_desc')]

        writes = []
        for file_name, ids in (('SkillBalancingData', skills), ('EquipmentBalancingData', [i for i in items if i in G.equipment]),
                               ('ClassItemBalancingData', [i for i in items if i in G.classes]),
                               ('InventoryBalancingData', invs), ('PigBalancingData', [name_id])):
            if ids:
                path = os.path.join(g.BALANCING, file_name + '.json')
                text = open(path, encoding='utf-8', newline='').read()
                for i in ids:
                    text = p.remove_row(text, i)
                writes.append((file_name, path, text))
        if texts:
            loca_path, loca_text = p.remove_loca(texts)
            writes.append(('Loca', loca_path, loca_text))
        for what, ids in (('abilities', skills), ('inventory', invs), ('items', items)):
            if ids:
                log('Also removing its %s: %s' % (what, ', '.join(ids)))
        log('Removing %s' % name_id)
        p.encode(writes, log)

        # Its own art (from updating it on its own): the atlas goes once no item left draws from it.
        saved = None
        try:
            saved = json.load(open(project_path(name_id), encoding='utf-8'))
        except (OSError, ValueError):
            pass
        if saved and (saved.get('guids') or {}).get('atlas:HR'):
            G = Game()
            rows = list(G.equipment.values()) + list(G.classes.values())
            for rev in range(saved.get('existing', {}).get('rev', 0) + 1):
                art = art_id(name_id, rev)
                if not any(('Forge_%s_' % art) in (r.get('AssetBaseId') or '') or r.get('AssetBaseId') == 'Forge_' + art for r in rows):
                    p.remove_atlas(saved, art, log)
                    forget_art(saved.get('publishedRig') or saved.get('rig'), [x for x in saved['guids'].values() if x])

    # The Forge's own records of it.
    if os.path.exists(project_path(name_id)):
        folder = os.path.join(os.path.dirname(DIR), '.trash')
        os.makedirs(folder, exist_ok=True)
        os.replace(project_path(name_id), os.path.join(folder, 'existing-%s-%s.json' % (name_id, time.strftime('%Y%m%d-%H%M%S'))))
        log('Moved its Forge project to projects/.trash')
    data = unticked()
    data.pop(name_id, None)
    for parent in list(data):
        data[parent] = [v for v in data[parent] if v != name_id]
        if not data[parent]:
            data.pop(parent)
    with open(_ticks_path(), 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=1)
    _set_added(name_id, False)
    log('Deleted %s.' % name_id)


# ------------------------------------------------------------------------------ duplicating

def duplicate(name_id, taken_ids):
    """A new Forge combatant that starts as a copy of this pig. Nothing is written to the game until it's uploaded."""
    G = Game()
    if G.pigs.get(name_id, {}).get('AssetId') not in G.rigs:
        raise ValueError('The Forge can\'t draw this combatant\'s base yet')
    st = state(G, name_id)
    base = p.slugify(st['name']) + '_copy'
    new_id, n = base, 2
    while new_id in taken_ids:
        new_id, n = '%s_%d' % (base, n), n + 1
    abilities = []
    for a in st['abilities']:
        a = dict(a)
        a.pop('origin', None)
        a['uid'] = p.uuid.uuid4().hex[:8]
        abilities.append(a)
    for slot in st['slots'].values():
        for layer in slot['layers']:
            layer['uid'] = p.uuid.uuid4().hex[:8]
            if layer.get('pair'):
                layer['pair'] = layer['pair'] + '_' + new_id
        if slot['layers']:
            slot['active'] = slot['layers'][-1]['uid']
    return {'id': new_id, 'name': st['name'] + ' copy', 'rig': st['rig'], 'variant': st['variant'], 'slots': st['slots'], 'abilities': abilities,
            'weapon': st['weapon'], 'stats': st['stats'], 'statsEdited': True, 'duplicateOf': name_id,
            'combos': st['existing']['combos'], 'keepSkills': st['existing']['keepSkills']}


# ------------------------------------------------------------------------------ what changed

def _norm_slot(slot):
    if not slot:
        return None
    layers = [{k: v for k, v in l.items() if k not in ('uid', 'pair')} for l in slot.get('layers', [])]
    hidden = bool(slot.get('hidden'))
    if not layers and not hidden:
        return None
    return {'layers': layers, 'hidden': hidden}


def _group_slots(rig_slots, group):
    if group == 'class':
        return list(GROUPS['class']) + [k for k, kind in rig_slots.items() if kind == 'feature']
    return list(GROUPS[group])


def diff(original, project):
    keys = set(original['slots']) | set(project['slots'])
    return {
        'name': original['name'] != project['name'],
        'rig': original['rig'] != project['rig'],
        'variant': original['variant'] != project['variant'],
        'slots': sorted(k for k in keys if _norm_slot(original['slots'].get(k)) != _norm_slot(project['slots'].get(k))),
        'weapon': [f for f in WEAPON_FIELDS if original['weapon'].get(f) != project['weapon'].get(f)],
        'stats': [f for f in RATIO_STATS + COPY_STATS if original['stats'].get(f) != project['stats'].get(f)],
    }


def _apply(d, original, project, x):
    """Variant x (a fresh state) with the edited pig's changes on top."""
    x = copy.deepcopy(x)
    if d['name'] and x['name'] == original['name']:
        x['name'] = project['name']
    if d['rig'] and x['rig'] == original['rig']:
        x['rig'] = project['rig']
        x['slots'] = copy.deepcopy(project['slots'])
    if d['variant']:
        x['variant'] = project['variant']
    for key in d['slots']:
        if key in project['slots']:
            x['slots'][key] = copy.deepcopy(project['slots'][key])
        else:
            x['slots'].pop(key, None)
    for f in d['weapon']:
        x['weapon'][f] = project['weapon'][f]
    for f in d['stats']:
        new, old = project['stats'][f], original['stats'][f]
        if f in RATIO_STATS and old and x['stats'].get(f) is not None:
            value = x['stats'][f] * new / old
            x['stats'][f] = round(value, 3) if f == 'sizeScale' else int(round(value))
        else:
            x['stats'][f] = copy.deepcopy(new)
    return x


def remap_combos(combos, mapping, removed, added, actives):
    """The game's turn plan with renamed abilities swapped in and removed ones taken out. None when it can't be kept:
    an ability was added, or nothing usable is left."""
    if added:
        return None
    out = []
    for c in combos or []:
        chain = [mapping.get(i, i) for i in c.get('ComboChain', []) if i not in removed]
        chain = [i for i in chain if i in actives]
        if chain:
            out.append({'Percentage': c.get('Percentage', 0), 'ComboChain': chain})
    if not out:
        return None
    total = sum(c['Percentage'] for c in out)
    old_total = sum(c.get('Percentage', 0) for c in combos)
    if total and abs(total - old_total) > 1e-6:
        for c in out:
            c['Percentage'] = round(c['Percentage'] * old_total / total, 2)
    return out


def _combos(row_combos, mapping, removed, added, actives, typed):
    kept = remap_combos(row_combos, mapping, removed, added, actives)
    return kept if kept is not None else p.combos_for(typed)


# ------------------------------------------------------------------------------ writing the edits back

def art_id(name_id, rev):
    return '%s__e%d' % (name_id, rev)


def _refs(row):
    out = [row.get('DefaultInventoryNameId') or '', row.get('LocaId') or '']
    out += list(row.get('SkillNameIds') or [])
    out += [x for x in (row.get('PassiveSkillNameId') or '').split(',') if x]
    for c in row.get('SkillCombos') or []:
        out += c.get('ComboChain', [])
    return out


def _uses(row, art):
    tag = art + '_'
    return any(tag in ref for ref in _refs(row))


def receivers_of(G, name_id):
    off = set(unticked().get(name_id, []))
    return [name_id] + [v for v in G.descendants(name_id) if v not in off]


def update(project, log):
    """Writes an edited existing combatant, and the same edits to its ticked variants, back into the game."""
    problems = p.validate(project)
    if problems:
        raise ValueError('; '.join(problems))
    G = Game()
    name_id = project['id']
    if name_id not in G.pigs:
        raise ValueError('The game has no combatant ' + name_id)
    ex = project['existing']
    if fingerprint(G, name_id) != ex.get('fingerprint'):
        raise ValueError('The game\'s data for this combatant changed since it was opened. Open it again from View existing, then redo the edit')
    original = ex['original']
    d = diff(original, project)
    receivers = receivers_of(G, name_id)
    variants = receivers[1:]

    # A copy that a pig outside the receivers still uses stays as it is: move to the next revision.
    rev = ex.get('rev', 0)
    outsiders = [r for n, r in G.pigs.items() if n not in receivers]
    while any(_uses(r, art_id(name_id, rev)) for r in outsiders):
        rev += 1
    if rev != ex.get('rev', 0):
        log('Some pigs outside this update still use the copies from the last one, so they keep them: this update makes new copies (revision %d)' % rev)
        project['guids'] = {}
        project.pop('publishedRig', None)
        for a in project['abilities']:
            if a.get('nameId'):
                row = G.skills.get(a['nameId'])
                if row:
                    a['row'] = copy.deepcopy(row)
                a.pop('nameId')
        ex['rev'] = rev
    art = art_id(name_id, rev)
    owned_tag = art + '_'

    # Abilities: the edited pig's own list decides; variants get the same swaps.
    skill_rows, texts, ids = p.build_skills(project, log)
    by_uid = {a['uid']: ids[i][0] for i, a in enumerate(project['abilities'])}
    origins = {a['uid']: a['origin'] for a in original['abilities'] if a.get('origin')}
    mapping = {o: by_uid[u] for u, o in origins.items() if u in by_uid}
    removed = {o for u, o in origins.items() if u not in by_uid}
    added = [(by_uid[a['uid']], a['type']) for a in project['abilities'] if a['uid'] not in origins]
    type_of = {i: t for i, t in ids}
    for sid, s in G.catalog.items():
        type_of.setdefault(sid, s['type'])

    # Everyone's new state.
    states = {name_id: project}
    fresh = {name_id: original}
    for v in variants:
        st = state(G, v)
        fresh[v] = snapshot(st)
        states[v] = _apply(d, original, project, st)
    rig_kinds = {}

    def kinds(rig_id):
        if rig_id not in rig_kinds:
            rig_kinds[rig_id] = {s['key']: s['kind'] for s in json.loads(g.rig_json(rig_id))['slots']} if rig_id in G.rigs else {}
        return rig_kinds[rig_id]

    # Art: a group (headgear and features, main hand, off hand) gets a new item when its art changed, or when the
    # pig already wears this combatant's copy (those are all rebuilt, so nothing points at a stale texture).
    entries = {}
    contents = {'class': {}, 'main': {}, 'off': {}}
    equipment_rows, class_rows = [], []
    new_items = {}  # pig -> {group: item NameId}
    art_dirty = False
    parent_items = G.items(G.pigs[name_id])
    for x in receivers:
        row = G.pigs[x]
        st, before = states[x], fresh[x]
        same_rig = st['rig'] == project['rig'] and st['rig'] in G.rigs
        current = G.items(row)
        for group in ('class', 'main', 'off'):
            keys = _group_slots(kinds(st['rig']), group)
            art_now = [_norm_slot(st['slots'].get(k)) for k in keys] + ([st['variant']] if group == 'class' else [])
            art_before = [_norm_slot(before['slots'].get(k)) for k in keys] + ([before['variant']] if group == 'class' else [])
            item = current.get(group)
            owned = bool(item) and owned_tag in item['NameId']
            weapon_changed = group == 'main' and any(st['weapon'].get(f) != before['weapon'].get(f) for f in WEAPON_FIELDS)
            edited = art_now != art_before
            art_changed = edited or (owned and same_rig)
            if not (art_changed or weapon_changed):
                continue
            source = st
            if not edited and owned:
                # Unchanged, but rebuilt with the rest. Wearing the edited pig's copy means having its art.
                parent_item = parent_items.get(group)
                if parent_item and parent_item['NameId'] == item['NameId']:
                    source = project
                    art_now = ['wears', item['NameId']]
            art_dirty = art_dirty or (edited and same_rig)
            if art_changed and not same_rig:
                log('%s: art not changed, its base is %s' % (x, st['rig']))
                art_changed = False
                if not weapon_changed:
                    continue
            key = json.dumps([art_now if art_changed else (item or {}).get('AssetBaseId'), st['weapon'] if group == 'main' else None], sort_keys=True)
            known = contents[group].get(key)
            if not known:
                tag = '' if not contents[group] else '_v%d' % (len(contents[group]) + 1)
                suffix = {'class': '_headgear', 'main': '_mainhand', 'off': '_offhand'}[group]
                item_id = 'pig_forge_%s%s%s' % (art, tag, suffix)
                if group == 'class':
                    template = item or G.classes['headgear_brute_lassie']
                    asset = 'Forge_%s%s%s' % (art, tag, p.variant_tag(st['variant'] or g.DEFAULT_VARIANT.get(st['rig'], '')))
                else:
                    template = item or G.equipment['pig_weapon_brute_lassie_rooster_01']
                    asset = 'Forge_%s%s%s' % (art, tag, '_MainHand' if group == 'main' else '_OffHand')
                if not art_changed:
                    asset = (item or {}).get('AssetBaseId', asset)
                new_row = copy.deepcopy(template)
                new_row.update({'NameId': item_id, 'AssetBaseId': asset})
                if group == 'class':
                    class_rows.append(new_row)
                else:
                    new_row.update({'ItemType': ITEM_TYPE[group], 'DirectAssetAndLoca': True})
                    if group == 'main':
                        w = st['weapon']
                        new_row.update({'AnimationIndex': int(w.get('animationIndex', 1)), 'IsRanged': bool(w.get('isRanged')),
                                        'ProjectileAssetId': w.get('projectile', '') if w.get('isRanged') else '',
                                        'DirectProjectileAssetAndLoca': bool(w.get('isRanged')) and bool(w.get('directProjectile', w.get('isRanged'))),
                                        'HitEffectSuffix': w.get('hitEffect', new_row.get('HitEffectSuffix', ''))})
                    equipment_rows.append(new_row)
                if art_changed:
                    _bake_group(source, group, keys, kinds(st['rig']), asset, entries)
                known = contents[group][key] = item_id
            new_items.setdefault(x, {})[group] = (known, item['NameId'] if item else None)

    # Inventories: a pig whose items changed, or who already uses this combatant's inventory, gets one of its own copies.
    inventory_rows = []
    inv_of = {}
    by_content = {}
    for x in receivers:
        row = G.pigs[x]
        current_inv = row.get('DefaultInventoryNameId') or ''
        if x not in new_items and owned_tag not in current_inv:
            continue
        content = G.content(row)
        for group, (new_id, old_id) in new_items.get(x, {}).items():
            level = content.pop(old_id, 1) if old_id else 1
            content[new_id] = level
        key = json.dumps(content, sort_keys=True)
        if key not in by_content:
            inv_id = 'pig_forge_%s_inv%d' % (art, len(by_content) + 1)
            source = G.inventories.get(current_inv) or {}
            by_content[key] = inv_id
            inventory_rows.append({'NameId': inv_id, 'DefaultInventoryContent': content, 'InitializingLevel': source.get('InitializingLevel', 0)})
        inv_of[x] = by_content[key]

    # Names: a pig keeps its LocaId when every pig sharing it ends up with the same name; otherwise it gets a copy.
    final_name = {x: states[x]['name'].strip() for x in receivers}
    loca_users = {}
    for n, r in G.pigs.items():
        loca_users.setdefault(r.get('LocaId'), []).append(n)
    loca_of = {}
    kept_locas = set()
    for x in receivers:
        loca = G.pigs[x].get('LocaId')
        if final_name[x] == G.name(G.pigs[x]):
            continue
        users = loca_users.get(loca, [])
        if all(u in final_name and final_name[u] == final_name[x] for u in users):
            loca_of[x] = loca
            kept_locas.add(loca)
    new_locas = {}
    for x in receivers:
        if final_name[x] == G.name(G.pigs[x]) or x in loca_of:
            continue
        if final_name[x] not in new_locas:
            k = len(new_locas) + 1
            while 'pig_forge_%s_name%d' % (art, k) in kept_locas:
                k += 1
            new_locas[final_name[x]] = 'pig_forge_%s_name%d' % (art, k)
        loca_of[x] = new_locas[final_name[x]]
    for x, loca in loca_of.items():
        texts[loca + '_name'] = final_name[x]

    # Pig rows.
    pig_rows = []
    keep = ex.get('keepSkills') or []
    for x in receivers:
        row = copy.deepcopy(G.pigs[x])
        st = states[x]
        actives, passives = G.skill_ids(row)
        if x == name_id:
            new_actives = [i for i, t in ids if t != 'Passive'] + [k['id'] for k in keep if not k['passive']]
            new_passives = [i for i, t in ids if t == 'Passive'] + [k['id'] for k in keep if k['passive']]
        else:
            def swap(lst, passive):
                out = [mapping.get(i, i) for i in lst if i not in removed]
                for i, t in added:
                    if (t == 'Passive') == passive and i not in out:
                        out.append(i)
                return out
            new_actives, new_passives = swap(actives, False), swap(passives, True)
        typed = [(i, type_of.get(i, 'Attack')) for i in new_actives]
        added_here = [i for i in new_actives if i not in actives and i not in mapping.values()]
        combos = row.get('SkillCombos') or []
        if new_actives != actives or added_here:
            combos = _combos(combos, mapping, removed, added_here, new_actives, typed)
        # Only what changed, so untouched fields keep their exact form (null stays null).
        stats, before = st['stats'], fresh[x]['stats']
        fields = {'hp': ('BaseHealth', int), 'attack': ('BaseAttack', int), 'sizeType': ('SizeType', str), 'sizeScale': ('SizeScale', float),
                  'strength': ('PigStrength', int), 'perLevelHealth': ('PerLevelHealth', int), 'perLevelAttack': ('PerLevelAttack', int),
                  'loot': ('LootTableDefeatBonus', lambda v: {k: int(n) for k, n in (v or {}).items()})}
        for key, (column, cast) in fields.items():
            if stats.get(key) != before.get(key):
                row[column] = cast(stats[key])
        if st['rig'] != fresh[x]['rig']:
            row['AssetId'] = st['rig']
        if new_actives != actives:
            row['SkillNameIds'] = new_actives
        if new_passives != passives:
            row['PassiveSkillNameId'] = ','.join(new_passives)
        if combos != (row.get('SkillCombos') or []):
            row['SkillCombos'] = combos
        if x in inv_of:
            row['DefaultInventoryNameId'] = inv_of[x]
        if x in loca_of:
            row['LocaId'] = loca_of[x]
        if row != G.pigs[x]:
            pig_rows.append(row)

    changed = [r['NameId'] for r in pig_rows]
    different = lambda rows, table: [r for r in rows if table.get(r['NameId']) != r]
    skill_rows, equipment_rows = different(skill_rows, G.skills), different(equipment_rows, G.equipment)
    class_rows, inventory_rows = different(class_rows, G.classes), different(inventory_rows, G.inventories)
    texts = {k: v for k, v in texts.items() if G.texts.get(k) != v}
    pivots_changed = p.write_bone_pivots(project, log)
    if not (changed or skill_rows or equipment_rows or class_rows or inventory_rows or texts or art_dirty or pivots_changed):
        log('Nothing to update: this combatant and its ticked variants already match the game')
        return project
    if entries:
        rig_id = project['rig']
        old_guids = [project.get('guids', {}).get('atlas:HR'), project.get('guids', {}).get('atlas:LR')]
        p.write_atlas(project, entries, log)
        p.register_atlas(project, log)
        forget_art(rig_id, [x for x in old_guids + [p.gid(project, 'atlas:HR'), p.gid(project, 'atlas:LR')] if x])

    stages = [('SkillBalancingData', skill_rows), ('EquipmentBalancingData', equipment_rows), ('ClassItemBalancingData', class_rows),
              ('InventoryBalancingData', inventory_rows), ('PigBalancingData', pig_rows)]
    writes = []
    for file_name, rows in stages:
        if rows:
            path = os.path.join(g.BALANCING, file_name + '.json')
            text = open(path, encoding='utf-8', newline='').read()
            for r in rows:
                text = p.upsert_row(text, r)
            writes.append((file_name, path, text))
    if texts:
        loca_path, loca_text = p.upsert_loca(texts)
        writes.append(('Loca', loca_path, loca_text))
    log('Updating %s' % ', '.join(changed) if changed else 'Updating abilities only')
    for x in variants:
        if x not in changed:
            log('%s already had these values' % x)
    p.encode(writes, log)

    # What the game has now becomes the starting point for the next edit.
    for a in project['abilities']:
        a['origin'] = by_uid[a['uid']]
    project['publishedAt'] = time.strftime('%Y-%m-%d %H:%M')
    G = Game()
    ex['combos'] = copy.deepcopy(G.pigs[name_id].get('SkillCombos') or [])
    ex['original'] = snapshot(project)
    ex['fingerprint'] = fingerprint(G, name_id)
    log('Done. %d combatant%s updated.' % (len(changed), '' if len(changed) == 1 else 's'))
    return project


def _bake_group(st, group, keys, kinds, asset, entries):
    """Adds the group's textures, named the way CharacterAssetController looks them up."""
    for key in keys:
        slot = st['slots'].get(key)
        if group == 'class':
            name = asset if key == 'Headgear' else asset + '_Back' if key == 'Headgear_Back' else '%s__%s' % (asset, key)
        elif key in ('MainHand', 'OffHand'):
            name = asset
        else:
            name = asset + ('_Arrow' if key == 'MainHand_Arrow' else '_Back')
        layers = [l for l in (slot or {}).get('layers', []) if not l.get('hidden')]
        if slot and slot.get('hidden'):
            if kinds.get(key) == 'feature':
                entries[name] = (p.Image.new('RGBA', (2, 2), (0, 0, 0, 0)), (0.0, 0.0))
        elif layers:
            entries[name] = p.bake_layers(layers)
