# Fit the perspective camera + model pose to the sketch (arms define the perspective)
import math, random, json
import shirt as S

T = dict(  # screen targets (HR) measured on the sketch
    near_cuff=(304.0, 187.5, 236.0),   # x centre, top y, bottom y
    far_cuff=(136.5, 147.5, 175.0),
    near_sh=(267.5, 177.5),            # sleeve top where it leaves the torso
    far_sh=(147.5, 142.5),
    hem_l=(117.5, 285.0), hem_r=(237.5, 291.0), hem_low=304.0,
    side_l=(135.0, 232.0), side_r=(249.0, 255.0),
)
NAMES = ['yaw', 'roll', 'camx', 'camy', 'camz', 'pitch', 'f', 'u0', 'v0', 'beta', 'delta', 'L', 'rc', 'shy', 'wx', 'wz', 'hs']


def arm(sign, p):
    b = math.radians(p['beta'])
    d = (sign * math.cos(b), sign * p['delta'], -sign * math.sin(b))
    l = math.sqrt(sum(v * v for v in d))
    d = tuple(v / l for v in d)
    s = (sign * 44.0 * p['wx'], p['shy'], 0.0)
    up = (0, -1, 0)
    e1 = S.norm(S.cross(d, up))
    e2 = S.norm(S.cross(e1, d))
    def ring(t, r):
        c = tuple(s[i] + d[i] * p['L'] * t for i in range(3))
        return [S.proj(*[c[i] + (e1[i] * math.cos(a) + e2[i] * math.sin(a)) * r for i in range(3)])
                for a in [k * 2 * math.pi / 24 for k in range(24)]]
    return ring


def loss(v, verbose=False):
    p = dict(zip(NAMES, v))
    p['yaw'] = 40.0
    pen = max(0, abs(p['beta']) - 15) ** 2 * 20 + max(0, abs(p['wx'] - 0.9) - 0.12) ** 2 * 4000 + max(0, abs(p['wz'] - 0.9) - 0.15) ** 2 * 4000 \
        + max(0, 0.6 - p['hs']) ** 2 * 4000 + (max(0, p['L'] - 62) ** 2 + max(0, 34 - p['L']) ** 2) * 10
    S.SCALE_XZ = (p['wx'], p['wz'])
    if p['camz'] < 160 or p['f'] < 50 or not (0.5 < p['rc'] / 20 < 1.8) or p['L'] < 20:
        return 1e9
    S.set_cam(**{k: p[k] for k in NAMES[:9] + ['hs']})
    err = []
    for sign, key, sh in ((1, 'near_cuff', 'near_sh'), (-1, 'far_cuff', 'far_sh')):
        ring = arm(sign, p)
        cuff = ring(1.0, p['rc'])
        ys = [q[1] for q in cuff]
        xs = [q[0] for q in cuff]
        tx, ty0, ty1 = T[key]
        err += [(sum(xs) / len(xs) - tx), (min(ys) - ty0), (max(ys) - ty1)]
        top = min(ring(0.12, 20.0), key=lambda q: q[1])
        err += [(top[0] - T[sh][0]) * 0.3, (top[1] - T[sh][1]) * 0.4]
    hem = [S.proj(*S.section(-math.pi + 2 * math.pi * i / 120, 321, lump=False)) for i in range(120)]
    l = min(hem, key=lambda q: q[0]); r = max(hem, key=lambda q: q[0]); lo = max(q[1] for q in hem)
    err += [l[0] - T['hem_l'][0], (l[1] - T['hem_l'][1]) * 0.5, r[0] - T['hem_r'][0], (r[1] - T['hem_r'][1]) * 0.5,
            lo - T['hem_low']]
    for key, y, side in (('side_l', 232, min), ('side_r', 255, max)):
        pts = [S.proj(*S.section(-math.pi + 2 * math.pi * i / 120, y, lump=False)) for i in range(120)]
        q = side(pts, key=lambda q: q[0])
        err += [(q[0] - T[key][0]) * 0.7]
    top = [S.proj(*S.section(-math.pi + 2 * math.pi * i / 120, 184, lump=False)) for i in range(120)]
    err += [(min(q[1] for q in top) - 142.0) * 1.2]
    if verbose:
        print([round(e, 1) for e in err])
    return sum(e * e for e in err) + pen


def nelder(f, x0, step, iters=1500):
    n = len(x0)
    pts = [x0] + [[x0[j] + (step[j] if j == i else 0) for j in range(n)] for i in range(n)]
    vals = [f(p) for p in pts]
    for it in range(iters):
        order = sorted(range(n + 1), key=lambda i: vals[i])
        pts = [pts[i] for i in order]; vals = [vals[i] for i in order]
        c = [sum(pts[i][j] for i in range(n)) / n for j in range(n)]
        xr = [c[j] + (c[j] - pts[-1][j]) for j in range(n)]; fr = f(xr)
        if fr < vals[0]:
            xe = [c[j] + 2 * (c[j] - pts[-1][j]) for j in range(n)]; fe = f(xe)
            pts[-1], vals[-1] = (xe, fe) if fe < fr else (xr, fr)
        elif fr < vals[-2]:
            pts[-1], vals[-1] = xr, fr
        else:
            xc = [c[j] + 0.5 * (pts[-1][j] - c[j]) for j in range(n)]; fc = f(xc)
            if fc < vals[-1]:
                pts[-1], vals[-1] = xc, fc
            else:
                for i in range(1, n + 1):
                    pts[i] = [pts[0][j] + 0.5 * (pts[i][j] - pts[0][j]) for j in range(n)]
                    vals[i] = f(pts[i])
    i = min(range(n + 1), key=lambda i: vals[i])
    return pts[i], vals[i]


if __name__ == '__main__':
    x0 = [40, 9, -16, 239, 240, -22, 249, 161, 297, -15, 0.21, 59, 15, 228, 0.88, 1.07, 0.8]
    step = [0, 6, 30, 40, 80, 5, 60, 10, 10, 10, 0.1, 15, 4, 8, 0.1, 0.15, 0.1]
    best = None
    rnd = random.Random(1)
    for r in range(10):
        start = x0 if r == 0 else [x0[j] + rnd.uniform(-1, 1) * step[j] * 1.5 for j in range(len(x0))]
        x, v = nelder(loss, start, step)
        x, v = nelder(loss, x, [s * 0.3 for s in step])
        print(r, round(v, 1), [round(a, 2) for a in x])
        if best is None or v < best[1]:
            best = (x, v)
    loss(best[0], verbose=True)
    d = dict(zip(NAMES, best[0])); d['yaw'] = 40.0
    json.dump(d, open('camera_fit.json', 'w'), indent=1)
