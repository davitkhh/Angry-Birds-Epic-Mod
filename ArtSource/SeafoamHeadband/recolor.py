"""Seafoam headband: recolour of PigPirate Headgear_Pirate.png (pure Python + PIL, no numpy).

red stripes   -> saturated light seafoam blue
black stripes -> near-white, with thin golden-yellow edges where they meet the blue
outline       -> kept dark (slight teal tint)

Usage (from the project root): python3 ArtSource/SeafoamHeadband/recolor.py [out.png]
"""
import sys
from PIL import Image, ImageFilter

SRC = "Assets/content/4_Characters/2_Pigs/6_PigPirate/BodyParts/Headgear_Pirate.png"
OUT = sys.argv[1] if len(sys.argv) > 1 else "ArtSource/SeafoamHeadband/Headgear_PirateSeafoam.png"

# ramps: brightness factor (1 = the stripe's lit base colour) -> RGB
WHITE = [(0.00, (22, 34, 38)), (0.35, (112, 134, 140)), (0.60, (178, 196, 200)),
         (0.85, (226, 236, 236)), (1.00, (246, 251, 249))]
SEAFOAM = [(0.00, (14, 46, 52)), (0.40, (14, 120, 132)), (0.55, (22, 160, 170)),
           (0.80, (48, 206, 210)), (1.00, (84, 232, 226))]
GOLD = [(0.00, (40, 30, 8)), (0.35, (140, 92, 10)), (0.60, (206, 148, 20)),
        (0.85, (240, 192, 40)), (1.00, (255, 216, 72))]
OUTLINE = (24, 36, 40)
FOLD = (96, 124, 132)     # fold-line colour inside the white stripe
GOLD_WIDTH = 2.0   # px, measured inward from the white/seafoam border
WHITE_SHADOW = 0.6  # how much of the source's black-stripe shadow depth the white keeps
OUTLINE_REACH = 3.0  # px from the transparent outside where dark pixels count as outline
EDGE_SMOOTH = 1.6  # px blur on the stripe boundary (the source edges are blocky 4px steps)

src = Image.open(SRC).convert("RGBA")
W, H = src.size
px = list(src.getdata())
N = W * H


def ramp(stops, t):
    t = min(max(t, 0.0), 1.0)
    for (x0, c0), (x1, c1) in zip(stops, stops[1:]):
        if t <= x1:
            f = (t - x0) / (x1 - x0)
            return tuple(c0[i] + (c1[i] - c0[i]) * f for i in range(3))
    return stops[-1][1]


def lerp(c0, c1, f):
    return tuple(c0[i] + (c1[i] - c0[i]) * f for i in range(3))


def dist_from(mask, maxd, W, H):
    """Chamfer distance (px) from True pixels, capped at maxd."""
    INF = 1e9
    d = [0.0 if m else INF for m in mask]
    D = 1.4142
    for _ in range(2):  # forward + backward passes, twice for safety
        for y in range(H):
            for x in range(W):
                i = y * W + x
                v = d[i]
                if x > 0: v = min(v, d[i - 1] + 1)
                if y > 0:
                    v = min(v, d[i - W] + 1)
                    if x > 0: v = min(v, d[i - W - 1] + D)
                    if x < W - 1: v = min(v, d[i - W + 1] + D)
                d[i] = v
        for y in range(H - 1, -1, -1):
            for x in range(W - 1, -1, -1):
                i = y * W + x
                v = d[i]
                if x < W - 1: v = min(v, d[i + 1] + 1)
                if y < H - 1:
                    v = min(v, d[i + W] + 1)
                    if x < W - 1: v = min(v, d[i + W + 1] + D)
                    if x > 0: v = min(v, d[i + W - 1] + D)
                d[i] = v
    return [min(v, maxd) for v in d]


vis = [p[3] > 8 for p in px]
mx = [max(p[:3]) for p in px]
red = [vis[i] and px[i][0] > px[i][1] + 100 for i in range(N)]                      # solid red
blk = [vis[i] and px[i][0] - px[i][1] < 12 and mx[i] >= 50 for i in range(N)]      # lit black (~#3C3C3C)
# distance from the outside; the image border counts as outside (the outline touches the top edge)
_pad = [True] * ((W + 2) * (H + 2))
for y in range(H):
    for x in range(W):
        _pad[(y + 1) * (W + 2) + x + 1] = px[y * W + x][3] < 100
_pd = dist_from(_pad, 6, W + 2, H + 2)
edge = [_pd[(y + 1) * (W + 2) + x + 1] for y in range(H) for x in range(W)]
outline = [vis[i] and not red[i] and not blk[i] and edge[i] <= 2.5 for i in range(N)]

# red-tinted pixels (incl. maroon creases) are stripe A (was red, now seafoam); neutral greys are
# stripe B (was black, now white with gold edges). The boundary is found at SS x resolution so stripe
# edges and gold lines come out anti-aliased.
SS = 4
def hires(vals):
    m = Image.new("L", (W, H)); m.putdata([int(round(255 * min(max(v, 0.0), 1.0))) for v in vals])
    m = m.resize((W * SS, H * SS), Image.BICUBIC).filter(ImageFilter.GaussianBlur(SS * EDGE_SMOOTH))
    return [v >= 128 for v in m.getdata()]
def redness(i):
    """0..1 share of red stripe in this pixel; dark red-tinted creases count as fully red."""
    r, g = px[i][0], px[i][1]
    if not vis[i] or r - g <= 10:
        return 0.0
    if mx[i] < 130:
        return 1.0 if r - g > 40 else 0.5   # faint reddish dark = fringe on the stripe edge
    return min((r - g) / 200.0, 1.0)
HW, HH = W * SS, H * SS
rv = [redness(i) for i in range(N)]
a_hi = hires(rv)                                                          # seafoam (ex-red)
b_hi = hires([0.0 if (outline[i] or not vis[i]) else 1.0 - rv[i] for i in range(N)])
b_hi = [b_hi[i] and not a_hi[i] for i in range(HW * HH)]                  # white (ex-black)
d_hi = dist_from(a_hi, (GOLD_WIDTH + 1) * SS, HW, HH)
gold_hi = [b_hi[i] and d_hi[i] <= GOLD_WIDTH * SS for i in range(HW * HH)]

def coverage(flags):
    m = Image.new("L", (HW, HH)); m.putdata([255 if f else 0 for f in flags])
    return [v / 255.0 for v in m.resize((W, H), Image.BOX).getdata()]
a_cov = coverage(a_hi)
b_cov = coverage(b_hi)
g_cov = coverage(gold_hi)

# thin dark fold lines (darker than their surroundings) stay defined inside the white stripe
_m = Image.new("L", (W, H)); _m.putdata(mx)
local = list(_m.filter(ImageFilter.BoxBlur(2)).getdata())

out = []
for i, p in enumerate(px):
    if not vis[i]:
        out.append((0, 0, 0, p[3]))
        continue
    tinted = px[i][0] - px[i][1] > 10
    tot = a_cov[i] + b_cov[i]
    fa = a_cov[i] / tot if tot > 0 else (1.0 if tinted else 0.0)
    shade = min(mx[i] / (fa * 229.0 + (1 - fa) * 60.0), 1.0)
    # the black stripes had much deeper shadows (33 vs 60) than the red ones; soften them for white
    shade_b = 1.0 - (1.0 - shade) * WHITE_SHADOW
    c = lerp(ramp(WHITE, shade_b), ramp(SEAFOAM, shade), fa)
    line = min(max((local[i] - mx[i] - 4) / 14.0, 0.0), 1.0) * (1 - fa)
    if line > 0:
        c = lerp(c, FOLD, line)
    gold = min(g_cov[i] / tot, 1.0) if tot > 0 else 0.0
    if gold > 0:
        c = lerp(c, ramp(GOLD, 0.45 + 0.55 * shade_b), gold)
    # outline: any dark pixel hugging the silhouette, including the ones anti-aliased against red
    if edge[i] <= OUTLINE_REACH:
        k = (200.0 - mx[i]) / 150.0 if tinted else (55.0 - mx[i]) / 20.0
        k = min(max(k, 0.0), 1.0)
        if k > 0:
            c = lerp(c, OUTLINE, k)
    out.append(tuple(int(round(min(max(v, 0), 255))) for v in c) + (p[3],))

dst = Image.new("RGBA", (W, H))
dst.putdata(out)
dst.save(OUT)
print("wrote", OUT)
