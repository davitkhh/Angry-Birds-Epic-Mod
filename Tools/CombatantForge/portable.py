"""Export and import of combatants as .forge files: a zip holding the project, a copy of its art, and the rows of any
abilities written for it, so it also opens on another copy of the game."""
import base64
import io
import json
import os
import re
import uuid
import zipfile

from PIL import Image

import gamedata as g
import publish as p

SERVER_ONLY = ('guids', 'publishedAt', 'publishedRig')
MAX_UNPACKED = 256 * 1024 * 1024


def export_bytes(project):
    project = json.loads(json.dumps(project))
    for key in SERVER_ONLY:
        project.pop(key, None)
    buf = io.BytesIO()
    texts = g.loca_texts()
    with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
        art = {}
        for slot in project.get('slots', {}).values():
            for layer in slot.get('layers', []):
                if layer['src'] not in art:
                    name = 'art/%d_%s.png' % (len(art), re.sub(r'[^\w-]', '_', layer.get('name', 'art')))
                    png = io.BytesIO()
                    g.sprite_image(layer['src']).save(png, 'PNG')
                    z.writestr(name, png.getvalue())
                    art[layer['src']] = name
        for ability in project.get('abilities', []):
            name_id = ability.get('nameId')
            row = p.read_row('SkillBalancingData', name_id) if name_id else None
            if row:
                ability['row'] = row
                ability['name'] = texts.get(name_id + '_name', ability.get('name', ''))
                ability['desc'] = texts.get(name_id + '_desc', ability.get('desc', ''))
        z.writestr('combatant.json', json.dumps({'format': 1, 'project': project, 'art': art}, indent=1, ensure_ascii=False))
    return buf.getvalue()


def _category(layer):
    name = layer.get('item') or layer.get('name') or ''
    for cat in ('Headgear', 'MainHand', 'OffHand'):
        if name.startswith(cat):
            return cat
    return 'Feature'


def _unique_upload(side, cat, name, png):
    """Saves art under a name of its own, reusing an identical file already there. Returns its library key."""
    folder = os.path.join(g.UPLOADS, 'Birds' if side == 'bird' else 'Pigs', cat)
    stem = re.sub(r'[^A-Za-z0-9_-]+', '_', name).strip('_') or 'Imported'
    for n in range(1, 1000):
        candidate = stem if n == 1 else '%s_%d' % (stem, n)
        path = os.path.join(folder, candidate + '.png')
        if os.path.exists(path):
            if Image.open(path).convert('RGBA').tobytes() == Image.open(io.BytesIO(png)).convert('RGBA').tobytes():
                return 'upload:' + os.path.relpath(path, g.UPLOADS), False
            continue
        saved = g.add_upload(side, cat, candidate + '.png', base64.b64encode(png).decode())
        return 'upload:' + os.path.relpath(os.path.join(g.ASSETS, saved), g.UPLOADS), True
    raise ValueError('Too many imported files named ' + stem)


def _resolves(key):
    try:
        g.sprite_image(key)
        return True
    except Exception:
        return False


def import_bytes(data, taken_ids):
    """Returns (project, notes). The project is new: nothing is written to the balancing data until it's uploaded."""
    notes = []
    try:
        z = zipfile.ZipFile(io.BytesIO(data))
    except zipfile.BadZipFile:
        raise ValueError('That is not a .forge file')
    if sum(i.file_size for i in z.infolist()) > MAX_UNPACKED:
        raise ValueError('That file is too large to import')
    try:
        manifest = json.loads(z.read('combatant.json'))
    except (KeyError, ValueError):
        raise ValueError('That is not a .forge file')
    project = manifest['project']
    art = manifest.get('art', {})
    if not any(r['id'] == project.get('rig') for r in g.rig_list()):
        raise ValueError('This game has no %s base' % project.get('rig'))
    p.migrate_project(project)
    side = g.rig_info(project['rig'])['side']
    remap = {}
    for slot in project.get('slots', {}).values():
        for layer in slot.get('layers', []):
            src = layer['src']
            if src not in remap:
                if _resolves(src):
                    remap[src] = src
                else:
                    if src not in art:
                        raise ValueError('The file is missing the art for ' + layer.get('name', src))
                    key, added = _unique_upload(side, _category(layer), layer.get('name', 'Imported'), z.read(art[src]))
                    remap[src] = key
                    if added:
                        notes.append('Added %s to the library' % layer.get('name'))
            layer['src'] = remap[src]
    base, n = project.get('id') or 'imported', 2
    new_id = base
    while new_id in taken_ids:
        new_id, n = '%s_%d' % (base, n), n + 1
    if new_id != base:
        notes.append('Imported as "%s" because "%s" already exists' % (new_id, base))
    project['id'] = new_id
    for key in SERVER_ONLY:
        project.pop(key, None)
    for ability in project.get('abilities', []):
        ability.setdefault('uid', uuid.uuid4().hex[:8])
        name_id = ability.pop('nameId', None)
        row = ability.get('row')
        if name_id and p.read_row('SkillBalancingData', name_id):
            # Same game: start from the skill that is already here.
            ability['source'] = name_id
            ability.pop('row', None)
        elif row:
            template = row.get('SkillTemplateType', '')
            if not g._template_source(template):
                notes.append('Ability "%s" needs the %s script, which this game doesn\'t have. It uses a stand-in until Claude rebuilds it.' % (ability.get('name'), template))
                ability.pop('row', None)
                ability['written'] = False
    return p.migrate_project(project), notes
