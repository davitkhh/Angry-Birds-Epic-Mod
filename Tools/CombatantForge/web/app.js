'use strict';
/* Combatant Forge UI. Coordinates: rig space is y-up, 1 unit = 1 texture pixel, same as SmoothMoves. */

const $ = id => document.getElementById(id);
const canvas = $('stage');
const ctx = canvas.getContext('2d');

const S = {
  rigs: [], rig: null, lib: [], skills: [], icons: [], colors: {}, projects: [],
  project: null, clip: 0, time: 0, playing: true, speed: 1, loop: true,
  view: { zoom: 1, x: 0, y: 0 }, selected: null,
  side: 'pig', cat: 'Headgear', search: '', rigFilter: '', split: new Set(), splitAll: false,
  level: 1, undo: [], redo: [], snapshot: '', viewJob: null, drawn: [], anchors: [], drag: null, hoverSlot: null, gizmo: null, idTouched: false,
};

/* ---------------------------------------------------------------- helpers */

function h(tag, attrs, ...children) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v === null || v === undefined || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'style') el.style.cssText = v;
    else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else if (k in el && k !== 'list') el[k] = v;
    else el.setAttribute(k, v);
  }
  for (const c of children.flat()) if (c !== null && c !== undefined) el.append(c);
  return el;
}
const api = async (path, body) => {
  const res = await fetch(path, body ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : undefined);
  const data = await res.json();
  if (!res.ok) throw new Error(data.error || res.statusText);
  return data;
};
let toastTimer;
function toast(text) {
  const t = $('toast');
  t.textContent = text;
  t.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove('show'), 3500);
}
const slugify = s => (s || '').toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
const spriteUrl = key => '/sprite/' + encodeURIComponent(key);
const iconUrl = (atlas, icon) => `/ngui/${encodeURIComponent(atlas)}/${encodeURIComponent(icon)}`;
const num = (v, d = 0) => { const n = parseFloat(v); return Number.isFinite(n) ? n : d; };
const round = (v, d = 2) => Math.round(v * 10 ** d) / 10 ** d;
const images = new Map();
function img(key) {
  let im = images.get(key);
  if (!im) { im = new Image(); im.src = spriteUrl(key); images.set(key, im); }
  return im;
}

/* ---------------------------------------------------------------- 2D matrices [a b c d e f] like canvas */

const mul = (m, n) => [
  m[0] * n[0] + m[2] * n[1], m[1] * n[0] + m[3] * n[1],
  m[0] * n[2] + m[2] * n[3], m[1] * n[2] + m[3] * n[3],
  m[0] * n[4] + m[2] * n[5] + m[4], m[1] * n[4] + m[3] * n[5] + m[5]];
const apply = (m, x, y) => [m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]];
function invert(m) {
  const det = m[0] * m[3] - m[1] * m[2] || 1e-9;
  return [m[3] / det, -m[1] / det, -m[2] / det, m[0] / det,
    (m[2] * m[5] - m[3] * m[4]) / det, (m[1] * m[4] - m[0] * m[5]) / det];
}

/* ---------------------------------------------------------------- rig evaluation */

function sampleVec(track, t) {
  if (!Array.isArray(track[0])) return track;
  const i = Math.min(track.length - 1, Math.floor(t));
  const j = Math.min(track.length - 1, i + 1);
  const f = t - i;
  return [track[i][0] + (track[j][0] - track[i][0]) * f, track[i][1] + (track[j][1] - track[i][1]) * f];
}
function sampleAngle(track, t) {
  if (!Array.isArray(track)) return track;
  const i = Math.min(track.length - 1, Math.floor(t));
  const j = Math.min(track.length - 1, i + 1);
  let d = track[j] - track[i];
  if (d > 180) d -= 360; else if (d < -180) d += 360;
  return track[i] + d * (t - i);
}
function pose(clip, t) {
  const rig = S.rig;
  const out = new Array(rig.paths.length);
  const rest = rig.clips[rig.restClip];
  for (let i = 0; i < rig.paths.length; i++) {
    const track = clip.tracks[i] || {};
    const r = rest.tracks[i] || {};
    const p = track.p ? sampleVec(track.p, t) : r.p ? sampleVec(r.p, 0) : [0, 0];
    const a = (track.r !== undefined ? sampleAngle(track.r, t) : r.r !== undefined ? sampleAngle(r.r, 0) : 0) * Math.PI / 180;
    const s = track.s ? sampleVec(track.s, t) : r.s ? sampleVec(r.s, 0) : [1, 1];
    const c = Math.cos(a), sn = Math.sin(a);
    const local = [c * s[0], sn * s[0], -sn * s[1], c * s[1], p[0], p[1]];
    out[i] = rig.parents[i] >= 0 ? mul(out[rig.parents[i]], local) : local;
  }
  const look = currentLook();
  if (look) out.spring = applyLook(out, clip, look);
  return out;
}

/* ---------------------------------------------------------------- looks: the spring drone (game: SpringDrone.cs) */

function currentLook() {
  return S.rig && S.project && S.project.variant && S.rig.looks ? S.rig.looks[S.project.variant] || null : null;
}
function lookHides(look, path) {
  return !!look && path.split('/').some(part => look.hide.includes(part));
}
function springState(look) {
  if (!S.spring || S.spring.look !== look) {
    S.spring = { look, h: look.RestLength + 0.6 * look.HopHeight, v: 0, ext: 0, bias: 0.25 * look.HopHeight, acc: 0 };
  }
  return S.spring;
}
// SpringDrone.Simulate: gravity, a push-only damped spring, and the pogo actuator that keeps it hopping.
function stepSpring(dt) {
  const look = currentLook();
  if (!look) { S.spring = null; return; }
  const st = springState(look);
  const pumping = !look.limp.includes(S.rig.clips[S.clip].name);
  const g = 9.81 * look.PixelsPerMeter, k = g / Math.max(1, look.StaticSag), c = 2 * look.DampingRatio * Math.sqrt(k);
  const step = 1 / 600;
  st.acc += Math.min(dt, 0.1);
  while (st.acc >= step) {
    st.acc -= step;
    const previous = st.v;
    let a = -g, contact = false;
    const compression = look.RestLength + st.ext - st.h;
    if (compression > 0) {
      const force = k * compression - c * st.v;
      if (force > 0) { a += force; contact = true; }
    }
    st.v += a * step;
    st.h += st.v * step;
    if (st.h < look.SolidLength) { st.h = look.SolidLength; if (st.v < 0) st.v = -st.v * look.SolidRestitution; }
    if (!pumping) { st.ext = 0; continue; }
    if (contact && previous < 0 && st.v >= 0) {
      const target = g * (look.RestLength + look.HopHeight + st.bias) - g * st.h;
      if (target > 0) {
        const needed = Math.sqrt(2 * target / k), current = look.RestLength + st.ext - st.h;
        st.ext = Math.min(look.MaxExtension, Math.max(0, st.ext + Math.max(0, needed - current)));
      }
    } else if (!contact && st.h > look.RestLength + st.ext) st.ext = 0;
    if (!contact && previous > 0 && st.v <= 0) {
      st.bias = Math.min(look.HopHeight * 1.5, Math.max(-look.HopHeight, st.bias + 0.8 * (look.HopHeight - (st.h - look.RestLength))));
    }
  }
}
// Moves the pod bones by the bounce (the hover bob of Idle is replaced by it); returns where the spring goes.
function applyLook(out, clip, look) {
  const paths = S.rig.paths, body = paths.indexOf('Root/Body');
  if (body < 0) return null;
  const st = S.spring && S.spring.look === look ? S.spring : null;
  const height = st ? st.h : look.RestLength - look.StaticSag;
  const restHeight = look.bodyRestY + look.mount[1] - look.ground;
  const hovering = look.hover.includes(clip.name);
  const hoverX = hovering ? out[body][4] - look.bodyRestX : 0, hoverY = hovering ? out[body][5] - look.bodyRestY : 0;
  const offset = height - restHeight - hoverY;
  paths.forEach((path, i) => {
    if (look.pod.some(pod => path === pod || path.startsWith(pod + '/'))) out[i] = [...out[i].slice(0, 4), out[i][4] - hoverX, out[i][5] + offset];
  });
  // Shadow: the regular drone's ratio (its Idle: 10 units up, 0.9 the size) from the pod's real height. A hover clip's
  // own shadow pulse goes with its bob.
  const bottom = paths.indexOf('Root/Bottom'), shadow = paths.indexOf('Root/Shadow/Shadow_Sprite');
  if (shadow >= 0 && bottom >= 0) {
    // Measured from where this drone rests on its spring (full size there), like the regular drone from its hover.
    const rise = out[bottom][5] - look.bottomRestY - (look.RestLength - look.StaticSag - restHeight);
    const size = Math.min(look.MaxShadow, Math.max(look.MinShadow, 1 - look.ShadowShrinkPerUnit * rise));
    const m = out[shadow];
    const sx = hovering ? Math.hypot(m[0], m[1]) || 1 : 1, sy = hovering ? Math.hypot(m[2], m[3]) || 1 : 1;
    out[shadow] = [m[0] / sx * size, m[1] / sx * size, m[2] / sy * size, m[3] / sy * size, m[4], m[5]];
  }
  // The spring hangs straight down from its pivot under the pod; planted, its foot is on the ground below.
  const top = apply(out[body], look.mount[0], look.mount[1]);
  const length = Math.min(look.RestLength + (st ? st.ext : 0), top[1] - look.ground);
  return { top, axis: [0, -1], length: Math.max(length, look.SolidLength) };
}
// Same mesh as SpringDrone.BuildMesh (and gamedata.spring_quads): cel-shaded bands from the outline inward, each
// shifted toward the light; back half of the coil, front half, then the plate at the pod and the rubber foot.
function drawSpring(V, base, spring, look) {
  if (!spring) return;
  const [ax, ay] = spring.axis, sx = -ay, sy = ax;
  const turns = Math.round(look.Coils), count = turns * 20, r = look.CoilRadius, wire = look.WireWidth;
  const end = wire * 0.6;
  // Closed last turn: the spring ends in a flat ring standing on the ground.
  const pitched = Math.max(1, turns - 1) / Math.max(1, turns), ring = Math.max(end, spring.length - end - r * 0.22);
  const pts = [];
  for (let i = 0; i <= count; i++) {
    const t = i / count, a = t * turns * Math.PI * 2;
    const along = end + (ring - end) * Math.min(1, t / pitched) + Math.sin(a) * r * 0.22, c = Math.cos(a) * r;
    pts.push({ p: [spring.top[0] + ax * along + sx * c, spring.top[1] + ay * along + sy * c], d: Math.sin(a) });
  }
  ctx.setTransform(...base);
  const quad = (corners, color) => {
    ctx.fillStyle = `rgb(${color[0]},${color[1]},${color[2]})`;
    ctx.beginPath();
    corners.forEach((c, i) => { const q = apply(V, c[0], c[1]); if (i) ctx.lineTo(...q); else ctx.moveTo(...q); });
    ctx.closePath(); ctx.fill();
  };
  const segment = (a, b, [w, o, color]) => {
    let dx = b[0] - a[0], dy = b[1] - a[1];
    const n = Math.hypot(dx, dy);
    if (n < 1e-4) return;
    dx /= n; dy /= n;
    let ux = -dy, uy = dx;
    if (uy < 0) { ux = -ux; uy = -uy; }
    const width = wire * w, shx = ux * wire * o, shy = uy * wire * o;
    const ex = dx * width / 2, ey = dy * width / 2, nx = ux * width / 2, ny = uy * width / 2;
    quad([[a[0] - ex - nx + shx, a[1] - ey - ny + shy], [a[0] - ex + nx + shx, a[1] - ey + ny + shy],
      [b[0] + ex + nx + shx, b[1] + ey + ny + shy], [b[0] + ex - nx + shx, b[1] + ey - ny + shy]], color);
  };
  for (const [front, name] of [[false, 'BackWire'], [true, 'FrontWire']]) {
    for (const band of look.bands[name]) {
      for (let i = 0; i < count; i++) {
        if (((pts[i].d + pts[i + 1].d) / 2 >= 0) === front) segment(pts[i].p, pts[i + 1].p, band);
      }
    }
  }
  // Mounting pad under the pod.
  for (const [w, o, color] of look.bands.PlateBands || []) {
    const halfLen = r + 6, halfThick = 5, half = halfLen - (1 - w) * halfThick, thick = halfThick * w;
    const x = spring.top[0] - ax * 2 * halfThick * o, y = spring.top[1] - ay * 2 * halfThick * o;
    quad([[x - sx * half - ax * thick, y - sy * half - ay * thick], [x + sx * half - ax * thick, y + sy * half - ay * thick],
      [x + sx * half + ax * thick, y + sy * half + ay * thick], [x - sx * half + ax * thick, y - sy * half + ay * thick]], color);
  }
}
function boneRow(clip, node, t) {
  const rows = clip.tex[node];
  const source = rows && rows.length ? rows : (S.rig.clips[S.rig.restClip].tex[node] || []);
  let row = source[0] || null;
  for (const r of source) { if (r[0] <= t) row = r; else break; }
  return row;
}
function slotMatrix(slot) {
  const rot = (slot.rot || 0) * Math.PI / 180, c = Math.cos(rot), s = Math.sin(rot);
  const sx = slot.sx ?? 1, sy = slot.sy ?? 1, k = slot.shear || 0;
  const cx = -slot.pivot[0] * slot.w, cy = -slot.pivot[1] * slot.h;
  // Same affine as publish.bake_sprite.
  return [c * sx, s * sx, (c * k - s) * sy, (s * k + c) * sy, (slot.tx || 0) + cx, (slot.ty || 0) + cy];
}

/* ---------------------------------------------------------------- drawing */

// In battle the whole character is scaled by SizeScale around its feet (the rig's origin). Size has no part in this.
function gameScale() {
  return num(S.project.stats.sizeScale) || 1;
}
function viewMatrix() {
  const z = S.view.zoom * (S.project ? gameScale() : 1);
  return [z, 0, 0, -z, S.view.x, S.view.y];
}
function visibleLayers(slot) {
  return slot && !slot.hidden ? slot.layers.filter(l => !l.hidden) : [];
}
function collectDraw() {
  const rig = S.rig, clip = rig.clips[S.clip];
  const poses = pose(clip, S.time);
  const look = currentLook();
  S.springDraw = look ? poses.spring : null;
  const list = [];
  const anchors = new Map();
  const pirate = S.project.variant ? rig.variants[S.project.variant] || null : rig.base || null;  // texture variant map, or the rig's default look (PigPirate: pirate)
  rig.bones.forEach(bone => {
    const slotDef = rig.slotByKey[bone.slot];
    if (bone.sprite < 0 || !slotDef) return;
    const row = boneRow(clip, bone.node, S.time);
    const M = poses[bone.sprite];
    const anyRow = row && row[1] ? row : (rig.anyRow[bone.node] || null);
    // A slot with several bones (gear + its Body twin) anchors on the one drawn in this frame.
    const shown = !!(row && row[1]);
    if (!anchors.has(bone.slot) || (shown && !anchors.get(bone.slot).shown)) {
      const center = anyRow ? [(anyRow[3] + anyRow[5]) / 2, (anyRow[4] + anyRow[6]) / 2] : [0, 0];
      anchors.set(bone.slot, { key: bone.slot, kind: slotDef.kind, world: apply(M, center[0], center[1]), shown });
    }
    if (!row || !row[1]) return;
    if (look && lookHides(look, rig.paths[bone.sprite])) return;
    const slot = S.project.slots[bone.slot];
    if (slot && slot.hidden) return;
    const isGear = slotDef.kind === 'gear';
    const layers = visibleLayers(slot);
    if (isGear && (!layers.length || !$('showGear').checked)) return;
    if (layers.length && (isGear || $('showFeatures').checked)) {
      layers.forEach((layer, seq) => list.push({ slot: bone.slot, bone, depth: row[2], seq, M, assigned: layer, w: layer.w, h: layer.h, local: slotMatrix(layer), image: img(layer.src) }));
    } else {
      let key = row[1], x0 = row[3], y0 = row[4], x1 = row[5], y1 = row[6];
      if (pirate && pirate[key]) {
        key = pirate[key];
        const tex = rig.textures[key];
        const cx = -tex.pivot[0] * tex.w, cy = -tex.pivot[1] * tex.h;
        x0 = cx - tex.w / 2; x1 = cx + tex.w / 2; y1 = cy - tex.h / 2; y0 = cy + tex.h / 2;
      }
      list.push({ slot: bone.slot, bone, depth: row[2], seq: 0, M, w: x1 - x0, h: y0 - y1, local: [1, 0, 0, 1, (x0 + x1) / 2, (y0 + y1) / 2], image: img(key) });
    }
  });
  // SmoothMoves: a lower depth draws on top. Within a slot, later layers draw on top.
  list.sort((a, b) => b.depth - a.depth || b.bone.node - a.bone.node || a.seq - b.seq);
  S.anchors = [...anchors.values()];
  return list;
}
function resizeCanvas() {
  const box = $('canvasBox').getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  const w = Math.round(box.width * dpr), h = Math.round(box.height * dpr);
  if (canvas.width !== w || canvas.height !== h) {
    const firstSize = !canvas.dataset.sized;
    canvas.width = w; canvas.height = h; canvas.dataset.sized = '1';
    if (firstSize && S.rig) fitView();
  }
  return dpr;
}
function draw(now = performance.now()) {
  const dpr = resizeCanvas();
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  if (!S.rig) return;
  const V = viewMatrix();
  const base = [dpr, 0, 0, dpr, 0, 0];
  // ground line
  ctx.setTransform(...base);
  ctx.strokeStyle = '#ffffff14';
  ctx.lineWidth = 1;
  const groundY = apply(V, 0, 0)[1];
  ctx.beginPath(); ctx.moveTo(0, groundY); ctx.lineTo(canvas.width / dpr, groundY); ctx.stroke();

  const list = collectDraw();
  S.drawn = list;
  if (S.springDraw) drawSpring(V, base, S.springDraw, currentLook());
  for (const d of list) {
    const local = mul(mul(V, d.M), d.local);
    d.screen = local;
    if (d.image.complete && d.image.naturalWidth) {
      ctx.setTransform(...mul(base, mul(local, [1, 0, 0, -1, 0, 0])));
      ctx.drawImage(d.image, -d.w / 2, -d.h / 2, d.w, d.h);
    }
  }
  drawFlights(now, base);
  ctx.setTransform(...base);
  if (showsTarget()) {
    if ($('showPaths').checked) drawPaths();
    targets().forEach(drawTarget);
  }
  syncTargetControls();
  drawGizmo();
  if ($('showSlots').checked || S.drag) drawAnchors(V);
}
function corners(d) {
  return [[-d.w / 2, d.h / 2], [d.w / 2, d.h / 2], [d.w / 2, -d.h / 2], [-d.w / 2, -d.h / 2]].map(p => apply(d.screen, p[0], p[1]));
}
function selectedDrawn() {
  const active = activeLayer(S.selected);
  return active ? S.drawn.find(d => d.assigned === active) || null : null;
}
function drawGizmo() {
  S.gizmo = null;
  const d = selectedDrawn();
  if (!d) return;
  const c = corners(d);
  ctx.strokeStyle = '#f0b23c';
  ctx.lineWidth = 1.5;
  ctx.setLineDash([5, 4]);
  ctx.beginPath(); c.forEach((p, i) => i ? ctx.lineTo(...p) : ctx.moveTo(...p)); ctx.closePath(); ctx.stroke();
  ctx.setLineDash([]);
  const mid = (a, b) => [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
  const center = mid(c[0], c[2]);
  const top = mid(c[0], c[1]);
  const len = Math.hypot(top[0] - center[0], top[1] - center[1]) || 1;
  const rotate = [top[0] + (top[0] - center[0]) / len * 26, top[1] + (top[1] - center[1]) / len * 26];
  ctx.beginPath(); ctx.moveTo(...top); ctx.lineTo(...rotate); ctx.stroke();
  const handles = [
    { id: 'c', p: c[0], su: -1, sv: 1 }, { id: 'c', p: c[1], su: 1, sv: 1 }, { id: 'c', p: c[2], su: 1, sv: -1 }, { id: 'c', p: c[3], su: -1, sv: -1 },
    { id: 'v', p: top, su: 0, sv: 1 }, { id: 'u', p: mid(c[1], c[2]), su: 1, sv: 0 }, { id: 'v', p: mid(c[2], c[3]), su: 0, sv: -1 }, { id: 'u', p: mid(c[3], c[0]), su: -1, sv: 0 },
    { id: 'r', p: rotate, su: 0, sv: 0 }];
  ctx.fillStyle = '#16181d';
  for (const hd of handles) {
    ctx.beginPath();
    if (hd.id === 'r') { ctx.arc(hd.p[0], hd.p[1], 6, 0, 7); ctx.fillStyle = '#f0b23c'; ctx.fill(); ctx.fillStyle = '#16181d'; }
    else { ctx.rect(hd.p[0] - 4.5, hd.p[1] - 4.5, 9, 9); ctx.fill(); ctx.stroke(); }
  }
  S.gizmo = { d, handles, center };
}
// While dragging, back slots are only offered if the art was separated from its front or Slot labels is on.
function anchorOffered(a) {
  return !S.drag || !S.drag.joined || $('showSlots').checked || (a.key !== 'Headgear_Back' && a.key !== 'OffHand_Back');
}
function drawAnchors(V) {
  ctx.font = '11px system-ui, sans-serif';
  ctx.textBaseline = 'middle';
  for (const a of S.anchors.filter(anchorOffered)) {
    if (S.drag && a.kind === 'feature' && a.key !== S.hoverSlot && S.drag.preferKind === 'gear') continue;
    const [x, y] = apply(V, a.world[0], a.world[1]);
    const on = a.key === S.hoverSlot;
    ctx.beginPath(); ctx.arc(x, y, on ? 7 : 4, 0, 7);
    ctx.fillStyle = on ? '#f0b23c' : a.kind === 'gear' ? '#ffffffcc' : '#8fd0ffcc';
    ctx.fill();
    const w = ctx.measureText(a.key).width + 8;
    ctx.fillStyle = on ? '#f0b23c' : '#000b';
    ctx.fillRect(x + 8, y - 8, w, 16);
    ctx.fillStyle = on ? '#231a05' : '#fff';
    ctx.fillText(a.key, x + 12, y);
  }
}
function fitView() {
  const r = canvas.getBoundingClientRect();
  if (!S.rig || !r.width) return;
  const saved = S.view;
  S.view = { zoom: 1, x: 0, y: 0 };
  const list = collectDraw().filter(d => d.bone.slot !== 'Shadow');
  let minx = -60, maxx = 60, miny = 0, maxy = 120;
  for (const d of list) {
    const m = mul(d.M, d.local);
    for (const [u, v] of [[-d.w / 2, -d.h / 2], [d.w / 2, d.h / 2], [-d.w / 2, d.h / 2], [d.w / 2, -d.h / 2]]) {
      const p = apply(m, u, v);
      minx = Math.min(minx, p[0]); maxx = Math.max(maxx, p[0]); miny = Math.min(miny, p[1]); maxy = Math.max(maxy, p[1]);
    }
  }
  S.view = saved;
  if (showsTarget()) {
    const gs = gameScale() || 1;
    for (const t of targets()) {
      minx = Math.min(minx, t.x / gs - 40); maxx = Math.max(maxx, t.x / gs + 40);
      miny = Math.min(miny, t.y / gs - 40); maxy = Math.max(maxy, t.y / gs + 40);
    }
  }
  // Room is made for a scaled-up character, but a scaled-down one stays small so Fit doesn't hide its scale.
  const scale = gameScale(), room = Math.max(1, Math.abs(scale));
  const zoom = Math.min(r.width / ((maxx - minx) * room + 160), r.height / ((maxy - miny) * room + 160), 4);
  S.view = { zoom, x: r.width / 2 - (minx + maxx) / 2 * zoom * scale, y: r.height / 2 + (miny + maxy) / 2 * zoom * scale };
}

/* ---------------------------------------------------------------- targets and projectile shots (Abilities tab) */

// Enemies the attacks aim at, in battle space (the combatant's feet at 0, 0, y up), at each target's body centre.
// Pigs face left, so targets start on their left; a pig on a bird rig is mirrored in battle, so they're on its right.
// Only for a combatant with an attack that fires a projectile: without one there's nothing to aim or draw.
const showsTarget = () => !!S.rig && !!S.project && !$('tab-abilities').hidden && S.project.abilities.some(a => shotFor(a));
const rigMirrored = () => !!S.rig && S.rig.side === 'bird';
const MAX_TARGETS = 20;
const ALL_ENEMIES = 3; // "every enemy" counts as a full party of three birds until you add more targets
const targetColor = i => i === 0 ? '#ef6b6b' : `hsl(${(i * 137.5 + 200) % 360}, 75%, 64%)`;
const targetLabel = i => i === 0 ? 'Target' : `Target ${i + 1}`;
function targetSpot(i) {
  // Columns of four, zig-zagging so a line between two targets doesn't run through a third.
  const col = Math.floor(i / 4), row = i % 4;
  const x = 470 + col * 190 + (row % 2) * 80;
  return { x: rigMirrored() ? x : -x, y: 40 + row * 85 };
}
// How an ability hits, from its template and parameters, the way the game plays it. shots: projectiles from the hand
// ('center' = the middle of the enemy side, where the game sends one projectile at several targets). links: damage
// passed from one target to another. ricochet: the game's Ricochet flies the attack's projectile along those links,
// each hop from the last enemy hit to the next (collateral and chain attacks); other links have no projectile.
// n: how many targets there are (the need doesn't depend on it).
function shotModel(a, n = targetCount()) {
  const p = a.params || {}, t = a.template || '';
  const range = k => Array.from({ length: Math.max(0, k) }, (_, i) => i);
  if (t === 'MusicShrapnelVolley') {
    const extra = Math.max(0, Math.round(p.extra_projectiles || 0));
    return { need: ALL_ENEMIES, shots: [...range(n), ...range(extra).map(i => i % n)], links: [] };
  }
  if (t === 'SprayAttackSkill') {
    const k = Math.max(1, Math.round(p.spray_count || 1));
    return { need: Math.min(k, ALL_ENEMIES), shots: range(k).map(i => i % n), links: [] };
  }
  if ('all' in p || 'delayed_all' in p) return { need: ALL_ENEMIES, shots: [n > 1 ? 'center' : 0], links: [] };
  // A huge count (Frankenpig's 99) means every other enemy, however many there are.
  const others = k => k >= 10 ? { need: ALL_ENEMIES, reach: n - 1 } : { need: Math.min(MAX_TARGETS, 1 + k), reach: Math.min(k, n - 1) };
  const hops = k => range(k).map(i => [i, i + 1]); // Ricochet.LastHit: each hit leaves from the one before
  if (t === 'AttackWithChainSkill' || t === 'AttackWithChainAndDotSkill' || 'chain' in p) {
    const o = others(Math.max(0, Math.round(p.chain || 0)));
    return { need: o.need, shots: [0], links: hops(o.reach), ricochet: true };
  }
  if (p.additional_enemies_affected > 0) {
    const o = others(Math.round(p.additional_enemies_affected));
    return { need: o.need, shots: [0], links: hops(o.reach), ricochet: true };
  }
  const extra = Math.round(t === 'BonusDamageAndRandomTarget' ? p.spray_count || 0 : 0);
  if (extra > 0) { const o = others(extra); return { need: o.need, shots: [0], links: range(o.reach).map(i => [0, i + 1]) }; }
  if (t === 'AttackAndLinkDamage') return { need: 2, shots: [0], links: n > 1 ? [[0, 1]] : [] };
  return { need: 1, shots: [0], links: [] };
}
function neededTargets() {
  const attacks = S.project ? S.project.abilities.filter(a => a.type === 'Attack') : [];
  return Math.min(MAX_TARGETS, Math.max(1, ...attacks.map(a => shotModel(a, 1).need)));
}
// The count follows the abilities until you change it with the Targets buttons.
function targetCount() {
  const key = S.rig ? S.rig.id + '|' + (S.project && S.project.id) : '';
  if (S.targetCountFor !== key) { S.targetCountFor = key; S.targetCountSet = null; }
  return Math.min(MAX_TARGETS, Math.max(1, S.targetCountSet || neededTargets()));
}
function targets() {
  if (!S.targetList || S.targetList.rig !== S.rig.id) S.targetList = { rig: S.rig.id, items: [] };
  const items = S.targetList.items;
  for (let i = items.length; i < targetCount(); i++) items.push(targetSpot(i));
  return items.slice(0, targetCount());
}
function targetCentre(list = targets()) {
  return { x: list.reduce((s, t) => s + t.x, 0) / list.length, y: list.reduce((s, t) => s + t.y, 0) / list.length };
}
function worldMatrix() { return [S.view.zoom, 0, 0, -S.view.zoom, S.view.x, S.view.y]; }
const toScreen = t => apply(worldMatrix(), t.x, t.y);
function targetUnder(p) {
  const list = targets();
  for (let i = list.length - 1; i >= 0; i--) { const [x, y] = toScreen(list[i]); if (Math.hypot(p[0] - x, p[1] - y) <= 22) return i; }
  return -1;
}
function targetOnScreen() {
  if (!showsTarget()) return true;
  const r = canvas.getBoundingClientRect();
  return targets().every(t => { const [x, y] = toScreen(t); return x > 20 && y > 20 && x < r.width - 20 && y < r.height - 20; });
}
function drawTarget(t, i) {
  const [x, y] = toScreen(t);
  const color = targetColor(i);
  ctx.save();
  ctx.lineWidth = 2;
  ctx.strokeStyle = color;
  ctx.fillStyle = color + '';
  ctx.globalAlpha = 0.22;
  ctx.beginPath(); ctx.arc(x, y, 15, 0, Math.PI * 2); ctx.fill();
  ctx.globalAlpha = 1;
  ctx.beginPath(); ctx.arc(x, y, 15, 0, Math.PI * 2); ctx.stroke();
  ctx.beginPath(); ctx.arc(x, y, 7, 0, Math.PI * 2); ctx.stroke();
  ctx.beginPath();
  ctx.moveTo(x - 21, y); ctx.lineTo(x - 9, y); ctx.moveTo(x + 9, y); ctx.lineTo(x + 21, y);
  ctx.moveTo(x, y - 21); ctx.lineTo(x, y - 9); ctx.moveTo(x, y + 9); ctx.lineTo(x, y + 21);
  ctx.stroke();
  ctx.fillStyle = color; ctx.font = '11px system-ui, sans-serif'; ctx.textAlign = 'center';
  ctx.fillText(targetLabel(i), x, y + 34);
  ctx.restore();
}

// Unity AnimationCurve: Hermite between keys [time, value, inSlope, outSlope]; an infinite slope holds the value.
function evalUnityCurve(keys, t) {
  if (!keys || !keys.length) return t;
  if (t <= keys[0][0]) return keys[0][1];
  const last = keys[keys.length - 1];
  if (t >= last[0]) return last[1];
  for (let i = 0; i < keys.length - 1; i++) {
    const a = keys[i], b = keys[i + 1];
    if (t > b[0]) continue;
    const dt = b[0] - a[0];
    if (!dt || !Number.isFinite(a[3]) || !Number.isFinite(b[2])) return a[1];
    const s = (t - a[0]) / dt, s2 = s * s, s3 = s2 * s;
    return (2 * s3 - 3 * s2 + 1) * a[1] + (s3 - 2 * s2 + s) * a[3] * dt + (-2 * s3 + 3 * s2) * b[1] + (s3 - s2) * b[2] * dt;
  }
  return last[1];
}
// CHMotionTween: start + curve(u) per axis * (end - start); X and Y swap when the end is lower than the start.
function flightPoint(info, start, end, u) {
  let ax, ay;
  if (info && info.curves && Object.keys(info.curves).length) { ax = info.curves.x ? evalUnityCurve(info.curves.x, u) : 0; ay = info.curves.y ? evalUnityCurve(info.curves.y, u) : 0; }
  else if (info && info.curve && info.curve.length) ax = ay = evalUnityCurve(info.curve, u);
  else ax = ay = u;
  if (end[1] < start[1]) [ax, ay] = [ay, ax];
  return [start[0] + ax * (end[0] - start[0]), start[1] + ay * (end[1] - start[1])];
}
const projectileInfo = new Map();
function loadProjectile(name) {
  if (!projectileInfo.has(name)) {
    const entry = { info: null, image: new Image() };
    projectileInfo.set(name, entry);
    api('/api/projectile/' + encodeURIComponent(name)).then(info => { entry.info = info; entry.image.src = info.image; }).catch(() => {});
  }
  return projectileInfo.get(name);
}
// The projectile an attack fires and the frame of its clip it fires on: the clip's Projectile event, or halfway
// through for a projectile the ability names (AttackSkillTemplate). A main hand projectile needs the event.
function shotFor(a) {
  if (a.type !== 'Attack' || !isRangedAttack(a)) return null;
  const own = abilityProjectile(a);
  const w = S.project.weapon || {};
  const name = own || (w.isRanged && (S.projectiles || []).includes(w.projectile) ? w.projectile : '');
  if (!name) return null;
  const clipName = abilityClip(a) || defaultClip(a);
  const clip = S.rig.clips.findIndex(c => c.name === clipName);
  if (clip < 0) return null;
  const event = (S.rig.clips[clip].events || []).find(e => e[1] === 'Projectile');
  const frame = event ? event[0] : own ? S.rig.clips[clip].length / 2 : null;
  if (frame === null) return null;
  loadProjectile(name);
  return { clip, frame, name, uid: a.uid, fired: false, last: 0 };
}
// Where the MainHand bone is on a frame of a clip, in battle space: SpawnProjectile starts there.
function handAt(clipIndex, time) {
  const clip = S.rig.clips[clipIndex];
  const bone = S.rig.bones.find(b => b.name === 'MainHand');
  const gs = gameScale();
  if (!bone || bone.bone < 0) return [0, (S.rig.bodyCenter || [0, 45])[1] * gs];
  const M = pose(clip, time)[bone.bone];
  return [M[4] * gs, M[5] * gs];
}
const shotEnd = (to, list) => { const t = to === 'center' ? targetCentre(list) : list[Math.min(to, list.length - 1)]; return [t.x, t.y]; };
// Plays the ability's clip; a ranged attack fires its projectiles at the targets as it would in battle.
function playAbility(a) {
  playClipNamed(abilityClip(a) || defaultClip(a));
  S.pathAbility = a.uid;
  S.shotPlan = shotFor(a);
  if (!targetOnScreen()) fitView();
}
S.flights = [];
function updateShots(now) {
  const plan = S.shotPlan;
  if (!plan || !S.rig) return;
  if (plan.clip !== S.clip) { S.shotPlan = null; return; }
  if (S.time < plan.last) plan.fired = false; // looped
  plan.last = S.time;
  if (plan.fired || S.time < plan.frame) return;
  plan.fired = true;
  const a = S.project.abilities.find(x => x.uid === plan.uid);
  const model = a ? shotModel(a) : { shots: [0] };
  const entry = loadProjectile(plan.name);
  const start = handAt(S.clip, S.time);
  const list = targets();
  // Several shots (a spray, the King DJ volley) leave one after another.
  model.shots.forEach((to, k) => S.flights.push({ entry, start, end: shotEnd(to, list), t0: now + k * 120, speed: S.speed }));
  // Ricochets: the attack waits out its clip, the target is hit, then a copy of the projectile hops from enemy to
  // enemy, each hop leaving when the one before arrives (at most 1 s each).
  if (model.ricochet) {
    const clip = S.rig.clips[S.clip];
    let t = now + Math.max(0, clip.length - S.time) / (clip.fps * S.speed) * 1000;
    for (const [i, j] of model.links) {
      if (i >= list.length || j >= list.length) continue;
      S.flights.push({ entry, start: [list[i].x, list[i].y], end: [list[j].x, list[j].y], t0: t, speed: S.speed, hop: true });
      t += Math.min(1, (entry.info && entry.info.duration) || 0.2) / S.speed * 1000;
    }
  }
}
// Ricochet.Launch turns a hop's projectile from "toward the enemy side" to the hop's direction.
function hopTurn(f) {
  const forward = Math.sign(f.start[0]) || -1;
  return Math.atan2(f.end[1] - f.start[1], f.end[0] - f.start[0]) - Math.atan2(0, forward);
}
function drawFlights(now, base) {
  const W = worldMatrix();
  S.flights = S.flights.filter(f => {
    const info = f.entry.info;
    if (now < f.t0) return true;
    if (!info) return now - f.t0 < 2000;
    const u = (now - f.t0) / 1000 * f.speed / (info.duration || 0.2);
    if (u >= 1) return false; // the game destroys it when its tween ends
    const [x, y] = flightPoint(info, f.start, f.end, u);
    const img = f.entry.image;
    if (img.complete && img.naturalWidth) {
      let m = mul(W, [1, 0, 0, 1, x, y]);
      if (f.hop) { const r = hopTurn(f), c = Math.cos(r), s = Math.sin(r); m = mul(m, [c, s, -s, c, 0, 0]); }
      if (rigMirrored()) m = mul(m, [-1, 0, 0, 1, 0, 0]);
      m = mul(m, info.local);
      ctx.setTransform(...mul(base, mul(m, [1, 0, 0, -1, 0, 0])));
      ctx.drawImage(img, -info.w / 2, -info.h / 2, info.w, info.h);
    }
    return true;
  });
}
// The ability whose paths are drawn: the one last played, else the first attack.
function pathAbility() {
  const list = S.project.abilities.filter(a => a.type === 'Attack');
  return list.find(a => a.uid === S.pathAbility) || list[0] || null;
}
function arrowHead(x0, y0, x1, y1) {
  const ang = Math.atan2(y1 - y0, x1 - x0);
  ctx.beginPath();
  ctx.moveTo(x1, y1);
  ctx.lineTo(x1 - 10 * Math.cos(ang - 0.4), y1 - 10 * Math.sin(ang - 0.4));
  ctx.lineTo(x1 - 10 * Math.cos(ang + 0.4), y1 - 10 * Math.sin(ang + 0.4));
  ctx.closePath(); ctx.fill();
}
// Solid: each projectile's flight from the hand. Dashed: damage passed on between targets (no projectile in the game).
function drawPaths() {
  const a = pathAbility();
  if (!a) return;
  const list = targets();
  const model = shotModel(a);
  const plan = shotFor(a);
  ctx.save();
  ctx.lineWidth = 2;
  ctx.font = '11px system-ui, sans-serif';
  if (plan) {
    const entry = loadProjectile(plan.name);
    const start = handAt(plan.clip, plan.frame);
    const drawn = new Set();
    model.shots.forEach(to => {
      const key = String(to);
      if (drawn.has(key)) return;
      drawn.add(key);
      const end = shotEnd(to, list);
      const color = to === 'center' ? '#f0b23c' : targetColor(Math.min(to, list.length - 1));
      ctx.strokeStyle = color; ctx.fillStyle = color; ctx.setLineDash([]);
      ctx.beginPath();
      let prev = null, last = null;
      for (let k = 0; k <= 24; k++) {
        const [fx, fy] = flightPoint(entry.info, start, end, k / 24);
        const pt = toScreen({ x: fx, y: fy });
        if (k === 0) ctx.moveTo(pt[0], pt[1]); else ctx.lineTo(pt[0], pt[1]);
        prev = last; last = pt;
      }
      ctx.stroke();
      if (prev) arrowHead(prev[0], prev[1], last[0], last[1]);
      if (to === 'center') {
        const [cx, cy] = toScreen({ x: end[0], y: end[1] });
        ctx.beginPath(); ctx.arc(cx, cy, 5, 0, Math.PI * 2); ctx.fill();
        ctx.textAlign = 'center'; ctx.fillText('enemy side centre', cx, cy - 10);
        // The game's one projectile reaches every target from there.
        ctx.setLineDash([2, 4]); ctx.lineWidth = 1;
        list.forEach((t, i) => { const [x, y] = toScreen(t); ctx.strokeStyle = targetColor(i); ctx.beginPath(); ctx.moveTo(cx, cy); ctx.lineTo(x, y); ctx.stroke(); });
        ctx.lineWidth = 2;
      }
    });
  }
  const hopsFly = !!(plan && model.ricochet);
  ctx.setLineDash(hopsFly ? [] : [7, 6]);
  model.links.forEach(([i, j]) => {
    if (i >= list.length || j >= list.length) return;
    ctx.strokeStyle = ctx.fillStyle = targetColor(j);
    ctx.beginPath();
    let prev = null, last = null;
    const start = [list[i].x, list[i].y], end = [list[j].x, list[j].y];
    for (let k = 0; k <= (hopsFly ? 24 : 1); k++) {
      const [fx, fy] = hopsFly ? flightPoint(loadProjectile(plan.name).info, start, end, k / 24) : k ? end : start;
      const pt = toScreen({ x: fx, y: fy });
      if (k === 0) ctx.moveTo(pt[0], pt[1]); else ctx.lineTo(pt[0], pt[1]);
      prev = last; last = pt;
    }
    ctx.stroke();
    if (prev) arrowHead(prev[0], prev[1], last[0], last[1]);
  });
  ctx.setLineDash([]);
  const r = canvas.getBoundingClientRect();
  ctx.fillStyle = '#c9ccd3'; ctx.textAlign = 'left';
  const note = !plan ? (isRangedAttack(a) ? 'fires nothing it can show (no projectile, or its clip has no Projectile event)' : 'melee: no projectile')
    : model.links.length && model.ricochet ? 'the projectile, then its ricochets from enemy to enemy'
    : model.links.length ? 'solid: projectile · dashed: damage passed on (no projectile in the game)' : 'solid: projectile path';
  ctx.fillText(`Paths of ${a.name || 'this attack'}: ${note}`, 12, r.height - 12);
  ctx.restore();
}
function syncTargetControls() {
  const shown = showsTarget();
  const n = shown ? targetCount() : 0, need = shown ? neededTargets() : 0;
  const key = `${shown}|${n}|${need}`;
  if (S.targetControlsKey === key) return; // runs every frame: only touch the page when something changed
  S.targetControlsKey = key;
  $('targetControls').hidden = !shown;
  if (!shown) return;
  $('targetCount').textContent = n;
  $('targetLess').disabled = n <= 1;
  $('targetMore').disabled = n >= MAX_TARGETS;
  $('targetControls').title = `Your attacks need ${need} target${need === 1 ? '' : 's'}${need === ALL_ENEMIES ? ' (an attack hits every enemy: add more for a bigger party)' : ''}. Drag a target to move it.`;
}
$('targetLess').addEventListener('click', () => { S.targetCountSet = Math.max(1, targetCount() - 1); syncTargetControls(); });
$('targetMore').addEventListener('click', () => { S.targetCountSet = Math.min(MAX_TARGETS, targetCount() + 1); syncTargetControls(); if (!targetOnScreen()) fitView(); });
$('showPaths').addEventListener('change', e => { try { localStorage.setItem('forge.paths', e.target.checked ? '1' : ''); } catch (err) { /* optional */ } });
try { $('showPaths').checked = localStorage.getItem('forge.paths') === '1'; } catch (e) { /* storage is optional */ }

let lastFrame = performance.now();
function tick(now) {
  const dt = Math.min(0.1, (now - lastFrame) / 1000);
  lastFrame = now;
  if (S.rig) {
    const clip = S.rig.clips[S.clip];
    const still = clip.length <= 1; // a one-frame clip has nothing to play
    if (still) S.time = 0;
    else if (S.playing) {
      S.time += dt * clip.fps * S.speed;
      stepSpring(dt * S.speed);
      if (S.time >= clip.length) {
        if (S.loop) S.time %= clip.length;
        else { S.time = clip.length; S.playing = false; syncPlay(); }
      }
    }
    if (document.activeElement !== $('scrub') || S.playing) $('scrub').value = clip.length ? S.time / clip.length * 1000 : 0;
    $('frameLabel').textContent = still ? '1 / 1' : `${Math.floor(S.time)} / ${Math.round(clip.length)}`;
    $('scrub').disabled = still;
  }
  updateShots(now);
  draw(now);
  requestAnimationFrame(tick);
}
function syncPlay() { $('playBtn').textContent = S.playing ? 'Pause' : 'Play'; }

/* ---------------------------------------------------------------- canvas interaction */

function pointerPos(e) {
  const r = canvas.getBoundingClientRect();
  return [e.clientX - r.left, e.clientY - r.top];
}
function inside(d, p) {
  const m = invert(d.screen);
  const [u, v] = apply(m, p[0], p[1]);
  return Math.abs(u) <= d.w / 2 && Math.abs(v) <= d.h / 2;
}
function slotLocal(d, p) {
  // Screen to the sprite's slot space (the space tx, ty are in).
  return apply(invert(mul(viewMatrix(), d.M)), p[0], p[1]);
}
function linkedOf(layer) {
  if (!layer.pair) return null;
  for (const slot of Object.values(S.project.slots)) {
    const other = slot.layers.find(l => l.pair === layer.pair && l !== layer);
    if (other) return other;
  }
  return null;
}
// The other half of a placed item whose front and back were separated, or were dropped one at a time.
function separatedPartner(key, layer) {
  if (!layer.item || linkedOf(layer)) return null;
  const free = k => ((S.project.slots[k] || {}).layers || []).filter(l => l.item === layer.item && !linkedOf(l));
  return free(key.endsWith('_Back') ? key.slice(0, -5) : key + '_Back')[free(key).indexOf(layer)] || null;
}
function eachLinked(layer, fn) {
  fn(layer, true);
  const other = linkedOf(layer);
  if (other) fn(other, false);
}
canvas.addEventListener('pointerdown', e => {
  if (!S.rig || (e.button !== 0 && e.button !== 1)) return;
  canvas.setPointerCapture(e.pointerId);
  const p = pointerPos(e);
  // Only the left button picks up art; the middle button pans.
  if (e.button === 1) { e.preventDefault(); S.gesture = { type: 'pan', p, x: S.view.x, y: S.view.y }; return; }
  if (showsTarget()) {
    const i = targetUnder(p);
    if (i >= 0) { const t = targets()[i]; S.gesture = { type: 'target', t, p, x: t.x, y: t.y }; return; }
  }
  const g = S.gizmo;
  if (g) {
    for (const hd of g.handles) {
      if (Math.hypot(hd.p[0] - p[0], hd.p[1] - p[1]) <= 9) { startHandle(hd, g.d, p); return; }
    }
    if (inside(g.d, p)) { startMove(g.d, p); return; }
  }
  // topmost first; a body or shadow never blocks what is drawn over it
  for (let i = S.drawn.length - 1; i >= 0; i--) {
    const d = S.drawn[i];
    if (d.slot !== 'Shadow' && inside(d, p)) {
      if (d.assigned) S.project.slots[d.slot].active = d.assigned.uid;
      selectSlot(d.slot);
      if (d.assigned) startMove(d, p); else S.gesture = { type: 'none' };
      return;
    }
  }
  S.gesture = { type: 'pan', p, x: S.view.x, y: S.view.y };
});
function startMove(d, p) {
  const s = d.assigned;
  const linked = linkedOf(s);
  S.gesture = { type: 'move', d, start: slotLocal(d, p), tx: s.tx, ty: s.ty, otx: linked && linked.tx, oty: linked && linked.ty };
}
function startHandle(hd, d, p) {
  const s = d.assigned;
  const linked = linkedOf(s);
  S.gesture = { type: 'handle', hd, d, rot: s.rot, sx: s.sx, sy: s.sy, tx: s.tx, ty: s.ty,
    other: linked ? { rot: linked.rot, sx: linked.sx, sy: linked.sy, tx: linked.tx, ty: linked.ty, cx: linked.tx - linked.pivot[0] * linked.w, cy: linked.ty - linked.pivot[1] * linked.h } : null,
    startAngle: handleAngle(d, p) };
}
function handleAngle(d, p) {
  const s = d.assigned;
  const local = slotLocal(d, p);
  const c = [s.tx - s.pivot[0] * s.w, s.ty - s.pivot[1] * s.h];
  return Math.atan2(local[1] - c[1], local[0] - c[0]);
}
canvas.addEventListener('pointermove', e => {
  const g = S.gesture;
  if (!g) { updateCursor(e); return; }
  const p = pointerPos(e);
  if (g.type === 'pan') {
    S.view.x = g.x + p[0] - g.p[0];
    S.view.y = g.y + p[1] - g.p[1];
  } else if (g.type === 'target') {
    const t = g.t;
    t.x = g.x + (p[0] - g.p[0]) / S.view.zoom;
    t.y = g.y - (p[1] - g.p[1]) / S.view.zoom;
  } else if (g.type === 'move') {
    const local = slotLocal(g.d, p);
    const dx = local[0] - g.start[0], dy = local[1] - g.start[1];
    const s = g.d.assigned;
    s.tx = g.tx + dx; s.ty = g.ty + dy;
    const other = linkedOf(s);
    if (other) { other.tx = g.otx + dx; other.ty = g.oty + dy; }
    refreshInspector();
  } else if (g.type === 'handle') {
    applyHandle(g, p, e.shiftKey);
    refreshInspector();
  }
});
function applyHandle(g, p, shift) {
  const s = g.d.assigned;
  const other = linkedOf(s);
  const c = [s.tx - s.pivot[0] * s.w, s.ty - s.pivot[1] * s.h];
  if (g.hd.id === 'r') {
    let deg = (handleAngle(g.d, p) - g.startAngle) * 180 / Math.PI;
    if (shift) deg = Math.round(deg / 15) * 15;
    s.rot = g.rot + deg;
    if (other && g.other) {
      // the back layer turns around the front's centre, so the pair holds together
      const a = deg * Math.PI / 180, ox = g.other.cx - c[0], oy = g.other.cy - c[1];
      const nx = c[0] + ox * Math.cos(a) - oy * Math.sin(a), ny = c[1] + ox * Math.sin(a) + oy * Math.cos(a);
      other.rot = g.other.rot + deg;
      other.tx = g.other.tx + nx - g.other.cx;
      other.ty = g.other.ty + ny - g.other.cy;
    }
    return;
  }
  // pointer in the sprite's unrotated, unsheared axes
  const local = slotLocal(g.d, p);
  const dx = local[0] - c[0], dy = local[1] - c[1];
  const rot = s.rot * Math.PI / 180, k = s.shear || 0;
  const co = Math.cos(rot), si = Math.sin(rot);
  const ru = co * dx + si * dy, rv = -si * dx + co * dy;
  const v = rv, u = ru - k * rv;
  const nx = g.hd.su ? u / (g.hd.su * s.w / 2) : null;
  const ny = g.hd.sv ? v / (g.hd.sv * s.h / 2) : null;
  let sx = s.sx, sy = s.sy;
  if (g.hd.id === 'c') {
    if (shift) { sx = nx; sy = ny; }
    else {
      const startLen = Math.hypot(g.sx * s.w, g.sy * s.h);
      const nowLen = Math.hypot(nx * s.w, ny * s.h);
      const f = startLen ? nowLen / startLen : 1;
      sx = g.sx * f; sy = g.sy * f;
    }
  } else if (g.hd.id === 'u') sx = nx;
  else sy = ny;
  s.sx = sx; s.sy = sy;
  if (other && g.other) {
    const fx = g.sx ? sx / g.sx : 1, fy = g.sy ? sy / g.sy : 1;
    other.sx = g.other.sx * fx; other.sy = g.other.sy * fy;
    const f = g.hd.id === 'c' ? fx : 1;
    other.tx = g.other.tx + (g.other.cx - c[0]) * (f - 1);
    other.ty = g.other.ty + (g.other.cy - c[1]) * (f - 1);
  }
}
function updateCursor(e) {
  const p = pointerPos(e);
  const g = S.gizmo;
  let cursor = 'grab';
  if (g) {
    if (g.handles.some(hd => Math.hypot(hd.p[0] - p[0], hd.p[1] - p[1]) <= 9)) cursor = 'crosshair';
    else if (inside(g.d, p)) cursor = 'move';
  }
  if (cursor === 'grab' && S.drawn.some(d => d.slot !== 'Shadow' && d.screen && inside(d, p))) cursor = 'pointer';
  if (showsTarget() && targetUnder(p) >= 0) cursor = 'move';
  canvas.style.cursor = cursor;
}
canvas.addEventListener('pointerup', () => {
  if (S.gesture && (S.gesture.type === 'move' || S.gesture.type === 'handle')) { renderInspector(); renderSlots(); touch(); }
  S.gesture = null;
});
canvas.addEventListener('wheel', e => {
  if (e.ctrlKey) return;  // Ctrl+scroll zooms the whole window instead
  e.preventDefault();
  const p = pointerPos(e);
  const f = Math.exp(-e.deltaY * (e.deltaMode ? 0.05 : 0.0016));
  const zoom = Math.max(0.2, Math.min(12, S.view.zoom * f));
  S.view.x = p[0] - (p[0] - S.view.x) * (zoom / S.view.zoom);
  S.view.y = p[1] - (p[1] - S.view.y) * (zoom / S.view.zoom);
  S.view.zoom = zoom;
}, { passive: false });

/* ---------------------------------------------------------------- library */

const CATS = [['Headgear', 'Headgear'], ['MainHand', 'Main hand'], ['OffHand', 'Off hand'], ['Feature', 'Features'], ['Projectile', 'Projectiles']];
// Projectiles are the game's projectile prefabs (birds' and pigs' alike): drag one onto an attack to have it fire it.
const projectileOwner = name => (name.split('_')[1] || '');
const projectileLabel = name => name.replace(/^Projectile_/, '');
function filteredProjectiles() {
  const q = S.search.trim().toLowerCase();
  return (S.projectiles || []).filter(n => (!S.rigFilter || projectileOwner(n) === S.rigFilter) && (!q || n.toLowerCase().includes(q)));
}
function projectileCard(name) {
  const el = h('div', { class: 'card projectile', title: `${projectileLabel(name)}. Drag it onto an attack in the Abilities tab to make that attack fire it.` },
    h('img', { src: `/projectileicon/${encodeURIComponent(name)}`, alt: '', loading: 'lazy', draggable: false }),
    h('span', { class: 'cName' }, projectileLabel(name)));
  el.addEventListener('pointerdown', e => {
    if (e.button !== 0) return;
    e.preventDefault();
    S.pendingDrag = { projectile: name, x: e.clientX, y: e.clientY };
  });
  return el;
}
// The attack under the pointer while a projectile is dragged.
function abilityUnderPointer(e) {
  const el = document.elementFromPoint(e.clientX, e.clientY);
  const card = el && el.closest('.ability');
  if (!card) return null;
  const a = S.project.abilities.find(x => x.uid === card.dataset.uid);
  return a && a.type === 'Attack' ? a : null;
}
function setAbilityProjectile(a, name) {
  a.projectile = name;
  // A projectile means a ranged attack: say so, unless the main hand already makes it ranged.
  if (a.ranged === 'melee' || (!a.ranged && !(S.project.weapon || {}).isRanged)) a.ranged = 'ranged';
  touch(); renderAbilities();
  playAbility(a);
  toast(`${a.name || 'The attack'} fires ${projectileLabel(name)}`);
}
function filteredLibrary() {
  const q = S.search.trim().toLowerCase();
  return S.lib.filter(i => i.side === S.side && i.cat === S.cat && (!S.rigFilter || i.rig === S.rigFilter) && (!q || i.name.toLowerCase().includes(q) || i.rig.toLowerCase().includes(q)));
}
let gridItems = [], gridShown = 0;
function setSide(side) {
  if (side !== 'game') S.spriteSide = side;
  S.side = side;
  $('searchInput').value = side === 'game' ? S.gameSearch || '' : S.search;
  renderLibrary();
}
function renderLibrary() {
  const game = S.side === 'game';
  document.querySelectorAll('#sideTabs button').forEach(b => b.classList.toggle('on', b.dataset.side === S.side));
  $('existingBtn').classList.toggle('on', game);
  $('existingBtn').setAttribute('aria-pressed', game);
  $('searchInput').placeholder = game ? 'Search combatants' : 'Search sprites';
  $('splitAll').closest('label').style.visibility = game ? 'hidden' : '';
  $('dropZone').style.display = game ? 'none' : '';
  $('rigFilter').title = game ? 'Only combatants on this base' : 'Only sprites from this character';
  if (game) { renderCombatants(); return; }
  $('catChips').replaceChildren(...CATS.map(([k, label]) => h('button', { class: S.cat === k ? 'on' : '', onclick: () => { S.cat = k; renderLibrary(); } }, label)));
  const projectiles = S.cat === 'Projectile';
  $('splitAll').closest('label').style.visibility = projectiles ? 'hidden' : '';
  $('dropZone').style.display = projectiles ? 'none' : '';
  if (projectiles) {
    const owners = [...new Set((S.projectiles || []).map(projectileOwner))].sort();
    if (S.rigFilter && !owners.includes(S.rigFilter)) S.rigFilter = '';
    $('rigFilter').replaceChildren(h('option', { value: '' }, 'All characters'), ...owners.map(r => h('option', { value: r, selected: r === S.rigFilter }, r)));
    gridItems = filteredProjectiles();
    gridShown = 0;
    $('grid').replaceChildren();
    $('grid').scrollTop = 0;
    $('libCount').textContent = `${gridItems.length} projectiles`;
    appendGrid();
    return;
  }
  document.querySelectorAll('#sideTabs button').forEach(b => b.classList.toggle('on', b.dataset.side === S.side));
  const rigs = [...new Set(S.lib.filter(i => i.side === S.side && i.cat === S.cat).map(i => i.rig))].sort();
  if (S.rigFilter && !rigs.includes(S.rigFilter)) S.rigFilter = '';
  $('rigFilter').replaceChildren(h('option', { value: '' }, 'All characters'), ...rigs.map(r => h('option', { value: r, selected: r === S.rigFilter }, r)));
  gridItems = filteredLibrary();
  gridShown = 0;
  $('grid').replaceChildren();
  $('grid').scrollTop = 0;
  $('libCount').textContent = `${gridItems.length} items`;
  appendGrid();
}
function appendGrid() {
  const next = gridItems.slice(gridShown, gridShown + 90);
  gridShown += next.length;
  $('grid').append(...next.map(S.side === 'game' ? combatantCard : S.cat === 'Projectile' ? projectileCard : card));
}
function partBox(sprite) {
  // Places one part the way the rig would: centre at -pivot * size relative to the bone.
  const cx = -sprite.pivot[0] * sprite.w, cy = -sprite.pivot[1] * sprite.h;
  return { sprite, x0: cx - sprite.w / 2, x1: cx + sprite.w / 2, y0: cy - sprite.h / 2, y1: cy + sprite.h / 2 };
}
function thumb(parts, tag) {
  // Square tile: the art is sized in percent of the tile, so it fills it at any column width.
  const boxes = parts.map(partBox);
  const minx = Math.min(...boxes.map(b => b.x0)), maxx = Math.max(...boxes.map(b => b.x1));
  const miny = Math.min(...boxes.map(b => b.y0)), maxy = Math.max(...boxes.map(b => b.y1));
  const side = Math.max(maxx - minx, maxy - miny) || 1;
  const midx = (minx + maxx) / 2, midy = (miny + maxy) / 2;
  const el = h('div', { class: 'thumb' });
  const art = h('div', { class: 'art' });
  boxes.forEach(b => {
    art.append(h('img', { src: spriteUrl(b.sprite.key), alt: '', loading: 'lazy', draggable: false,
      style: `width:${(b.x1 - b.x0) / side * 100}%;height:${(b.y1 - b.y0) / side * 100}%;left:${(b.x0 - midx + side / 2) / side * 100}%;top:${(midy - b.y1 + side / 2) / side * 100}%` }));
  });
  el.append(art);
  if (tag) el.append(h('span', { class: 'tag' }, tag));
  return el;
}
function card(item) {
  const parts = item.parts;
  const hasPair = !!(parts.back || parts.arrow);
  const split = hasPair && (S.splitAll || S.split.has(item.id));
  const count = [parts.front, parts.back, parts.arrow].filter(Boolean).length;
  const label = item.name.replace(/^(Headgear|MainHand|OffHand)_/, '');
  const hint = hasPair ? ' Double-click to ' + (split ? 'join' : 'separate') + ' its layers.' : '';
  const el = h('div', { class: 'card' + (split ? ' split' : ''), style: split ? `grid-column:span ${count}` : null, title: `${label} (${item.rig}).${hint}` });
  if (split) {
    for (const [part, name] of [['front', 'Front'], ['back', 'Back'], ['arrow', 'Arrow']]) {
      if (!parts[part]) continue;
      const t = thumb([parts[part]], name);
      startDragOn(t, item, part, false);
      el.append(t);
    }
  } else {
    const t = thumb(parts.back ? [parts.back, parts.front] : [parts.front]);
    if (parts.back) t.append(h('span', { class: 'badge' }, 'F+B'));
    if (item.uploaded) t.append(h('span', { class: 'tag' }, 'Added'));
    el.append(t);
    startDragOn(el, item, 'front', true);
  }
  if (hasPair) {
    el.addEventListener('dblclick', () => {
      if (S.split.has(item.id)) S.split.delete(item.id); else S.split.add(item.id);
      el.replaceWith(card(item));
    });
  }
  return el;
}

canvas.addEventListener('mousedown', e => { if (e.button === 1) e.preventDefault(); }); // no autoscroll

/* drag from the library to a slot */
function startDragOn(el, item, part, joined) {
  el.addEventListener('pointerdown', e => {
    if (e.button !== 0) return;
    e.preventDefault();
    S.pendingDrag = { item, part, joined, x: e.clientX, y: e.clientY };
  });
}
window.addEventListener('pointermove', e => {
  if (S.pendingDrag && S.pendingDrag.projectile && !S.drag && Math.hypot(e.clientX - S.pendingDrag.x, e.clientY - S.pendingDrag.y) > 4) {
    S.drag = { projectile: S.pendingDrag.projectile };
    S.pendingDrag = null;
    $('dragGhost').replaceChildren(h('img', { src: `/projectileicon/${encodeURIComponent(S.drag.projectile)}`, alt: '' }));
    $('dragGhost').hidden = false;
    setTab('abilities');
  }
  if (S.drag && S.drag.projectile) {
    const ghost = $('dragGhost');
    ghost.style.left = e.clientX + 'px';
    ghost.style.top = e.clientY + 'px';
    const target = abilityUnderPointer(e);
    S.drag.target = target;
    document.querySelectorAll('.ability').forEach(r => r.classList.toggle('target', !!target && r.dataset.uid === target.uid));
    return;
  }
  if (S.pendingDrag && !S.drag && Math.hypot(e.clientX - S.pendingDrag.x, e.clientY - S.pendingDrag.y) > 4) {
    const { item, part, joined } = S.pendingDrag;
    S.drag = { item, part, joined, preferKind: item.cat === 'Feature' ? 'feature' : 'gear' };
    S.pendingDrag = null;
    const sprite = item.parts[part];
    const ghost = $('dragGhost');
    ghost.replaceChildren(h('img', { src: spriteUrl(sprite.key), alt: '' }));
    ghost.hidden = false;
    setTab('slots');
  }
  if (!S.drag) return;
  const ghost = $('dragGhost');
  ghost.style.left = e.clientX + 'px';
  ghost.style.top = e.clientY + 'px';
  S.hoverSlot = slotUnderPointer(e);
  document.querySelectorAll('.slotRow').forEach(r => r.classList.toggle('target', r.dataset.slot === S.hoverSlot));
});
window.addEventListener('pointerup', () => {
  S.pendingDrag = null;
  if (S.drag && S.drag.projectile) {
    const { projectile, target } = S.drag;
    S.drag = null;
    $('dragGhost').hidden = true;
    document.querySelectorAll('.ability.target').forEach(r => r.classList.remove('target'));
    if (target) setAbilityProjectile(target, projectile);
    else toast('Drop it on an attack in the Abilities tab');
    return;
  }
  if (!S.drag) return;
  const { item, part, joined } = S.drag;
  const slot = S.hoverSlot;
  S.drag = null;
  S.hoverSlot = null;
  $('dragGhost').hidden = true;
  document.querySelectorAll('.slotRow.target').forEach(r => r.classList.remove('target'));
  if (slot) assign(item, part, slot, joined);
});
function preferredSlot(item, part, key) {
  if (item.cat === 'Feature') return key === item.group || key.startsWith(item.group);
  if (part === 'back') return key === item.cat + '_Back';
  if (part === 'arrow') return key === 'MainHand_Arrow';
  return key === item.cat;
}
function slotUnderPointer(e) {
  const under = document.elementFromPoint(e.clientX, e.clientY);
  const row = under && under.closest && under.closest('.slotRow');
  if (row) return row.dataset.slot;
  if (under !== canvas) return null;
  const p = pointerPos(e);
  const V = viewMatrix();
  let best = null, bestD = Infinity;
  for (const a of S.anchors.filter(anchorOffered)) {
    if (S.drag.preferKind === 'gear' && a.kind === 'feature') continue;
    const [x, y] = apply(V, a.world[0], a.world[1]);
    let dist = Math.hypot(x - p[0], y - p[1]);
    if (preferredSlot(S.drag.item, S.drag.part, a.key)) dist *= 0.55;
    if (dist < bestD) { bestD = dist; best = a.key; }
  }
  return best;
}
function makeLayer(sprite, itemName) {
  return { uid: newUid(), src: sprite.key, name: sprite.name, item: itemName, w: sprite.w, h: sprite.h, pivot: sprite.pivot, tx: 0, ty: 0, rot: 0, sx: 1, sy: 1, shear: 0 };
}
function ensureSlot(key) {
  return S.project.slots[key] || (S.project.slots[key] = { layers: [], hidden: false });
}
function activeLayer(key) {
  const slot = S.project && S.project.slots[key];
  if (!slot || !slot.layers.length) return null;
  return slot.layers.find(l => l.uid === slot.active) || slot.layers[slot.layers.length - 1];
}
function addLayer(key, layer) {
  const slot = ensureSlot(key);
  slot.layers.push(layer);
  slot.active = layer.uid;
  return layer;
}
// A feature from another rig starts at the size and place of the part it replaces.
function fitToOriginal(layer, slotKey) {
  const bone = S.rig.bones.find(b => b.slot === slotKey && S.rig.anyRow[b.node]);
  if (!bone) return;
  const r = S.rig.anyRow[bone.node];
  const w = Math.abs(r[5] - r[3]), ht = Math.abs(r[4] - r[6]);
  const scale = Math.min(w / layer.w, ht / layer.h);
  layer.sx = layer.sy = scale;
  layer.tx = (r[3] + r[5]) / 2 + layer.pivot[0] * layer.w;
  layer.ty = (r[4] + r[6]) / 2 + layer.pivot[1] * layer.h;
}
function assign(item, part, slotKey, joined) {
  const layer = addLayer(slotKey, makeLayer(item.parts[part], item.name));
  if (S.rig.slotByKey[slotKey].kind === 'feature') fitToOriginal(layer, slotKey);
  if (joined) {
    if (item.parts.back) {
      const backKey = slotKey + '_Back';
      if (S.rig.slotByKey[backKey]) {
        const back = addLayer(backKey, makeLayer(item.parts.back, item.name));
        layer.pair = back.pair = newUid();
      } else {
        toast(`${S.rig.id} has no ${backKey} slot, so the back layer was left off`);
      }
    }
    if (item.parts.arrow && slotKey === 'MainHand' && S.rig.slotByKey.MainHand_Arrow) {
      addLayer('MainHand_Arrow', makeLayer(item.parts.arrow, item.name));
    }
  }
  if (slotKey === 'MainHand' && item.weapon && S.project.slots.MainHand.layers.length === 1) S.project.weapon = { ...S.project.weapon, ...item.weapon };
  selectSlot(slotKey);
  touch();
}

/* ---------------------------------------------------------------- slots panel */

function selectSlot(key) {
  S.selected = key;
  renderInspector();
  renderSlots();
}
function setSlotHidden(key, hidden) {
  const slot = ensureSlot(key);
  slot.hidden = hidden;
  if (!hidden && !slot.layers.length) delete S.project.slots[key];
  touch();
  renderSlots();
  renderInspector();
}
function renderSlots() {
  if (!S.rig) return;
  const group = (title, rows) => rows.length ? [h('div', { class: 'groupTitle' }, title), ...rows] : [];
  const row = def => {
    const slot = S.project.slots[def.key];
    const layers = slot ? slot.layers : [];
    const hidden = !!(slot && slot.hidden);
    const empty = def.kind === 'gear' ? 'Empty' : 'Original';
    const named = layers.length > 1 ? `${layers.length} layers` : layers.length ? layers[0].name : empty;
    const info = (hidden ? 'Hidden · ' : '') + named;
    const shown = visibleLayers({ layers, hidden: false }).pop();
    const thumbStyle = shown ? `background-image:url("${spriteUrl(shown.src)}")` : !layers.length && def.kind === 'feature' && def.textures[0] ? `background-image:url("${spriteUrl(def.textures[0])}");opacity:.65` : '';
    const canHide = layers.length || def.kind === 'feature' || hidden;
    return h('div', { class: 'slotRow' + (S.selected === def.key ? ' sel' : '') + (hidden ? ' off' : ''), 'data-slot': def.key, onclick: () => selectSlot(def.key) },
      h('div', { class: 'slotThumb', style: thumbStyle }),
      h('div', {}, h('div', { class: 'slotName' }, def.key), h('div', { class: 'slotInfo' }, info)),
      h('div', { class: 'slotActions' },
        canHide ? h('button', { title: hidden ? 'Show this slot again' : 'Hide this slot on this combatant, keeping its art', onclick: ev => { ev.stopPropagation(); setSlotHidden(def.key, !hidden); } }, hidden ? 'Show' : 'Hide') : null,
        layers.length ? h('button', { title: 'Remove all art from this slot', onclick: ev => { ev.stopPropagation(); clearSlot(def.key); } }, 'Clear') : null));
  };
  const gear = S.rig.slots.filter(d => d.kind === 'gear').map(row);
  const features = S.rig.slots.filter(d => d.kind === 'feature').map(row);
  $('slotList').replaceChildren(...group('Equipment', gear), ...group('Features', features));
  $('emptyHint').textContent = Object.keys(S.project.slots).length ? '' : 'Drag art from the library onto a slot on the character, or onto a row in the Slots list. Scroll to zoom, drag empty space to pan.';
}
function clearSlot(key) {
  const slot = S.project.slots[key];
  if (!slot) return;
  slot.layers.forEach(l => { const other = linkedOf(l); if (other) delete other.pair; });
  delete S.project.slots[key];
  touch();
  renderInspector();
  renderSlots();
}
function removeLayer(key, layer) {
  const slot = S.project.slots[key];
  const other = linkedOf(layer);
  if (other) delete other.pair;
  slot.layers = slot.layers.filter(l => l !== layer);
  if (!slot.layers.length && !slot.hidden) delete S.project.slots[key];
  touch(); renderInspector(); renderSlots();
}
function moveLayer(key, layer, d) {
  const list = S.project.slots[key].layers;
  const i = list.indexOf(layer), j = i + d;
  if (j < 0 || j >= list.length) return;
  [list[i], list[j]] = [list[j], list[i]];
  touch(); renderInspector();
}
function layerList(key, slot) {
  // Top of the list draws on top.
  return h('div', { class: 'layers' }, [...slot.layers].reverse().map(layer => h('div', { class: 'layerRow' + (layer === activeLayer(key) ? ' on' : '') + (layer.hidden ? ' off' : ''), onclick: () => { slot.active = layer.uid; renderInspector(); } },
    h('div', { class: 'layerThumb', style: `background-image:url("${spriteUrl(layer.src)}")` }),
    h('span', { class: 'layerName', title: layer.name }, layer.name),
    h('div', { class: 'layerActions' },
      h('button', { title: 'Draw above', 'aria-label': 'Draw above', onclick: ev => { ev.stopPropagation(); moveLayer(key, layer, 1); } }, '↑'),
      h('button', { title: 'Draw below', 'aria-label': 'Draw below', onclick: ev => { ev.stopPropagation(); moveLayer(key, layer, -1); } }, '↓'),
      h('button', { onclick: ev => { ev.stopPropagation(); layer.hidden = !layer.hidden; touch(); renderInspector(); renderSlots(); } }, layer.hidden ? 'Show' : 'Hide'),
      h('button', { class: 'danger', onclick: ev => { ev.stopPropagation(); removeLayer(key, layer); } }, 'Remove')))));
}
const TRANSFORM_FIELDS = [['tx', 'X'], ['ty', 'Y'], ['rot', 'Rotation °'], ['sx', 'Scale X'], ['sy', 'Scale Y'], ['shear', 'Skew']];
function renderInspector() {
  const box = $('inspector');
  const def = S.rig && S.rig.slotByKey[S.selected];
  const slot = S.project && S.project.slots[S.selected];
  const s = activeLayer(S.selected);
  box.replaceChildren();
  if (!def) return;
  if (!s) {
    box.append(h('div', { class: 'row between' }, h('h3', {}, S.selected), h('span', { class: 'dim' }, slot && slot.hidden ? 'Hidden on this combatant' : 'Drag art here to fill this slot')));
    return;
  }
  box.append(h('div', { class: 'row between' }, h('h3', {}, S.selected + (slot.hidden ? ' (hidden)' : '')), h('span', { class: 'dim' }, 'Drop more art to add a layer')));
  box.append(layerList(S.selected, slot));
  box.append(h('div', { class: 'row between' }, h('span', { class: 'dim' }, s.name),
    linkedOf(s) ? h('button', { title: 'Move this layer separately from its front or back', onclick: () => { const o = linkedOf(s); delete o.pair; delete s.pair; touch(); renderInspector(); renderSlots(); } }, 'Separate front and back')
      : separatedPartner(S.selected, s) ? h('button', { title: 'Move the front and back together again. Neither moves until you do.', onclick: () => { s.pair = separatedPartner(S.selected, s).pair = newUid(); touch(); renderInspector(); renderSlots(); } }, 'Join front and back') : null));
  box.append(h('div', { class: 'fields' }, TRANSFORM_FIELDS.map(([f, label]) => h('label', {}, label,
    h('input', { type: 'number', step: f === 'rot' ? 1 : f === 'tx' || f === 'ty' ? 1 : 0.01, 'data-field': f, value: round(s[f] ?? 0), oninput: ev => {
      setField(s, f, num(ev.target.value)); touch();
    } })))));
  box.append(h('div', { class: 'row wrap' },
    h('button', { onclick: () => { eachLinked(s, x => { x.sx = -x.sx; }); touch(); refreshInspector(true); } }, 'Flip X'),
    h('button', { onclick: () => { eachLinked(s, x => { x.sy = -x.sy; }); touch(); refreshInspector(true); } }, 'Flip Y'),
    h('button', { onclick: () => { eachLinked(s, x => Object.assign(x, { tx: 0, ty: 0, rot: 0, sx: 1, sy: 1, shear: 0 })); touch(); refreshInspector(true); } }, 'Reset'),
    h('span', { class: 'dim' }, 'Corner: keep shape. Shift+corner: free. Shift+rotate: 15° steps.')));
}
function setField(s, f, value) {
  const delta = value - (s[f] ?? 0);
  const ratio = (s[f] ?? 0) ? value / s[f] : 1;
  s[f] = value;
  const other = linkedOf(s);
  if (!other) return;
  if (f === 'sx' || f === 'sy') other[f] = (other[f] ?? 1) * ratio;
  else other[f] = (other[f] ?? 0) + delta;
  refreshInspector();
}
function refreshInspector(force) {
  if (force) { renderInspector(); return; }
  const s = activeLayer(S.selected);
  if (!s) return;
  document.querySelectorAll('#inspector input[data-field]').forEach(el => {
    if (el !== document.activeElement) el.value = round(s[el.dataset.field] ?? 0);
  });
}
function playClipNamed(name) {
  const i = S.rig.clips.findIndex(c => c.name === name);
  if (i >= 0) { S.clip = i; S.time = 0; S.playing = true; $('clipSelect').value = i; syncPlay(); }
}

/* ---------------------------------------------------------------- abilities */

function newUid() { return Math.random().toString(36).slice(2, 10); }
// Mathf.Round: halves go to the even number.
function mathfRound(v) {
  const r = Math.round(v);
  return Math.abs(v % 1) === 0.5 && r % 2 ? r - 1 : r;
}
// PigGameData at difficulty 0: base + base * modifier%, in floats. The game uses the health modifier for attack too.
function statAtLevel(base) {
  const f = Math.fround, b = f(num(base));
  return mathfRound(f(b + f(b * f(f(S.powerLevels[S.level] || 0) / 100))));
}
function levelStats() {
  const st = S.project.stats;
  return { hp: statAtLevel(st.hp), attack: statAtLevel(st.attack) };
}
const formulaCache = new Map();
function evalFormula(expr, params, stats, duration, level = S.level) {
  let fn = formulaCache.get(expr);
  if (!fn) {
    // Formulas are plain arithmetic read from the game's skill scripts, checked by the server (gamedata.EXPR_OK).
    fn = new Function('p', 'A', 'H', 'L', 'D', 'F', `return ${expr};`);
    formulaCache.set(expr, fn);
  }
  return fn(k => num(params[k]), stats.attack, stats.hp, level, num(duration), num(Object.values(params)[0]));
}
// A game skill's text before it belongs to a combatant: anything based on attack or health reads as a percent of it.
function menuDescription(skill) {
  return (skill.desc || '').replace(/\{value_\d+\}(%?)/g, (token, sign) => {
    const v = (skill.values || {})[token.replace('%', '')];
    if (!v || !v.expr) return token;
    const result = round(evalFormula(v.expr, skill.params || {}, { attack: 100, hp: 100 }, skill.duration, 1));
    return result + (sign || v.percent || /\b[AH]\b/.test(v.expr) ? '%' : '');
  });
}
function resolveValues(ability) {
  const values = ability.values || {};
  const params = ability.params || {};
  const stats = levelStats();
  return text => (text || '').replace(/\{value_\d+\}/g, m => {
    const v = values[m];
    if (!v || !v.expr) return m;
    const result = evalFormula(v.expr, params, stats, ability.duration);
    return (/\b[AHL]\b/.test(v.expr) ? Math.round(result) : round(result)) + (v.percent ? '%' : '');
  });
}
// A damage number printed as a percent instead of what the ability really deals.
function showsRawPercent(ability) {
  const values = ability.values || {};
  return Object.entries(values).some(([k, v]) => v.expr && /damage_in_percent/.test(v.expr) && !/\bA\b/.test(v.expr) && (ability.desc || '').includes(k));
}
function descNodes(ability) {
  // [rrggbb]text[-] colour codes, as NGUI labels render them
  const text = resolveValues(ability)(ability.desc);
  const root = document.createDocumentFragment();
  const stack = [root];
  const re = /\[([0-9a-fA-F]{6})\]|\[-\]/g;
  let last = 0, m;
  const push = t => { if (t) stack[stack.length - 1].append(t); };
  while ((m = re.exec(text))) {
    push(text.slice(last, m.index));
    last = re.lastIndex;
    if (m[1]) { const span = h('span', { style: `color:#${m[1]}` }); stack[stack.length - 1].append(span); stack.push(span); }
    else if (stack.length > 1) stack.pop();
  }
  push(text.slice(last));
  return root;
}
function orderedAbilities() {
  const list = S.project.abilities;
  return [...list.filter(a => a.type !== 'Passive'), ...list.filter(a => a.type === 'Passive')];
}
function renderTooltip() {
  const tip = $('tooltip');
  if (!S.project) return;
  const OX = 230, OY = 330, SPACING = 167;
  const abilities = orderedAbilities();
  const n = abilities.length;
  const offset = (3 - n) * SPACING;
  const X = x => x + OX, Y = y => OY - y;
  tip.replaceChildren();
  const skin = (name, x, y, w, h_) => tip.append(h('img', { class: 'skin', src: iconUrl('GenericElements', name), alt: '', draggable: false, style: `left:${x}px;top:${y}px;width:${w}px;height:${h_}px` }));
  const bodyH = 522 - offset;
  const top = Y(311), midTop = Y(251), bottom = midTop + bodyH;
  skin('InfoOverlay_TL', X(-210), top, 122, 60); skin('InfoOverlay_T', X(-88), top, 204, 60); skin('InfoOverlay_TR', X(116), top, 100, 60);
  skin('InfoOverlay_L', X(-210), midTop, 122, bodyH); skin('InfoOverlay_R', X(116), midTop, 100, bodyH);
  tip.append(h('div', { class: 'fill', style: `left:${X(-88)}px;top:${midTop}px;width:204px;height:${bodyH}px` }));
  skin('InfoOverlay_BL', X(-210), bottom, 122, 44); skin('InfoOverlay_B', X(-88), bottom, 204, 44); skin('InfoOverlay_BR', X(116), bottom, 100, 44);
  skin('Resource_XP', X(-189) - 37, Y(284) - 37, 75, 75);
  tip.append(h('div', { class: 'ttValue', style: `left:${X(-213)}px;top:${Y(285) - 20}px;width:48px;font-size:${S.level > 99 ? 22 : 32}px;line-height:40px;color:#1a2f5a` }, String(S.level)));
  tip.append(h('button', { class: 'ttLevelBtn', title: 'Lower level', 'aria-label': 'Lower level', disabled: S.level <= 1, style: `left:${X(-189) - 42}px;top:${Y(284) + 16}px`, onclick: () => setLevel(S.level - 1) }, '−'));
  tip.append(h('button', { class: 'ttLevelBtn', title: 'Higher level', 'aria-label': 'Higher level', disabled: S.level >= maxLevel(), style: `left:${X(-189) + 18}px;top:${Y(284) + 16}px`, onclick: () => setLevel(S.level + 1) }, '+'));
  tip.append(h('input', { class: 'ttName abs', 'aria-label': 'Combatant name', value: S.project.name, placeholder: 'Name', style: `left:${X(-150)}px;top:${Y(276) - 22}px;width:300px;height:45px`,
    oninput: ev => { S.project.name = ev.target.value; syncNameFromTooltip(); } }));
  // health
  skin('Character_Health_Small', X(-80) - 16, Y(225) - 16, 32, 32);
  skin('InfoOverlay_HealthBar_Body', X(15) - 76, Y(225) - 12, 152, 24);
  skin('InfoOverlay_HealthBar_Fill', X(15) - 72, Y(225) - 8, 144, 16);
  const { hp, attack } = levelStats();
  $('levelStats').textContent = `Level ${S.level} · Health ${hp} · Attack ${attack}`;
  tip.append(h('div', { class: 'ttValue', style: `left:${X(15) - 65}px;top:${Y(225) - 15}px;width:130px;font-size:21px;line-height:30px;color:#fff;text-shadow:0 1px 1px #000` }, `${hp}/${hp}`));
  // skills
  abilities.forEach((a, i) => {
    const sy = 120 - i * SPACING;
    tip.append(h('div', { class: 'ttBox', title: 'Change the strip colour', style: `left:${X(-207)}px;top:${Y(sy + 47) - 20}px;width:414px;height:40px;background:#${a.color}`, onclick: () => openColorPicker(a) }));
    tip.append(h('input', { class: 'ttSkillName abs', 'aria-label': 'Ability name', value: a.name, style: `left:${X(-114)}px;top:${Y(sy + 50) - 18}px;width:300px;height:36px`,
      oninput: ev => { a.name = ev.target.value; touch(); renderAbilityTitles(); } }));
    const icon = a.atlas && a.icon ? h('img', { class: 'ttIcon', src: iconUrl(a.atlas, a.icon), alt: 'Ability icon', title: 'Change the icon', draggable: false, style: `left:${X(-163) - 42}px;top:${Y(sy + 47) - 42}px;width:84px;height:84px`, onclick: () => openIconPicker(a) })
      : h('button', { class: 'abs', style: `left:${X(-163) - 28}px;top:${Y(sy + 47) - 20}px`, onclick: () => openIconPicker(a) }, 'Icon');
    tip.append(icon);
    if (a.params && num(a.params.charge) > 0) tip.append(h('div', { class: 'ttCharge', style: `left:${X(0) - 73}px;top:${Y(sy + 23) - 6}px;width:146px` }));
    const desc = h('div', { class: 'ttDesc abs', title: 'Click to edit', style: `left:${X(-190)}px;top:${Y(sy - 40 + 46)}px;width:380px;min-height:90px;max-height:${SPACING - 62}px;overflow:hidden`, onclick: () => editDesc(a, desc) });
    desc.append(descNodes(a));
    tip.append(desc);
  });
  const height = bottom + 44;
  tip.style.height = height + 'px';
  fitTooltip(height);
}
function maxLevel() {
  return Math.max(1, ...Object.keys(S.powerLevels).map(Number));
}
function setLevel(level) {
  S.level = Math.max(1, Math.min(maxLevel(), level));
  renderTooltip();
}
function fitTooltip(height) {
  const outer = $('tooltipOuter');
  const avail = outer.clientWidth || 440;
  const scale = Math.min(1, avail / 450);
  const tip = $('tooltip');
  tip.style.width = '450px';
  tip.style.left = Math.max(0, (avail - 450 * scale) / 2) + 'px';
  tip.style.transform = `scale(${scale})`;
  outer.style.height = height * scale + 12 + 'px';
}
function editDesc(ability, el) {
  const box = h('textarea', { class: 'ttDescEdit abs', 'aria-label': 'Ability description', value: ability.desc, style: el.style.cssText + ';height:' + Math.max(100, el.offsetHeight) + 'px', spellcheck: false });
  box.addEventListener('input', () => { ability.desc = box.value; touch(); });
  box.addEventListener('blur', () => { renderTooltip(); renderAbilities(); });
  el.replaceWith(box);
  box.focus();
}
function renderAbilityTitles() {
  document.querySelectorAll('.ability').forEach(card => {
    const a = S.project.abilities.find(x => x.uid === card.dataset.uid);
    if (a) card.querySelector('.title').textContent = a.name || 'Unnamed';
  });
}
/* Which clip an ability plays. The game stores it in the skill parameter anim_clip as a 24-bit FNV-1a hash of the clip
   name (ForgeSkillAnimation.cs, publish.clip_hash); no parameter means the usual clip. */
function clipHash(name) {
  let h = 0x811c9dc5;
  for (let i = 0; i < name.length; i++) { h ^= name.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; }
  return h & 0xFFFFFF;
}
function defaultClip(a) {
  if (a.type === 'Support') return 'Action_Support';
  const own = 'Action_Attack_' + ((S.project.weapon || {}).animationIndex || 1);
  return S.rig.clips.some(c => c.name === own) ? own : 'Action_Attack_1';
}
function abilityClip(a) {
  if ('anim' in a) return a.anim || '';
  const value = a.params && a.params.anim_clip;
  if (value === undefined) return '';
  const clip = S.rig.clips.find(c => clipHash(c.name) === Math.round(value));
  return clip ? clip.name : '';
}
// The battle events a clip carries that the default one has: a melee attack walks up on Movement and hits on Impact,
// a ranged one fires on Projectile, a support takes effect on Impact.
function missingEvents(a, clipName) {
  const tagsOf = name => new Set((S.rig.clips.find(c => c.name === name) || { tags: [] }).tags || []);
  const want = tagsOf(defaultClip(a)), have = tagsOf(clipName);
  let needed = ['Movement', 'Impact', 'Projectile'];
  if (a.type === 'Attack' && isRangedAttack(a)) {
    // Ranged: no walking, damage after the clip; a projectile the ability names fires halfway without the event.
    needed = abilityProjectile(a) ? [] : ['Projectile'];
    return needed.filter(t => !have.has(t) && (want.has(t) || (S.project.weapon || {}).isRanged));
  }
  if (a.type === 'Attack') needed = ['Movement', 'Impact'];
  return needed.filter(t => want.has(t) && !have.has(t));
}
const EVENT_NOTES = { Movement: 'it won\'t step up to its target', Impact: 'the hit lands after a short fallback delay instead of on a frame of the clip', Projectile: 'it won\'t fire its projectile' };
function animationRow(a) {
  if (a.type === 'Passive' || !S.rig) return null;
  const current = abilityClip(a);
  const usable = S.rig.clips.filter(c => c.name !== 'Base');
  const label = c => { const t = (c.tags || []).filter(x => ['Movement', 'Impact', 'Projectile'].includes(x)); return t.length ? `${c.name} (${t.join(', ')})` : c.name; };
  const play = () => playAbility(a);
  const select = h('select', { 'aria-label': 'Animation for this ability', onchange: ev => { a.anim = ev.target.value; touch(); renderAbilities(); playAbility(a); } },
    h('option', { value: '', selected: !current }, `Usual: ${defaultClip(a)}`),
    ...usable.map(c => h('option', { value: c.name, selected: c.name === current }, label(c))));
  const missing = current ? missingEvents(a, current) : [];
  return [h('div', { class: 'row animRow' }, h('label', {}, 'Animation', select), h('button', { class: 'iconBtn', title: 'Play it on the character', 'aria-label': 'Play this animation', onclick: play }, '▶')),
    missing.length ? h('span', { class: 'warnNote' }, `${current} has no ${missing.join(' or ')} event, so ${missing.map(m => EVENT_NOTES[m]).join(', and ')}.`) : null];
}
// How an attack reaches its target, overriding its weapon: ranged/melee (skill parameter anim_ranged) and the projectile
// it fires (anim_projectile, hashed like the clip). A ranged attack stays in place and fires on the clip's Projectile
// event, or halfway through a clip without one.
function abilityRanged(a) {
  if ('ranged' in a) return a.ranged || '';
  const v = a.params && a.params.anim_ranged;
  return v === undefined ? '' : v >= 0.5 ? 'ranged' : 'melee';
}
function abilityProjectile(a) {
  if ('projectile' in a) return a.projectile || '';
  const value = a.params && a.params.anim_projectile;
  if (value === undefined) return '';
  return (S.projectiles || []).find(n => clipHash(n) === Math.round(value)) || '';
}
function isRangedAttack(a) {
  const r = abilityRanged(a);
  if (r) return r === 'ranged';
  return !!((S.project.weapon || {}).isRanged || abilityProjectile(a));
}
function projectileRow(a) {
  if (a.type !== 'Attack' || !S.projectiles) return null;
  const w = S.project.weapon || {};
  const ranged = abilityRanged(a);
  const rangedSel = h('select', { 'aria-label': 'Melee or ranged', onchange: ev => { a.ranged = ev.target.value; touch(); renderAbilities(); } },
    h('option', { value: '', selected: !ranged }, `Usual: ${w.isRanged ? 'ranged' : 'melee'} (main hand)`),
    h('option', { value: 'melee', selected: ranged === 'melee' }, 'Melee: walks up and hits'),
    h('option', { value: 'ranged', selected: ranged === 'ranged' }, 'Ranged: stays in place'));
  const rows = [h('div', { class: 'row animRow' }, h('label', {}, 'Reach', rangedSel))];
  if (!isRangedAttack(a)) return rows;
  const current = abilityProjectile(a);
  const usual = w.isRanged && w.projectile ? `Main hand's: ${projectileLabel(w.projectile)}` : 'None';
  const select = h('select', { 'aria-label': 'Projectile for this ability', onchange: ev => { a.projectile = ev.target.value; touch(); renderAbilities(); playAbility(a); } },
    h('option', { value: '', selected: !current }, usual), ...(S.projectiles || []).map(n => h('option', { value: n, selected: n === current }, projectileLabel(n))));
  const shown = current || (w.isRanged && w.projectile && (S.projectiles || []).includes(w.projectile) ? w.projectile : '');
  rows.push(h('div', { class: 'row animRow projectileDrop', title: 'Drag a projectile from the library\'s Projectiles onto this attack' },
    shown ? h('img', { src: `/projectileicon/${encodeURIComponent(shown)}`, alt: '' }) : null,
    h('label', {}, 'Projectile', select)));
  const clip = abilityClip(a) || defaultClip(a);
  const tags = (S.rig.clips.find(c => c.name === clip) || { tags: [] }).tags || [];
  if (current) rows.push(h('span', { class: 'dim note' }, `Fires ${projectileLabel(current)} ${tags.includes('Projectile') ? `on ${clip}'s Projectile event` : `halfway through ${clip}, which has no Projectile event`}.`));
  return rows;
}
function statusOf(a) {
  if (a.written) return ['done', `Written by Claude (${a.template || 'template'})`];
  if (a.source) return a.nameId ? ['', 'Own copy of a game ability'] : ['', 'From the game: ' + a.source];
  return ['pending', 'Not written yet. Battle uses a stand-in until Claude writes it.'];
}
function renderAbilities() {
  const list = S.project.abilities;
  syncGenerateAll();
  $('abilityList').replaceChildren(...list.map((a, i) => {
    const [cls, text] = statusOf(a);
    const design = h('textarea', { 'aria-label': 'What this ability does', placeholder: a.source ? 'Optional: how Claude should change this ability' : 'Describe what this ability does. Claude builds it from this.', value: a.design || '',
      oninput: ev => { a.design = ev.target.value; touch(); createBtn.disabled = !a.design.trim() || !S.claude; syncGenerateAll(); } });
    const createBtn = h('button', { disabled: !(a.design || '').trim() || !S.claude, title: S.claude ? 'Runs Claude Code on this project to build the ability' : 'Claude Code was not found on this machine', onclick: () => createAbility(a) }, a.written ? 'Rewrite with Claude' : 'Create with Claude');
    return h('div', { class: 'ability', 'data-uid': a.uid, style: `--strip:#${a.color}` },
      h('div', { class: 'row' },
        h('button', { class: 'iconBtn', title: 'Move up', 'aria-label': 'Move up', disabled: i === 0, onclick: () => moveAbility(i, -1) }, '↑'),
        h('button', { class: 'iconBtn', title: 'Move down', 'aria-label': 'Move down', disabled: i === list.length - 1, onclick: () => moveAbility(i, 1) }, '↓'),
        h('input', { class: 'title', 'aria-label': 'Ability name', value: a.name || '', placeholder: 'Unnamed', maxlength: 40,
          oninput: ev => { a.name = ev.target.value; touch(); renderTooltip(); renderStats(); } }),
        h('select', { 'aria-label': 'Ability type', onchange: ev => changeType(a, ev.target.value) }, ['Attack', 'Support', 'Passive'].map(t => h('option', { selected: t === a.type }, t))),
        h('button', { class: 'danger', onclick: () => { S.project.abilities.splice(i, 1); touch(); renderAbilities(); renderTooltip(); renderStats(); } }, 'Remove')),
      h('span', { class: `status ${cls}` }, text),
      showsRawPercent(a) ? h('span', { class: 'warnNote' }, 'This tooltip prints damage as a percent. Rewrite it with Claude, or use the placeholder that holds the real number.') : null,
      animationRow(a),
      projectileRow(a),
      design,
      h('div', { class: 'row' }, createBtn));
  }));
  if (!list.length) $('abilityList').append(h('p', { class: 'dim' }, 'No abilities yet. A combatant needs at least one attack or support ability to act in battle.'));
}
function moveAbility(i, d) {
  const list = S.project.abilities;
  [list[i], list[i + d]] = [list[i + d], list[i]];
  touch(); renderAbilities(); renderTooltip();
}
function changeType(a, type) {
  if (a.color === S.colors[a.type]) a.color = S.colors[type];
  a.type = type;
  touch(); renderAbilities(); renderTooltip(); renderStats();
}
function addAbility(a) {
  S.project.abilities.push(a);
  touch(); renderAbilities(); renderTooltip(); renderStats();
}
$('addNew').addEventListener('click', () => addAbility({ uid: newUid(), type: 'Attack', name: 'New ability', desc: 'Click here to write what the tooltip says.', color: S.colors.Attack, atlas: 'Skills_Pigs_01', icon: 'Attack', design: '' }));
$('addExisting').addEventListener('click', () => {
  const modal = $('modal');
  modal.className = '';
  const listBox = h('div', { class: 'list' });
  const search = h('input', { type: 'search', class: 'grow', placeholder: 'Search by name, id or what it says', oninput: render });
  const typeSel = h('select', { 'aria-label': 'Type', onchange: render }, h('option', { value: '' }, 'All types'), ...['Attack', 'Support', 'Passive'].map(t => h('option', {}, t)));
  function render() {
    const q = search.value.toLowerCase();
    const rows = S.skills.filter(s => (!typeSel.value || s.type === typeSel.value) && (!q || s.id.includes(q) || s.name.toLowerCase().includes(q) || menuDescription(s).toLowerCase().includes(q))).slice(0, 120);
    listBox.replaceChildren(...rows.map(s => h('button', { class: 'pick', onclick: () => {
      addAbility({ uid: newUid(), type: s.type, source: s.id, name: s.name, desc: s.desc, color: s.color, atlas: s.atlas, icon: s.icon, params: s.params, values: s.values, template: s.template, duration: s.duration, design: '' });
      modal.close();
    } },
      s.atlas && s.icon ? h('img', { src: iconUrl(s.atlas, s.icon), alt: '', loading: 'lazy' }) : h('span'),
      h('span', {}, h('strong', {}, s.name), ' ', h('span', { class: 'dim' }, `${s.type} · ${s.id}`), h('small', {}, menuDescription(s).replace(/\[[0-9a-fA-F]{6}\]|\[-\]/g, ''))))));
    if (!rows.length) listBox.append(h('p', { class: 'dim' }, 'Nothing matches.'));
  }
  modal.replaceChildren(h('div', { class: 'row between' }, h('h3', {}, 'Add an ability from the game'), h('button', { onclick: () => modal.close() }, 'Cancel')),
    h('div', { class: 'row', style: 'margin-top:8px' }, search, typeSel), listBox);
  render();
  modal.showModal();
  search.focus();
});
function openIconPicker(ability) {
  const modal = $('modal');
  modal.className = '';
  const grid = h('div', { class: 'iconGrid' });
  const search = h('input', { type: 'search', class: 'grow', placeholder: 'Search icons', oninput: render });
  const atlasSel = h('select', { 'aria-label': 'Icon sheet', onchange: render }, S.icons.map(g => h('option', { selected: g.atlas === ability.atlas }, g.atlas)));
  function render() {
    const q = search.value.toLowerCase();
    const group = S.icons.find(g => g.atlas === atlasSel.value);
    grid.replaceChildren(...group.icons.filter(n => n.toLowerCase().includes(q)).map(name => h('button', { class: 'iconCell' + (name === ability.icon && group.atlas === ability.atlas ? ' on' : ''), title: name, onclick: () => {
      ability.atlas = group.atlas; ability.icon = name; touch(); renderTooltip(); modal.close();
    } }, h('img', { src: iconUrl(group.atlas, name), alt: '', loading: 'lazy' }), h('span', {}, name))));
  }
  modal.replaceChildren(h('div', { class: 'row between' }, h('h3', {}, `Icon for ${ability.name || 'this ability'}`), h('button', { onclick: () => modal.close() }, 'Cancel')),
    h('div', { class: 'row', style: 'margin-top:8px' }, search, atlasSel), grid);
  render();
  modal.showModal();
}

/* colour picker: the game's three strip colours, then HSV, RGB and hex */
const hexToRgb = hex => [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16));
const rgbToHex = rgb => rgb.map(v => Math.round(v).toString(16).padStart(2, '0')).join('').toUpperCase();
function rgbToHsv([r, g, b]) {
  r /= 255; g /= 255; b /= 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
  let hue = 0;
  if (d) hue = max === r ? ((g - b) / d + 6) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [hue * 60, max ? d / max * 100 : 0, max * 100];
}
function hsvToRgb([hue, s, v]) {
  s /= 100; v /= 100;
  const f = n => { const k = (n + hue / 60) % 6; return (v - v * s * Math.max(0, Math.min(k, 4 - k, 1))) * 255; };
  return [f(5), f(3), f(1)];
}
function openColorPicker(ability) {
  const modal = $('modal');
  modal.className = 'small';
  let rgb = hexToRgb(ability.color), hsv = rgbToHsv(rgb);
  const dot = h('div', { id: 'svDot' });
  const sv = h('div', { id: 'svBox' }, dot);
  const hexInput = h('input', { class: 'mono grow', maxLength: 6, 'aria-label': 'Hex colour' });
  const inputs = {};
  const sliderRow = (id, label, max) => {
    const range = h('input', { type: 'range', min: 0, max, step: 1, 'aria-label': label });
    const number = h('input', { type: 'number', min: 0, max, 'aria-label': label });
    const onInput = v => {
      const val = Math.max(0, Math.min(max, num(v)));
      if ('rgb'.includes(id)) { rgb['rgb'.indexOf(id)] = val; hsv = rgbToHsv(rgb); }
      else { hsv['hsv'.indexOf(id.toLowerCase())] = val; rgb = hsvToRgb(hsv); }
      paint(id);
    };
    range.addEventListener('input', () => onInput(range.value));
    number.addEventListener('input', () => onInput(number.value));
    inputs[id] = { range, number };
    return h('div', { class: 'sliderRow' }, h('span', {}, label), range, number);
  };
  function paint(skip) {
    const hex = rgbToHex(rgb);
    ability.color = hex;
    sv.style.background = `linear-gradient(to top, #000, transparent), linear-gradient(to right, #fff, hsl(${hsv[0]}, 100%, 50%))`;
    dot.style.left = hsv[1] + '%';
    dot.style.top = (100 - hsv[2]) + '%';
    if (document.activeElement !== hexInput) hexInput.value = hex;
    const vals = { r: rgb[0], g: rgb[1], b: rgb[2], H: hsv[0], S: hsv[1], V: hsv[2] };
    for (const [id, el] of Object.entries(inputs)) {
      if (id !== skip) el.range.value = vals[id];
      if (document.activeElement !== el.number) el.number.value = Math.round(vals[id]);
    }
    renderTooltip();
    const card = document.querySelector(`.ability[data-uid="${ability.uid}"]`);
    if (card) card.style.setProperty('--strip', '#' + hex);
    touch();
  }
  const dragSv = e => {
    const r = sv.getBoundingClientRect();
    hsv[1] = Math.max(0, Math.min(100, (e.clientX - r.left) / r.width * 100));
    hsv[2] = Math.max(0, Math.min(100, 100 - (e.clientY - r.top) / r.height * 100));
    rgb = hsvToRgb(hsv);
    paint();
  };
  sv.addEventListener('pointerdown', e => { sv.setPointerCapture(e.pointerId); dragSv(e); });
  sv.addEventListener('pointermove', e => { if (e.buttons) dragSv(e); });
  hexInput.addEventListener('input', () => {
    const v = hexInput.value.replace(/[^0-9a-fA-F]/g, '');
    if (v.length === 6) { rgb = hexToRgb(v); hsv = rgbToHsv(rgb); paint(); }
  });
  const presets = [['Attack', 'Attack'], ['Support', 'Support'], ['Passive', 'Passive']].map(([k, label]) => h('button', { style: `background:#${S.colors[k]}`, onclick: () => { rgb = hexToRgb(S.colors[k]); hsv = rgbToHsv(rgb); paint(); } }, label));
  modal.replaceChildren(h('div', { class: 'picker' },
    h('div', { class: 'row between' }, h('h3', {}, 'Strip colour'), h('button', { onclick: () => modal.close() }, 'Done')),
    h('div', { class: 'presets' }, presets), sv,
    sliderRow('H', 'H', 360), sliderRow('S', 'S', 100), sliderRow('V', 'V', 100),
    sliderRow('r', 'R', 255), sliderRow('g', 'G', 255), sliderRow('b', 'B', 255),
    h('label', {}, 'Hex #', hexInput)));
  paint();
  modal.showModal();
}

/* ---------------------------------------------------------------- stats */

function renderStats() {
  const st = S.project.stats;
  const field = (label, key, type, step) => h('label', {}, label, h('input', { type, step, value: st[key], oninput: ev => { const value = type === 'number' ? num(ev.target.value) : ev.target.value; if (key === 'sizeScale' && value <= 0) return; /* the pig would vanish in battle */ st[key] = value; S.project.statsEdited = true; touch(); renderTooltipIfVisible(); } }));
  const actives = S.project.abilities.filter(a => a.type !== 'Passive');
  const attacks = actives.filter(a => a.type === 'Attack'), supports = actives.filter(a => a.type === 'Support');
  let plan = 'It has no ability it can use in battle yet.';
  const gamePlan = existingPlan();
  if (gamePlan) plan = gamePlan;
  else if (attacks.length) plan = `Each turn: ${supports.map(a => a.name + ', then ').join('')}${attacks.length > 1 ? 'one of ' : ''}${attacks.map(a => a.name).join(' or ')}${attacks.length > 1 ? ', equally often' : ''}.`;
  else if (supports.length) plan = `Each turn: ${supports.map(a => a.name).join(', then ')}.`;
  const resetScale = h('button', { title: 'Back to 1, the size the game draws this character at unscaled', disabled: num(st.sizeScale) === 1, onclick: ev => { ev.preventDefault(); st.sizeScale = 1; S.project.statsEdited = true; touch(); renderStats(); } }, 'Reset');
  const scaleField = field('Scale', 'sizeScale', 'number', 0.05);
  const scaleInput = scaleField.querySelector('input');
  scaleInput.addEventListener('input', () => { resetScale.disabled = num(st.sizeScale) === 1; });
  scaleField.append(h('div', { class: 'row' }, scaleInput, resetScale));
  $('statsForm').replaceChildren(
    field('Health', 'hp', 'number', 1), field('Attack', 'attack', 'number', 1),
    h('label', {}, 'Size', h('select', { onchange: ev => { st.sizeType = ev.target.value; S.project.statsEdited = true; touch(); } }, ['Small', 'Medium', 'Large', 'Boss'].map(t => h('option', { selected: t === st.sizeType }, t)))),
    scaleField, field('Strength', 'strength', 'number', 1),
    h('p', { class: 'note' }, 'Health and Attack are the level 1 values. In battle the pig takes the level of the battle, and the game\'s level table raises both. Change the level in the tap-and-hold preview to see them. Each battle can add its own difficulty on top.'),
    h('p', { class: 'note' }, 'Scale is how big it looks in battle, and the preview shows it. Size doesn\'t change how big it looks: it sets the room it takes on the battlefield, its tap area and the size of effects on it.'),
    h('p', { class: 'note' }, plan),
    isExisting()
      ? h('p', { class: 'note' }, 'This is the game\'s own ', h('code', {}, S.project.id), '. Update in game writes your edits into it. Inventories, items, abilities and names that other pigs share are copied for it first, so those pigs stay as they are.')
      : h('p', { class: 'note' }, 'The defeat rewards come from the first game pig on this base. Own inventory: ', h('code', {}, `pig_forge_${S.project.id || 'id'}_01`), '.'),
    isExisting() ? variantBox() : null);
}
// The game's turn plan, which an existing combatant keeps unless an ability is added.
function existingPlan() {
  const ex = isExisting() ? S.project.existing : null;
  if (!ex || !ex.combos || !ex.combos.length) return null;
  const origins = new Set(S.project.existing.original.abilities.map(a => a.origin));
  if (S.project.abilities.some(a => a.type !== 'Passive' && !origins.has(a.origin))) return null;
  const nameOf = id => { const a = S.project.abilities.find(x => x.origin === id); const s = S.skills.find(x => x.id === id); return a ? a.name : s ? s.name : id; };
  const live = new Set(S.project.abilities.map(a => a.origin));
  const parts = ex.combos.map(c => [c.Percentage, c.ComboChain.filter(i => live.has(i)).map(nameOf)]).filter(([, chain]) => chain.length);
  if (!parts.length) return null;
  return 'Each turn, as in the game: ' + parts.map(([pct, chain]) => `${round(pct, 1)}% ${chain.join(', then ')}`).join(' · ') + '.';
}
function variantBox() {
  const box = h('div', { class: 'variantBox' }, h('h3', {}, 'Variants'));
  if (!S.existing) {
    box.append(h('p', { class: 'dim' }, 'Loading…'));
    loadExisting().then(() => { if (isExisting()) renderStats(); });
    return box;
  }
  const kids = existingDescendants(S.project.id);
  if (!kids.length) { box.append(h('p', { class: 'dim' }, 'This combatant has no variants.')); return box; }
  const off = new Set(S.existing.unticked[S.project.id] || []);
  box.append(h('p', { class: 'note' }, 'Ticked variants get the same edits when you update: the same art, abilities and settings, and Health, Attack and Scale scaled by the same factor. A name change reaches variants that had the same name.'));
  for (const v of kids) {
    box.append(h('label', { class: 'check', title: `Tick to give ${v.id} the same edits` },
      h('span', { class: 'vName' }, h('input', { type: 'checkbox', checked: !off.has(v.id), onchange: ev => setTick(S.project.id, v.id, ev.target.checked) }), ' ', v.name, ' ', h('span', { class: 'vId' }, v.id)),
      h('span', { class: 'vStats' }, `${v.hp} HP · ${v.attack} ATK`)));
  }
  return box;
}
function renderTooltipIfVisible() { renderTooltip(); }

/* ---------------------------------------------------------------- project */

function blankProject(rig) {
  return { id: '', name: '', rig, variant: '', slots: {}, abilities: [], weapon: { animationIndex: 1, isRanged: false, projectile: '', hitEffect: '_Melee_Smash' }, stats: { hp: 100, attack: 10, sizeType: 'Medium', sizeScale: 1, strength: 5, perLevelHealth: 0, perLevelAttack: 0, loot: {} } };
}
let saveTimer, historyTimer;
const SERVER_FIELDS = ['guids', 'publishedAt', 'publishedRig', 'existing'];
const SERVER_ABILITY_FIELDS = ['nameId', 'written', 'template', 'params', 'duration', 'values', 'origin'];
const isExisting = () => !!(S.project && S.project.kind === 'existing');
function queueSave() {
  $('saveState').textContent = S.project.id ? 'Saving…' : 'Set a name to save';
  clearTimeout(saveTimer);
  saveTimer = setTimeout(saveNow, 500);
}
function touch() {
  queueSave();
  clearTimeout(historyTimer);
  historyTimer = setTimeout(commitHistory, 350);
}
function commitHistory() {
  clearTimeout(historyTimer);
  const now = JSON.stringify(S.project);
  if (now === S.snapshot) return;
  S.undo.push(S.snapshot);
  if (S.undo.length > 200) S.undo.shift();
  S.redo = [];
  S.snapshot = now;
}
function resetHistory() {
  S.undo = []; S.redo = [];
  S.snapshot = JSON.stringify(S.project);
}
async function stepHistory(from, to, label) {
  commitHistory();
  if (!from.length) { toast(`Nothing to ${label}`); return; }
  to.push(S.snapshot);
  const restored = JSON.parse(from.pop());
  // What uploads and Claude decided stays, whatever the edit being undone.
  for (const f of SERVER_FIELDS) { if (f in S.project) restored[f] = S.project[f]; else delete restored[f]; }
  const current = Object.fromEntries(S.project.abilities.map(a => [a.uid, a]));
  for (const a of restored.abilities) { const c = current[a.uid]; if (c) for (const f of SERVER_ABILITY_FIELDS) { if (f in c) a[f] = c[f]; } }
  if (S.project.publishedAt) restored.id = S.project.id;
  const rigChanged = restored.rig !== S.rig.id;
  S.project = restored;
  S.snapshot = JSON.stringify(restored);
  if (rigChanged) await loadRig(restored.rig);
  $('nameInput').value = restored.name || '';
  $('idInput').value = restored.id || '';
  renderBase(); renderSlots(); renderInspector(); renderAbilities(); renderTooltip(); renderStats();
  queueSave();
}
async function saveNow() {
  if (!S.project.id || S.deleting) return;
  try {
    await api('/api/save', { ...S.project, renamedFrom: S.savedId && S.savedId !== S.project.id ? S.savedId : undefined });
    S.savedId = S.project.id;
    $('saveState').textContent = S.project.publishedAt ? `Saved · ${isExisting() ? 'updated' : 'uploaded'} ${S.project.publishedAt}` : 'Saved';
    S.projects = (await api('/api/bootstrap')).projects;
    renderProjectList();
  } catch (e) { $('saveState').textContent = 'Not saved: ' + e.message; }
}
function renderProjectList() {
  const first = isExisting() ? h('option', { value: '', selected: true }, `Game: ${S.project.name || S.project.id}`) : h('option', { value: '' }, S.project && S.project.id ? '—' : 'Unsaved');
  $('projectSelect').replaceChildren(first, ...S.projects.map(p => h('option', { value: p.id, selected: !isExisting() && S.project && p.id === S.project.id }, p.name || p.id)));
}
function syncNameFromTooltip() {
  $('nameInput').value = S.project.name;
  nameChanged();
}
function nameChanged() {
  // A running job writes back to this id, so it can't change until the job ends.
  if (!S.idTouched && !S.project.publishedAt && !idLocked()) {
    S.project.id = slugify(S.project.name);
    $('idInput').value = S.project.id;
  }
  touch();
}
let rigRequest = 0;
async function loadRig(id) {
  const request = ++rigRequest;
  const rig = await api('/api/rig/' + encodeURIComponent(id));
  if (request !== rigRequest) return false; // a newer choice replaced this one while it loaded
  rig.slotByKey = Object.fromEntries(rig.slots.map(s => [s.key, s]));
  rig.restClip = Math.max(0, rig.clips.findIndex(c => c.name === 'Idle'));
  rig.anyRow = {};
  for (const clip of [rig.clips[rig.restClip], ...rig.clips]) {
    for (const [node, rows] of Object.entries(clip.tex)) {
      if (!rig.anyRow[node]) { const r = rows.find(x => x[1]); if (r) rig.anyRow[node] = r; }
    }
  }
  S.rig = rig;
  $('clipSelect').replaceChildren(...rig.clips.map((c, i) => h('option', { value: i }, c.name)));
  S.clip = rig.restClip; S.time = 0;
  $('clipSelect').value = S.clip;
  if (S.project.variant && !Object.keys(rig.variants[S.project.variant] || {}).length && !(rig.looks || {})[S.project.variant]) S.project.variant = '';
  const dropped = Object.keys(S.project.slots).filter(k => !rig.slotByKey[k]);
  dropped.forEach(k => delete S.project.slots[k]);
  if (dropped.length) toast(`${id} has no ${dropped.join(', ')} slot, so that art was removed`);
  if (!rig.slotByKey[S.selected]) S.selected = null;
  renderBase();
  fitView();
  renderSlots();
  renderInspector();
}
async function openProject(project) {
  S.project = project;
  S.savedId = project.id || null;
  S.level = 1;
  S.selected = null;
  S.idTouched = !!project.id;
  $('nameInput').value = project.name || '';
  $('idInput').value = project.id || '';
  $('idInput').disabled = !!project.publishedAt || project.kind === 'existing';
  syncExistingMode();
  if (S.side !== 'game') S.side = (S.rigs.find(r => r.id === project.rig) || {}).side || 'pig';
  await loadRig(project.rig);
  if (!project.statsEdited && !project.id) project.stats = await api('/api/stats/' + encodeURIComponent(project.rig));
  resetHistory();
  renderLibrary(); renderProjectList(); renderAbilities(); renderTooltip(); renderStats();
  $('saveState').textContent = project.publishedAt ? `${project.kind === 'existing' ? 'Updated' : 'Uploaded'} ${project.publishedAt}` : '';
}

/* ---------------------------------------------------------------- the game's own combatants */

async function loadExisting() {
  S.existing = await api('/api/existing');
  S.existingById = Object.fromEntries(S.existing.pigs.map(x => [x.id, x]));
  return S.existing;
}
function existingDescendants(id) {
  const out = [];
  const walk = parent => S.existing.pigs.filter(x => x.parent === parent).sort((a, b) => a.id.localeCompare(b.id)).forEach(x => { out.push(x); walk(x.id); });
  walk(id);
  return out;
}
async function setTick(parent, variant, on) {
  try {
    const res = await api('/api/existing_tick', { parent, variant, on });
    S.existing.unticked = res.unticked;
  } catch (e) { toast(e.message); }
  // The same ticks show in the library and in the Stats tab.
  refreshCombatants();
  if (isExisting() && S.project.id === parent) renderStats();
}
async function openExisting(id) {
  clearTimeout(saveTimer);
  await saveNow();
  try {
    const { project, note } = await api('/api/existing_open', { id });
    await openProject(project);
    refreshCombatants();
    if (note) toast(note);
  } catch (e) { toast(e.message); }
}
async function duplicateExisting(id) {
  clearTimeout(saveTimer);
  await saveNow();
  try {
    const { project } = await api('/api/existing_duplicate', { id });
    S.projects = (await api('/api/bootstrap')).projects;
    await openProject(project);
    toast(`Duplicated as "${project.name}". It is a new combatant: upload it to add it to the game.`);
  } catch (e) { toast(e.message); }
}
function closeFileMenu() {
  $('filePanel').hidden = true;
  $('fileBtn').setAttribute('aria-expanded', 'false');
}
$('fileBtn').addEventListener('click', e => {
  e.stopPropagation();
  const open = $('filePanel').hidden;
  $('filePanel').hidden = !open;
  $('fileBtn').setAttribute('aria-expanded', String(open));
});
document.addEventListener('pointerdown', e => { if (!$('fileMenu').contains(e.target)) closeFileMenu(); });
document.addEventListener('keydown', e => { if (e.key === 'Escape' && !$('filePanel').hidden) { closeFileMenu(); $('fileBtn').focus(); } });
['existingBtn', 'importBtn', 'exportBtn', 'deleteBtn'].forEach(id => $(id).addEventListener('click', closeFileMenu));

// Delete: the project file goes to projects/.trash; an uploaded combatant can also be taken out of the game.
$('deleteBtn').addEventListener('click', () => {
  const project = S.project;
  if (!project || isExisting()) return;
  const saved = !!project.id && S.projects.some(x => x.id === project.id);
  const name = project.name || project.id || 'this combatant';
  if (!saved) {
    if (confirm(`Discard ${name}? It was never saved.`)) { S.deleting = true; clearTimeout(saveTimer); openProject(blankProject(project.rig)).finally(() => { S.deleting = false; }); }
    return;
  }
  const modal = $('modal');
  modal.className = '';
  const fromGame = h('input', { type: 'checkbox', checked: !!project.publishedAt });
  const go = h('button', { class: 'danger', onclick: () => deleteProject(project, fromGame.checked) }, 'Delete');
  modal.replaceChildren(
    h('h3', {}, `Delete ${name}?`),
    h('p', { class: 'dim' }, 'Its project file moves to the projects/.trash folder, so it can be put back by hand.'),
    project.publishedAt ? h('label', { class: 'check', style: 'margin:6px 0' }, fromGame,
      ` Also remove pig_forge_${project.id} from the game: the combatant, its inventory and items, its own abilities, its text and its art`) : null,
    project.publishedAt ? h('p', { class: 'dim note' }, 'If a battle or another combatant still uses it, nothing is removed from the game and the project stays.') : null,
    h('div', { class: 'row', style: 'justify-content:flex-end;margin-top:10px' }, h('button', { onclick: () => modal.close() }, 'Cancel'), go));
  modal.showModal();
  go.focus();
});
async function deleteProject(project, fromGame) {
  $('modal').close();
  clearTimeout(saveTimer);
  S.deleting = true;
  try {
    const res = await api('/api/delete', { id: project.id, fromGame });
    if (res.job) {
      const job = await followJob(res.job, `Delete ${project.name || project.id}`);
      if (job.error) { toast(job.error); return; }
      S.skills = await api('/api/skills');
    }
    S.projects = (await api('/api/bootstrap')).projects;
    S.savedId = null;
    await openProject(blankProject(project.rig));
    toast(`Deleted ${project.name || project.id}${fromGame ? ' and took it out of the game' : ''}`);
  } catch (e) {
    toast(e.message);
  } finally {
    S.deleting = false;
  }
}

// View existing swaps the left panel to the game's combatants; pressing it again, or Pigs or Birds, goes back.
$('existingBtn').addEventListener('click', () => setSide(S.side === 'game' ? S.spriteSide || 'pig' : 'game'));

// The Combatants tab of the library: the game's pigs as tiles, variants folded under their parent.
function renderCombatants() {
  $('catChips').replaceChildren(...[['all', 'All'], ['updated', 'Updated by the Forge']].map(([k, label]) =>
    h('button', { class: (S.gameFilter || 'all') === k ? 'on' : '', onclick: () => { S.gameFilter = k; renderLibrary(); } }, label)));
  if (!S.existing) {
    $('grid').replaceChildren(h('p', { class: 'dim' }, 'Loading…'));
    loadExisting().then(() => { if (S.side === 'game') renderLibrary(); }).catch(e => toast(e.message));
    return;
  }
  const rigs = [...new Set(S.existing.pigs.map(x => x.rig))].sort();
  if (S.gameRig && !rigs.includes(S.gameRig)) S.gameRig = '';
  $('rigFilter').replaceChildren(h('option', { value: '' }, 'All characters'), ...rigs.map(r => h('option', { value: r, selected: r === S.gameRig }, r)));
  const q = (S.gameSearch || '').trim().toLowerCase();
  const filtering = !!(q || S.gameRig || S.gameFilter === 'updated');
  const hit = x => (!q || x.id.includes(q) || x.name.toLowerCase().includes(q)) && (!S.gameRig || x.rig === S.gameRig) && (S.gameFilter !== 'updated' || x.updatedAt);
  const open = S.existingOpen || (S.existingOpen = new Set());
  const roots = S.existing.pigs.filter(x => !x.parent).sort((a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
  const items = [];
  let shown = 0;
  for (const r of roots) {
    const kids = existingDescendants(r.id);
    const kidHits = kids.filter(hit);
    if (!hit(r) && !kidHits.length) continue;
    shown++;
    items.push({ x: r, root: r, kids, variant: false });
    // A search opens the groups with a matching variant.
    for (const k of open.has(r.id) ? kids : filtering ? kidHits : []) items.push({ x: k, root: r, kids: [], variant: true });
  }
  gridItems = items;
  gridShown = 0;
  $('grid').replaceChildren();
  $('libCount').textContent = `${shown} combatant${shown === 1 ? '' : 's'}`;
  appendGrid();
}
function refreshCombatants() {
  if (S.side !== 'game') return;
  const grid = $('grid');
  const keep = grid.scrollTop;
  renderLibrary();
  while (gridShown < gridItems.length && grid.scrollHeight < keep + grid.clientHeight + 200) appendGrid();
  grid.scrollTop = keep;
}
// Variants mostly share their parent's name: the tile shows what sets it apart, such as bird3 or boss.
function variantLabel(x, root) {
  const suffix = x.id.startsWith(root.id + '_') ? x.id.slice(root.id.length + 1) : x.id;
  return x.name === root.name ? suffix : `${x.name} · ${suffix}`;
}
function combatantCard({ x, root, kids, variant }) {
  const why = x.supported ? '' : `The Forge can't draw the ${x.rig} base yet, so this one can't be opened.`;
  const current = isExisting() && S.project.id === x.id;
  const open = S.existingOpen.has(x.id);
  const el = h('div', { class: 'card combatant' + (variant ? ' variant' : '') + (x.supported ? '' : ' off') + (current ? ' current' : ''),
    title: `${x.name}\n${x.id} · ${x.rig}\n${x.hp} HP · ${x.attack} ATK${x.updatedAt ? '\nUpdated ' + x.updatedAt : ''}\n${why || 'Click to view and edit it.'}`,
    onclick: () => { if (x.supported) openExisting(x.id); } },
    x.supported ? h('img', { class: 'cIcon', src: `/pigicon/${encodeURIComponent(x.id)}`, alt: '', loading: 'lazy', draggable: false }) : h('div', { class: 'cNoIcon' }, x.rig),
    h('span', { class: 'cName' }, variant ? variantLabel(x, root) : x.name));
  if (x.updatedAt) el.append(h('span', { class: 'cEdited' }, 'Edited'));
  if (kids.length) {
    el.append(h('button', { class: 'cKids', title: open ? 'Hide its variants' : `Show its ${kids.length} variant${kids.length === 1 ? '' : 's'}`,
      onclick: ev => { ev.stopPropagation(); if (open) S.existingOpen.delete(x.id); else S.existingOpen.add(x.id); refreshCombatants(); } }, open ? '−' : `+${kids.length}`));
  }
  if (variant) {
    const off = new Set(S.existing.unticked[root.id] || []);
    el.append(h('input', { type: 'checkbox', class: 'cTick', title: `Ticked: gets the edits made to ${root.name} (${root.id})`, checked: !off.has(x.id),
      onclick: ev => ev.stopPropagation(), onchange: ev => setTick(root.id, x.id, ev.target.checked) }));
  }
  if (x.supported) {
    el.append(h('button', { class: 'cDup', title: 'Duplicate: a new combatant that starts as a copy of this one', onclick: ev => { ev.stopPropagation(); duplicateExisting(x.id); } }, '⧉'));
  }
  return el;
}

/* ---------------------------------------------------------------- upload and Claude jobs */

// Jobs keep running while minimized; the top bar shows a chip for each one.
const running = new Map();
function idLocked() {
  return running.size > 0 || !!S.generating;
}
function syncIdLock() {
  $('idInput').disabled = idLocked() || !!(S.project && S.project.publishedAt) || isExisting();
}
function syncExistingMode() {
  const ex = isExisting();
  $('uploadBtn').textContent = ex ? 'Update in game' : 'Upload to game';
  $('uploadBtn').title = ex ? 'Write the edits into this game combatant, and into its ticked variants (Stats tab)' : '';
  $('exportBtn').disabled = ex;
  $('deleteBtn').disabled = ex;
  $('deleteBtn').title = ex ? 'The game\'s own combatants can\'t be deleted here: battles use them' : 'Delete this combatant';
  $('exportBtn').title = ex ? 'Duplicate it from View existing to export a copy' : 'Save this combatant and its art as a .forge file';
}
function renderJobChips() {
  syncIdLock();
  $('jobChips').replaceChildren(...[...running.entries()].filter(([id]) => id !== S.viewJob).map(([id, j]) => h('button', { class: 'jobChip', title: 'Show progress', onclick: () => showJob(id) }, `${j.title}…`)));
}
function renderJobView() {
  const j = running.get(S.viewJob);
  if (!j) return;
  const job = j.job || { log: [] };
  $('logTitle').textContent = job.done ? `${j.title}: ${job.error ? (job.stopped ? 'stopped' : 'failed') : 'done'}` : j.title;
  const body = $('logBody');
  const atEnd = body.scrollTop + body.clientHeight >= body.scrollHeight - 30;
  body.textContent = job.log.join('\n');
  if (atEnd) body.scrollTop = body.scrollHeight;
  $('logStop').hidden = !(job.stoppable && !job.done);
  $('logStop').disabled = !!job.stopped;
  $('logClose').textContent = job.done ? 'Close' : 'Minimize';
}
function showJob(id) {
  S.viewJob = id;
  renderJobView();
  if (!$('logDialog').open) $('logDialog').showModal();
  renderJobChips();
}
function followJob(jobId, title, show = true) {
  running.set(jobId, { title, job: null });
  if (show) showJob(jobId); else renderJobChips();
  return new Promise(resolve => {
    const poll = async () => {
      let job;
      try { job = await api('/api/job/' + jobId); }
      catch (e) { setTimeout(poll, 1500); return; }
      const entry = running.get(jobId);
      entry.job = job;
      if (S.viewJob === jobId) renderJobView();
      const last = job.log[job.log.length - 1] || '';
      if (!job.done && /^Unity did not encode/.test(last) && entry.asked !== last) {
        // Asks once per request, so Minimize stays minimized.
        entry.asked = last;
        showJob(jobId);
      }
      if (!job.done) { setTimeout(poll, 700); return; }
      const needsLook = job.error && !job.stopped || job.log.some(l => l.startsWith('WARNING'));
      if (S.viewJob !== jobId) {
        if (needsLook) showJob(jobId);
        else toast(`${title}: ${job.stopped ? 'stopped' : 'done'}`);
      }
      if (S.viewJob !== jobId) running.delete(jobId);
      renderJobChips();
      resolve(job);
    };
    poll();
  });
}
async function applyJobResult(job) {
  S.skills = await api('/api/skills');
  if (S.existing) loadExisting().then(() => { refreshCombatants(); if (isExisting()) renderStats(); }).catch(() => {});
  S.projects = (await api('/api/bootstrap')).projects;
  renderProjectList();
  const result = job.project;
  if (!result || result.id !== S.project.id) return;
  // Only what the job decided: you may have kept editing while it ran.
  for (const f of SERVER_FIELDS) if (f in result) S.project[f] = result[f];
  const fields = job.stoppable ? [...SERVER_ABILITY_FIELDS, 'name', 'desc'] : SERVER_ABILITY_FIELDS;
  const byUid = Object.fromEntries(S.project.abilities.map(a => [a.uid, a]));
  for (const a of result.abilities) {
    const mine = byUid[a.uid];
    if (mine) for (const f of fields) { if (f in a) mine[f] = a[f]; }
  }
  S.snapshot = JSON.stringify(S.project);
  syncIdLock();
  renderAbilities(); renderTooltip(); renderStats(); renderSlots(); renderInspector();
  $('saveState').textContent = S.project.publishedAt ? `${isExisting() ? 'Updated' : 'Uploaded'} ${S.project.publishedAt}` : 'Saved';
}
$('uploadBtn').addEventListener('click', async () => {
  clearTimeout(saveTimer);
  const check = await api('/api/validate', S.project);
  if (check.problems.length) { toast(check.problems.join('. ')); return; }
  $('uploadBtn').disabled = true;
  try {
    const { job } = await api('/api/publish', S.project);
    await applyJobResult(await followJob(job, isExisting() ? 'Update in game' : 'Upload to game'));
  } catch (e) { toast(e.message); }
  $('uploadBtn').disabled = false;
});
// One Claude job at a time: each sees the abilities the previous one named and edits the same skill files.
async function runAbilityJob(ability, title, show) {
  await saveNow();
  const { job } = await api('/api/ability', { uid: ability.uid, project: S.project });
  const result = await followJob(job, title, show);
  await applyJobResult(result);
  // Done without trouble: close the window instead of leaving the log up.
  const clean = !result.error && !result.log.some(l => l.startsWith('WARNING'));
  if (clean && S.viewJob === job && $('logDialog').open) {
    $('logDialog').close();
    result.autoClosed = true;
    toast(`${ability.name || 'Ability'}: written`);
  }
  return result;
}
async function createAbility(ability) {
  clearTimeout(saveTimer);
  if (!S.project.id) { toast('Give the combatant a name first'); return; }
  if (!confirm(`Run Claude Code on this project now to build "${ability.name}"? It can edit the skill data and scripts, and uses your Claude account.`)) return;
  try { await runAbilityJob(ability, `Claude: ${ability.name}`, true); }
  catch (e) { toast(e.message); }
}
function abilitiesToGenerate() {
  return S.project.abilities.filter(a => !a.written && (a.design || '').trim());
}
function syncGenerateAll() {
  const count = abilitiesToGenerate().length;
  const btn = $('generateAll');
  btn.textContent = S.generating ? 'Generating…' : count ? `Generate all (${count})` : 'Generate all';
  btn.disabled = !S.claude || !!S.generating || !count;
}
async function generateAll() {
  clearTimeout(saveTimer);
  if (!S.project.id) { toast('Give the combatant a name first'); return; }
  const uids = abilitiesToGenerate().map(a => a.uid);
  if (!uids.length) return;
  if (!confirm(`Run Claude Code on this project now to build ${uids.length} abilit${uids.length > 1 ? 'ies' : 'y'}, one at a time? It can edit the skill data and scripts, and uses your Claude account.`)) return;
  const projectId = S.project.id;
  S.generating = true;
  syncGenerateAll();
  syncIdLock();
  let show = true;
  try {
    for (let i = 0; i < uids.length; i++) {
      if (S.project.id !== projectId) { toast('Stopped generating: another combatant was opened'); break; }
      const ability = S.project.abilities.find(a => a.uid === uids[i]);
      if (!ability) continue; // removed while waiting
      const job = await runAbilityJob(ability, `Claude ${i + 1} of ${uids.length}: ${ability.name}`, show);
      // Minimized stays minimized for the next ability.
      show = !!S.viewJob || !!job.autoClosed;
      if (job.error) {
        const rest = uids.length - i - 1;
        if (rest) toast(`${job.stopped ? 'Stopped' : 'Failed'} on ${ability.name}. ${rest} not started.`);
        break;
      }
    }
  } catch (e) { toast(e.message); }
  S.generating = false;
  syncGenerateAll();
  syncIdLock();
}
$('generateAll').addEventListener('click', generateAll);
$('logClose').addEventListener('click', () => $('logDialog').close());
$('logDialog').addEventListener('close', () => {
  const j = running.get(S.viewJob);
  if (j && j.job && j.job.done) running.delete(S.viewJob);
  S.viewJob = null;
  renderJobChips();
});
$('logStop').addEventListener('click', async () => {
  $('logStop').disabled = true;
  await api('/api/job_stop', { job: S.viewJob });
});

/* ---------------------------------------------------------------- uploading art */

async function uploadFiles(files) {
  const pngs = [...files].filter(f => /\.png$/i.test(f.name));
  if (!pngs.length) { toast('Only PNG files can be added'); return; }
  const read = f => new Promise((res, rej) => { const r = new FileReader(); r.onload = () => res({ name: f.name, data: r.result }); r.onerror = rej; r.readAsDataURL(f); });
  try {
    const result = await api('/api/upload', { side: $('uploadSide').value, cat: $('uploadCat').value, files: await Promise.all(pngs.map(read)) });
    S.lib = result.library;
    S.side = $('uploadSide').value;
    S.cat = $('uploadCat').value;
    S.search = '';
    $('searchInput').value = '';
    S.rigFilter = 'Uploaded';
    renderLibrary();
    toast(`Added ${result.saved.length} file${result.saved.length > 1 ? 's' : ''} to Assets/${result.saved[0].split('/').slice(0, -1).join('/')}`);
  } catch (e) { toast(e.message); }
}
const zone = $('dropZone');
zone.addEventListener('click', e => { if (!['SELECT', 'OPTION'].includes(e.target.tagName)) $('fileInput').click(); });
zone.addEventListener('keydown', e => { if (e.key === 'Enter' && e.target === zone) $('fileInput').click(); });
$('fileInput').addEventListener('change', e => { uploadFiles(e.target.files); e.target.value = ''; });
for (const el of [zone, $('library')]) {
  el.addEventListener('dragover', e => { if (e.dataTransfer.types.includes('Files')) { e.preventDefault(); zone.classList.add('over'); } });
  el.addEventListener('dragleave', () => zone.classList.remove('over'));
  el.addEventListener('drop', e => { e.preventDefault(); zone.classList.remove('over'); uploadFiles(e.dataTransfer.files); });
}

/* ---------------------------------------------------------------- wiring */

function setTab(tab) {
  document.querySelectorAll('#sideNav button').forEach(b => b.classList.toggle('on', b.dataset.tab === tab));
  for (const t of ['slots', 'abilities', 'stats']) $('tab-' + t).hidden = t !== tab;
  if (tab === 'abilities') { renderTooltip(); if (!targetOnScreen()) fitView(); }
}
document.querySelectorAll('#sideNav button').forEach(b => b.addEventListener('click', () => setTab(b.dataset.tab)));
document.querySelectorAll('#sideTabs button').forEach(b => b.addEventListener('click', () => setSide(b.dataset.side)));
$('grid').addEventListener('scroll', e => { const g = e.target; if (g.scrollTop + g.clientHeight > g.scrollHeight - 200 && gridShown < gridItems.length) appendGrid(); });
new ResizeObserver(() => { const g = $('grid'); g.style.setProperty('--tile', parseFloat(getComputedStyle(g).gridTemplateColumns) + 'px'); }).observe($('grid'));
$('searchInput').addEventListener('input', e => { if (S.side === 'game') S.gameSearch = e.target.value; else S.search = e.target.value; renderLibrary(); });
$('rigFilter').addEventListener('change', e => { if (S.side === 'game') S.gameRig = e.target.value; else S.rigFilter = e.target.value; renderLibrary(); });
$('splitAll').addEventListener('change', e => { S.splitAll = e.target.checked; renderLibrary(); });
async function setBase(rigId, variant) {
  const sameRig = rigId === S.project.rig;
  S.project.rig = rigId;
  S.project.variant = variant;
  if (!sameRig) {
    if (await loadRig(rigId) === false) return;
    if (!S.project.statsEdited) S.project.stats = await api('/api/stats/' + encodeURIComponent(rigId));
    if (S.side !== 'game') S.side = S.rig.side;
    renderLibrary(); renderStats(); renderTooltip(); renderAbilities();
  }
  renderBase();
  touch();
}
function variantName(variant) {
  return variant ? ' · ' + variant.charAt(0).toUpperCase() + variant.slice(1) : '';
}
function rigIconUrl(id, variant) {
  return `/rigicon/${encodeURIComponent(id)}${variant ? '/' + variant : ''}`;
}
function renderBase() {
  if (!S.project) return;
  $('baseIcon').src = rigIconUrl(S.project.rig, S.project.variant);
  $('baseName').textContent = S.project.rig + variantName(S.project.variant);
}
$('baseBtn').addEventListener('click', () => {
  const modal = $('modal');
  modal.className = '';
  const tile = (rig, variant) => h('button', { class: 'baseTile' + (rig.id === S.project.rig && variant === (S.project.variant || '') ? ' on' : ''), title: rig.id + variantName(variant), onclick: () => { modal.close(); setBase(rig.id, variant); } },
    h('img', { src: rigIconUrl(rig.id, variant), alt: '', loading: 'lazy' }), h('span', {}, rig.id + variantName(variant)));
  const group = (side, label) => [h('div', { class: 'baseGroup' }, label), h('div', { class: 'baseGrid' }, S.rigs.filter(r => r.side === side).flatMap(r => [tile(r, ''), ...(r.variants || []).map(v => tile(r, v))]))];
  modal.replaceChildren(h('div', { class: 'row between' }, h('h3', {}, 'Choose a base'), h('button', { onclick: () => modal.close() }, 'Cancel')),
    h('div', { id: 'baseList' }, group('bird', 'Birds'), group('pig', 'Pigs')));
  modal.showModal();
});
$('exportBtn').addEventListener('click', async () => {
  if (!S.project.id) { toast('Give the combatant a name first'); return; }
  clearTimeout(saveTimer);
  await saveNow();
  window.location.href = '/api/export/' + S.project.id;
});
$('importBtn').addEventListener('click', () => $('importInput').click());
$('importInput').addEventListener('change', async e => {
  const file = e.target.files[0];
  e.target.value = '';
  if (!file) return;
  const data = await new Promise((res, rej) => { const r = new FileReader(); r.onload = () => res(r.result); r.onerror = rej; r.readAsDataURL(file); });
  try {
    clearTimeout(saveTimer);
    await saveNow();
    const result = await api('/api/import', { data });
    S.lib = result.library;
    S.projects = (await api('/api/bootstrap')).projects;
    await openProject(result.project);
    toast(result.notes.length ? result.notes.join('. ') : `Imported ${result.project.name || result.project.id}`);
  } catch (err) { toast(err.message); }
});
function setupResizer(id, cssVar, fromLeft) {
  const el = $(id);
  const key = 'forge.' + cssVar;
  try { const saved = localStorage.getItem(key); if (saved) document.body.style.setProperty(cssVar, saved); } catch (e) { /* storage is optional */ }
  el.addEventListener('pointerdown', e => {
    el.setPointerCapture(e.pointerId);
    el.classList.add('on');
    const main = document.querySelector('main').getBoundingClientRect();
    const move = ev => {
      const width = fromLeft ? ev.clientX - main.left : main.right - ev.clientX;
      const value = Math.round(Math.max(240, Math.min(main.width - 620, width))) + 'px';
      document.body.style.setProperty(cssVar, value);
      try { localStorage.setItem(key, value); } catch (err) { /* storage is optional */ }
      if (S.project) renderTooltip();
    };
    const up = () => { el.classList.remove('on'); el.removeEventListener('pointermove', move); el.removeEventListener('pointerup', up); };
    el.addEventListener('pointermove', move);
    el.addEventListener('pointerup', up);
  });
}
setupResizer('resizeLeft', '--library-width', true);
setupResizer('resizeRight', '--side-width', false);
$('clipSelect').addEventListener('change', e => { S.clip = +e.target.value; S.time = 0; S.playing = true; syncPlay(); });
$('playBtn').addEventListener('click', () => {
  const clip = S.rig.clips[S.clip];
  if (!S.playing && S.time >= clip.length) S.time = 0;
  S.playing = !S.playing; syncPlay();
});
$('scrub').addEventListener('input', e => { S.playing = false; syncPlay(); S.time = e.target.value / 1000 * S.rig.clips[S.clip].length; });
$('speedSelect').addEventListener('change', e => { S.speed = +e.target.value; });
$('loopCheck').addEventListener('change', e => { S.loop = e.target.checked; });
$('fitBtn').addEventListener('click', fitView);
$('nameInput').addEventListener('input', e => { S.project.name = e.target.value; nameChanged(); renderTooltip(); });
$('idInput').addEventListener('input', e => { S.idTouched = true; e.target.value = slugify(e.target.value) + (e.target.value.endsWith('_') ? '_' : ''); S.project.id = e.target.value.replace(/_+$/, ''); renderStats(); touch(); });
// Switching saves the current combatant first, if it can be saved (it has an id).
async function leaveProject() {
  clearTimeout(saveTimer);
  await saveNow();
}
$('newBtn').addEventListener('click', async () => {
  closeFileMenu();
  await leaveProject();
  openProject(blankProject(S.project ? S.project.rig : 'MinionPig'));
});
$('projectSelect').addEventListener('change', async e => {
  const id = e.target.value; // saving redraws this list, so read the choice first
  if (!id) return;
  closeFileMenu();
  await leaveProject();
  openProject(await api('/api/project/' + id));
});
window.addEventListener('resize', () => { renderTooltip(); fitView(); });
window.addEventListener('keydown', e => {
  const t = e.target;
  if ($('modal').open || $('logDialog').open) return;
  const typing = t.matches && t.matches('textarea, input:not([type=checkbox]):not([type=range])');
  if ((e.ctrlKey || e.metaKey) && !typing && e.key.toLowerCase() === 'z') {
    e.preventDefault();
    if (e.shiftKey) stepHistory(S.redo, S.undo, 'redo'); else stepHistory(S.undo, S.redo, 'undo');
    return;
  }
  if ((e.ctrlKey || e.metaKey) && !typing && e.key.toLowerCase() === 'y') {
    e.preventDefault();
    stepHistory(S.redo, S.undo, 'redo');
    return;
  }
  if (t.matches && t.matches('input, textarea, select')) return;
  if (e.code === 'Space') { e.preventDefault(); $('playBtn').click(); }
  const s = activeLayer(S.selected);
  if (!s) return;
  if (e.key === 'Delete' || e.key === 'Backspace') { removeLayer(S.selected, s); return; }
  const step = e.shiftKey ? 10 : 1;
  const d = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, step], ArrowDown: [0, -step] }[e.key];
  if (d) {
    e.preventDefault();
    eachLinked(s, x => { x.tx += d[0]; x.ty += d[1]; });
    refreshInspector(); touch();
  }
});

async function boot() {
  const data = await api('/api/bootstrap');
  S.rigs = data.rigs; S.colors = data.colors; S.projects = data.projects; S.claude = data.claude;
  const pill = $('unityState');
  pill.textContent = data.unity ? 'Unity open' : 'Unity closed: upload encodes in batch mode';
  pill.classList.toggle('on', data.unity);
  [S.lib, S.skills, S.icons, S.powerLevels, S.projectiles] = await Promise.all([api('/api/library'), api('/api/skills'), api('/api/icons'), api('/api/powerlevels'), api('/api/projectiles')]);
  await openProject(blankProject('MinionPig'));
  requestAnimationFrame(tick);
}
window.forge = { S, assign, openProject, renderTooltip, setTab, setLevel, stepHistory, activeLayer, renderAbilities, generateAll, openExisting, duplicateExisting, loadExisting, setTick };
boot().catch(e => { document.body.prepend(h('pre', { style: 'color:#ef6b6b;padding:12px' }, 'Could not start: ' + e.message)); });
