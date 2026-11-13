"""Projectile projects: the Forge's second section. A projectile is a list of components (the flying body, air
trails, impact effects, things left where it bounces...), each a stack of layers drawn with the same asset system as
combatant slots, plus physics (how it is fired, flies, bounces, fades and what it spawns).

Saved in projects/projectiles/<id>.json. A game projectile (Resources/Prefabs/4_Characters/4_Projectiles) opens as a
new project whose body is the prefab's drawable parts (effects.info) with its flight time; the preview simulates the
physics. publish() uploads one to the game (Resources/ForgeProjectiles, flown by ForgeProjectiles.cs) under the name
ForgeProjectile_<id>, which combatant attacks can then fire.
"""
import base64
import copy
import json
import math
import os
import re
import time
import uuid

import effects
import gamedata as g
from PIL import Image

DIR = None  # set by forge.py: <projects>/projectiles

PHYSICS = {
    # how it comes into being: fired from the origin, trailing behind another component, or spawned where another
    # component is fired from (launch), hits its target, bounces or ends
    'spawn': 'fire', 'of': '',
    'count': 1, 'interval': 0.08, 'spread': 0.0,
    # flight (fired ones)
    # before the impact
    'flightTime': 0.25, 'easing': 'even', 'arc': 0.0, 'hops': 0, 'hopHeight': 30.0, 'align': False, 'spin': 0.0,
    'fadeFrom': 0.0, 'fadeTo': 0.0,
    # after the impact
    'bounces': 0, 'bounceArc': 60.0, 'bounceDistance': 140.0, 'bounceDecay': 0.6,
    'afterSpeed': 1.0, 'afterDirection': 'onward', 'afterAlign': False, 'afterSpin': 0.0, 'linger': 0.0,
    # trails
    'rate': 30.0,
    # spawned ones and trails
    'life': 0.4, 'scaleStart': 1.0, 'scaleEnd': 1.0, 'fade': True, 'squash': 0.0, 'scatter': 0.0, 'gravity': 0.0,
    'trailAlign': False, 'randomTurn': False, 'vary': {}, 'accel': 0.0, 'afterAccel': 0.0,
    'style': 'pieces', 'textureMode': 'stretch', 'alphaKeys': None,
    # slow motion components: how much, how long, eased over, and whether the whole battle or only this projectile
    'slowScale': 0.3, 'slowDuration': 0.5, 'slowEase': 0.1, 'slowEaseOut': 0.1, 'slowScope': 'game',
}
DEFAULTS = {
    'body': {},
    'fired': {'spawn': 'launch', 'life': 0.25, 'scaleStart': 0.8, 'scaleEnd': 1.3},
    'trail': {'spawn': 'trail', 'life': 0.3, 'scaleEnd': 0.4, 'rate': 30.0},
    'impact': {'spawn': 'hit', 'life': 0.4, 'scaleStart': 0.6, 'scaleEnd': 1.2},
    'bounce': {'spawn': 'bounce', 'life': 0.5, 'squash': 0.5},
    'slowmo': {'spawn': 'hit'},
}


def physics(kind, of=''):
    p = dict(PHYSICS, vary={})
    p.update(DEFAULTS.get(kind, {}))
    if p['spawn'] != 'fire':
        p['of'] = of
    return p


def component(kind, name, of=''):
    return {'uid': uuid.uuid4().hex[:8], 'name': name, 'type': kind, 'physics': physics(kind, of)}


def blank(name=''):
    body = component('body', 'Body')
    return {'kind': 'projectile', 'id': '', 'name': name, 'rig': '__projectile', 'components': [body], 'slots': {body['uid']: {'layers': [], 'hidden': False}},
            'abilities': [], 'stats': {}, 'weapon': {}, 'variant': ''}


# ------------------------------------------------------------------------------ files

def path(pid):
    if not re.fullmatch(r'[a-z0-9_]+', pid or ''):
        raise ValueError('bad projectile id')
    return os.path.join(DIR, pid + '.json')


def save(project):
    os.makedirs(DIR, exist_ok=True)
    target = path(project['id'])
    with open(target + '.tmp', 'w', encoding='utf-8') as f:
        json.dump(project, f, indent=1, ensure_ascii=False)
    os.replace(target + '.tmp', target)


def load(pid):
    return json.load(open(path(pid), encoding='utf-8'))


def listing():
    if not os.path.isdir(DIR):
        return []
    out = []
    for name in sorted(os.listdir(DIR)):
        if name.endswith('.json'):
            try:
                d = json.load(open(os.path.join(DIR, name), encoding='utf-8'))
                out.append({'id': d['id'], 'name': d.get('name', ''), 'source': d.get('source', '')})
            except (OSError, ValueError, KeyError):
                pass
    return out


def trash(pid):
    src = path(pid)
    if not os.path.exists(src):
        return None
    folder = os.path.join(DIR, '.trash')
    os.makedirs(folder, exist_ok=True)
    target = os.path.join(folder, '%s-%s.json' % (pid, time.strftime('%Y%m%d-%H%M%S')))
    os.replace(src, target)
    return target


def unique_id(base):
    base = re.sub(r'[^a-z0-9]+', '_', base.lower()).strip('_') or 'projectile'
    taken = {x['id'] for x in listing()}
    pid, n = base, 2
    while pid in taken:
        pid, n = '%s_%d' % (base, n), n + 1
    return pid


# ------------------------------------------------------------------------------ game projectiles as layers

def _mul(a, b):
    return [a[0] * b[0] + a[2] * b[1], a[1] * b[0] + a[3] * b[1], a[0] * b[2] + a[2] * b[3], a[1] * b[2] + a[3] * b[3],
            a[0] * b[4] + a[2] * b[5] + a[4], a[1] * b[4] + a[3] * b[5] + a[5]]


def _node_matrix(n):
    c, s = math.cos(n['angle']), math.sin(n['angle'])
    sx, sy = n['scale']
    return [c * sx, s * sx, -s * sy, c * sy, n['pos'][0], n['pos'][1]]


def _layer_from(matrix, src, name, w, h):
    """A Forge layer (app.js slotMatrix) drawing an image of size w x h centred where matrix puts (0, 0)."""
    a, b, c, d, e, f = matrix
    rot = math.atan2(b, a)
    sx = math.hypot(a, b) or 1e-6
    cs, sn = math.cos(rot), math.sin(rot)
    # second column = ((cos*k - sin)*sy, (sin*k + cos)*sy): turned back by rot it is (k*sy, sy)
    xr, yr = cs * c + sn * d, -sn * c + cs * d
    sy = yr if abs(yr) > 1e-9 else 1e-6
    return {'uid': uuid.uuid4().hex[:8], 'src': src, 'name': name, 'item': name, 'w': w, 'h': h, 'pivot': [0, 0],
            'tx': round(e, 3), 'ty': round(f, 3), 'rot': round(math.degrees(rot), 3), 'sx': round(sx, 4), 'sy': round(sy, 4), 'shear': round(xr / sy, 4)}


def game_layers(name):
    """The drawable parts of a game projectile prefab as layers, placed relative to the part that flies (its tween).
    For one uploaded from this section: its fired component's layers."""
    if name.startswith(PREFIX):
        project = from_uploaded(name)
        body = next((c for c in project['components'] if c['physics'].get('spawn') == 'fire'), None)
        layers = copy.deepcopy(project['slots'][body['uid']]['layers']) if body else []
        for layer in layers:
            layer['uid'] = uuid.uuid4().hex[:8]
        return layers
    info = effects.info(name)
    if not info:
        raise ValueError('The game has no projectile ' + name)
    nodes = info['nodes']
    flying = info['tween'] if info['tween'] is not None else 0
    layers = []
    for i, n in enumerate(nodes):
        if not n.get('image') or not n.get('active', True):
            continue
        # chain from this part up to (not including) the flying part
        m, j, ok = [1, 0, 0, 1, 0, 0], i, False
        while j is not None:
            if j == flying:
                ok = True
                break
            m = _mul(_node_matrix(nodes[j]), m)
            j = nodes[j]['parent']
        if not ok:
            continue
        x0, y0, x1, y1 = n['bounds']
        m = _mul(m, [1, 0, 0, 1, (x0 + x1) / 2, (y0 + y1) / 2])
        key = n['image']
        src = 'fx:' + key if key.startswith('mesh:') else key.split(':', 1)[1]
        layers.append(_layer_from(m, src, n['path'].split('/')[-1] or name, round(x1 - x0, 3), round(y1 - y0, 3)))
    return layers


def _hermite(keys, t):
    if not keys:
        return t
    if t <= keys[0][0]:
        return keys[0][1]
    for a, b in zip(keys, keys[1:]):
        if t <= b[0]:
            dt = b[0] - a[0]
            if not dt or not math.isfinite(a[3]) or not math.isfinite(b[2]):
                return a[1]
            s = (t - a[0]) / dt
            return (2 * s**3 - 3 * s**2 + 1) * a[1] + (s**3 - 2 * s**2 + s) * a[3] * dt + (-2 * s**3 + 3 * s**2) * b[1] + (s**3 - s**2) * b[2] * dt
    return keys[-1][1]


def _minmax(body, field):
    """A ParticleSystem MinMaxCurve's typical value: the constant, or the middle of a random-between-two-constants."""
    m = re.search(r'\b%s:\n\s+serializedVersion: \d+\n\s+minMaxState: (\d)\n\s+scalar: ([-\d.eE+]+)(?:\n\s+minScalar: ([-\d.eE+]+))?' % field, body)
    if not m:
        return None
    state, hi, lo = int(m.group(1)), float(m.group(2)), float(m.group(3) or 0)
    return (hi + lo) / 2 if state == 3 else hi


def _module(body, name):
    m = re.search(r'\n  %s:\n(.*?)(?=\n  \w+Module:|\Z)' % name, body, re.S)
    return m.group(1) if m else ''


def game_behaviour(name, body_uid, flight_time):
    """Components for what a game projectile does besides its art: each TrailRenderer becomes an air trail (thin pieces
    turned along the path, shrinking like its width curve and fading over its time), each ParticleSystem a trail of
    particles (rate, lifetime, size, drift, rotation, fade). Returns [(component, layers)]."""
    path = effects.prefab_path(name)
    text = g.read_text(path)
    blocks = effects._blocks(text)
    by_go = {}
    for kind, body in blocks.values():
        go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
        if go:
            by_go.setdefault(go.group(1), {})[kind] = body
    names = {fid: re.search(r'm_Name: (.*)', body).group(1).strip() for fid, (kind, body) in blocks.items() if kind == 'GameObject'}
    distance = 480.0  # a typical shot, to turn per-distance emission into per-second
    out = []
    for go, comps in by_go.items():
        label = names.get(go, 'Effect')
        trail = comps.get('TrailRenderer')
        if trail:
            mat = re.search(r'm_Materials:\n  - \{fileID: \d+, guid: (\w+)', trail)
            if mat:
                life = float((re.search(r'm_Time: ([\d.eE+-]+)', trail) or [0, 0.5])[1])
                width = float((re.search(r'widthMultiplier: ([\d.eE+-]+)', trail) or [0, 20])[1])
                curve = re.findall(r'value: ([\d.eE+-]+)', trail.split('widthCurve', 1)[1].split('colorGradient', 1)[0]) if 'widthCurve' in trail else []
                end = float(curve[-1]) / float(curve[0]) if len(curve) >= 2 and float(curve[0]) else 1.0
                rate = 60.0
                # the gradient's alpha keys (0 = at the shot, 1 = the tail)
                n_alpha = int((re.search(r'm_NumAlphaKeys: (\d+)', trail) or [0, 2])[1])
                alphas = [float(a) for a in re.findall(r'key\d: \{r: [\d.eE+-]+, g: [\d.eE+-]+, b: [\d.eE+-]+, a: ([\d.eE+-]+)\}', trail)][:n_alpha]
                atimes = [int(a) / 65535 for a in re.findall(r'atime\d: (\d+)', trail)][:n_alpha]
                keys = [[round(t, 4), round(a, 4)] for t, a in zip(atimes, alphas)] or None
                mode = int((re.search(r'm_TextureMode: (\d+)', trail) or [0, 0])[1])
                c = component('trail', 'Air trail (%s)' % label, body_uid)
                c['physics'].update({'rate': rate, 'life': round(life, 3), 'scaleStart': 1.0, 'scaleEnd': round(end, 3), 'fade': True, 'trailAlign': True,
                                     'style': 'ribbon', 'alphaKeys': keys, 'textureMode': 'tile' if mode == 1 else 'stretch'})
                step = distance / max(0.05, flight_time) / rate  # units between pieces
                layer = {'uid': uuid.uuid4().hex[:8], 'src': 'fx:tex:%s:1x1' % mat.group(1), 'name': 'Trail', 'item': 'Trail',
                         'w': round(step * 1.6, 2), 'h': round(width, 2), 'pivot': [0, 0], 'tx': 0, 'ty': 0, 'rot': 0, 'sx': 1, 'sy': 1, 'shear': 0}
                out.append((c, [layer]))
        ps, renderer = comps.get('ParticleSystem'), comps.get('ParticleSystemRenderer')
        if ps and renderer and re.search(r'm_Enabled: 1', renderer):
            mat = re.search(r'm_Materials:\n  - \{fileID: \d+, guid: (\w+)', renderer)
            if not mat:
                continue
            init, emit = _module(ps, 'InitialModule'), _module(ps, 'EmissionModule')
            uvm = _module(ps, 'UVModule')
            tiles = (1, 1)
            if re.search(r'enabled: 1', uvm[:40]):
                tx, ty = re.search(r'tilesX: (\d+)', uvm), re.search(r'tilesY: (\d+)', uvm)
                tiles = (int(tx.group(1)) if tx else 1, int(ty.group(1)) if ty else 1)
            image = effects._texture_image(mat.group(1), tiles)
            if image is None:
                continue
            size = _minmax(init, 'startSize') or 20.0
            tw, th = image.size
            rate = _minmax(emit, 'rateOverTime') or _minmax(emit, 'm_Rate') or 0.0
            per_distance = _minmax(emit, 'rateOverDistance') or _minmax(emit, 'm_RateOverDistance') or 0.0
            if not rate and per_distance:
                rate = per_distance * distance / max(0.05, flight_time)
            bursts = re.findall(r'countCurve:\n\s+serializedVersion: \d+\n\s+minMaxState: \d\n\s+scalar: ([\d.]+)', emit) or re.findall(r'cnt: ([\d.]+)', emit)
            c = component('trail', 'Particles (%s)' % label, body_uid)
            sized = re.search(r'enabled: 1', _module(ps, 'SizeModule')[:40])
            colored = re.search(r'enabled: 1', _module(ps, 'ColorModule')[:40])
            c['physics'].update({'rate': round(min(240.0, rate or 30.0), 2), 'life': round(_minmax(init, 'startLifetime') or 0.3, 3),
                                 'scatter': round(abs(_minmax(init, 'startSpeed') or 0.0), 2), 'gravity': round((_minmax(init, 'gravityModifier') or 0.0) * 100, 2),
                                 'scaleStart': 1.0, 'scaleEnd': 0.3 if sized else 1.0, 'fade': bool(colored) or True,
                                 'randomTurn': abs(_minmax(init, 'startRotation') or 0.0) > 0.01})
            if not rate and bursts:
                # a burst when it's fired, not a stream
                c['type'], c['name'] = 'fired', 'Burst (%s)' % label
                c['physics'].update({'spawn': 'launch', 'count': int(float(bursts[0]))})
            layer = {'uid': uuid.uuid4().hex[:8], 'src': 'fx:tex:%s:%dx%d' % (mat.group(1), tiles[0], tiles[1]), 'name': label, 'item': label,
                     'w': round(size, 2), 'h': round(size * th / max(1, tw), 2), 'pivot': [0, 0], 'tx': 0, 'ty': 0, 'rot': 0, 'sx': 1, 'sy': 1, 'shear': 0}
            out.append((c, [layer]))
    return out


def from_game(name):
    """A new (unsaved) project that starts as a copy of a game projectile."""
    layers = game_layers(name)
    proj = g.projectile_info(name)
    project = blank(name.replace('Projectile_', '').replace('_', ' '))
    body = project['components'][0]
    project['slots'][body['uid']]['layers'] = layers
    if layers:
        project['slots'][body['uid']]['active'] = layers[-1]['uid']
    p = body['physics']
    p['flightTime'] = round(proj.get('duration') or 0.25, 3)
    # The prefab's own animation (an arrow's bob and tilt) becomes an arc, with the art turned along the path.
    info = effects.info(name)
    lift, turns = 0.0, False
    for c in info['clips']:
        for cv in c['curves']:
            if cv['kind'] == 'pos':
                ys = [k[1][1] for k in cv['keys']]
                lift = max(lift, max(ys) - min(ys))
            if cv['kind'] in ('rot', 'euler'):
                turns = True
    p['arc'] = round(lift, 1)
    p['align'] = turns
    # its speed along the way: the CHMotionTween's horizontal curve
    curve = (proj.get('curves') or {}).get('x') or proj.get('curve') or []
    if curve:
        mid = _hermite(curve, 0.5)
        p['easing'] = 'decel' if mid > 0.6 else 'accel' if mid < 0.4 else 'even'
    # trails and particles, as their own components
    # (drawn under the body, as the game's trails are)
    for i, (comp, comp_layers) in enumerate(game_behaviour(name, body['uid'], p['flightTime'])):
        project['components'].insert(i, comp)
        project['slots'][comp['uid']] = {'layers': comp_layers, 'hidden': False, 'active': comp_layers[-1]['uid'] if comp_layers else None}
    project['source'] = name
    project['id'] = unique_id(project['name'])
    return project


def duplicate(project):
    copy_ = copy.deepcopy(project)
    copy_['name'] = (project.get('name') or project['id']) + ' copy'
    copy_['id'] = unique_id(copy_['name'])
    for k in ('source', 'published', 'publishedAt'):
        copy_.pop(k, None)
    return copy_


# ------------------------------------------------------------------------------ import / export

def export_bytes(project):
    """JSON with the project and the art of every uploaded layer, so it opens on another machine."""
    art = {}
    for slot in project.get('slots', {}).values():
        for layer in slot.get('layers', []):
            src = layer.get('src', '')
            if src.startswith('upload:') and src not in art:
                full = os.path.join(g.UPLOADS, src[len('upload:'):])
                if os.path.exists(full):
                    art[src] = base64.b64encode(open(full, 'rb').read()).decode()
    return json.dumps({'forgeProjectile': 1, 'project': project, 'art': art}, ensure_ascii=False).encode()


def import_bytes(data):
    doc = json.loads(data)
    if not doc.get('forgeProjectile'):
        raise ValueError('That is not a projectile file')
    project = doc['project']
    for src, b64 in (doc.get('art') or {}).items():
        rel = src[len('upload:'):]
        full = os.path.normpath(os.path.join(g.UPLOADS, rel))
        if full.startswith(g.UPLOADS + os.sep) and not os.path.exists(full):
            os.makedirs(os.path.dirname(full), exist_ok=True)
            open(full, 'wb').write(base64.b64decode(b64))
    project['kind'] = 'projectile'
    project['id'] = unique_id(project.get('id') or project.get('name') or 'imported')
    return project


# ------------------------------------------------------------------------------ into the game

PREFIX = 'ForgeProjectile_'
GAME_FIELDS = ['count', 'interval', 'spread', 'flightTime', 'easing', 'accel', 'arc', 'hops', 'hopHeight', 'align', 'spin', 'fadeFrom', 'fadeTo',
               'bounces', 'bounceArc', 'bounceDistance', 'bounceDecay', 'afterSpeed', 'afterAccel', 'afterDirection', 'afterAlign', 'afterSpin',
               'linger', 'rate', 'life', 'scaleStart', 'scaleEnd', 'fade', 'squash', 'scatter', 'gravity', 'trailAlign', 'randomTurn', 'style', 'textureMode',
               'slowScale', 'slowDuration', 'slowEase', 'slowEaseOut', 'slowScope']


def resources_dir():
    return os.path.join(g.ASSETS, 'Resources', 'ForgeProjectiles')


def game_name(pid):
    return PREFIX + pid


def published_names():
    """Projectiles uploaded to the game, from Resources/ForgeProjectiles/index.txt."""
    path = os.path.join(resources_dir(), 'index.txt')
    if not os.path.exists(path):
        return []
    return [line.strip() for line in open(path, encoding='utf-8') if line.strip()]


def _guid():
    return uuid.uuid4().hex


def _meta(path, kind, wrap=1):
    meta = path + '.meta'
    if os.path.exists(meta):
        return
    if kind == 'folder':
        body = 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    elif kind == 'text':
        body = 'TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    else:
        # uncompressed, no mipmaps, any size: the pictures are drawn at their own size
        body = ('TextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 13\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 0\n'
                '    sRGBTexture: 1\n    linearTexture: 0\n  isReadable: 0\n  textureFormat: 1\n  maxTextureSize: 4096\n  textureSettings:\n    serializedVersion: 2\n'
                '    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: %d\n    wrapV: 1\n    wrapW: 1\n  nPOTScale: 0\n  lightmap: 0\n  compressionQuality: 50\n'
                '  alphaUsage: 1\n  alphaIsTransparency: 1\n  textureType: 0\n  textureShape: 1\n  platformSettings:\n  - serializedVersion: 4\n'
                '    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 4096\n    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 0\n'
                '    compressionQuality: 50\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 0\n  userData: \n  assetBundleName: \n'
                '  assetBundleVariant: \n') % (0 if wrap == 0 else 1)
    open(meta, 'w', encoding='utf-8').write('fileFormatVersion: 2\nguid: %s\n%s' % (_guid(), body))


def _sized(layer):
    """The layer's picture at the size it is drawn (its w x h), as the Forge's preview stretches it."""
    image = g.sprite_image(layer['src'])
    w, h = max(1, int(round(abs(layer.get('w') or image.width)))), max(1, int(round(abs(layer.get('h') or image.height))))
    if image.size != (w, h):
        image = image.convert('RGBa').resize((w, h), Image.LANCZOS).convert('RGBA')
    return image


def bake_component(layers):
    """A component's visible layers as one picture and its pivot (0-1 from the bottom left) at the component's origin."""
    import publish
    images = {}
    keyed = []
    for i, layer in enumerate(layers):
        images['L%d' % i] = _sized(layer)
        keyed.append(dict(layer, src='L%d' % i))
    image, pivot = publish.bake_layers(keyed, source=lambda key: images[key])
    return image, (0.5 + pivot[0], 0.5 + pivot[1])


def publish(project):
    """Uploads the projectile to the game: Resources/ForgeProjectiles/<name>.json (its components and physics), a picture
    per component in <name>/, and its name in index.txt. Returns the game name."""
    pid = project.get('id')
    if not pid:
        raise ValueError('Save the projectile with a name first')
    name = game_name(pid)
    root = resources_dir()
    folder = os.path.join(root, name)
    os.makedirs(folder, exist_ok=True)
    _meta(root, 'folder')
    _meta(folder, 'folder')
    comps = [c for c in project['components'] if not (project['slots'].get(c['uid']) or {}).get('hidden')]
    index_of = {c['uid']: i for i, c in enumerate(comps)}
    written = set()
    out = []
    for c in comps:
        p = dict(physics(c['type']), **c['physics'])
        layers = [l for l in (project['slots'].get(c['uid']) or {}).get('layers', []) if not l.get('hidden')]
        entry = {'uid': c['uid'], 'name': c['name'], 'type': c['type'], 'spawn': p.get('spawn', 'fire'),
                 'of': index_of.get(p.get('of'), -1) if p.get('spawn') != 'fire' else -1, 'image': '', 'pivotX': 0.5, 'pivotY': 0.5, 'ribbonImage': ''}
        for k in GAME_FIELDS:
            v = p.get(k, PHYSICS.get(k))
            if v is None:
                continue
            entry[k] = v if isinstance(v, (bool, str)) else (int(round(v)) if k == 'count' else float(v))
        entry['vary'] = [{'key': k, 'range': float(v)} for k, v in (p.get('vary') or {}).items() if v]
        entry['alphaKeys'] = [{'t': float(t), 'a': float(a)} for t, a in (p.get('alphaKeys') or [])]
        if layers and p.get('spawn') == 'trail' and p.get('style') == 'ribbon':
            layer = layers[0]
            image = g.sprite_image(layer['src'])
            file = os.path.join(folder, c['uid'] + '_ribbon.png')
            image.save(file)
            _meta(file, 'texture', wrap=0 if p.get('textureMode') == 'tile' else 1)
            written.add(os.path.basename(file))
            entry.update({'ribbonImage': 'ForgeProjectiles/%s/%s_ribbon' % (name, c['uid']),
                          'ribbonWidth': float(abs(layer.get('h', image.height)) * abs(layer.get('sy', 1))), 'ribbonTile': float(abs(layer.get('w', image.width)))})
        elif layers:
            image, pivot = bake_component(layers)
            file = os.path.join(folder, c['uid'] + '.png')
            image.save(file)
            _meta(file, 'texture')
            written.add(os.path.basename(file))
            entry.update({'image': 'ForgeProjectiles/%s/%s' % (name, c['uid']), 'pivotX': round(pivot[0], 5), 'pivotY': round(pivot[1], 5)})
        out.append(entry)
    # pictures of components that are gone
    for f in os.listdir(folder):
        base = f[:-5] if f.endswith('.meta') else f
        if base.endswith('.png') and base not in written:
            os.remove(os.path.join(folder, f))
    path = os.path.join(root, name + '.json')
    json.dump({'name': project.get('name') or pid, 'components': out}, open(path, 'w', encoding='utf-8'), indent=1)
    _meta(path, 'text')
    names = published_names()
    if name not in names:
        names.append(name)
        index = os.path.join(root, 'index.txt')
        open(index, 'w', encoding='utf-8').write('\n'.join(sorted(names)) + '\n')
        _meta(index, 'text')
    project['published'] = name
    project['publishedAt'] = time.strftime('%Y-%m-%d %H:%M')
    save(project)
    return name


def published_project(name):
    """The saved project behind an uploaded projectile name, or None."""
    pid = name[len(PREFIX):] if name.startswith(PREFIX) else None
    if not pid or not os.path.exists(path(pid)):
        return None
    return load(pid)


def from_uploaded(name):
    """An uploaded projectile as an editable project: its saved project, or, when that file is gone (renamed or
    deleted), one rebuilt from what was uploaded (each component's baked picture as one layer, all its physics), saved
    as a project again and still linked to the same game projectile."""
    import shutil
    project = published_project(name)
    if project:
        return project
    src = os.path.join(resources_dir(), name + '.json')
    if not os.path.exists(src):
        raise ValueError('No uploaded projectile ' + name)
    data = json.load(open(src, encoding='utf-8'))
    pid = name[len(PREFIX):]
    project = blank(data.get('name') or pid)
    project['components'], project['slots'] = [], {}
    uids = [c['uid'] for c in data['components']]
    art = os.path.join(g.UPLOADS, 'projectiles', name)
    for c in data['components']:
        phys = physics(c['type'])
        for k in GAME_FIELDS:
            if k in c:
                phys[k] = c[k]
        phys['spawn'] = c.get('spawn', 'fire')
        phys['of'] = uids[c['of']] if c.get('of', -1) >= 0 and c['of'] < len(uids) else ''
        phys['vary'] = {v['key']: v['range'] for v in c.get('vary') or []}
        phys['alphaKeys'] = [[k['t'], k['a']] for k in c.get('alphaKeys') or []] or None
        project['components'].append({'uid': c['uid'], 'name': c.get('name') or c['type'], 'type': c['type'], 'physics': phys})
        layers = []
        picture = c.get('ribbonImage') or c.get('image')
        file = os.path.join(os.path.dirname(resources_dir()), picture + '.png') if picture else None
        if file and os.path.exists(file):
            os.makedirs(art, exist_ok=True)
            shutil.copyfile(file, os.path.join(art, os.path.basename(file)))
            w, h = Image.open(file).size
            layer = {'uid': uuid.uuid4().hex[:8], 'src': 'upload:projectiles/%s/%s' % (name, os.path.basename(file)), 'name': c.get('name') or 'Art',
                     'item': c.get('name') or 'Art', 'tx': 0, 'ty': 0, 'rot': 0, 'sx': 1, 'sy': 1, 'shear': 0}
            if c.get('ribbonImage'):
                layer.update({'w': float(c.get('ribbonTile') or w), 'h': float(c.get('ribbonWidth') or h), 'pivot': [0, 0]})
            else:
                # the baked picture's pivot (0-1 from its bottom left) is the component's origin
                layer.update({'w': float(w), 'h': float(h), 'pivot': [round(c.get('pivotX', 0.5) - 0.5, 5), round(c.get('pivotY', 0.5) - 0.5, 5)]})
            layers.append(layer)
        project['slots'][c['uid']] = {'layers': layers, 'hidden': False, 'active': layers[-1]['uid'] if layers else None}
    project['id'] = pid
    project['published'] = name
    project['recovered'] = time.strftime('%Y-%m-%d %H:%M')
    save(project)
    return project


def published_art(name):
    """PNG of an uploaded projectile's body (its first fired component), for the library and the combatant preview."""
    import io
    folder = os.path.join(resources_dir(), name)
    data = json.load(open(os.path.join(resources_dir(), name + '.json'), encoding='utf-8'))
    body = next((c for c in data['components'] if c['spawn'] == 'fire' and c.get('image')), None)
    if body:
        return open(os.path.join(folder, body['uid'] + '.png'), 'rb').read(), body, data
    buf = io.BytesIO()
    Image.new('RGBA', (2, 2), (0, 0, 0, 0)).save(buf, 'PNG')
    return buf.getvalue(), None, data


def published_info(name):
    """projectile_info's shape for an uploaded projectile: a straight flight of its flight time (its arc, bounces and
    trails play in the Projectiles section)."""
    png, body, data = published_art(name)
    w = h = 2
    local = [1, 0, 0, 1, 0, 0]
    if body:
        im = Image.open(os.path.join(resources_dir(), name, body['uid'] + '.png'))
        w, h = im.size
        # the sprite's centre relative to the shot's position
        local = [1, 0, 0, 1, (0.5 - body['pivotX']) * w, (0.5 - body['pivotY']) * h]
    return {'name': name, 'w': w, 'h': h, 'local': local, 'duration': (body or {}).get('flightTime', 0.25), 'curves': {}, 'curve': [],
            'image': '/projectileart/' + name, 'forge': True}
