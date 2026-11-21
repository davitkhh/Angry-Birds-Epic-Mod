/* Gold tooth icon renderer (see gold_tooth_icon.py, which compiles and drives this).
 *
 * Ray-marches an SDF molar orthographically and writes a supersampled RGBA raw
 * buffer, cel-shaded with the approved gold palette (GOLD_TOOTH_STYLE.md).
 *
 * args: out.raw size tilt_deg angle_deg outline_max_px ss margin_px   (px = final-size pixels;
 *       outline runs from 0.5x outline_max on the lit top-left to outline_max bottom-right)
 * stdout: "glint x y" in final-size pixel coords (brightest specular on the crown)
 */
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { double x, y, z; } v3;
static v3 V(double x, double y, double z) { v3 r = {x, y, z}; return r; }
static v3 add(v3 a, v3 b) { return V(a.x + b.x, a.y + b.y, a.z + b.z); }
static v3 sub(v3 a, v3 b) { return V(a.x - b.x, a.y - b.y, a.z - b.z); }
static v3 mul(v3 a, double s) { return V(a.x * s, a.y * s, a.z * s); }
static double dot(v3 a, v3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
static double len(v3 a) { return sqrt(dot(a, a)); }
static v3 norm(v3 a) { return mul(a, 1.0 / len(a)); }
static v3 cross(v3 a, v3 b) { return V(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
static double clampd(double x, double a, double b) { return x < a ? a : x > b ? b : x; }

static double smin(double a, double b, double k) {
    double h = clampd(0.5 + 0.5 * (b - a) / k, 0, 1);
    return b + (a - b) * h - k * h * (1 - h);
}
static double smax(double a, double b, double k) { return -smin(-a, -b, k); }

static double cone_capsule(v3 p, v3 a, v3 b, double ra, double rb) {
    v3 pa = sub(p, a), ba = sub(b, a);
    double h = clampd(dot(pa, ba) / dot(ba, ba), 0, 1);
    return len(sub(pa, mul(ba, h))) - (ra + (rb - ra) * h);
}

/* tooth space: +y = crown (chewing surface), roots toward -y */
static double tooth_sdf(v3 q) {
    double x = q.x, y = q.y, z = q.z;
    double cy = 0.28, yc = clampd(y - cy, -0.4, 0.3);
    double rx = 0.60 + 0.06 * yc, rz = 0.52 + 0.05 * yc, ry = 0.38, e = 2.4;
    double v = pow(pow(fabs(x / rx), e) + pow(fabs((y - cy) / ry), e) + pow(fabs(z / rz), e), 1 / e);
    double d = (v - 1) * 0.30;
    static const double cusp[4][3] = {{-1, -1, 0.29}, {1, -1, 0.27}, {-1, 1, 0.27}, {1, 1, 0.30}};
    for (int i = 0; i < 4; i++) {
        v3 c = V(cusp[i][0] * 0.28, 0.50, cusp[i][1] * 0.24);
        d = smin(d, len(sub(q, c)) - cusp[i][2], 0.14);
    }
    /* cross-shaped central fissure between the cusps */
    double groove = fmin(fabs(x), fabs(z)) - 0.035;
    double carve = fmax(groove, -(y - 0.50));
    d = smax(d, -carve, 0.06);
    /* two roots shaped after the user's reference tooth: wide where they leave
     * the crown, set toward its outer edges, nearly parallel with a rounded arch between
     * them, tapering to narrow rounded tips that turn slightly inward */
    double ra = cone_capsule(q, V(-0.38, 0.04, 0), V(-0.41, -0.36, 0.0), 0.21, 0.15);
    ra = smin(ra, cone_capsule(q, V(-0.41, -0.36, 0.0), V(-0.33, -0.84, 0.0), 0.15, 0.045), 0.08);
    double rb = cone_capsule(q, V(0.38, 0.04, 0), V(0.41, -0.36, 0.0), 0.21, 0.15);
    rb = smin(rb, cone_capsule(q, V(0.41, -0.36, 0.0), V(0.34, -0.84, 0.0), 0.15, 0.045), 0.08);
    d = smin(d, ra, 0.07);
    d = smin(d, rb, 0.07);
    return d;
}

static v3 RX, RY, RZ; /* tooth basis vectors expressed in view space */
static v3 to_tooth(v3 p) { return V(dot(p, RX), dot(p, RY), dot(p, RZ)); }
static double f(v3 p) { return tooth_sdf(to_tooth(p)); }

static int march(double px, double py, double *t_out) {
    double t = 0;
    for (int i = 0; i < 200; i++) {
        double d = f(V(px, py, 3.0 - t));
        if (d < 0.0008) { *t_out = t; return 1; }
        t += d * 0.8;
        if (t > 6) break;
    }
    *t_out = 1e9;
    return 0;
}

static const double G_LIT[3] = {255, 226, 84}, G_BODY[3] = {250, 182, 30}, G_SHADE[3] = {196, 112, 10},
                    G_DEEP[3] = {150, 72, 6}, G_SHINE[3] = {255, 252, 210}, RIM[3] = {255, 244, 168}, V3W[3] = {255, 255, 255},
                    OUT_LIT[3] = {136, 76, 0}, OUT_DARK[3] = {92, 38, 0};

static void mix(double *c, const double *b, double t) {
    t = clampd(t, 0, 1);
    for (int i = 0; i < 3; i++) c[i] += (b[i] - c[i]) * t;
}
static double hash2(int x, int y) {
    unsigned h = (unsigned)x * 374761393u + (unsigned)y * 668265263u;
    h = (h ^ (h >> 13)) * 1274126177u;
    return ((h ^ (h >> 16)) & 0xffffff) / (double)0xffffff;
}
static double vnoise(double x, double y) {
    int xi = (int)floor(x), yi = (int)floor(y);
    double fx = x - xi, fy = y - yi;
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
    double a = hash2(xi, yi), b = hash2(xi + 1, yi), c = hash2(xi, yi + 1), d = hash2(xi + 1, yi + 1);
    return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
}
/* two octaves, -1..1: wobbles band edges so they look brushed, not computed */
static double brush(double sx, double sy) {
    return (vnoise(sx * 9, sy * 9) * 0.65 + vnoise(sx * 23 + 7, sy * 23 + 3) * 0.35) * 2 - 1;
}
static double step_(double x, double e, double w) { return clampd((x - e) / w + 0.5, 0, 1); }

/* exact Euclidean distance (px) to the nearest pixel with feat[k] != 0 */
static void edt1(const double *f, int n, double *d, int *v, double *z) {
    int k = 0;
    v[0] = 0; z[0] = -1e30; z[1] = 1e30;
    for (int q = 1; q < n; q++) {
        double s;
        for (;;) {
            s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            if (s <= z[k] && k > 0) k--; else break;
        }
        if (s <= z[k]) { v[0] = q; z[0] = -1e30; z[1] = 1e30; k = 0; continue; }
        k++; v[k] = q; z[k] = s; z[k + 1] = 1e30;
    }
    k = 0;
    for (int q = 0; q < n; q++) {
        while (z[k + 1] < q) k++;
        d[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]];
    }
}
static double *edt(const unsigned char *feat, int res) {
    long n = (long)res * res;
    double *g = malloc(n * sizeof(double)), *f = malloc(res * sizeof(double)), *d = malloc(res * sizeof(double));
    double *z = malloc((res + 1) * sizeof(double));
    int *v = malloc(res * sizeof(int));
    for (long k = 0; k < n; k++) g[k] = feat[k] ? 0 : 1e20;
    for (int i = 0; i < res; i++) {
        for (int j = 0; j < res; j++) f[j] = g[(long)j * res + i];
        edt1(f, res, d, v, z);
        for (int j = 0; j < res; j++) g[(long)j * res + i] = d[j];
    }
    for (int j = 0; j < res; j++) {
        edt1(g + (long)j * res, res, d, v, z);
        for (int i = 0; i < res; i++) g[(long)j * res + i] = sqrt(d[i]);
    }
    free(f); free(d); free(z); free(v);
    return g;
}

int main(int argc, char **argv) {
    if (argc < 8) { fprintf(stderr, "usage\n"); return 1; }
    const char *out = argv[1];
    int size = atoi(argv[2]);
    double tilt = atof(argv[3]) * M_PI / 180, ang = atof(argv[4]) * M_PI / 180;
    double outline_hi = atof(argv[5]), outline_lo = outline_hi * 0.5, outline_px = outline_hi;
    int ss = atoi(argv[6]);
    double margin = atof(argv[7]);

    RY = V(cos(tilt) * cos(ang), cos(tilt) * sin(ang), sin(tilt));
    RX = mul(norm(cross(RY, V(0, 0, 1))), -1);
    RZ = cross(RX, RY);

    /* pass 1: silhouette bounds */
    double x0 = 1e9, x1 = -1e9, y0 = 1e9, y1 = -1e9, t;
    int pr = 300;
    double pe = 1.8;
    for (int j = 0; j < pr; j++)
        for (int i = 0; i < pr; i++) {
            double px = ((i + 0.5) / pr * 2 - 1) * pe, py = -((j + 0.5) / pr * 2 - 1) * pe;
            if (march(px, py, &t)) {
                if (px < x0) x0 = px; if (px > x1) x1 = px;
                if (py < y0) y0 = py; if (py > y1) y1 = py;
            }
        }
    double half = fmax(x1 - x0, y1 - y0) / 2 + pe / pr;
    double usable = size / 2.0 - margin - outline_px;
    double extent = half * (size / 2.0) / usable;
    double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;

    int res = size * ss;
    long n = (long)res * res;
    unsigned char *hit = calloc(n, 1);
    double *depth = malloc(n * sizeof(double));
    double *col = calloc(n * 3, sizeof(double));
    double *ndl_buf = calloc(n, sizeof(double));
    v3 L = norm(V(-0.66, 0.62, 0.26)), Vv = V(0, 0, 1), H = norm(add(L, Vv));

    double best = -1; int bi = 0, bj = 0;
    for (int j = 0; j < res; j++)
        for (int i = 0; i < res; i++) {
            long k = (long)j * res + i;
            double px = ((i + 0.5) / res * 2 - 1) * extent + cx, py = -((j + 0.5) / res * 2 - 1) * extent + cy;
            if (!march(px, py, &t)) { depth[k] = 1e9; continue; }
            hit[k] = 1;
            depth[k] = t;
            v3 p = V(px, py, 3.0 - t);
            double e = 0.0015;
            v3 nn = norm(V(f(add(p, V(e, 0, 0))) - f(sub(p, V(e, 0, 0))),
                           f(add(p, V(0, e, 0))) - f(sub(p, V(0, e, 0))),
                           f(add(p, V(0, 0, e))) - f(sub(p, V(0, 0, e)))));
            double acc = 0;
            for (int s = 1; s <= 5; s++) {
                double h = 0.035 * s;
                acc += (h - f(add(p, mul(nn, h)))) / (1 << s);
            }
            double occ = 1 - clampd(1 - 6.0 * acc, 0, 1);
            /* house-style diagonal falloff across the sprite: k = 0.55u + 0.45v (GOLD_TOOTH_STYLE.md) */
            double u = (px - (cx - half)) / (2 * half), vv = ((cy + half) - py) / (2 * half);
            double ndl = dot(nn, L);
            ndl_buf[k] = ndl;
            double kk = (ndl + 1) / 2 - 0.8 * (0.55 * u + 0.45 * vv - 0.5);
            double *c = col + k * 3;
            double sx = (i + 0.5) / res, sy = (j + 0.5) / res, wob = brush(sx, sy);
            /* toon metal: hard 3-4 tone bands from the light, edges brushed */
            double kb = kk + 0.035 * wob;
            memcpy(c, G_DEEP, sizeof G_DEEP);
            mix(c, G_SHADE, step_(kb, 0.34, 0.012));
            mix(c, G_BODY, step_(kb, 0.56, 0.012));
            mix(c, G_LIT, step_(kb, 0.80, 0.012));
            /* stylised horizon reflection (cartoon chrome): sky-facing reflections go pale,
             * a hard dark band sits right under the horizon, below it warm bounce light */
            v3 rf = sub(mul(nn, 2 * nn.z), Vv); /* reflect(-V) about n */
            double hy = rf.y + 0.05 * wob - 0.10 * (0.55 * u + 0.45 * vv - 0.5);
            double facing = clampd((nn.z - 0.15) / 0.2, 0, 1);
            mix(c, G_SHADE, step_(-hy, 0.02, 0.01) * step_(hy, -0.16, 0.01) * 0.85 * facing);
            mix(c, G_LIT, step_(hy, 0.30, 0.012) * 0.7 * facing);
            /* crevices: crisp, not smudgy */
            mix(c, G_SHADE, step_(occ, 0.22, 0.03) * 0.9);
            mix(c, G_DEEP, step_(occ, 0.45, 0.03));
            /* warm bounce light on the shadow-side rim */
            double fres = pow(1 - clampd(nn.z, 0, 1), 2);
            mix(c, G_BODY, step_(fres * step_(-ndl, 0.05, 0.3), 0.35, 0.02) * 0.6);
            /* graphic highlight shapes: hard-edged pale shine, white core */
            double spec = pow(clampd(dot(nn, H), 0, 1), 30) + 0.04 * wob;
            mix(c, G_SHINE, step_(spec, 0.42, 0.02));
            mix(c, V3W, step_(spec, 0.78, 0.02));
            if (nn.z > 0.55 && to_tooth(p).y > 0.62 && spec > best) { best = spec; bi = i; bj = j; }
        }

    /* inner contour where near parts overlap far parts (depth jump) */
    unsigned char *edge = calloc(n, 1), *outside = calloc(n, 1);
    for (long k = 0; k < n; k++) outside[k] = !hit[k];
    for (int j = 1; j < res - 1; j++)
        for (int i = 1; i < res - 1; i++) {
            long k = (long)j * res + i;
            if (!hit[k]) continue;
            double d = depth[k], m = 0;
            long nb[4] = {k - 1, k + 1, k - res, k + res};
            for (int q = 0; q < 4; q++) if (hit[nb[q]]) m = fmax(m, fabs(depth[nb[q]] - d));
            if (m > 0.06) edge[k] = 1;
        }
    double *d_obj = edt(hit, res), *d_out = edt(outside, res), *d_edge = edt(edge, res);

    /* Game icon outline (measured on Consumables.png): a dark, saturated shade of the
     * object's own hue (yellow items: ~(136,76,0)), thin on the lit top-left (~2 px)
     * and heavy on the shadow bottom-right (4-6 px), a thin pale rim just inside the
     * lit edge, and thinner inner lines. */
    unsigned char *rgba = calloc(n * 4, 1);
    for (int j = 0; j < res; j++)
        for (int i = 0; i < res; i++) {
            long k = (long)j * res + i;
            unsigned char *o = rgba + k * 4;
            double sx = (i + 0.5) / res, sy = (j + 0.5) / res;
            double diag = clampd((0.55 * sx + 0.45 * sy - 0.25) / 0.55, 0, 1);
            double oc[3];
            memcpy(oc, OUT_LIT, sizeof oc);
            mix(oc, OUT_DARK, diag);
            if (hit[k]) {
                double c[3];
                memcpy(c, col + k * 3, sizeof c);
                /* pale rim inside the lit silhouette edge */
                double din = d_out[k] / ss;
                double rim = clampd(1.3 - din, 0, 1) * clampd((ndl_buf[k] - 0.15) / 0.3, 0, 1) * (1 - diag);
                mix(c, RIM, rim * 0.85);
                /* inner contour: thinner than the silhouette, tapered by the same light */
                double wi = (0.55 + 0.6 * diag) * ss;
                mix(c, oc, clampd(wi - d_edge[k] + 0.5, 0, 1));
                for (int q = 0; q < 3; q++) o[q] = (unsigned char)lround(c[q]);
                o[3] = 255;
                continue;
            }
            double w = (outline_lo + (outline_hi - outline_lo) * diag) * ss;
            if (d_obj[k] <= w) {
                for (int q = 0; q < 3; q++) o[q] = (unsigned char)lround(oc[q]);
                o[3] = 255;
            }
        }

    FILE *fp = fopen(out, "wb");
    fwrite(rgba, 1, n * 4, fp);
    fclose(fp);

    printf("glint %.2f %.2f\n", (bi + 0.5) / ss, (bj + 0.5) / ss);
    return 0;
}
