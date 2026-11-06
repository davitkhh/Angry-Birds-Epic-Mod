# Compose a SmoothMoves rig's clip frame from source sprites (approximate preview).
import re, sys, math, os
from PIL import Image
ROOT = '/home/davit/Downloads/3.0.1Decomp/'
rig, clip, frame_no, out = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
overrides = dict(a.split('=') for a in sys.argv[5:])  # bone=texturename

def atlas_tex(path):
    t = open(path).read().split('\n')
    sec = None; d = {'textureGUIDs': [], 'defaultPivotOffsets': [], 'texturePaths': [], 'textureNames': []}
    for l in t:
        m = re.match(r'^  (\w+):', l)
        if m: sec = m.group(1); continue
        if sec in d and l.startswith('  - '):
            d[sec].append(l[4:].strip())
    res = {}
    for g, p, tp, n in zip(d['textureGUIDs'], d['defaultPivotOffsets'], d['texturePaths'], d['textureNames']):
        x, y = map(float, re.findall(r'[-\d.e]+', p))
        tp = tp.replace('Assets/Content', 'Assets/content')
        res[g] = (tp, (x, y), n)
    return res
tex = {}
for f in os.listdir(ROOT + 'Assets/MonoBehaviour'):
    if f.startswith(rig + '_Atlas') and f.endswith('.asset') and '_low' not in f:
        tex.update(atlas_tex(ROOT + 'Assets/MonoBehaviour/' + f))
byname = {v[2]: (k, v) for k, v in tex.items()}

lines = open(ROOT + 'Assets/MonoBehaviour/%s.asset' % rig).read().split('\n')
names = [l.split('boneName: ')[1] for l in lines if 'boneName: ' in l]
# dfs list
dfs = []; i = lines.index('  dfsBoneNodeList:') + 1
while lines[i].startswith('  - parentBoneIndex'):
    p = int(lines[i].split(': ')[1]); b = int(lines[i+1].split(': ')[1]); dfs.append((p, b)); i += 3
parent = {}
for p, b in dfs: parent[b] = dfs[p][1] if p >= 0 else -1
# clip
start = [k for k, l in enumerate(lines) if l == '  - animationName: ' + clip][0]
end = next(k for k in range(start + 1, len(lines)) if lines[k].startswith('  - animationName: ') or lines[k].startswith('  ') and not lines[k].startswith('   '))
state = {}
cur = None; kf = None; field = None
for l in lines[start:end]:
    m = re.match(r'^    - boneDataIndex: (\d+)', l)
    if m: cur = int(m.group(1)); continue
    m = re.match(r'^      - boneDataIndex: (\d+)', l)
    if m: kf = {}; continue
    m = re.match(r'^        (\w+):(.*)', l)
    if m:
        field = m.group(1); v = m.group(2).strip()
        if field == 'frame': kf['frame'] = int(v)
        if field in ('textureGUID', 'depth'): kf[field] = v
        if field == 'pivotOffset': kf['pivot'] = tuple(map(float, re.findall(r'[-\d.e]+', v)))
        if field == 'useDefaultPivot': kf['udp'] = v
        if field == 'useTextureGUID': kf['utg'] = v
        if field == 'usePivotOffset': kf['upo'] = v
        if field == 'useDepth': kf['ud'] = v
        if field == 'textureIndex' and kf.get('frame', 99) <= frame_no:
            st = state.setdefault(cur, {})
            for key in ('pos', 'rot', 'scl'):
                if key in kf: st[key] = kf[key]
            if kf.get('utg') == '1': st['tex'] = kf.get('textureGUID', '')
            if kf.get('upo') == '1': st['pivot'] = kf.get('pivot'); st['udp'] = kf.get('udp')
            if kf.get('ud') == '1': st['depth'] = int(kf['depth'])
        continue
    m = re.match(r'^          val: (.*)', l)
    if m and kf is not None:
        nums = list(map(float, re.findall(r'[-\d.e]+', m.group(1))))
        if field == 'localPosition3': kf['pos'] = nums
        if field == 'localRotation': kf['rot'] = nums[0]
        if field == 'localScale3': kf['scl'] = nums
world = {}
def W(b):
    if b in world: return world[b]
    st = state.get(b, {}); x, y, z = (st.get('pos') or [0, 0, 0])[:3]; r = st.get('rot', 0); sx, sy = (st.get('scl') or [1, 1, 1])[:2]
    if parent[b] < 0: world[b] = (x, y, r, sx, sy); return world[b]
    px, py, pr, psx, psy = W(parent[b]); a = math.radians(pr)
    wx = px + (x * psx) * math.cos(a) - (y * psy) * math.sin(a)
    wy = py + (x * psx) * math.sin(a) + (y * psy) * math.cos(a)
    world[b] = (wx, wy, pr + r, psx * sx, psy * sy); return world[b]
items = []
for b in range(len(names)):
    st = state.get(b, {}); g = st.get('tex')
    if names[b] in overrides:
        g = byname[overrides[names[b]]][0] if overrides[names[b]] in byname else None
        if g is None and overrides[names[b]] != 'none':
            p = overrides[names[b]]; items.append((st.get('depth', 0), b, p, (0, 0))); continue
    if not g or g not in tex: continue
    tp, piv, n = tex[g]
    if st.get('udp') == '0' and st.get('pivot'): piv = st['pivot']
    items.append((st.get('depth', 0), b, ROOT + tp, piv))
S = 1000; canvas = Image.new('RGBA', (S, S), (0, 0, 0, 0)); C = S // 2
for depth, b, p, piv in sorted(items, key=lambda t: -t[0]):
    im = Image.open(p).convert('RGBA'); wx, wy, r, sx, sy = W(b)
    w, h = im.size
    im = im.resize((max(1, int(round(w * abs(sx)))), max(1, int(round(h * abs(sy))))), Image.LANCZOS)
    if sx < 0: im = im.transpose(Image.FLIP_LEFT_RIGHT)
    # pivot offset: fraction of size, image space y up; centre shifts opposite
    cx, cy = -piv[0] * im.width, -piv[1] * im.height
    rim = im.rotate(r, expand=True, resample=Image.BICUBIC)
    a = math.radians(r); rx = cx * math.cos(a) - cy * math.sin(a); ry = cx * math.sin(a) + cy * math.cos(a)
    X = C + wx + rx - rim.width / 2; Y = C - (wy + ry) - rim.height / 2
    canvas.alpha_composite(rim, (int(round(X)), int(round(Y))))
    print(names[b], depth, os.path.basename(p), round(wx), round(wy), r, piv)
canvas = canvas.crop(canvas.getbbox()); canvas.save(out)
