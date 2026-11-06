# Small painting toolkit: cairo for vector drawing, PIL for masks / compositing.
import cairo, math, random
from PIL import Image, ImageFilter, ImageChops

SS = 6            # supersample factor over HR pixels
W, H = 390, 390   # canvas in HR pixels


def hexc(h, a=1.0):
    h = h.lstrip('#')
    return (int(h[0:2], 16) / 255, int(h[2:4], 16) / 255, int(h[4:6], 16) / 255, a)


def mix(c1, c2, t):
    return tuple(c1[i] * (1 - t) + c2[i] * t for i in range(len(c1)))


def scale_rgb(c, k):
    if isinstance(k, (int, float)):
        k = (k, k, k)
    return (min(1, c[0] * k[0]), min(1, c[1] * k[1]), min(1, c[2] * k[2])) + tuple(c[3:])


def new_surface():
    s = cairo.ImageSurface(cairo.FORMAT_ARGB32, W * SS, H * SS)
    c = cairo.Context(s)
    c.scale(SS, SS)
    return s, c


def to_pil(s):
    s.flush()
    return Image.frombuffer('RGBA', (s.get_width(), s.get_height()), bytes(s.get_data()),
                            'raw', 'BGRa', s.get_stride(), 1).copy()


def blank(mode='RGBA'):
    return Image.new(mode, (W * SS, H * SS), 0)


# ---------- paths ----------
def smooth(ctx, pts, closed=True, tension=1.0):
    """Catmull-Rom spline through pts."""
    n = len(pts)
    if n < 2:
        return
    ctx.move_to(*pts[0])
    rng = range(n if closed else n - 1)
    for i in rng:
        p0 = pts[(i - 1) % n] if (closed or i > 0) else pts[0]
        p1 = pts[i]
        p2 = pts[(i + 1) % n]
        p3 = pts[(i + 2) % n] if (closed or i + 2 < n) else pts[-1]
        t = tension / 6
        c1 = (p1[0] + (p2[0] - p0[0]) * t, p1[1] + (p2[1] - p0[1]) * t)
        c2 = (p2[0] - (p3[0] - p1[0]) * t, p2[1] - (p3[1] - p1[1]) * t)
        ctx.curve_to(*c1, *c2, *p2)
    if closed:
        ctx.close_path()


def poly(ctx, pts):
    ctx.move_to(*pts[0])
    for p in pts[1:]:
        ctx.line_to(*p)
    ctx.close_path()


def mask_from(draw_fn):
    s, c = new_surface()
    c.set_source_rgba(1, 1, 1, 1)
    draw_fn(c)
    return to_pil(s).getchannel('A')


def shape_mask(pts, closed=True, smooth_it=True):
    def f(c):
        (smooth if smooth_it else poly)(c, pts) if closed else None
        c.fill()
    return mask_from(f)


# ---------- mask ops ----------
def shift(m, dx, dy):
    out = Image.new('L', m.size, 0)
    out.paste(m, (int(round(dx * SS)), int(round(dy * SS))))
    return out


def mul(a, b):
    return ImageChops.multiply(a, b)


def sub(a, b):
    return ImageChops.subtract(a, b)


def add(a, b):
    return ImageChops.add(a, b)


def inv(a):
    return ImageChops.invert(a)


def scale_mask(m, k):
    return m.point(lambda v: int(v * k))


def blur(m, r):
    return m.filter(ImageFilter.GaussianBlur(r * SS))


def dilate(m, r):
    """Round dilation by r HR px (blur + threshold)."""
    if r <= 0:
        return m
    s = r * SS / 2.0
    b = m.filter(ImageFilter.GaussianBlur(s))
    # Φ(-2) ≈ 0.023 → ~6/255 at distance 2s = r
    return b.point(lambda v: 0 if v < 3 else (255 if v > 12 else int((v - 3) * 255 / 9)))


def erode(m, r):
    return inv(dilate(inv(m), r))


def crescent(m, dx, dy, soft=0.0):
    """Part of m not covered by m shifted by (dx,dy)."""
    c = sub(m, shift(m, dx, dy))
    return blur(c, soft) if soft else c


# ---------- painting ----------
def paint(base, color, mask, op=1.0):
    r, g, b = [int(round(v * 255)) for v in color[:3]]
    a = color[3] if len(color) > 3 else 1.0
    layer = Image.new('RGBA', base.size, (r, g, b, 0))
    k = op * a
    layer.putalpha(mask if k >= 0.999 else scale_mask(mask, k))
    base.alpha_composite(layer)


def clip_alpha(img, mask):
    a = ImageChops.multiply(img.getchannel('A'), mask)
    out = img.copy()
    out.putalpha(a)
    return out


def with_outline(img, color, width, extra_mask=None):
    a = img.getchannel('A')
    if extra_mask is not None:
        a = ImageChops.lighter(a, extra_mask)
    o = dilate(a, width)
    out = blank()
    paint(out, color, o)
    out.alpha_composite(img)
    return out


def noise(scale, sigma=60, seed=0):
    """Gaussian noise (L, centred 128) with blobs of ~scale HR px."""
    random.seed(seed)
    w = max(2, int(W / scale) + 2)
    h = max(2, int(H / scale) + 2)
    n = Image.effect_noise((w, h), sigma)
    return n.resize((W * SS, H * SS), Image.BICUBIC)


def levels(m, lo, hi):
    return m.point(lambda v: 0 if v <= lo else (255 if v >= hi else int((v - lo) * 255 / (hi - lo))))


def downsample(img, k):
    """Downsample SS canvas to k× HR, premultiplied."""
    return img.convert('RGBa').resize((W * k, H * k), Image.LANCZOS).convert('RGBA')


def bezier(p0, p1, p2, t):
    return ((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
            (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1])


def strand(ctx, p0, p1, p2, w0, w1, steps=12):
    """Tapered quadratic strand as a filled polygon."""
    left, right = [], []
    for i in range(steps + 1):
        t = i / steps
        x, y = bezier(p0, p1, p2, t)
        t2 = min(1, t + 0.01)
        x2, y2 = bezier(p0, p1, p2, t2)
        if t2 == t:
            x2, y2 = x, y
            xa, ya = bezier(p0, p1, p2, t - 0.01)
            dx, dy = x - xa, y - ya
        else:
            dx, dy = x2 - x, y2 - y
        L = math.hypot(dx, dy) or 1
        nx, ny = -dy / L, dx / L
        w = (w0 + (w1 - w0) * t) / 2
        left.append((x + nx * w, y + ny * w))
        right.append((x - nx * w, y - ny * w))
    poly(ctx, left + right[::-1])


def dashed(ctx, pts, dash, gap, width, color, closed=False):
    ctx.save()
    ctx.set_source_rgba(*color)
    ctx.set_line_width(width)
    ctx.set_line_cap(cairo.LINE_CAP_ROUND)
    ctx.set_dash([dash, gap])
    smooth(ctx, pts, closed=closed)
    ctx.stroke()
    ctx.restore()
