"""Eye beams: a helmet's eyes briefly shine bright light when its wearer uses certain abilities (game: HelmetBeams.cs).

In the Forge the beams are a layer of their own kind in a slot's stack ({'kind': 'beams', ...}); they are never baked
into the slot's texture. Upload writes them to Assets/Resources/HelmetBeams.json under the slot's texture name, with the
slot's layers above the beams baked into a "cover" texture (Assets/Resources/ForgeBeams/) so they still draw over them.

The game's own helmets with beams are the file's "builtin" entries (Headgear_StoneGuard). Their eyes, direction and
look are the presets the Forge gives a beams layer that follows that art.

A beams layer:
  uid, kind 'beams', src 'beams:eye' (the library tile's picture), name, hidden,
  follow       uid of the art layer in the same slot whose placement the beams use (its pixels: eyes are in its space);
               none = the beams layer's own tx/ty/rot/sx/sy/shear (w/h/pivot of the tile picture)
  eyes         [[x, y], ...] in that art's space (from its centre, y up)
  angle        direction in that art's space, degrees (0 = +x); turns and flips with the art
  on           {'types': ['Support', 'Attack'], 'abilities': [ability uid, ...]}
  color, length, spread, duration, delay, intensity
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFilter

import gamedata as g

CONFIG = os.path.join(g.ASSETS, 'Resources', 'HelmetBeams.json')
COVER_DIR = os.path.join(g.ASSETS, 'Resources', 'ForgeBeams')
SPRITE_KEY = 'beams:eye'
LOOK = ('color', 'length', 'spread', 'duration', 'delay', 'intensity')
DEFAULTS = {'color': 'FFD940', 'length': 430.0, 'spread': 36.0, 'duration': 0.75, 'delay': 0.1, 'intensity': 1.0, 'angle': 0.0}
KINDS = ('Support', 'Attack')


def is_beams(layer):
    return (layer or {}).get('kind') == 'beams'


def art_layers(layers):
    return [l for l in layers if not l.get('kind')]  # not beams, nor bells (bells.py)


# ------------------------------------------------------------------------------ config file

def _read():
    try:
        with open(CONFIG, encoding='utf-8') as f:
            data = json.load(f)
    except (OSError, ValueError):
        data = {}
    return data.get('beams') or []


def presets():
    """texture name -> the game's own beams for that art, in the Forge's layer form (eyes as pairs)."""
    out = {}
    for e in _read():
        if not e.get('builtin'):
            continue
        flat = e.get('eyes') or []
        preset = {k: e.get(k, DEFAULTS[k]) for k in LOOK}
        preset.update({'eyes': [[flat[i], flat[i + 1]] for i in range(0, len(flat) - 1, 2)], 'angle': e.get('angle', 0.0),
                       'on': {'types': [t for t in e.get('on') or [] if t in KINDS], 'abilities': []}})
        out[e['texture']] = preset
    return out


# ------------------------------------------------------------------------------ library

def icon_image():
    """The library tile and layer thumbnail: two eyes shining to the right."""
    k = 3
    w, h = 120, 72
    big = Image.new('RGBA', (w * k, h * k), (0, 0, 0, 0))
    glow = Image.new('RGBA', big.size, (0, 0, 0, 0))
    pen = ImageDraw.Draw(glow)
    for ey in (30, 44):
        ex = 16
        for spread, colour in ((19, (255, 214, 64, 120)), (7, (255, 244, 190, 200))):
            s = math.tan(math.radians(spread))
            pen.polygon([(ex * k, (ey - 2) * k), (ex * k, (ey + 2) * k), ((w - 2) * k, (ey + 2 + s * (w - ex)) * k), ((w - 2) * k, (ey - 2 - s * (w - ex)) * k)], fill=colour)
    glow = glow.filter(ImageFilter.GaussianBlur(4 * k))
    big.alpha_composite(glow)
    pen = ImageDraw.Draw(big)
    for ey in (30, 44):
        r = 7
        pen.ellipse(((16 - r) * k, (ey - r) * k, (16 + r) * k, (ey + r) * k), fill=(255, 236, 140, 255))
        r = 3.5
        pen.ellipse(((16 - r) * k, (ey - r) * k, (16 + r) * k, (ey + r) * k), fill=(255, 255, 245, 255))
    return big.resize((w, h), Image.LANCZOS)


def library_items():
    w, h = 120, 72
    return [{'id': '%s/beams/Eye_Beams' % side, 'side': side, 'rig': '', 'name': 'Eye_Beams', 'cat': 'Feature', 'group': 'Beams', 'beams': True,
             'parts': {'front': {'key': SPRITE_KEY, 'name': 'Eye beams', 'w': w, 'h': h, 'pivot': [0, 0]}}} for side in ('bird', 'pig')]


def decorate(items):
    """Gives library art that has the game's own beams (the basic Stoneguard helmet) its preset."""
    known = presets()
    for item in items:
        front = (item.get('parts') or {}).get('front') or {}
        if front.get('name') in known:
            item['beams'] = known[front['name']]
    return items


# ------------------------------------------------------------------------------ upload

def _affine(layer):
    import publish
    return [round(v, 5) for v in publish._layer_affine(layer)]


def _tokens(on, abilities, skill_ids):
    on = on or {}
    types = [t for t in on.get('types') or [] if t in KINDS]
    tokens = list(types)
    for a in abilities:
        if a.get('uid') in (on.get('abilities') or []) and a.get('type') not in types and skill_ids.get(a['uid']):
            tokens.append('skill:' + skill_ids[a['uid']])
    return tokens


def slot_entries(texture, slot_key, slot, abilities, skill_ids, log):
    """[(entry, cover image or None)] for a slot's shown beams layers. skill_ids: ability uid -> skill NameId."""
    if not slot or slot.get('hidden'):
        return []
    layers = slot.get('layers') or []
    shown_art = [l for l in art_layers(layers) if not l.get('hidden')]
    out = []
    for i, layer in enumerate(layers):
        if not is_beams(layer) or layer.get('hidden'):
            continue
        if not shown_art:
            log('%s: eye beams need some art in the slot to shine from; left out' % slot_key)
            continue
        on = _tokens(layer.get('on'), abilities, skill_ids)
        if not on:
            log('%s: eye beams turn on for no ability; left out' % slot_key)
            continue
        followed = next((l for l in layers if l.get('uid') == layer.get('follow') and not l.get('kind')), None)
        eyes = [v for e in layer.get('eyes') or [] for v in (float(e[0]), float(e[1]))]
        if not eyes:
            log('%s: eye beams have no eyes; left out' % slot_key)
            continue
        entry = {'texture': texture, 'slot': slot_key, 'on': on, 'eyes': eyes, 'affine': _affine(followed or layer),
                 'angle': float(layer.get('angle', 0.0))}
        entry.update({k: (layer.get(k, DEFAULTS[k]) if k == 'color' else float(layer.get(k, DEFAULTS[k]))) for k in LOOK})
        above = [l for l in layers[i + 1:] if not l.get('kind') and not l.get('hidden')]
        cover = None
        if above:
            import publish
            cover, pivot = publish.bake_layers(above)
            entry['coverPivot'] = [round(pivot[0], 6), round(pivot[1], 6)]
        out.append((entry, cover))
    return out


def project_entries(project, names, skill_ids, log):
    """names: slot key -> the texture name upload gives it."""
    out = []
    for key, slot in (project.get('slots') or {}).items():
        if key in names:
            out += slot_entries(names[key], key, slot, project.get('abilities') or [], skill_ids, log)
    return out


def _owned(texture, base):
    import publish
    return texture == base or any(texture == base + s for s in list(publish.GEAR_TEXTURE.values()) + ['_Arrow']) or texture.startswith(base + '__')


def write(bases, entries, log):
    """Replaces every non-builtin entry of the textures named after these asset bases (Forge_<id>...) with entries.
    Returns whether the file changed."""
    from projectiles import _meta
    old = _read()
    keep = [e for e in old if e.get('builtin') or not any(_owned(e.get('texture', ''), b) for b in bases)]
    dropped = [e for e in old if e not in keep]
    new = []
    for n, (entry, cover) in enumerate(entries):
        entry = dict(entry)
        entry['cover'] = ''
        entry.setdefault('coverPivot', [0, 0])
        if cover is not None:
            os.makedirs(COVER_DIR, exist_ok=True)
            _meta(COVER_DIR, 'folder')
            name = '%s__%s_%d_cover' % (entry['texture'], entry['slot'], n)
            path = os.path.join(COVER_DIR, name + '.png')
            cover.save(path)
            _meta(path, 'image', wrap=0)
            entry['cover'] = 'ForgeBeams/' + name
        new.append(entry)
    still = {e['cover'] for e in new if e['cover']}
    for e in dropped:
        if e.get('cover') and e['cover'] not in still:
            path = os.path.join(g.ASSETS, 'Resources', e['cover'] + '.png')
            for f in (path, path + '.meta'):
                if os.path.exists(f):
                    os.remove(f)
    result = keep + new
    if result == old:
        return False
    text = json.dumps({'beams': result}, indent=1) + '\n'
    with open(CONFIG, 'w', encoding='utf-8') as f:
        f.write(text)
    _meta(CONFIG, 'text')
    for entry in new:
        log('Eye beams on %s (%s): on %s' % (entry['slot'], entry['texture'], ', '.join(entry['on'])))
    if dropped and not new:
        log('Eye beams removed')
    return True
