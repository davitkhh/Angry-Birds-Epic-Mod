"""Bells: a ring with a big and a small brass bell hanging off a slot's art; they swing with the character and ring when
knocked (game: ScytheBells.cs; art + sounds: ArtSource/ScytheBells/bells.py).

In the Forge the bells are a layer of their own kind in a slot's stack ({'kind': 'bells', ...}); they are never baked into
the slot's texture. Upload writes them to Assets/Resources/ScytheBellsConfig.json ("weapons", matched by the slot's
texture name), with the slot's layers above the bells baked into a "cover" texture (Assets/Resources/ForgeBells/) so
they still draw over the bells.

The game's own belled item (the Dark Cupid scythe with bells) is the file's "builtin" entry; the file's "presets" say where
the ring goes on art the Forge knows (the Dark Cupid scythe's collar), which a bells layer following that art starts from.

A bells layer:
  uid, kind 'bells', src 'bells:icon' (the library tile's picture), name, hidden,
  follow    uid of the art layer in the same slot the bells hang from (the ring is in its space); none = the bells
            layer's own tx/ty/rot/sx/sy/shear (w/h/pivot of the tile picture)
  ring      [x, y] the ring's centre in that art's space (from its centre, y up)
  (any number of bells layers per slot; each is its own set of bells in the game)
  ringArt   'Ring' (cut to pass behind the Dark Cupid scythe's handle; only right on that art, unturned),
            'RingPlain' (a whole ring) or '' (no ring drawn)
  materials [name per bell, big first] (ScytheBellsConfig "materials": Brass, Silver, Bronze); missing = the bell's default
  on        {'always': bool, 'types': ['Attack', 'Support', 'Rage'], 'abilities': [ability uid, ...]}
            always = strikes ring whenever the bells knock; types/abilities = those abilities ring them out (and shake them)
"""
import io
import json
import os
import wave

from PIL import Image

import gamedata as g

CONFIG = os.path.join(g.ASSETS, 'Resources', 'ScytheBellsConfig.json')
ATLAS = os.path.join(g.ASSETS, 'Resources', 'ScytheBells.png')
COVER_DIR = os.path.join(g.ASSETS, 'Resources', 'ForgeBells')
SPRITE_KEY = 'bells:icon'
ATLAS_KEY = 'bells:atlas'
KINDS = ('Attack', 'Support', 'Rage')
RINGS = ('Ring', 'RingPlain', '')


def is_bells(layer):
    return (layer or {}).get('kind') == 'bells'


def _read():
    try:
        with open(CONFIG, encoding='utf-8') as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def preview_config():
    """What the Forge's preview needs to draw and ring the bells as the game does: sprites, bells, presets, the sounds."""
    data = _read()
    builtin = next((w for w in data.get('weapons') or [] if w.get('builtin')), {})
    corner, centre = builtin.get('ringCorner') or [0, 0], builtin.get('ringCentre') or [0, 0]
    return {'texel': data.get('texel', 2), 'sprites': data.get('sprites') or [], 'bells': data.get('bells') or [],
            'presets': data.get('presets') or [], 'hangRadius': builtin.get('hangRadius', 2.65),
            'materials': data.get('materials') or [], 'reference': data.get('reference', 'Brass'),
            # where the handle-cut ring's cell sits relative to the ring's centre (sprite space, y up)
            'ringOffset': [corner[0] - centre[0], corner[1] - centre[1]]}


def sprite_image(name):
    if name == 'atlas':
        return Image.open(ATLAS).convert('RGBA')
    return icon_image()


def _cell(atlas, sprite, texel):
    x, y, w, h = sprite['rect']
    return atlas.crop((int(x), atlas.height - int(y) - int(h), int(x + w), atlas.height - int(y)))


def icon_image():
    """The library tile and layer thumbnail: the ring with both bells hanging off it, at 4x."""
    data = _read()
    texel = data.get('texel', 2)
    sprites = {s['name']: s for s in data.get('sprites') or []}
    atlas = Image.open(ATLAS).convert('RGBA')
    k = 4.0 / texel
    w, h = 120, 120
    out = Image.new('RGBA', (w, h), (0, 0, 0, 0))

    def put(name, at, pivot=None):
        s = sprites.get(name)
        if not s:
            return
        im = _cell(atlas, s, texel)
        im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)
        pv = pivot if pivot is not None else [s['pivot'][0] * 4, s['pivot'][1] * 4]
        out.alpha_composite(im, (int(at[0] - pv[0]), int(at[1] - pv[1])))
    centre = (w / 2.0, 22)
    put('RingPlain', centre)
    hang = 2.65 * 4
    for bell, dx in (('BellLarge', -14), ('BellSmall', 22)):
        b = next((x for x in data.get('bells') or [] if x.get('sprite') == bell), None)
        if b:
            cp = b.get('clapperPivot') or [0, 0]
            put(b['clapper'], (centre[0] + dx + cp[0] * 4, centre[1] + hang + cp[1] * 4))
        put(bell, (centre[0] + dx, centre[1] + hang))
    return out


def library_items():
    return [{'id': '%s/bells/Bells' % side, 'side': side, 'rig': '', 'name': 'Bells', 'cat': 'Feature', 'group': 'Bells', 'bells': True,
             'parts': {'front': {'key': SPRITE_KEY, 'name': 'Bells', 'w': 120, 'h': 120, 'pivot': [0, 0]}}} for side in ('bird', 'pig')]


def sound_wav():
    """bell_ring.wav as 16-bit PCM (the recording is 24-bit, which the desktop window's WebKit may not decode)."""
    first = (_read().get('bells') or [{}])[0]
    path = os.path.join(g.ASSETS, 'Resources', (first.get('soundClip') or 'Sound/Characters/Generic/bell_ring') + '.wav')
    with wave.open(path, 'rb') as src:
        channels, width, rate, frames = src.getnchannels(), src.getsampwidth(), src.getframerate(), src.readframes(src.getnframes())
    if width == 3:
        frames = b''.join(frames[i + 1:i + 3] for i in range(0, len(frames), 3))
    elif width == 4:
        frames = b''.join(frames[i + 2:i + 4] for i in range(0, len(frames), 4))
    elif width != 2:
        raise ValueError('unsupported sample width %d' % width)
    buf = io.BytesIO()
    with wave.open(buf, 'wb') as out:
        out.setnchannels(channels)
        out.setsampwidth(2)
        out.setframerate(rate)
        out.writeframes(frames)
    return buf.getvalue()


# ------------------------------------------------------------------------------ upload

def _tokens(on, abilities, skill_ids):
    on = on or {}
    types = [t for t in on.get('types') or [] if t in KINDS]
    tokens = (['Always'] if on.get('always') else []) + types
    for a in abilities:
        if a.get('uid') in (on.get('abilities') or []) and a.get('type') not in types and skill_ids.get(a['uid']):
            tokens.append('skill:' + skill_ids[a['uid']])
    return tokens


def _sprite_size(name):
    s = next((x for x in _read().get('sprites') or [] if x.get('name') == name), None)
    return s['size'] if s else [0, 0]


def slot_entries(texture, slot_key, slot, abilities, skill_ids, log):
    """[(entry, cover image or None)] for each of a slot's shown bells layers, bottom first (the game draws later sets
    in front). skill_ids: ability uid -> skill NameId."""
    if not slot or slot.get('hidden'):
        return []
    layers = slot.get('layers') or []
    return [_entry(texture, slot_key, layers, i, layer, abilities, skill_ids, log)
            for i, layer in enumerate(layers) if is_bells(layer) and not layer.get('hidden')]


def _entry(texture, slot_key, layers, i, layer, abilities, skill_ids, log):
    import publish
    on = _tokens(layer.get('on'), abilities, skill_ids)
    if not on:
        log('%s: the bells ring for nothing (no "always", no ability); they will swing silently' % slot_key)
    followed = next((l for l in layers if l.get('uid') == layer.get('follow') and not is_bells(l) and not l.get('kind')), None)
    a, b, d, e, cx, cy = publish._layer_affine(followed or layer)
    rx, ry = (float(v) for v in (layer.get('ring') or [0, 0]))
    centre = [a * rx + b * ry + cx, d * rx + e * ry + cy]
    ring = layer.get('ringArt', 'RingPlain')
    ring = ring if ring in RINGS else 'RingPlain'
    corner = centre
    if ring == 'Ring':
        preset = next((p for p in _read().get('presets') or [] if followed and p.get('ring') == 'Ring' and
                       followed.get('src', '').endswith(':' + p['art'])), None)
        upright = abs(a - 1) < 0.02 and abs(e - 1) < 0.02 and abs(b) < 0.02 and abs(d) < 0.02
        builtin = next((w for w in _read().get('weapons') or [] if w.get('builtin') and w.get('ring') == 'Ring'), None)
        if preset and upright and builtin:
            # the cut ring sits where the game's own belled scythe has it, relative to the ring's centre
            off = [builtin['ringCorner'][0] - builtin['ringCentre'][0], builtin['ringCorner'][1] - builtin['ringCentre'][1]]
            corner = [centre[0] + off[0], centre[1] + off[1]]
        else:
            log('%s: the handle-cut ring only fits the Dark Cupid scythe as it is; using a whole ring' % slot_key)
            ring = 'RingPlain'
    if ring == 'RingPlain':
        w, h = _sprite_size('RingPlain')
        corner = [centre[0] - w / 2.0, centre[1] + h / 2.0]
    known = {m['name'] for m in _read().get('materials') or []}
    metals = [m if m in known else '' for m in layer.get('materials') or []]
    entry = {'builtin': False, 'items': [], 'texture': texture, 'slot': slot_key, 'ring': ring, 'materials': metals,
             'ringCorner': [round(v, 3) for v in corner], 'ringCentre': [round(v, 3) for v in centre],
             'hangRadius': preview_config()['hangRadius'], 'on': on, 'cover': '', 'coverPivot': [0, 0]}
    above = [l for l in layers[i + 1:] if not l.get('kind') and not l.get('hidden')]
    cover = None
    if above:
        cover, pivot = publish.bake_layers(above)
        entry['coverPivot'] = [round(pivot[0], 6), round(pivot[1], 6)]
    return entry, cover


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
    data = _read()
    if not data:
        log('Bells: %s is missing; run ArtSource/ScytheBells/bells.py' % CONFIG)
        return False
    old = data.get('weapons') or []
    keep = [e for e in old if e.get('builtin') or not any(_owned(e.get('texture', ''), b) for b in bases)]
    dropped = [e for e in old if e not in keep]
    new = []
    for n, (entry, cover) in enumerate(entries):
        entry = dict(entry)
        if cover is not None:
            os.makedirs(COVER_DIR, exist_ok=True)
            _meta(COVER_DIR, 'folder')
            name = '%s__%s_%d_bells_cover' % (entry['texture'], entry['slot'], n)  # n: unique per set
            path = os.path.join(COVER_DIR, name + '.png')
            cover.save(path)
            _meta(path, 'image', wrap=0)
            entry['cover'] = 'ForgeBells/' + name
        new.append(entry)
    still = {e['cover'] for e in new if e.get('cover')}
    for e in dropped:
        if e.get('cover') and e['cover'] not in still:
            path = os.path.join(g.ASSETS, 'Resources', e['cover'] + '.png')
            for f in (path, path + '.meta'):
                if os.path.exists(f):
                    os.remove(f)
    result = keep + new
    if result == old:
        return False
    data['weapons'] = result
    with open(CONFIG, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, indent=1)
        f.write('\n')
    _meta(CONFIG, 'text')
    for entry in new:
        log('Bells on %s (%s): ring %s' % (entry['slot'], entry['texture'], ', '.join(entry['on']) or 'never (swing only)'))
    if dropped and not new:
        log('Bells removed')
    return True
