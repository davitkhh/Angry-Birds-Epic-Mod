# Straw / hay strands in the game's outlined style
import math, random
from paint import *

HAY = [hexc('#f2cb4c'), hexc('#f7da6c'), hexc('#e8b93c'), hexc('#fbe896'), hexc('#d9a532')]
HAY_DARK = hexc('#b9862a')
HAY_LINE = hexc('#7a4e12')


def hay_layer(strands, seed=0, outline=0.55, under=True):
    """strands: list of (p0, p1, p2, width). Returns an RGBA layer."""
    rnd = random.Random(seed)
    s, c = new_surface()
    # dark under-layer for depth inside bundles
    if under:
        for p0, p1, p2, w in strands:
            c.set_source_rgba(*HAY_DARK)
            strand(c, p0, p1, p2, w * 1.25, w * 0.7)
            c.fill()
    for p0, p1, p2, w in strands:
        col = rnd.choice(HAY)
        c.set_source_rgba(*HAY_LINE)
        strand(c, p0, p1, p2, w + outline * 2, w * 0.3 + outline * 2)
        c.fill()
        c.set_source_rgba(*col)
        strand(c, p0, p1, p2, w, w * 0.3)
        c.fill()
        # light streak along the straw
        c.set_source_rgba(1, 0.98, 0.85, 0.55)
        q0 = bezier(p0, p1, p2, 0.08)
        q2 = bezier(p0, p1, p2, 0.7)
        q1 = bezier(p0, p1, p2, 0.4)
        nx, ny = q2[1] - q0[1], -(q2[0] - q0[0])
        L = math.hypot(nx, ny) or 1
        o = w * 0.18
        strand(c, (q0[0] + nx / L * o, q0[1] + ny / L * o), (q1[0] + nx / L * o, q1[1] + ny / L * o),
               (q2[0] + nx / L * o, q2[1] + ny / L * o), w * 0.28, w * 0.1)
        c.fill()
    return to_pil(s)


def burst(origin, direction, spread, n, length, width, gravity=0.0, seed=0, origin_jitter=(0, 0)):
    """fan of strands from origin along direction (radians), spread (radians)"""
    rnd = random.Random(seed)
    out = []
    for i in range(n):
        a = direction + rnd.uniform(-spread, spread)
        L = length * rnd.uniform(0.55, 1.0)
        ox = origin[0] + rnd.uniform(-1, 1) * origin_jitter[0]
        oy = origin[1] + rnd.uniform(-1, 1) * origin_jitter[1]
        bend = rnd.uniform(-0.25, 0.25)
        p2 = (ox + math.cos(a) * L, oy + math.sin(a) * L + gravity * L * 0.35)
        p1 = (ox + math.cos(a + bend) * L * 0.5, oy + math.sin(a + bend) * L * 0.5)
        out.append(((ox, oy), p1, p2, width * rnd.uniform(0.75, 1.15)))
    rnd.shuffle(out)
    return out
