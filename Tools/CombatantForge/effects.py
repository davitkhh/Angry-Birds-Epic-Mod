"""Battle visual effects and projectiles for the preview: prefabs as trees of drawable parts plus their legacy animation.

A prefab becomes nodes (one per GameObject: local position, z rotation, scale, active) of which some draw an image: a
mesh (MeshFilter + MeshRenderer, rasterised here once with its material's texture and colour), or a CHMeshSprite. Its
legacy Animation clips become curves (position, rotation, scale, colour alpha, active) the browser plays. Particles,
trails and Animator-driven parts aren't drawn.

Skill effects come from Root.unity's VisualEffectsBalancing: per skill AssetId, prefabs spawned at Start, Movement or
Impact on the attacker or its targets (SkillBattleDataBase.SpawnVisualEffects, BattleMgrBase.InstantiateEffect).
"""
import io
import json
import math
import os
import re
import struct
import threading

from PIL import Image, ImageDraw

import gamedata as g

_lock = threading.Lock()
_prefab_index = None
_settings = (None, {})
_infos = {}

TIMINGS = {0: 'Start', 1: 'Movement', 2: 'Impact', 3: 'Affected', 4: 'Triggered',
           # the Forge's own (ForgeEffectExtras.Timing*)
           10: 'SlowMotion', 11: 'ProjectileLaunch', 12: 'ProjectileHit', 13: 'ProjectileBounce', 14: 'ProjectileEnd'}
TARGETS = {0: 'Origin', 1: 'Target', 2: 'OriginArea', 3: 'TargetArea', 4: 'CenterArea', 5: 'GlobalCamera', 6: 'GlobalScreen', 7: 'SlowMotion',
           20: 'Projectile'}  # the Forge's "at the projectile" (BattleMgrBase.SpawnEffectAt)
ANCHORS = {0: 'Bottom', 1: 'Center', 2: 'MainHand', 3: 'OffHand'}
NUM = r'(-?[\d.]+(?:[eE][-+]?\d+)?|-?Infinity|NaN)'


def prefab_path(name):
    """Effect and projectile prefabs live under Resources/Prefabs; the asset providers load them by file name."""
    global _prefab_index
    with _lock:
        if _prefab_index is None:
            index = {}
            for folder, _, files in os.walk(os.path.join(g.ASSETS, 'Resources', 'Prefabs')):
                for f in files:
                    if f.endswith('.prefab'):
                        index.setdefault(f[:-7], os.path.join(folder, f))
            _prefab_index = index
        return _prefab_index.get(name)


# ------------------------------------------------------------------------------ skill effect settings

def extras(raw):
    """The Forge's settings of an effect entry (raw VisualEffect fields): {'text': {...}} for a SkillFX_Generic_Text
    with its own text, {'after': {...}} for after-images, {'slow': {...}} for slow motion, and 'textDefault' (the word a
    game entry shows). See ForgeEffectExtras.cs."""
    out = {}
    ident = str(raw.get('EffectTextIdent') or '').strip()
    if ident.startswith('forge:'):
        try:
            data = json.loads(ident[len('forge:'):])
        except ValueError:
            data = {}
        if str(raw.get('PrefabName') or '').strip() == 'Forge_AfterImages':
            out['after'] = {k: data[k] for k in ('duration', 'interval', 'life', 'opacity', 'tint', 'behind', 'spread', 'driftX', 'driftY', 'grow') if k in data}
        elif 'text' in data:
            out['text'] = {k: data[k] for k in ('text', 'color', 'outline', 'size', 'rotation', 'arc') if k in data}
        for k in ('turn', 'turnWith', 'bone', 'offsetX', 'offsetY'):
            if k in data:
                out[k] = data[k]
    elif ident and str(raw.get('HasText', '0')).strip() in ('1', 'True', 'true'):
        out['textDefault'] = g.loca_texts().get(ident, ident)
    if str(raw.get('TargetCombatant', '0')).strip() == '7':
        num = lambda k, d: float(raw.get(k) or d)
        out['slow'] = {'scale': num('SlowMotionTimeScale', 0.3), 'duration': num('SlowMotionDuration', 0.5), 'ease': num('InterpolationTime', 0.1)}
        if ident.startswith('forge:'):
            try:
                eo = json.loads(ident[len('forge:'):]).get('easeOut')
            except ValueError:
                eo = None
            if eo is not None and eo >= 0:
                out['slow']['easeOut'] = eo
    return out


def skill_effects(asset_id):
    """The VisualEffects listed for a skill AssetId in Root.unity, in order."""
    global _settings
    path = os.path.join(g.ASSETS, 'Scenes', 'Root.unity')
    stamp = os.path.getmtime(path)
    with _lock:
        if _settings[0] != stamp:
            text = g.read_text(path)
            i = text.index('  VisualEffectSettings:')
            seg = text[i:text.index('\n  BubbleSettings:', i)]
            table = {}
            for block in re.split(r'\n  - BalancingId: ', seg)[1:]:
                bid = block.split('\n', 1)[0].strip()
                effects = []
                for e in re.split(r'\n    - SpawnTiming: ', '\n' + block)[1:]:
                    f = dict(re.findall(r'^\s+(\w+): ?(.*)$', '      SpawnTiming: ' + e, re.M))
                    effects.append({'timing': TIMINGS.get(int(f.get('SpawnTiming', 0)), 'Start'), 'delay': float(f.get('DelayInSeconds', 0) or 0),
                                    'target': TARGETS.get(int(f.get('TargetCombatant', 0)), 'Origin'), 'anchor': ANCHORS.get(int(f.get('TargetAnchor', 0)), 'Bottom'),
                                    'prefab': f.get('PrefabName', '').strip(), 'onHit': f.get('OnHitEffect', '0').strip() == '1',
                                    'onHitPrefix': f.get('OnHitPrefix', '').strip(), 'doNotMirror': f.get('DoNotMirror', '0').strip() == '1',
                                    'raw': {k: v.strip() for k, v in f.items()}})
                    effects[-1].update(extras(effects[-1]['raw']))
                table[bid] = effects
            _settings = (stamp, table)
        return list(_settings[1].get(asset_id, []))


CAMERA_SHAKES = ['Camera_Shake_Subtle', 'Camera_Shake_Weak', 'Camera_Shake_Medium', 'Camera_Shake_Strong', 'Camera_Shake_Heavy', 'Camera_Shake_Massive',
                 'Camera_Shake_Earthquake']
_names = (None, None)


def effect_names():
    """Effects an ability can spawn: every prefab the game's effect provider loads (Root.unity entries under
    Prefabs/5_Skills), grouped by folder, plus the camera shakes (spawned by name). [{name, group}]"""
    global _names
    path = os.path.join(g.ASSETS, 'Scenes', 'Root.unity')
    stamp = os.path.getmtime(path)
    if _names[0] != stamp:
        text = g.read_text(path)
        seen, out = set(), []
        for name, where in re.findall(r'- NameId: (\S+)\n    AssetLoadingType: \d+\n    Path: ([^\n]*)', text):
            if '5_Skills/' not in where or name in seen:
                continue
            seen.add(name)
            group = re.sub(r'^\d+_', '', where.split('5_Skills/', 1)[1].split('/', 1)[0])
            out.append({'name': name, 'group': group})
        out.sort(key=lambda x: (x['group'], x['name']))
        out += [{'name': n, 'group': 'Camera'} for n in CAMERA_SHAKES]
        _names = (stamp, out)
    return _names[1]


# ------------------------------------------------------------------------------ prefab parsing

def _blocks(text):
    """{fileID: (type name, body)} of a Unity YAML file."""
    out = {}
    for m in re.finditer(r'^--- !u!\d+ &(\d+)(?: stripped)?\n(\w+):\n(.*?)(?=^--- |\Z)', text, re.M | re.S):
        out[m.group(1)] = (m.group(2), m.group(3))
    return out


def _vec(body, key, n):
    m = re.search(r'%s: \{x: %s, y: %s(?:, z: %s)?(?:, w: %s)?\}' % (key, NUM, NUM, NUM, NUM), body)
    if not m:
        return None
    return [float(v) for v in m.groups()[:n]]


def _guid(body, key):
    m = re.search(r'%s: \{fileID: \d+, guid: (\w+)' % key, body)
    return m.group(1) if m else None


def _material(guid):
    """(texture path, rgba colour, colour property name) of a material."""
    path = g.asset_path(guid) if guid else None
    if not path:
        return None, (1, 1, 1, 1), '_Color'
    text = g.read_text(path)
    tex = re.search(r'- _MainTex:\n\s+m_Texture: \{fileID: \d+, guid: (\w+)', text)
    tex_path = g.asset_path(tex.group(1)) if tex else None
    for prop in ('_TintColor', '_Color'):
        c = re.search(r'- %s: \{r: %s, g: %s, b: %s, a: %s\}' % (prop, NUM, NUM, NUM, NUM), text)
        if c:
            return tex_path, tuple(float(v) for v in c.groups()), prop
    return tex_path, (1, 1, 1, 1), '_Color'


def _mesh(guid):
    """(positions [(x, y)], uvs [(u, v)], triangles [(a, b, c)]) of a YAML mesh asset."""
    path = g.asset_path(guid) if guid else None
    if not path or not path.endswith('.asset'):
        return None
    text = g.read_text(path)
    count = int(re.search(r'm_VertexCount: (\d+)', text).group(1))
    size = int(re.search(r'm_DataSize: (\d+)', text).group(1))
    data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]*)', text).group(1))
    chans = re.findall(r'- stream: (\d+)\n\s+offset: (\d+)\n\s+format: (\d+)\n\s+dimension: (\d+)', text)
    if not count or len(chans) < 4 or not data:
        return None
    stride = size // count
    pos_off = int(chans[0][1])
    uv = next((c for c in chans[3:5] if int(c[3]) == 2 and int(c[2]) == 0), None)
    if uv is None:
        return None
    uv_off = int(uv[1])
    positions, uvs = [], []
    for i in range(count):
        base = i * stride
        x, y, _ = struct.unpack_from('<3f', data, base + pos_off)
        u, v = struct.unpack_from('<2f', data, base + uv_off)
        positions.append((x, y))
        uvs.append((u, v))
    idx = bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]*)', text).group(1))
    ind = list(struct.unpack('<%dH' % (len(idx) // 2), idx))
    tris = [tuple(ind[i:i + 3]) for i in range(0, len(ind) - 2, 3)]
    return positions, uvs, tris


def _raster_mesh(mesh_guid, mat_guid):
    """The mesh drawn with its texture and colour: (image, bounds [x0, y0, x1, y1] in mesh units), cached as a PNG."""
    mesh = _mesh(mesh_guid)
    tex_path, color, prop = _material(mat_guid)
    if not mesh or not tex_path or not os.path.exists(tex_path):
        return None, None
    positions, uvs, tris = mesh
    xs, ys = [p[0] for p in positions], [p[1] for p in positions]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    w, h = max(1.0, x1 - x0), max(1.0, y1 - y0)
    k = min(4.0, 512.0 / max(w, h))  # pixels per mesh unit
    W, H = max(1, int(math.ceil(w * k))), max(1, int(math.ceil(h * k)))
    try:
        tex = Image.open(tex_path).convert('RGBA')
    except OSError:
        return None, None
    tw, th = tex.size
    out = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    for a, b, c in tris:
        if max(a, b, c) >= len(positions):
            continue
        dst = [((positions[i][0] - x0) * k, (y1 - positions[i][1]) * k) for i in (a, b, c)]
        src = [(uvs[i][0] * tw, (1 - uvs[i][1]) * th) for i in (a, b, c)]
        # affine from destination pixels back to texture pixels
        (dx0, dy0), (dx1, dy1), (dx2, dy2) = dst
        det = (dx1 - dx0) * (dy2 - dy0) - (dx2 - dx0) * (dy1 - dy0)
        if abs(det) < 1e-9:
            continue
        coeffs = []
        for s in (0, 1):
            s0, s1, s2 = src[0][s], src[1][s], src[2][s]
            ca = ((s1 - s0) * (dy2 - dy0) - (s2 - s0) * (dy1 - dy0)) / det
            cb = ((s2 - s0) * (dx1 - dx0) - (s1 - s0) * (dx2 - dx0)) / det
            coeffs += [ca, cb, s0 - ca * dx0 - cb * dy0]
        piece = tex.transform((W, H), Image.AFFINE, tuple(coeffs), Image.BILINEAR)
        mask = Image.new('L', (W, H), 0)
        ImageDraw.Draw(mask).polygon(dst, fill=255)
        out.paste(piece, (0, 0), mask)
    # the material's colour; _TintColor shaders double it (Unity's particle convention)
    gain = 2.0 if prop == '_TintColor' else 1.0
    r, gg, bb, _ = color
    if (r, gg, bb) != (1, 1, 1) or gain != 1:
        cr, cg, cb, ca = out.split()
        cr = cr.point(lambda v: min(255, int(v * r * gain)))
        cg = cg.point(lambda v: min(255, int(v * gg * gain)))
        cb = cb.point(lambda v: min(255, int(v * bb * gain)))
        out = Image.merge('RGBA', (cr, cg, cb, ca))
    return out, [x0, y0, x1, y1]


def _texture_image(mat_guid, tiles):
    """A particle or trail material's texture (its first frame for a sprite-sheet particle), coloured like the material."""
    tex_path, color, prop = _material(mat_guid)
    if not tex_path or not os.path.exists(tex_path):
        return None
    image = Image.open(tex_path).convert('RGBA')
    tx, ty = tiles
    if tx > 1 or ty > 1:
        w, h = image.size
        image = image.crop((0, 0, w // tx, h // ty))
    gain = 2.0 if prop == '_TintColor' else 1.0
    r, gg, bb, a = color
    if (r, gg, bb) != (1, 1, 1) or gain != 1:
        cr, cg, cb, ca = image.split()
        image = Image.merge('RGBA', (cr.point(lambda v: min(255, int(v * r * gain))), cg.point(lambda v: min(255, int(v * gg * gain))),
                                     cb.point(lambda v: min(255, int(v * bb * gain))), ca))
    return image


def image_png(key):
    """PNG for an image key from info(): mesh:<mesh guid>:<material guid> or sprite:<atlas guid>:<name>."""
    path = os.path.join(g.CACHE, 'fx', re.sub(r'[^\w.-]', '_', key) + '.%d.png' % g.CACHE_VERSION)
    if os.path.exists(path):
        return open(path, 'rb').read()
    kind, _, rest = key.partition(':')
    if kind == 'mesh':
        mesh, _, mat = rest.partition(':')
        image = _raster_mesh(mesh, mat)[0]
    elif kind == 'tex':
        mat, _, tiles = rest.partition(':')
        tx, _, ty = (tiles or '1x1').partition('x')
        image = _texture_image(mat, (int(tx or 1), int(ty or 1)))
    else:
        image = g.sprite_image(rest)
    if image is None:
        image = Image.new('RGBA', (2, 2), (0, 0, 0, 0))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    image.save(path)
    return open(path, 'rb').read()


# ------------------------------------------------------------------------------ legacy animation clips

def _keys(chunk, n):
    if n == 1:
        rows = re.findall(r'time: %s\n\s+value: %s\n\s+inSlope: %s\n\s+outSlope: %s' % (NUM, NUM, NUM, NUM), chunk)
        return [[float(t), [float(v)], [float(i)], [float(o)]] for t, v, i, o in rows]
    vec = r'\{x: %s, y: %s, z: %s%s\}' % (NUM, NUM, NUM, ', w: ' + NUM if n == 4 else '')
    rows = re.findall(r'time: %s\n\s+value: %s\n\s+inSlope: %s\n\s+outSlope: %s' % (NUM, vec, vec, vec), chunk)
    out = []
    for r in rows:
        f = [float(v) for v in r]
        out.append([f[0], f[1:1 + n], f[1 + n:1 + 2 * n], f[1 + 2 * n:1 + 3 * n]])
    return out


def clip(guid):
    """{length, loop, curves: [{path, kind, attr, keys}]} of a legacy .anim. kind: pos, rot (quaternion), euler, scale
    or float (attr: the animated property, such as _Color.a or m_IsActive)."""
    path = g.asset_path(guid) if guid else None
    if not path or not path.endswith('.anim'):
        return None
    text = g.read_text(path)
    curves = []
    for section, kind, n in (('m_RotationCurves', 'rot', 4), ('m_EulerCurves', 'euler', 3), ('m_PositionCurves', 'pos', 3),
                             ('m_ScaleCurves', 'scale', 3), ('m_FloatCurves', 'float', 1)):
        m = re.search(r'^  %s:\n(.*?)(?=^  m_\w+:)' % section, text, re.M | re.S)
        if not m:
            continue
        for item in re.split(r'\n  - curve:', '\n' + m.group(1))[1:]:
            p = re.search(r'\n    path: ?(.*)', item)
            attr = re.search(r'\n    attribute: ?(.*)', item)
            keys = _keys(item, n)
            if keys:
                curves.append({'path': p.group(1).strip() if p else '', 'kind': kind, 'attr': attr.group(1).strip().strip("'") if attr else '', 'keys': keys})
    stop = re.search(r'm_StopTime: %s' % NUM, text)
    wrap = re.search(r'm_WrapMode: (\d+)', text)
    return {'length': float(stop.group(1)) if stop else 1.0, 'loop': bool(wrap and wrap.group(1) == '2'), 'curves': curves}


# ------------------------------------------------------------------------------ prefab -> drawable tree

# ------------------------------------------------------------------------------ particle systems

def _sub(body, field, indent=None):
    """The YAML block under 'field:' (its deeper-indented lines), or '' when it's not there."""
    m = re.search(r'^( *)%s:[ ]*\n' % re.escape(field), body, re.M) if indent is None else re.search(r'^%s%s:[ ]*\n' % (' ' * indent, re.escape(field)), body, re.M)
    if not m:
        return ''
    depth = len(m.group(1)) if indent is None else indent
    lines = []
    for line in body[m.end():].split('\n'):
        if line.strip() and len(line) - len(line.lstrip(' ')) <= depth and not line.lstrip().startswith('- ') or (line.strip() and len(line) - len(line.lstrip(' ')) < depth):
            break
        lines.append(line)
    return '\n'.join(lines)


def _num(body, field, default=0.0):
    m = re.search(r'^\s*%s: %s' % (re.escape(field), NUM), body, re.M)
    return float(m.group(1)) if m else default


def _curve(block):
    """[[time, value, inSlope, outSlope]] of an AnimationCurve block (Infinity slopes become 0: a step)."""
    out = []
    for k in re.findall(r'time: %s\n\s+value: %s\n\s+inSlope: (\S+)\n\s+outSlope: (\S+)' % (NUM, NUM), block):
        sl = [float(v) if re.match(r'^-?[\d.]+(e[-+]?\d+)?$', v, re.I) else 0.0 for v in k[2:]]
        out.append([float(k[0]), float(k[1])] + sl)
    return out


def _mmc(body, field):
    """A MinMaxCurve: {'s': state (0 constant, 1 curve, 2 random between curves, 3 random between constants),
    'hi', 'lo', 'max', 'min'}; None when absent."""
    b = _sub(body, field)
    if not b:
        m = re.search(r'^\s*%s: %s' % (re.escape(field), NUM), body, re.M)  # an old plain number
        return {'s': 0, 'hi': float(m.group(1)), 'lo': 0.0, 'max': [], 'min': []} if m else None
    return {'s': int(_num(b, 'minMaxState', 0)), 'hi': _num(b, 'scalar', 0.0), 'lo': _num(b, 'minScalar', 0.0),
            'max': _curve(_sub(b, 'maxCurve')), 'min': _curve(_sub(b, 'minCurve'))}


def _grad(block):
    """A Gradient: {'c': [[t, r, g, b]], 'a': [[t, a]], 'fixed'}."""
    if not block:
        return None
    keys = [tuple(float(v) for v in m) for m in re.findall(r'key\d: \{r: %s, g: %s, b: %s, a: %s\}' % (NUM, NUM, NUM, NUM), block)]
    ct = [int(v) / 65535 for v in re.findall(r'ctime\d: (\d+)', block)]
    at = [int(v) / 65535 for v in re.findall(r'atime\d: (\d+)', block)]
    nc, na = int(_num(block, 'm_NumColorKeys', 2)), int(_num(block, 'm_NumAlphaKeys', 2))
    return {'c': [[ct[i], keys[i][0], keys[i][1], keys[i][2]] for i in range(min(nc, len(keys), len(ct)))],
            'a': [[at[i], keys[i][3]] for i in range(min(na, len(keys), len(at)))], 'fixed': _num(block, 'm_Mode', 0) == 1}


def _mmg(body, field):
    """A MinMaxGradient: {'s': state (0 colour, 1 gradient, 2 random between colours, 3 between gradients, 4 random
    from the gradient), 'hi', 'lo' colours, 'gmax', 'gmin'}."""
    b = _sub(body, field)
    if not b:
        return None
    def col(k):
        m = re.search(r'%s: \{r: %s, g: %s, b: %s, a: %s\}' % (k, NUM, NUM, NUM, NUM), b)
        return [float(v) for v in m.groups()] if m else [1.0, 1.0, 1.0, 1.0]
    return {'s': int(_num(b, 'minMaxState', 0)), 'hi': col('maxColor'), 'lo': col('minColor'),
            'gmax': _grad(_sub(b, 'maxGradient')), 'gmin': _grad(_sub(b, 'minGradient'))}


def _module(body, name):
    b = _sub(body, name, 2)
    return b if b and re.search(r'^\s*enabled: 1', b, re.M) else ''


def particles(ps, renderer):
    """What the preview needs to simulate a ParticleSystem (app.js drawParticles); None when it draws nothing."""
    if not renderer or not re.search(r'm_Enabled: 1', renderer):
        return None
    mat = re.search(r'm_Materials:\n  - \{fileID: \d+, guid: (\w+)', renderer)
    if not mat:
        return None
    tex_path, _, _ = _material(mat.group(1))
    if not tex_path or not os.path.exists(tex_path):
        return None
    init = _sub(ps, 'InitialModule', 2)
    emit = _sub(ps, 'EmissionModule', 2)
    shape, vel, force, clamp = _module(ps, 'ShapeModule'), _module(ps, 'VelocityModule'), _module(ps, 'ForceModule'), _module(ps, 'ClampVelocityModule')
    size_l, color_l, rot_l, uv = _module(ps, 'SizeModule'), _module(ps, 'ColorModule'), _module(ps, 'RotationModule'), _module(ps, 'UVModule')
    bursts = []
    for m in re.finditer(r'- time: %s\n\s+minCount: (\d+)\n\s+maxCount: (\d+)(?:\n\s+cycleCount: (\d+))?(?:\n\s+repeatInterval: %s)?' % (NUM, NUM), emit):
        bursts.append([float(m.group(1)), int(m.group(2)), int(m.group(3)), int(m.group(4) or 1), float(m.group(5) or 0.01)])
    if not bursts:  # older layout: time0..3 / cnt0..3
        n = int(_num(emit, 'm_BurstCount', 0))
        for i in range(n):
            c = int(_num(emit, 'cnt%d' % i, 0))
            bursts.append([_num(emit, 'time%d' % i, 0), c, int(_num(emit, 'cntmax%d' % i, c)), 1, 0.01])
    radius = _sub(shape, 'radius')
    tiles = (int(_num(uv, 'tilesX', 1)), int(_num(uv, 'tilesY', 1))) if uv else (1, 1)
    return {
        'image': 'tex:%s:1x1' % mat.group(1), 'tiles': tiles,
        'duration': _num(ps, 'lengthInSec', 1), 'loop': _num(ps, 'looping', 0) == 1, 'delay': _mmc(ps, 'startDelay'),
        'world': _num(ps, 'moveWithTransform', 1) == 0, 'max': int(_num(init, 'maxNumParticles', 1000)),
        'life': _mmc(init, 'startLifetime'), 'speed': _mmc(init, 'startSpeed'), 'size': _mmc(init, 'startSize'),
        'rot': _mmc(init, 'startRotation'), 'color': _mmg(init, 'startColor'), 'gravity': _mmc(init, 'gravityModifier'),
        'rate': _mmc(emit, 'rateOverTime') if emit and re.search(r'^\s*enabled: 1', emit, re.M) else None,
        'bursts': bursts if emit and re.search(r'^\s*enabled: 1', emit, re.M) else [],
        'shape': {'type': int(_num(shape, 'type', 4)), 'radius': (_num(radius, 'value', 1) if radius else _num(shape, 'radius', 1)), 'angle': _num(shape, 'angle', 25),
                  'box': [_num(shape, 'boxX', 1), _num(shape, 'boxY', 1)], 'arc': (_num(_sub(shape, 'arc'), 'value', 360) if _sub(shape, 'arc') else _num(shape, 'arc', 360))} if shape else None,
        'vel': {'x': _mmc(vel, 'x'), 'y': _mmc(vel, 'y')} if vel else None,
        'force': {'x': _mmc(force, 'x'), 'y': _mmc(force, 'y')} if force else None,
        'clamp': {'mag': _mmc(clamp, 'magnitude'), 'dampen': _num(clamp, 'dampen', 1)} if clamp else None,
        'sizeLife': _mmc(size_l, 'curve') if size_l else None,
        'colorLife': _mmg(color_l, 'gradient') if color_l else None,
        'rotLife': _mmc(rot_l, 'curve') if rot_l else None,
        'uv': {'frame': _mmc(uv, 'frameOverTime'), 'start': _mmc(uv, 'startFrame'), 'cycles': _num(uv, 'cycles', 1)} if uv else None,
        'render': {'mode': int(_num(renderer, 'm_RenderMode', 0)), 'length': _num(renderer, 'm_LengthScale', 2), 'velocity': _num(renderer, 'm_VelocityScale', 0)},
    }


def info(name):
    """{name, life, nodes, clips, tween} for an effect or projectile prefab; None when there's no such prefab."""
    path = prefab_path(name)
    if not path:
        return None
    stamp = os.path.getmtime(path)
    with _lock:
        cached = _infos.get(name)
        if cached and cached[0] == stamp:
            return cached[1]
    text = g.read_text(path)
    blocks = _blocks(text)
    gos = {fid: body for fid, (kind, body) in blocks.items() if kind == 'GameObject'}
    transforms = {}
    for fid, (kind, body) in blocks.items():
        if kind == 'Transform':
            go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1)
            transforms[go] = {'id': fid, 'body': body, 'father': re.search(r'm_Father: \{fileID: (\d+)\}', body).group(1)}
    by_tid = {t['id']: go for go, t in transforms.items()}
    comps = {}
    for fid, (kind, body) in blocks.items():
        m = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
        if m and kind != 'Transform':
            comps.setdefault(m.group(1), []).append((kind, body))
    root = next((go for go, t in transforms.items() if t['father'] == '0'), None)
    if root is None:
        return None
    order, paths = [], {}

    def walk(go, prefix):
        order.append(go)
        paths[go] = prefix
        kids = [c for c, t in transforms.items() if t['father'] == transforms[go]['id']]
        for c in sorted(kids, key=lambda c: int(re.search(r'm_RootOrder: (\d+)', transforms[c]['body']).group(1)) if re.search(r'm_RootOrder: (\d+)', transforms[c]['body']) else 0):
            name_m = re.search(r'm_Name: (.*)', gos.get(c, ''))
            walk(c, (prefix + '/' if prefix else '') + (name_m.group(1).strip() if name_m else c))
    walk(root, '')
    index = {go: i for i, go in enumerate(order)}
    nodes, clips, tween, life, arrival_id = [], [], None, 0.0, None
    world3 = {}  # each node's full 3D rotation and scale from the root (static), for particle emitters tilted in 3D
    for go in order:
        t = transforms[go]['body']
        q = _vec(t, 'm_LocalRotation', 4) or [0, 0, 0, 1]
        pos = _vec(t, 'm_LocalPosition', 3) or [0, 0, 0]
        scale = _vec(t, 'm_LocalScale', 3) or [1, 1, 1]
        x, y, z, w = q
        rot = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
               [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
               [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
        local3 = [[rot[r][c] * scale[c] for c in range(3)] for r in range(3)]
        parent3 = world3.get(by_tid.get(transforms[go]['father']))
        world3[go] = [[sum(parent3[r][k] * local3[k][c] for k in range(3)) for c in range(3)] for r in range(3)] if parent3 else local3
        active = re.search(r'm_IsActive: (\d)', gos.get(go, ''))
        node = {'path': paths[go], 'parent': index.get(by_tid.get(transforms[go]['father'])), 'pos': pos[:3], 'angle': 2 * math.atan2(q[2], q[3]),
                'scale': scale[:2], 'active': not active or active.group(1) == '1'}
        kinds = dict((k, b) for k, b in comps.get(go, []))
        if 'MeshFilter' in kinds and 'MeshRenderer' in kinds and re.search(r'm_Enabled: 1', kinds['MeshRenderer']):
            mesh_guid, mat_guid = _guid(kinds['MeshFilter'], 'm_Mesh'), re.search(r'm_Materials:\n  - \{fileID: \d+, guid: (\w+)', kinds['MeshRenderer'])
            if mesh_guid and mat_guid:
                # bounds straight from the vertices: the image itself is drawn only when it's asked for (image_png)
                mesh = _mesh(mesh_guid)
                tex_path, color, prop = _material(mat_guid.group(1))
                if mesh and tex_path and os.path.exists(tex_path):
                    xs, ys = [q[0] for q in mesh[0]], [q[1] for q in mesh[0]]
                    bounds = [min(xs), min(ys), max(xs), max(ys)]
                    node.update({'image': 'mesh:%s:%s' % (mesh_guid, mat_guid.group(1)), 'bounds': bounds,
                                 'alpha': min(1.0, color[3] * (2.0 if prop == '_TintColor' else 1.0)), 'alphaGain': 2.0 if prop == '_TintColor' else 1.0})
        if 'ParticleSystem' in kinds:
            try:
                spec = particles(kinds['ParticleSystem'], kinds.get('ParticleSystemRenderer'))
            except Exception as ex:  # a layout this parser doesn't know: the rest of the effect still draws
                spec = None
                print('particles of %s/%s: %s' % (name, paths[go], ex))
            if spec:
                # shape space (x, y, z) onto the screen plane through the emitter's whole 3D orientation, without the
                # 2D part drawFxTree already applies to the node (its position); and the hierarchy's size scale
                m3 = world3[go]
                spec['basis'] = [[round(v, 5) for v in m3[0]], [round(v, 5) for v in m3[1]]]
                spec['hscale'] = round(abs(m3[0][0] * m3[1][1] - m3[0][1] * m3[1][0]) ** 0.5 or 1.0, 5)
                spec['scaling'] = int(_num(kinds['ParticleSystem'], 'scalingMode', 0))
                node['particles'] = spec
        for kind, body in comps.get(go, []):
            if kind != 'MonoBehaviour':
                continue
            sprite = re.search(r'm_SpriteName: (.*)', body)
            atlas = _guid(body, 'm_SmoothMovesAtlas')
            label = re.search(r'\n  mText: (.*)', body)
            if label and re.search(r'\n  mFontSize: ', body) and 'image' not in node:
                col = re.search(r'\n  mColor: \{r: %s, g: %s, b: %s, a: %s\}' % (NUM, NUM, NUM, NUM), body)
                ecol = re.search(r'\n  mEffectColor: \{r: %s, g: %s, b: %s, a: %s\}' % (NUM, NUM, NUM, NUM), body)
                node['label'] = {'text': label.group(1).strip(), 'size': _num(body, 'mFontSize', 32),
                                 'color': [float(v) for v in col.groups()] if col else [1, 1, 1, 1],
                                 'effect': int(_num(body, 'mEffectStyle', 0)), 'effectColor': [float(v) for v in ecol.groups()] if ecol else [0, 0, 0, 1]}
                node['alpha'] = node['label']['color'][3]
            if sprite and atlas and 'image' not in node:
                w = float(re.search(r'm_Width: ([-\d.]+)', body).group(1))
                h = float(re.search(r'm_Height: ([-\d.]+)', body).group(1))
                col = re.search(r'm_Color: \{r: %s, g: %s, b: %s, a: %s\}' % (NUM, NUM, NUM, NUM), body)
                node.update({'image': 'sprite:%s:%s' % (atlas, sprite.group(1).strip()), 'bounds': [-w / 2, -h / 2, w / 2, h / 2],
                             'alpha': float(col.group(4)) if col else 1.0, 'alphaGain': 1.0})
            if 'm_AnimationCurvesPerAxis:' in body and tween is None:
                tween = index[go]
            # ProjectileArrivalEffects: the effect setting it plays on the combatant it reaches
            arrival = re.search(r'm_EffectSettingId: (\S+)', body)
            if arrival and not arrival_id:
                arrival_id = arrival.group(1)
            d = re.search(r'm_DestroyTime: %s' % NUM, body)
            if d:
                life = max(life, float(d.group(1)))
        if 'Animation' in kinds:
            for guid in re.findall(r'- \{fileID: 7400000, guid: (\w+)', kinds['Animation']):
                c = clip(guid)
                if c:
                    c['owner'] = index[go]
                    clips.append(c)
                    break
        nodes.append(node)
    life = life or max([c['length'] for c in clips] + [0.5])
    result = {'name': name, 'life': life, 'nodes': nodes, 'clips': clips, 'tween': tween,
              'arrival': [e for e in skill_effects(arrival_id) if e['timing'] == 'Impact'] if arrival_id else []}
    with _lock:
        _infos[name] = (stamp, result)
    return result


# ------------------------------------------------------------------------------ the library's Effects category

FX_FOLDERS = ('5_Skills', os.path.join('4_Characters', '4_Projectiles'))


def _prefab_files():
    root = os.path.join(g.ASSETS, 'Resources', 'Prefabs')
    out = []
    for sub in FX_FOLDERS:
        for folder, _, files in os.walk(os.path.join(root, sub)):
            out += [os.path.join(folder, f) for f in sorted(files) if f.endswith('.prefab')]
    return out


def _short(name):
    return re.sub(r'^(SkillFX_|SKillFX_|ScreenFX_|Projectile_)', '', name)


_library_lock = threading.Lock()


def library_items():
    with _library_lock:  # built once in the background at start (forge.make_server); later calls read the cache
        return _library_items()


def _library_items():
    """Every distinct image in the game's effect and projectile prefabs, as library items (category Effect): the
    drawable parts (meshes, sprites), then particle and trail textures. Grouped by folder (Hits, Spells, ...)."""
    files = _prefab_files()
    stamp = '%d-%d' % (len(files), int(max(os.path.getmtime(f) for f in files)) if files else 0)
    cache = os.path.join(g.CACHE, 'fx', 'library.%d.json' % g.CACHE_VERSION)
    try:
        saved = json.load(open(cache, encoding='utf-8'))
        if saved.get('stamp') == stamp:
            return saved['items']
    except (OSError, ValueError):
        pass
    items, seen = [], set()

    def add(group, label, key, w, h, center=(0.0, 0.0)):
        if key in seen or w <= 1 or h <= 1:
            return
        seen.add(key)
        pivot = [round(-center[0] / w, 5), round(-center[1] / h, 5)]
        items.append({'id': 'fx/' + key, 'side': 'pig', 'rig': group, 'name': label, 'cat': 'Effect', 'group': group,
                      'parts': {'front': {'key': key, 'name': label, 'w': round(w, 2), 'h': round(h, 2), 'pivot': pivot}}})

    for path in files:
        name = os.path.basename(path)[:-7]
        folder = os.path.basename(os.path.dirname(path))
        group = re.sub(r'^\d+_', '', folder)
        try:
            data = info(name)
        except Exception:
            data = None
        for n in (data or {}).get('nodes', []):
            if not n.get('image'):
                continue
            x0, y0, x1, y1 = n['bounds']
            key = 'fx:' + n['image'] if n['image'].startswith('mesh:') else n['image'].split(':', 1)[1]
            part = n['path'].split('/')[-1]
            add(group, '%s · %s' % (_short(name), part) if part and part != name else _short(name), key, x1 - x0, y1 - y0, ((x0 + x1) / 2, (y0 + y1) / 2))
        # particles and trails: their material's texture
        text = g.read_text(path)
        blocks = _blocks(text)
        uv = {}
        for kind, body in blocks.values():
            if kind == 'ParticleSystem':
                go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
                m = re.search(r'UVModule:\n\s+enabled: (\d)\n.*?tilesX: (\d+)\n\s+tilesY: (\d+)', body, re.S)
                if go and m and m.group(1) == '1':
                    uv[go.group(1)] = (int(m.group(2)), int(m.group(3)))
        for kind, body in blocks.values():
            if kind not in ('ParticleSystemRenderer', 'TrailRenderer'):
                continue
            mat = re.search(r'm_Materials:\n  - \{fileID: \d+, guid: (\w+)', body)
            go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
            if not mat:
                continue
            tex_path, _, _ = _material(mat.group(1))
            if not tex_path or not os.path.exists(tex_path) or tex_path in seen:
                continue
            seen.add(tex_path)
            tiles = uv.get(go.group(1) if go else '', (1, 1)) if kind == 'ParticleSystemRenderer' else (1, 1)
            try:
                with Image.open(tex_path) as im:
                    w, h = im.size
            except OSError:
                continue
            w, h = w / tiles[0], h / tiles[1]
            k = min(1.0, 128.0 / max(w, h))
            label = os.path.splitext(os.path.basename(tex_path))[0]
            add('Trails' if kind == 'TrailRenderer' else 'Particles', label, 'fx:tex:%s:%dx%d' % (mat.group(1), tiles[0], tiles[1]), w * k, h * k)
    items.sort(key=lambda i: (i['group'], i['name']))
    os.makedirs(os.path.dirname(cache), exist_ok=True)
    json.dump({'stamp': stamp, 'items': items}, open(cache, 'w', encoding='utf-8'))
    return items

