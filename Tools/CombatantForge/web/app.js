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
/* A slot's rotation pivot (S.project.bonePivots[slot] = [x, y] in the slot's art space): its bone turns around that
   point instead of its own origin. Each frame the bone moves by (rest rotation·scale - current rotation·scale)·pivot,
   so the pivot stays where it is in the rest pose while the bone still follows its parent and animated position
   (the game does the same: ForgeBonePivots.cs). Returns {pose index of the bone: pivot in bone space}. */
function restLocal(rig, i) {
  const r = (rig.clips[rig.restClip].tracks[i]) || {};
  const p = r.p ? sampleVec(r.p, 0) : [0, 0], a = (r.r !== undefined ? sampleAngle(r.r, 0) : 0) * Math.PI / 180, s = r.s ? sampleVec(r.s, 0) : [1, 1];
  const c = Math.cos(a), sn = Math.sin(a);
  return [c * s[0], sn * s[0], -sn * s[1], c * s[1], p[0], p[1]];
}
function bonePivotMap(rig) {
  const pivots = S.project && S.project.bonePivots;
  if (!pivots || rig.kind === 'projectile') return null;
  const out = {};
  for (const b of rig.bones) {
    const pv = b.slot && pivots[b.slot];
    if (!pv || b.bone == null || b.bone < 0) continue;
    // art space (the sprite node) to the bone's space, through the sprite node's rest placement
    const Ls = b.sprite != null && rig.parents[b.sprite] === b.bone ? restLocal(rig, b.sprite) : [1, 0, 0, 1, 0, 0];
    out[b.bone] = apply(Ls, pv[0], pv[1]);
  }
  return Object.keys(out).length ? out : null;
}
function pose(clip, t) {
  const rig = S.rig;
  const out = new Array(rig.paths.length);
  const rest = rig.clips[rig.restClip];
  const pivots = bonePivotMap(rig);
  for (let i = 0; i < rig.paths.length; i++) {
    const track = clip.tracks[i] || {};
    const r = rest.tracks[i] || {};
    const p = track.p ? sampleVec(track.p, t) : r.p ? sampleVec(r.p, 0) : [0, 0];
    const a = (track.r !== undefined ? sampleAngle(track.r, t) : r.r !== undefined ? sampleAngle(r.r, 0) : 0) * Math.PI / 180;
    const s = track.s ? sampleVec(track.s, t) : r.s ? sampleVec(r.s, 0) : [1, 1];
    const c = Math.cos(a), sn = Math.sin(a);
    const local = [c * s[0], sn * s[0], -sn * s[1], c * s[1], p[0], p[1]];
    if (pivots && pivots[i]) {
      const L0 = restLocal(rig, i), [px, py] = pivots[i];
      local[4] += (L0[0] - local[0]) * px + (L0[2] - local[2]) * py;
      local[5] += (L0[1] - local[1]) * px + (L0[3] - local[3]) * py;
    }
    out[i] = rig.parents[i] >= 0 ? mul(out[rig.parents[i]], local) : local;
  }
  const look = currentLook();
  if (look) out.spring = applyLook(out, clip, look);
  return out;
}

/* ---------------------------------------------------------------- looks: the spring drone (game: SpringDrone.cs) */

// The drone's spring is a feature: a Spring row in the Slots list and Spring tiles in the library. It's kept as the
// project's look (variant 'spring' / 'springsteel'), which is what publishing and the game read.
const SPRING_SLOT = 'Spring';
function rigLooks() {
  return (S.rig && S.rig.looks) || {};
}
function hasLooks() {
  return Object.keys(rigLooks()).length > 0;
}
function lookLabel(name) {
  return name === 'springsteel' ? 'Steel spring' : name === 'spring' ? 'Spring' : name;
}
function lookSpriteKey(name) {
  return `look:${S.rig.id}:${name}`;
}
function setLook(name) {
  S.project.variant = name;
  S.spring = null;
  selectSlot(SPRING_SLOT);
  renderBase();
  touch();
}
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
      // As SpringDrone.StanceBottom: the actuator extends, or gives way to soak up a landing.
      const needed = Math.sqrt(2 * Math.max(0, target) / k), current = look.RestLength + st.ext - st.h;
      st.ext = Math.min(look.MaxExtension, Math.max(-0.5 * look.MaxExtension, st.ext + (needed - current)));
    } else if (!contact && st.h > look.RestLength + Math.max(0, st.ext)) st.ext = 0;
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
    // The top is tucked up under the pod's flat bottom (TuckDepth); the spring draws behind the pod.
    const tuck = look.TuckDepth || 0;
    const along = -tuck + (ring + tuck) * Math.min(1, t / pitched) + Math.sin(a) * r * 0.22, c = Math.cos(a) * r;
    pts.push({ p: [spring.top[0] + ax * along + sx * c, spring.top[1] + ay * along + sy * c], d: Math.sin(a) });
  }
  ctx.setTransform(...base);
  const quad = (corners, color) => {
    ctx.fillStyle = `rgb(${color[0]},${color[1]},${color[2]})`;
    ctx.beginPath();
    corners.forEach((c, i) => { const q = apply(V, c[0], c[1]); if (i) ctx.lineTo(...q); else ctx.moveTo(...q); });
    ctx.closePath(); ctx.fill();
  };
  // One continuous ribbon per run of wire, mitred at the joints and capped at the ends (SpringDrone.AddStrip).
  const up = (a, b) => {
    const dx = b[0] - a[0], dy = b[1] - a[1], n = Math.hypot(dx, dy) || 1e-6;
    const ux = -dy / n, uy = dx / n;
    return { n: uy >= 0 ? [ux, uy] : [-ux, -uy], d: [dx / n, dy / n] };
  };
  const strip = (run, [w, o, color]) => {
    const P = run.map(i => pts[i].p), segs = P.slice(0, -1).map((p, j) => up(p, P[j + 1]));
    if (!segs.length) return;
    const width = wire * w;
    const rows = P.map((p, j) => {
      const adj = [segs[j - 1], segs[j]].filter(Boolean).map(sg => sg.n);
      let nx = adj.reduce((a, v) => a + v[0], 0), ny = adj.reduce((a, v) => a + v[1], 0);
      const nl = Math.hypot(nx, ny) || 1e-6;
      nx /= nl; ny /= nl;
      const miter = 1 / Math.max(0.5, nx * adj[0][0] + ny * adj[0][1]);
      let [x, y] = p;
      if (j === 0) { x -= segs[0].d[0] * width / 2; y -= segs[0].d[1] * width / 2; }
      else if (j === P.length - 1) { x += segs[j - 1].d[0] * width / 2; y += segs[j - 1].d[1] * width / 2; }
      const cx = x + nx * wire * o, cy = y + ny * wire * o, h = width / 2 * miter;
      return [[cx - nx * h, cy - ny * h], [cx + nx * h, cy + ny * h]];
    });
    // Filled as one outline (down one edge, back up the other) so the canvas leaves no seams between pieces.
    quad([...rows.map(r => r[0]), ...rows.map(r => r[1]).reverse()], color);
  };
  for (const [front, name] of [[false, 'BackWire'], [true, 'FrontWire']]) {
    const runs = [];
    let cur = null;
    for (let i = 0; i < count; i++) {
      if (((pts[i].d + pts[i + 1].d) / 2 >= 0) === front) { cur = cur || [i]; cur.push(i + 1); }
      else if (cur) { runs.push(cur); cur = null; }
    }
    if (cur) runs.push(cur);
    // Band by band over the whole half, so inner bands cover the neighbouring run's outline where runs meet.
    for (const band of look.bands[name]) runs.forEach(run => strip(run, band));
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
  if (hasLooks()) {
    // Drop target on the canvas: the spring, or under the pod when it has none.
    const body = rig.paths.indexOf('Root/Body');
    const at = S.springDraw ? [S.springDraw.top[0], S.springDraw.top[1] - S.springDraw.length / 2] : body >= 0 ? apply(poses[body], 0, -60) : [0, 20];
    anchors.set(SPRING_SLOT, { key: SPRING_SLOT, kind: 'feature', world: at, shown: true });
  }
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
function draw(now = vnow()) {
  const dpr = resizeCanvas();
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  if (!S.rig) return;
  const V = viewMatrix();
  // A camera shake jiggles the whole view for a moment.
  const shake = S.shake && now >= S.shake.t0 && now < S.shake.until ? S.shake.amp * (1 - (now - S.shake.t0) / (S.shake.until - S.shake.t0)) : 0;
  const base = [dpr, 0, 0, dpr, shake ? (Math.random() * 2 - 1) * shake * dpr : 0, shake ? (Math.random() * 2 - 1) * shake * dpr : 0];
  // ground line
  ctx.setTransform(...base);
  ctx.strokeStyle = '#ffffff14';
  ctx.lineWidth = 1;
  const groundY = apply(V, 0, 0)[1];
  ctx.beginPath(); ctx.moveTo(0, groundY); ctx.lineTo(canvas.width / dpr, groundY); ctx.stroke();

  const list = collectDraw();
  S.drawn = list;
  const ghostsBehind = updateGhosts(list);
  if (ghostsBehind) drawGhosts(base, V, true);
  const paint = d => {
    const local = mul(mul(V, d.M), d.local);
    d.screen = local;
    if (d.image.complete && d.image.naturalWidth) {
      ctx.setTransform(...mul(base, mul(local, [1, 0, 0, -1, 0, 0])));
      ctx.drawImage(d.image, -d.w / 2, -d.h / 2, d.w, d.h);
    }
  };
  if (S.springDraw) {
    // As in the game (SpringDrone.PlaceShadow): the shadow on the ground, the spring over it, then the rest of the rig.
    list.filter(d => d.slot === 'Shadow').forEach(paint);
    drawSpring(V, base, S.springDraw, currentLook());
    list.filter(d => d.slot !== 'Shadow').forEach(paint);
  } else list.forEach(paint);
  drawGhosts(base, V, false);
  drawFlights(now, base);
  ctx.setTransform(...base);
  drawSkillFx(now, base);
  ctx.setTransform(...base);
  if (showsTarget()) {
    if ($('showPaths').checked) drawPaths();
    targets().forEach(drawTarget);
  }
  screenOverlays(now, dpr);
  ctx.setTransform(...base);
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
// The selected slot's rotation pivot: an orange cross (a grey dot at the bone's own origin when it has none).
function slotBone(key) {
  return S.rig && S.rig.bones.find(b => b.slot === key && b.bone != null && b.bone >= 0);
}
function pivotScreen(key) {
  const b = slotBone(key);
  if (!b) return null;
  const poses = pose(S.rig.clips[S.clip], S.time);
  const pv = bonePivotMap(S.rig);
  const pb = pv && pv[b.bone] || [0, 0];
  return apply(viewMatrix(), ...apply(poses[b.bone], pb[0], pb[1]));
}
function drawPivot() {
  if (!S.project || S.mode === 'projectile' || !S.selected || !slotBone(S.selected)) return;
  const has = !!(S.project.bonePivots || {})[S.selected];
  if (!has && !S.pivotEdit) return;
  const at = pivotScreen(S.selected);
  if (!at) return;
  ctx.save();
  ctx.strokeStyle = has ? '#ff8a3d' : '#9aa0a6';
  ctx.lineWidth = 2;
  ctx.beginPath(); ctx.arc(at[0], at[1], 6, 0, 7); ctx.moveTo(at[0] - 11, at[1]); ctx.lineTo(at[0] + 11, at[1]); ctx.moveTo(at[0], at[1] - 11); ctx.lineTo(at[0], at[1] + 11); ctx.stroke();
  ctx.restore();
}
function drawGizmo() {
  drawPivot();
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
const showsTarget = () => S.mode === 'projectile' ? !!S.rig && !$('tab-pphysics').hidden
  : !!S.rig && !!S.project && !$('tab-abilities').hidden && S.project.abilities.some(a => shotFor(a));
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
  const p = paramsOf(a), t = a.template || '';
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
    // one made in the Projectiles section flies with its own physics, trails and effects (projectiles.js simulateWith)
    if (name.startsWith('ForgeProjectile_')) api('/api/projectile_project/' + encodeURIComponent(name.slice('ForgeProjectile_'.length))).then(p => { entry.forge = p; }).catch(() => {});
    entry.fx = loadFx(name);
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
  // The skill's own effects play with the shot (start, movement, impact on the targets).
  S.fxPlan = (a.type === 'Attack' || a.type === 'Support') && S.rig ? { clip: S.clip, uid: a.uid, asset: abilityAsset(a), done: new Set(), last: 0,
    impactOnHit: !!S.shotPlan && impactOnHit(a), impactAt: null } : null;
  if (S.fxPlan) prefetchFx(a);
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
  if (plan.name.startsWith('ForgeProjectile_')) {
    // SkillBattleDataBase.SpawnProjectile hands the targets to ForgeProjectiles: it fires at each (How many > 1), the one,
    // or the centre of several
    const aimed = model.shots.filter(to => to !== 'center').map(to => shotEnd(to, list));
    const ends = aimed.length ? aimed : list.map(t => [t.x, t.y]);
    S.flights.push({ entry, forgeShot: true, start, list: ends, centre: ends.length > 1 ? shotEnd('center', list) : null, t0: now, speed: S.speed,
      seed: Math.floor(Math.random() * 100000), mirror: rigMirrored(), onEvent: (kind, x, y) => fxEvent(plan.uid, kind, [x, y]) });
  } else
  model.shots.forEach((to, k) => S.flights.push({ entry, start, end: shotEnd(to, list), t0: now + k * 120, speed: S.speed, toCentre: to === 'center',
    onEvent: (kind, x, y) => fxEvent(plan.uid, kind, [x, y]) }));
  // when the first shot lands: the Impact event, if the ability counts the projectile's hit as one
  if (S.fxPlan && S.fxPlan.impactOnHit && S.fxPlan.uid === plan.uid) {
    const shot = S.flights.find(f => f.entry === entry && f.t0 >= now - 1);
    let secs = (entry.info && entry.info.duration) || 0.2;
    if (shot && shot.forgeShot && entry.forge && window.forgeProjectiles) secs = forgeProjectiles.firstHit(entry.forge, shot);
    S.fxPlan.impactAt = now + secs * 1000 / S.speed;
  }
  // Ricochets: the attack waits out its clip, the target is hit, then a copy of the projectile hops from enemy to
  // enemy, each hop leaving when the one before arrives (at most 3 s each), without the shot's fired pieces.
  if (model.ricochet) {
    const clip = S.rig.clips[S.clip];
    let t = now + Math.max(0, clip.length - S.time) / (clip.fps * S.speed) * 1000;
    const impacts = S.fxPlan && S.fxPlan.impactOnHit && S.fxPlan.uid === plan.uid;
    if (impacts) S.fxPlan.hopImpacts = S.fxPlan.hopImpacts || [];  // earlier loops' hops may still be in the air
    for (const [i, j] of model.links) {
      if (i >= list.length || j >= list.length) continue;
      const hop = plan.name.startsWith('ForgeProjectile_')
        ? { entry, forgeShot: true, start: [list[i].x, list[i].y], list: [[list[j].x, list[j].y]], centre: null, t0: t, speed: S.speed, seed: Math.floor(Math.random() * 100000), mirror: rigMirrored(), ricochet: true }
        : { entry, start: [list[i].x, list[i].y], end: [list[j].x, list[j].y], t0: t, speed: S.speed, hop: true };
      S.flights.push(hop);
      // Ricochet.Launch: the hop's flight, at most 3 s (the next hop leaves only when this one arrives)
      const secs = Math.min(3, hop.forgeShot && entry.forge && window.forgeProjectiles ? forgeProjectiles.firstHit(entry.forge, hop) : (entry.info && entry.info.duration) || 0.2);
      t += secs / S.speed * 1000;
      // each hop's arrival is an Impact event too, on the enemy it reaches (AttackSkillTemplate's hop loop)
      if (impacts) S.fxPlan.hopImpacts.push({ at: t, target: list[j] });
    }
  }
}
// Ricochet.Launch turns a hop's projectile from "toward the enemy side" to the hop's direction.
function hopTurn(f) {
  const forward = Math.sign(f.start[0]) || -1;
  return Math.atan2(f.end[1] - f.start[1], f.end[0] - f.start[0]) - Math.atan2(0, forward);
}

/* Effects and projectiles as the game builds them (effects.py): a tree of parts, some drawing a mesh or sprite image,
   moved by the prefab's legacy animation clips (position, rotation, scale, colour alpha, active). */
const fxCache = new Map();
function loadFx(name) {
  if (!fxCache.has(name)) {
    const entry = { info: null, images: {} };
    fxCache.set(name, entry);
    api('/api/fx/' + encodeURIComponent(name)).then(info => {
      if (!info) return;
      info.pathIndex = Object.fromEntries(info.nodes.map((n, i) => [n.path, i]));
      for (const n of info.nodes) {
        for (const key of [n.image, n.particles && n.particles.image]) {
          if (key && !entry.images[key]) { const im = new Image(); im.src = '/fximage/' + encodeURIComponent(key); entry.images[key] = im; }
        }
      }
      entry.info = info;
    }).catch(() => {});
  }
  return fxCache.get(name);
}
const curveAt = (keys, j, t) => evalUnityCurve(keys.map(k => [k[0], k[1][j], k[2][j], k[3][j]]), t);
function fxPose(info, t) {
  const st = info.nodes.map(n => ({ pos: n.pos, angle: n.angle, scale: n.scale, active: n.active, alpha: n.alpha ?? 1 }));
  for (const c of info.clips) {
    const tt = c.loop ? t % (c.length || 1) : Math.min(t, c.length || t);
    const owner = info.nodes[c.owner].path;
    for (const cv of c.curves) {
      const i = info.pathIndex[cv.path ? (owner ? owner + '/' + cv.path : cv.path) : owner];
      if (i === undefined) continue;
      const s = st[i];
      if (cv.kind === 'pos') s.pos = [0, 1, 2].map(j => curveAt(cv.keys, j, tt));
      else if (cv.kind === 'rot') { const q = [0, 1, 2, 3].map(j => curveAt(cv.keys, j, tt)); s.angle = 2 * Math.atan2(q[2], q[3]); }
      else if (cv.kind === 'euler') s.angle = curveAt(cv.keys, 2, tt) * Math.PI / 180;
      else if (cv.kind === 'scale') s.scale = [curveAt(cv.keys, 0, tt), curveAt(cv.keys, 1, tt)];
      else if (cv.attr === 'm_IsActive') s.active = curveAt(cv.keys, 0, tt) > 0.5;
      else if (/(^|[.\]])_?m?(Tint)?Color\.a$/.test(cv.attr)) s.alpha = curveAt(cv.keys, 0, tt) * (info.nodes[i].alphaGain || 1);
    }
  }
  return st;
}
// Draws the tree under root (a matrix in screen space). place(i, parentMatrix, localMatrix) can replace a part's matrix.
/* Particle systems (effects.particles): replayed from each particle's birth with fixed random numbers, so every frame
   agrees. Emission (bursts, rate, loops), start lifetime / speed / size / rotation / colour, the shape it's emitted
   from (through the emitter's 3D orientation), velocity and force over lifetime, gravity, speed limit with damping,
   size / colour / rotation over lifetime, sprite-sheet frames, and world-space particles staying where they were
   emitted when the effect moves (inst keeps where the emitter was). */
const prand = (a, b, c) => { const x = Math.sin(a * 127.1 + b * 311.7 + c * 74.7) * 43758.5453; return x - Math.floor(x); };
const plerp = (a, b, t) => a + (b - a) * t;
function mmcAt(c, u, r) {
  if (!c) return 0;
  const curve = keys => keys && keys.length ? evalUnityCurve(keys, u) : 1;
  switch (c.s) {
    case 1: return c.hi * curve(c.max);
    case 2: return c.hi * plerp(curve(c.min), curve(c.max), r);
    case 3: return plerp(c.lo, c.hi, r);
    default: return c.hi;
  }
}
function gradAt(g, u) {
  if (!g) return [1, 1, 1, 1];
  const at = (keys, n) => {
    if (!keys.length) return Array(n).fill(1);
    if (u <= keys[0][0]) return keys[0].slice(1);
    for (let i = 1; i < keys.length; i++) {
      if (u <= keys[i][0]) {
        if (g.fixed) return keys[i].slice(1);
        const k = (u - keys[i - 1][0]) / Math.max(1e-6, keys[i][0] - keys[i - 1][0]);
        return keys[i].slice(1).map((v, j) => plerp(keys[i - 1][j + 1], v, k));
      }
    }
    return keys[keys.length - 1].slice(1);
  };
  return [...at(g.c, 3), at(g.a, 1)[0]];
}
function mmgAt(c, u, r) {
  if (!c) return [1, 1, 1, 1];
  switch (c.s) {
    case 1: return gradAt(c.gmax, u);
    case 2: return c.lo.map((v, i) => plerp(v, c.hi[i], r));
    case 3: { const a = gradAt(c.gmin, u), b = gradAt(c.gmax, u); return a.map((v, i) => plerp(v, b[i], r)); }
    case 4: return gradAt(c.gmax, r);
    default: return c.hi;
  }
}
// Where a particle starts and which way it goes, in the emitter's shape space (z points away from the camera).
function shapeStart(sh, r1, r2, r3) {
  if (!sh) return { p: [0, 0, 0], d: [0, 0, 1] };
  const ang = r1 * Math.PI * 2 * Math.min(1, (sh.arc > 1 ? sh.arc : 360) / 360), c = Math.cos(ang), s = Math.sin(ang);
  switch (sh.type) {
    case 0: case 1: case 2: case 3: {  // sphere / hemisphere (seen from the front)
      const rr = sh.type % 2 ? sh.radius : sh.radius * Math.cbrt(r2);
      return { p: [c * rr, s * rr, 0], d: [c, s, 0] };
    }
    case 4: case 7: case 8: case 9: {  // cone: from its base disc, spreading by its angle around +z
      const k = sh.type === 7 || sh.type === 9 ? 1 : Math.sqrt(r2), sp = Math.sin(sh.angle * Math.PI / 180) * k;
      return { p: [c * sh.radius * k, s * sh.radius * k, 0], d: [c * sp, s * sp, Math.cos(sh.angle * Math.PI / 180 * k)] };
    }
    case 5: case 15: case 16: case 18:  // box / rectangle: anywhere in it, going along +z
      return { p: [(r2 - 0.5) * sh.box[0], (r3 - 0.5) * sh.box[1], 0], d: [0, 0, 1] };
    case 10: case 11: {  // circle (in the screen plane), outward
      const rr = sh.type === 11 ? sh.radius : sh.radius * Math.sqrt(r2);
      return { p: [c * rr, s * rr, 0], d: [c, s, 0] };
    }
    case 12: return { p: [(r2 * 2 - 1) * sh.radius, 0, 0], d: [0, 1, 0] };  // edge, going up
    default: return { p: [0, 0, 0], d: [0, 0, 1] };
  }
}
// Every particle alive at t: [{x, y (shape space projected), age frac, size, rot, color, frame, vx, vy, born}].
function simulateParticles(ps, t, seed) {
  const out = [];
  const delay = mmcAt(ps.delay, 0, 0.5), dur = Math.max(0.01, ps.duration);
  const births = [];
  const end = ps.loop ? t : Math.min(t, delay + dur);
  for (let cyc = 0; delay + cyc * dur <= end && cyc < 200; cyc++) {
    if (!ps.loop && cyc > 0) break;
    const c0 = delay + cyc * dur;
    for (const [bt, lo, hi, cycles, interval] of ps.bursts) {
      for (let k = 0; k < Math.max(1, cycles); k++) {
        const tb = c0 + bt + k * interval;
        if (tb > t || tb > c0 + dur) continue;
        const n = Math.round(plerp(lo, hi, prand(seed, cyc * 31 + k, 9)));
        for (let j = 0; j < n; j++) births.push(tb);
      }
    }
    const rate = ps.rate ? mmcAt(ps.rate, 0, 0.5) : 0;
    if (rate > 0) for (let j = 0; ; j++) { const tb = c0 + j / rate; if (tb > Math.min(t, c0 + dur) || j > 2000) break; births.push(tb); }
  }
  births.sort((a, b) => a - b);
  const g = ps.gravity ? mmcAt(ps.gravity, 0, 0.5) * 9.81 : 0;
  let alive = 0;
  births.forEach((tb, k) => {
    const R = salt => prand(seed + 0.37, k, salt);
    const u0 = ((tb - delay) % dur) / dur;
    const life = Math.max(0.01, mmcAt(ps.life, u0, R(1)));
    const age = t - tb;
    if (age < 0 || age >= life) return;
    if (++alive > ps.max) return;
    const st = shapeStart(ps.shape, R(2), R(3), R(4));
    const sp = mmcAt(ps.speed, u0, R(5));
    let v = st.d.map(x => x * sp), p = st.p.slice();
    const rvx = R(6), rvy = R(7), rfx = R(8), rfy = R(10), rcl = R(11);
    const dt = 1 / 60, steps = Math.min(240, Math.ceil(age / dt));
    let lastTotal = v.slice();
    for (let i = 0; i < steps; i++) {
      const h = Math.min(dt, age - i * dt), f = (i * dt) / life;
      if (ps.force) { v[0] += mmcAt(ps.force.x, f, rfx) * h; v[1] += mmcAt(ps.force.y, f, rfy) * h; }
      v[1] -= g * h;
      const add = ps.vel ? [mmcAt(ps.vel.x, f, rvx), mmcAt(ps.vel.y, f, rvy), 0] : [0, 0, 0];
      let total = [v[0] + add[0], v[1] + add[1], v[2]];
      if (ps.clamp) {
        const lim = mmcAt(ps.clamp.mag, f, rcl), mag = Math.hypot(total[0], total[1], total[2]);
        if (mag > lim && mag > 0) {
          const k = (lim + (mag - lim) * (1 - ps.clamp.dampen)) / mag;
          total = total.map(x => x * k);
          v = [total[0] - add[0], total[1] - add[1], total[2]];
        }
      }
      p = p.map((x, j) => x + total[j] * h);
      lastTotal = total;
    }
    const f = age / life;
    let size = mmcAt(ps.size, u0, R(12)) * (ps.sizeLife ? mmcAt(ps.sizeLife, f, R(13)) : 1);
    if (ps.scaling === 0) size *= ps.hscale || 1;
    const rot = mmcAt(ps.rot, u0, R(14)) + (ps.rotLife ? mmcAt(ps.rotLife, f, R(15)) * age : 0);
    const c0 = mmgAt(ps.color, u0, R(16)), c1 = ps.colorLife ? mmgAt(ps.colorLife, f, R(17)) : [1, 1, 1, 1];
    const tiles = ps.tiles[0] * ps.tiles[1];
    let frame = 0;
    if (ps.uv && tiles > 1) {
      const u = (f * (ps.uv.cycles || 1)) % 1;
      frame = Math.floor(mmcAt(ps.uv.start, u0, R(18)) + mmcAt(ps.uv.frame, u, R(19)) * tiles) % tiles;
    }
    const B = ps.basis || [[1, 0, 0], [0, 1, 0]];
    const proj = q => [B[0][0] * q[0] + B[0][1] * q[1] + B[0][2] * q[2], B[1][0] * q[0] + B[1][1] * q[1] + B[1][2] * q[2]];
    const [x, y] = proj(p), [vx, vy] = proj(lastTotal);
    out.push({ x, y, size, rot, color: [c0[0] * c1[0], c0[1] * c1[1], c0[2] * c1[2], c0[3] * c1[3]], frame, vx, vy, born: tb });
  });
  return out;
}
// A sprite-sheet frame multiplied by a colour, cached (particles are mostly white or a few tints).
const tintCache = new Map();
function particleImage(img, tiles, frame, color) {
  const q = v => Math.round(Math.max(0, Math.min(1, v)) * 15);
  const plain = q(color[0]) === 15 && q(color[1]) === 15 && q(color[2]) === 15;
  const key = `${img.src}|${frame}|${plain ? '' : [q(color[0]), q(color[1]), q(color[2])].join(',')}`;
  let c = tintCache.get(key);
  if (!c) {
    const tw = img.naturalWidth / tiles[0], th = img.naturalHeight / tiles[1];
    const fx = frame % tiles[0], fy = Math.floor(frame / tiles[0]);
    c = document.createElement('canvas');
    c.width = Math.max(1, Math.round(tw)); c.height = Math.max(1, Math.round(th));
    const x = c.getContext('2d');
    x.drawImage(img, fx * tw, fy * th, tw, th, 0, 0, c.width, c.height);
    if (!plain) {
      x.globalCompositeOperation = 'multiply';
      x.fillStyle = `rgb(${q(color[0]) * 17},${q(color[1]) * 17},${q(color[2]) * 17})`;
      x.fillRect(0, 0, c.width, c.height);
      x.globalCompositeOperation = 'destination-in';
      x.drawImage(img, fx * tw, fy * th, tw, th, 0, 0, c.width, c.height);
    }
    if (tintCache.size > 400) tintCache.clear();
    tintCache.set(key, c);
  }
  return c;
}
function drawParticles(entry, n, i, t, world, root, base, inst) {
  const ps = n.particles, img = entry.images[ps.image];
  if (!img || !img.complete || !img.naturalWidth) return;
  const seed = (inst && (inst.pseed ??= Math.random() * 1000)) || 1;
  const parts = simulateParticles(ps, t, seed + i);
  const lin = [root[0], root[1], root[2], root[3]];
  // world-space particles stay where the emitter was when they were born (inst remembers where it has been)
  let hist = null;
  if (ps.world && inst) {
    inst.phist = inst.phist || {};
    hist = inst.phist[i] = inst.phist[i] || [];
    if (!hist.length || hist[hist.length - 1][0] < t) hist.push([t, world[i][4], world[i][5]]);
    if (hist.length > 600) hist.splice(0, hist.length - 600);
  }
  const originAt = tb => {
    if (!hist || !hist.length) return [world[i][4], world[i][5]];
    let best = hist[0];
    for (const h of hist) { if (h[0] <= tb) best = h; else break; }
    return [best[1], best[2]];
  };
  for (const q of parts) {
    if (q.color[3] <= 0.004 || q.size <= 0) continue;
    const [ox, oy] = originAt(q.born);
    const pic = particleImage(img, ps.tiles, q.frame, q.color);
    const aspect = pic.height / pic.width;
    let m = [lin[0], lin[1], lin[2], lin[3], ox + lin[0] * q.x + lin[2] * q.y, oy + lin[1] * q.x + lin[3] * q.y];
    let sx = q.size, sy = q.size * aspect;
    if (ps.render.mode === 1) {
      // stretched along its movement
      const a = Math.atan2(q.vy, q.vx), c = Math.cos(a), s = Math.sin(a);
      m = mul(m, [c, s, -s, c, 0, 0]);
      sx = q.size * Math.max(1, ps.render.length) + Math.hypot(q.vx, q.vy) * ps.render.velocity;
    } else {
      const c = Math.cos(-q.rot), s = Math.sin(-q.rot);  // Unity turns particles clockwise
      m = mul(m, [c, s, -s, c, 0, 0]);
    }
    ctx.globalAlpha = Math.min(1, q.color[3]);
    ctx.setTransform(...mul(base, mul(m, [1, 0, 0, -1, 0, 0])));
    ctx.drawImage(pic, -sx / 2, -sy / 2, sx, sy);
  }
  ctx.globalAlpha = 1;
}
/* After-images (Forge_AfterImages, the game's AfterImageTrail): every interval of real time the rig's pose is kept as
   a still copy that fades from the effect's opacity over its lifetime, tinted, behind or in front of the rig. */
const AFTER_DEFAULTS = { duration: 0.6, interval: 0.06, life: 0.35, opacity: 0.6, tint: 'ffffff', behind: true, spread: 0, driftX: 0, driftY: 0, grow: 1 };
S.ghosts = [];
function updateGhosts(list) {
  const real = performance.now(), tr = S.ghostTrail;
  if (tr && real >= tr.next && real <= tr.until) {
    // spread: each copy starts up to that far off; drift: forward is toward the enemy (pigs face left)
    const ang = Math.random() * Math.PI * 2, rr = (tr.af.spread || 0) * Math.sqrt(Math.random());
    S.ghosts.push({ born: real, af: tr.af, off: [Math.cos(ang) * rr, Math.sin(ang) * rr], fwd: S.rig.side === 'bird' ? 1 : -1, parts: list.filter(d => d.image.complete && d.image.naturalWidth && !/shadow/i.test(d.slot || '')).map(d => ({ image: d.image, w: d.w, h: d.h, M: mul(d.M, d.local) })) });
    tr.next = real + tr.interval;
  }
  if (tr && real > tr.until) S.ghostTrail = null;
  S.ghosts = S.ghosts.filter(g => real - g.born < g.af.life * 1000 / S.speed);
  return S.ghosts.some(g => g.af.behind);
}
const hexRgb = hex => { const v = parseInt(String(hex || 'ffffff').replace('#', ''), 16) || 0; return [(v >> 16 & 255) / 255, (v >> 8 & 255) / 255, (v & 255) / 255]; };
function drawGhosts(base, V, behind) {
  const real = performance.now();
  for (const g of S.ghosts) {
    if (!!g.af.behind !== behind) continue;
    const frac = (real - g.born) / (g.af.life * 1000 / S.speed), age = (real - g.born) / 1000 * S.speed;
    const a = g.af.opacity * (1 - frac);
    if (a <= 0) continue;
    const k = 1 + ((g.af.grow || 1) - 1) * Math.min(1, frac);
    const move = [g.off[0] + (g.af.driftX || 0) * g.fwd * age, g.off[1] + (g.af.driftY || 0) * age];
    const place = [k, 0, 0, k, move[0], move[1]];  // scaled around the rig's origin, then moved
    const tint = hexRgb(g.af.tint);
    ctx.globalAlpha = Math.min(1, a);
    for (const d of g.parts) {
      const pic = tint.every(v => v > 0.97) ? d.image : particleImage(d.image, [1, 1], 0, tint);
      ctx.setTransform(...mul(base, mul(V, mul(place, mul(d.M, [1, 0, 0, -1, 0, 0])))));
      ctx.drawImage(pic, -d.w / 2, -d.h / 2, d.w, d.h);
    }
  }
  ctx.globalAlpha = 1;
}
/* Text labels in effects (SkillFX_Generic_Text's UILabel): the word, coloured, outlined, sized, turned and bent along an
   arc as the effect's text settings say (ForgeEffectExtras.ApplyText), with the effect's own pop, rise and fade. */
const TEXT_DEFAULTS = { text: 'Immune!', color: 'ffffff', outline: '000000', size: 1, rotation: 0, arc: 0 };
function drawLabel(n, i, world, alpha, base, inst) {
  const lab = n.label, s = inst && inst.text ? inst.text : { ...TEXT_DEFAULTS, text: lab.text, outline: '' };
  const text = String(s.text ?? '');
  if (!text || alpha <= 0) return;
  let m = world[i];
  // words read left to right on a mirrored effect (mirrored = handedness opposite to the view's, which flips y)
  const W = worldMatrix(), det = x => x[0] * x[3] - x[1] * x[2];
  if (det(m) * det(W) < 0) m = mul(m, [-1, 0, 0, 1, 0, 0]);
  const rot = (s.rotation || 0) * Math.PI / 180, k = s.size || 1;
  m = mul(m, [Math.cos(rot) * k, Math.sin(rot) * k, -Math.sin(rot) * k, Math.cos(rot) * k, 0, 0]);
  const px = lab.size || 54;
  ctx.font = `bold ${px}px "Trebuchet MS", "Arial Black", sans-serif`;
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  ctx.lineJoin = 'round';
  const [r, g, b] = hexRgb(s.color), col = `rgb(${r * 255 | 0},${g * 255 | 0},${b * 255 | 0})`;
  const outline = s.outline ? (() => { const [orr, og, ob] = hexRgb(s.outline); return `rgb(${orr * 255 | 0},${og * 255 | 0},${ob * 255 | 0})`; })() : 'rgba(40,24,10,0.9)';
  ctx.globalAlpha = Math.max(0, Math.min(1, alpha));
  // one glyph placed at (x, y) (y up) turned by phi
  const glyph = (ch, x, y, phi) => {
    ctx.setTransform(...mul(base, mul(mul(m, [Math.cos(phi), Math.sin(phi), -Math.sin(phi), Math.cos(phi), x, y]), [1, 0, 0, -1, 0, 0])));
    ctx.lineWidth = px * 0.16;
    ctx.strokeStyle = outline; ctx.strokeText(ch, 0, 0);
    ctx.fillStyle = col; ctx.fillText(ch, 0, 0);
  };
  if (Math.abs(s.arc || 0) < 0.5 || text.length < 2) glyph(text, 0, 0, 0);
  else {
    const widths = [...text].map(ch => ch === ' ' ? px * 0.35 : ctx.measureText(ch).width);
    const total = widths.reduce((a, b2) => a + b2, 0), radius = total / (s.arc * Math.PI / 180);
    let x = 0;
    [...text].forEach((ch, j) => {
      const phi = (x + widths[j] / 2 - total / 2) / radius;
      glyph(ch, radius * Math.sin(phi), radius * Math.cos(phi) - radius, -phi);
      x += widths[j];
    });
  }
  ctx.globalAlpha = 1;
}
function drawFxTree(entry, t, root, base, place, inst) {
  const info = entry.info;
  const st = fxPose(info, t);
  const world = [], on = [], z = [];
  info.nodes.forEach((n, i) => {
    const s = st[i], c = Math.cos(s.angle), sn = Math.sin(s.angle);
    const local = mul([1, 0, 0, 1, s.pos[0], s.pos[1]], [c * s.scale[0], sn * s.scale[0], -sn * s.scale[1], c * s.scale[1], 0, 0]);
    const parent = n.parent == null ? root : world[n.parent];
    world[i] = (place && place(i, parent, local, s)) || mul(parent, local);
    on[i] = s.active && (n.parent == null || on[n.parent]);
    z[i] = (s.pos[2] || 0) + (n.parent == null ? 0 : z[n.parent]);
  });
  // Unity draws transparent parts back to front: the farther (higher z) first.
  const order = info.nodes.map((n, i) => i).filter(i => (info.nodes[i].image || info.nodes[i].particles || info.nodes[i].label) && on[i]).sort((a, b) => z[b] - z[a]);
  for (const i of order) {
    const n = info.nodes[i];
    if (n.label) { drawLabel(n, i, world, st[i].alpha, base, inst); continue; }
    if (n.particles) { drawParticles(entry, n, i, t, world, root, base, inst); if (!n.image) continue; }
    const img = entry.images[n.image];
    const alpha = Math.max(0, Math.min(1, st[i].alpha));
    if (!img || !img.complete || !img.naturalWidth || alpha <= 0) continue;
    const [x0, y0, x1, y1] = n.bounds;
    ctx.globalAlpha = alpha;
    ctx.setTransform(...mul(base, mul(world[i], [1, 0, 0, -1, 0, 0])));
    ctx.drawImage(img, x0, -y1, x1 - x0, y1 - y0);
  }
  ctx.globalAlpha = 1;
  return !!order.length;
}

// A skill's effects (Root.unity VisualEffectsBalancing by the skill's AssetId), spawned at Start, Movement and Impact
// like SkillBattleDataBase.SpawnVisualEffects. Pig effects are mirrored unless DoNotMirror; screen flashes and camera
// shakes are played as simple overlays.
const skillFxCache = new Map();
function loadSkillFx(asset) {
  if (!asset) return [];
  if (!skillFxCache.has(asset)) {
    const entry = { list: null };
    skillFxCache.set(asset, entry);
    api('/api/skillfx/' + encodeURIComponent(asset)).then(list => { entry.list = list; }).catch(() => { entry.list = []; });
  }
  return skillFxCache.get(asset);
}
function abilityAsset(a) {
  const row = S.skills.find(s => s.id === (a.nameId || a.source));
  return (row && row.asset) || (a.type === 'Attack' ? 'Smash' : a.type === 'Support' ? 'Protect' : '');
}
S.fxPlaying = [];
// only: the targets it plays on (a ricochet hop's enemy), else the attack's own.
// The pose index of the bone an anchor sits on (the game's BodyCenter is the Center bone, BodyRoot the Bottom bone).
function anchorBone(anchor) {
  const want = { Center: 'Center', Bottom: 'Bottom', MainHand: 'MainHand', OffHand: 'OffHand' }[anchor] || 'Bottom';
  const i = S.rig.paths.findIndex(p => p.split('/').pop() === want);
  return i >= 0 ? i : anchor === 'Bottom' ? 0 : -1;
}
const boneIndex = name => S.rig ? S.rig.paths.findIndex(p => p.split('/').pop() === name) : -1;
// A projectile event in the preview (fired, hits, bounces, stops, or its slow motion): the ability's effects with that
// timing play, "At the projectile" ones where it happened.
function fxEvent(uid, kind, pos) {
  const plan = S.fxPlan;
  if (!plan || plan.uid !== uid) return;
  const timing = { launch: 'ProjectileLaunch', hit: 'ProjectileHit', bounce: 'ProjectileBounce', end: 'ProjectileEnd', slowmo: 'SlowMotion' }[kind];
  if (timing) spawnSkillFx(timing, plan, vnow(), null, pos);
}
function spawnSkillFx(timing, plan, now, only, atPos) {
  const a = S.project.abilities.find(x => x.uid === plan.uid);
  const entry = { list: a ? effectsFor(a) : null };
  if (!entry.list) return;
  const list = targets();
  const hit = only || (a && shotModel(a).shots.includes('center') ? list : list.slice(0, 1));
  const gs = gameScale(), bc = (S.rig.bodyCenter || [0, 45])[1] * gs;
  const centre = targetCentre(hit);
  for (const e of entry.list) {
    if (e.timing !== timing) continue;
    if (e.target === 'SlowMotion') {
      if (timing === 'SlowMotion') continue;  // a slow motion can't start itself
      const sl = e.slow || {};
      S.slowmo = { start: performance.now() + e.delay * 1000 / S.speed, scale: Math.max(0.01, sl.scale ?? 0.3), duration: (sl.duration ?? 0.5) / S.speed, ease: (sl.ease ?? 0.1) / S.speed,
        easeOut: (sl.easeOut ?? sl.ease ?? 0.1) / S.speed };
      spawnSkillFx('SlowMotion', plan, now + e.delay * 1000 / S.speed);
      continue;
    }
    if (e.prefab === 'Forge_AfterImages') {
      // the attacker's copies (targets are only markers here)
      const af = { ...AFTER_DEFAULTS, ...(e.after || {}) };
      if (e.target === 'Origin' || e.target === 'OriginArea') {
        const start = performance.now() + e.delay * 1000 / S.speed;
        S.ghostTrail = { start, until: start + af.duration * 1000 / S.speed, interval: Math.max(20, af.interval * 1000 / S.speed), next: start, af };
      }
      continue;
    }
    const name = e.onHit ? e.onHitPrefix + ((S.project.weapon || {}).hitEffect || '') : e.prefab;
    if (!name) continue;
    const t0 = now + e.delay * 1000 / S.speed;
    if (/^Camera_Shake/i.test(name)) {
      const amp = /Massive/i.test(name) ? 16 : /Strong|Heavy|Large/i.test(name) ? 10 : /Medium/i.test(name) ? 6 : 3;
      S.shake = { t0, until: t0 + (amp > 8 ? 450 : 300) / S.speed, amp };
      continue;
    }
    if (/^ScreenFX_/i.test(name)) {
      const dark = /Darken|Black/i.test(name);
      const secs = (name.match(/(\d+)-(\d+)s/) || [])[0] ? parseFloat(name.match(/(\d+)-(\d+)s/)[1] + '.' + name.match(/(\d+)-(\d+)s/)[2]) : /Short/i.test(name) ? 0.15 : 0.3;
      S.screenFx = { t0, until: t0 + secs * 1000 / S.speed, color: dark ? '0,0,0' : '255,255,255', alpha: dark ? 0.35 : 0.55 };
      continue;
    }
    const anchorAt = (x, y, isOrigin) => {
      if (isOrigin) return e.anchor === 'MainHand' || e.anchor === 'OffHand' ? handAt(plan.clip, S.time) : e.anchor === 'Center' ? [0, bc] : [0, 0];
      return e.anchor === 'Bottom' ? [x, y - 45] : [x, y];
    };
    const spots = e.target === 'Projectile' ? (atPos ? [atPos] : [])
      : e.target === 'Target' ? hit.map(t => anchorAt(t.x, t.y, false))
      : e.target === 'TargetArea' ? [anchorAt(centre.x, centre.y, false)]
      : e.target === 'CenterArea' ? [[centre.x / 2, (centre.y + bc) / 2]]
      : [anchorAt(0, 0, true)];
    const mirror = rigMirrored() ? e.doNotMirror : !e.doNotMirror;
    loadFx(name);
    const text = e.text ? { ...TEXT_DEFAULTS, ...e.text } : e.textDefault ? { ...TEXT_DEFAULTS, text: e.textDefault, outline: '' } : null;
    // on the attacker it sits on its bone and goes where the animation takes it (the game parents it there)
    const raw = e.raw || {}, parented = !['1', 'true', 'True', true].includes(raw.DoNotParentOnBone);
    const follow = e.target === 'Origin' && parented ? anchorBone(e.anchor) : -1;
    const turnBone = e.turnWith === 'bone' ? boneIndex(e.bone) : -1;
    const useBoneRot = ['1', 'true', 'True', true].includes(raw.UseTargetBoneRotation);
    // its offset: x toward the enemy (pigs face left), y up
    const fwd = S.rig && S.rig.side === 'bird' ? 1 : -1, offset = [(e.offsetX || 0) * fwd * (e.target === 'Target' || e.target === 'TargetArea' ? -1 : 1), e.offsetY || 0];
    for (const at of spots) S.fxPlaying.push({ name, t0, at, mirror, speed: S.speed, text, follow, turn: e.turn || 0, turnWith: e.turnWith || 'anchor', turnBone, useBoneRot, offset });
  }
}
function updateSkillFx(now) {
  const plan = S.fxPlan;
  if (!plan || !S.rig) return;
  if (plan.clip !== S.clip) { S.fxPlan = null; return; }
  if (S.time < plan.last) plan.done = new Set(); // looped (a projectile still in the air lands, and is the Impact, after the loop)
  plan.last = S.time;
  if (plan.impactOnHit) {
    // the projectile's hit is the Impact event (updateShots sets when it lands)
    plan.done.add('Impact');
    if (plan.impactAt != null && now >= plan.impactAt) { plan.impactAt = null; spawnSkillFx('Impact', plan, now); }
    for (const hi of (plan.hopImpacts || []).filter(x => now >= x.at)) spawnSkillFx('Impact', plan, now, [hi.target]);
    if (plan.hopImpacts) plan.hopImpacts = plan.hopImpacts.filter(x => now < x.at);
  }
  const clip = S.rig.clips[S.clip];
  const at = { Start: 0, Movement: ((clip.events || []).find(e => e[1] === 'Movement') || [])[0], Impact: ((clip.events || []).find(e => e[1] === 'Impact') || [])[0] };
  for (const [timing, frame] of Object.entries(at)) {
    if (frame === undefined || plan.done.has(timing) || S.time < frame) continue;
    plan.done.add(timing);
    spawnSkillFx(timing, plan, now);
  }
}
function drawSkillFx(now, base) {
  const W = worldMatrix();
  const poses = S.rig ? pose(S.rig.clips[S.clip], S.time) : null, gs = gameScale();
  const angleOf = m => Math.atan2(m[1], m[0]);
  S.fxPlaying = S.fxPlaying.filter(p => {
    if (now < p.t0) return true;
    const entry = fxCache.get(p.name);
    // not loaded yet: it starts when it is, rather than being skipped or shown half over
    if (!entry || !entry.info || Object.values(entry.images).some(im => !im.complete)) { p.born = p.born || now; p.t0 = now; return now - p.born < 5000; }
    const t = (now - p.t0) / 1000 * p.speed;
    if (t > entry.info.life) return false;
    // where its bone is now, and how it turns: with its anchor (from when it spawned, or wholly with UseTargetBoneRotation),
    // upright, or with another bone; plus its own turn
    let at = p.at, rot = (p.turn || 0) * Math.PI / 180;
    if (poses && p.follow >= 0 && poses[p.follow]) {
      const M = poses[p.follow];
      at = [M[4] * gs, M[5] * gs];
      if (p.turnWith === 'anchor') {
        if (p.spawnAngle === undefined) p.spawnAngle = angleOf(M);
        rot += p.useBoneRot ? angleOf(M) : angleOf(M) - p.spawnAngle;
      }
    }
    if (poses && p.turnWith === 'bone' && p.turnBone >= 0 && poses[p.turnBone]) rot += angleOf(poses[p.turnBone]);
    if (p.offset) at = [at[0] + p.offset[0], at[1] + p.offset[1]];
    p.lastAt = at; p.lastRot = rot;
    let root = mul(W, [1, 0, 0, 1, at[0], at[1]]);
    if (rot) root = mul(root, [Math.cos(rot), Math.sin(rot), -Math.sin(rot), Math.cos(rot), 0, 0]);
    if (p.mirror) root = mul(root, [-1, 0, 0, 1, 0, 0]);
    drawFxTree(entry, t, root, base, null, p);
    return true;
  });
}
function screenOverlays(now, dpr) {
  const fx = S.screenFx;
  if (!fx || now < fx.t0) return;
  if (now > fx.until) { S.screenFx = null; return; }
  const k = 1 - (now - fx.t0) / (fx.until - fx.t0);
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.fillStyle = `rgba(${fx.color},${fx.alpha * k})`;
  ctx.fillRect(0, 0, canvas.width, canvas.height);
}
// ProjectileArrivalEffects: a projectile that carries its own hit plays it on the combatant it reaches (each target
// and each ricochet hop), not on the attack clip's Impact event. A shot at the middle of a side plays nothing.
function spawnArrivalFx(f, now) {
  const fx = f.entry.fx;
  if (!fx || !fx.info || !fx.info.arrival || !fx.info.arrival.length || f.toCentre) return;
  for (const e of fx.info.arrival) {
    if (e.target !== 'Target' || !e.prefab) continue;
    const at = e.anchor === 'Bottom' ? [f.end[0], f.end[1] - 45] : f.end;
    loadFx(e.prefab);
    S.fxPlaying.push({ name: e.prefab, t0: now + e.delay * 1000 / f.speed, at, mirror: rigMirrored() ? e.doNotMirror : !e.doNotMirror, speed: f.speed });
  }
}
function drawFlights(now, base) {
  const W = worldMatrix();
  S.flights = S.flights.filter(f => {
    const info = f.entry.info;
    if (now < f.t0) return true;
    if (f.forgeShot) {
      if (!f.entry.forge || !window.forgeProjectiles) return now - f.t0 < 2000;
      return forgeProjectiles.drawShot(f.entry.forge, f, (now - f.t0) / 1000 * f.speed, base, W);
    }
    if (!info) return now - f.t0 < 2000;
    const u = (now - f.t0) / 1000 * f.speed / (info.duration || 0.2);
    if (f.onEvent && !f.launched) { f.launched = true; f.onEvent('launch', f.start[0], f.start[1]); }
    if (u >= 1) { spawnArrivalFx(f, now); if (f.onEvent) f.onEvent('hit', f.end[0], f.end[1]); return false; } // the game destroys it when its tween ends
    const [x, y] = flightPoint(info, f.start, f.end, u);
    // The prefab's own animation (the arrow's arc and tilt, the barf's wobble) plays from launch; CHMotionTween moves its
    // tween part through the battle, everything else follows.
    const fx = f.entry.fx;
    if (fx && fx.info && fx.info.tween != null) {
      const elapsed = (now - f.t0) / 1000 * f.speed;
      let root = mul(W, [1, 0, 0, 1, f.start[0], f.start[1]]);
      if (rigMirrored()) root = mul(root, [-1, 0, 0, 1, 0, 0]);
      const drew = drawFxTree(fx, elapsed, root, base, (i, parent, local) => {
        if (i !== fx.info.tween) return null;
        let m = mul(W, [1, 0, 0, 1, x, y]);
        if (f.hop) { const r = hopTurn(f), c = Math.cos(r), s = Math.sin(r); m = mul(m, [c, s, -s, c, 0, 0]); }
        if (rigMirrored()) m = mul(m, [-1, 0, 0, 1, 0, 0]);
        return mul(m, [local[0], local[1], local[2], local[3], 0, 0]);
      }, f);
      if (drew) return true;
    }
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
  if (!$('targetCount')) return; // being typed into
  S.targetControlsKey = key;
  $('targetControls').hidden = !shown;
  if (!shown) return;
  $('targetCount').textContent = n;
  $('targetLess').disabled = n <= 1;
  $('targetMore').disabled = n >= MAX_TARGETS;
  $('targetControls').title = `Your attacks need ${need} target${need === 1 ? '' : 's'}${need === ALL_ENEMIES ? ' (an attack hits every enemy: add more for a bigger party)' : ''}. Drag a target to move it.`;
}
// Clicking the count turns it into a box to type a number (1-20); Enter or leaving it applies, Escape cancels.
$('targetCount').title = 'Click to type how many targets';
$('targetCount').addEventListener('click', () => {
  const span = $('targetCount');
  const input = h('input', { type: 'number', min: 1, max: MAX_TARGETS, value: targetCount(), class: 'mono', style: 'width:52px', 'aria-label': 'Number of targets' });
  let done = false;
  const finish = apply => {
    if (done) return;
    done = true;
    const n = Math.round(num(input.value, targetCount()));
    if (apply && n >= 1) { S.targetCountSet = Math.min(MAX_TARGETS, n); S.targetControlsKey = null; }
    input.replaceWith(span);
    syncTargetControls();
    if (!targetOnScreen()) fitView();
  };
  input.addEventListener('keydown', ev => { if (ev.key === 'Enter') finish(true); if (ev.key === 'Escape') finish(false); ev.stopPropagation(); });
  input.addEventListener('blur', () => finish(true));
  span.replaceWith(input);
  input.focus(); input.select();
});
$('targetLess').addEventListener('click', () => { S.targetCountSet = Math.max(1, targetCount() - 1); syncTargetControls(); });
$('targetMore').addEventListener('click', () => { S.targetCountSet = Math.min(MAX_TARGETS, targetCount() + 1); syncTargetControls(); if (!targetOnScreen()) fitView(); });
$('showPaths').addEventListener('change', e => { try { localStorage.setItem('forge.paths', e.target.checked ? '1' : ''); } catch (err) { /* optional */ } });
try { $('showPaths').checked = localStorage.getItem('forge.paths') === '1'; } catch (e) { /* storage is optional */ }

let lastFrame = performance.now();
/* Slow motion (the game's SlowMotion effect: BattleMgrBase.ChangeTimeScale): the preview runs on a virtual clock that
   eases down to the effect's time scale, holds it for its duration and eases back. Everything that plays (the clip,
   projectiles, effects) reads this clock; after-images keep real time, as the game's AfterImageTrail does. */
const VCLOCK = { real: performance.now(), virt: performance.now() };
function timeScale(real) {
  const s = S.slowmo;
  if (!s) return 1;
  const e = (real - s.start) / 1000, ease = Math.max(1e-4, s.ease), out = s.easeOut ?? s.ease, easeOut = Math.max(1e-4, out);
  if (e < 0) return 1;
  if (e < s.ease) return 1 + (s.scale - 1) * (e / ease);
  if (e < s.ease + s.duration) return s.scale;
  if (e < s.ease + s.duration + out) return s.scale + (1 - s.scale) * ((e - s.ease - s.duration) / easeOut);
  S.slowmo = null;
  return 1;
}
function vnow(real = performance.now()) { return VCLOCK.virt + (real - VCLOCK.real) * timeScale(real); }
/* Tongues: each ability's Values / Animation / Effects sections get a tab sticking out of the side panel's left edge
   into the canvas, level with the section, in the ability's colour, so they're easy to open and close. They live in a
   fixed layer (the panel scrolls and would clip them) that follows the rows every frame. */
const tongueLayer = document.createElement('div');
tongueLayer.id = 'tongues';
document.body.append(tongueLayer);
const tongues = [];
function syncTongues() {
  const tab = $('tab-abilities');
  const show = tab && !tab.hidden && S.mode !== 'projectile';
  const rows = show ? [...document.querySelectorAll('#abilityList details[data-tongue]')] : [];
  const area = show ? tab.getBoundingClientRect() : null, side = show ? $('side').getBoundingClientRect() : null;
  let used = 0;
  for (const d of rows) {
    const sum = d.querySelector('summary');
    if (!sum) continue;
    const r = sum.getBoundingClientRect();
    if (r.height <= 0 || r.top < area.top - 1 || r.bottom > area.bottom + 1) continue;  // scrolled out of the panel
    let t = tongues[used];
    if (!t) {
      t = document.createElement('button');
      t.className = 'tongue';
      t.addEventListener('click', () => { if (t.details) t.details.open = !t.details.open; });
      tongueLayer.append(t);
      tongues.push(t);
    }
    used++;
    t.details = d;
    const card = d.closest('.ability');
    const color = card ? getComputedStyle(card).getPropertyValue('--strip').trim() : '';
    const label = (d.open ? '▾ ' : '▸ ') + d.dataset.tongue;
    if (t.textContent !== label) t.textContent = label;
    t.title = `${d.open ? 'Close' : 'Open'} ${d.dataset.tongue} of ${(card && card.querySelector('.title') || {}).value || 'this ability'}`;
    t.classList.toggle('open', d.open);
    // joined to the card's edge, a little taller than the row so it's easy to hit
    const edge = card ? card.getBoundingClientRect().left : side.left, h2 = Math.max(24, r.height + 4);
    t.style.cssText = `top:${r.top + r.height / 2 - h2 / 2}px;height:${h2}px;right:${window.innerWidth - edge}px;padding-right:${10 + Math.max(0, edge - side.left)}px;--tongue:${color || 'var(--accent)'}`;
    t.hidden = false;
  }
  for (let i = used; i < tongues.length; i++) tongues[i].hidden = true;
}
function tick(real) {
  const scale = timeScale(real);
  const now = vnow(real);
  VCLOCK.real = real; VCLOCK.virt = now;
  const dt = Math.min(0.1, (real - lastFrame) / 1000) * scale;
  lastFrame = real;
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
  updateSkillFx(now);
  draw(now);
  syncTongues();
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
// Setting a slot's pivot on the canvas: click or drag puts it under the pointer (in the slot's art space).
function setPivotAt(e) {
  const b = slotBone(S.selected);
  if (!b) return;
  const rig = S.rig, i = b.bone;
  const poses = pose(rig.clips[S.clip], S.time);
  const world = apply(invert(viewMatrix()), ...pointerPos(e));
  // pointer = parent · (pos + L0 · p): undo the parent, the bone's current position and its rest rotation
  const parent = rig.parents[i] >= 0 ? poses[rig.parents[i]] : [1, 0, 0, 1, 0, 0];
  const track = rig.clips[S.clip].tracks[i] || {}, rest = rig.clips[rig.restClip].tracks[i] || {};
  const pos = track.p ? sampleVec(track.p, S.time) : rest.p ? sampleVec(rest.p, 0) : [0, 0];
  const inParent = apply(invert(parent), world[0], world[1]);
  const L0 = restLocal(rig, i);
  const pBone = apply(invert([L0[0], L0[1], L0[2], L0[3], 0, 0]), inParent[0] - pos[0], inParent[1] - pos[1]);
  const Ls = b.sprite != null && rig.parents[b.sprite] === i ? restLocal(rig, b.sprite) : [1, 0, 0, 1, 0, 0];
  const pArt = apply(invert(Ls), pBone[0], pBone[1]);
  S.project.bonePivots = { ...(S.project.bonePivots || {}), [S.selected]: [Math.round(pArt[0] * 10) / 10, Math.round(pArt[1] * 10) / 10] };
}
canvas.addEventListener('pointerdown', e => {
  if (!S.pivotEdit || e.button !== 0 || S.mode === 'projectile' || !slotBone(S.selected)) return;
  e.stopImmediatePropagation(); e.preventDefault();
  S.pivotDrag = true;
  try { canvas.setPointerCapture(e.pointerId); } catch (err) { /* not a live pointer */ }
  setPivotAt(e);
  renderInspector();
}, true);
canvas.addEventListener('pointermove', e => { if (S.pivotDrag) { e.stopImmediatePropagation(); setPivotAt(e); } }, true);
canvas.addEventListener('pointerup', e => { if (S.pivotDrag) { e.stopImmediatePropagation(); S.pivotDrag = false; touch(); renderInspector(); } }, true);
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

const CATS = [['Headgear', 'Headgear'], ['MainHand', 'Main hand'], ['OffHand', 'Off hand'], ['Feature', 'Features'], ['Projectile', 'Projectiles'], ['Effect', 'Effects']];
// Effects (effects.library_items: the game's effect and projectile art, particle and trail textures) are the same for
// pigs and birds, and load the first time the category is opened.
const sideOk = i => i.side === S.side || i.cat === 'Effect';
function ensureEffects() {
  if (S.lib.some(i => i.cat === 'Effect') || S.fxLibLoading) return true;
  S.fxLibLoading = true;
  api('/api/fx_library').then(items => { S.lib = S.lib.concat(items); }).catch(e => toast('Effects: ' + e.message))
    .finally(() => { S.fxLibLoading = false; if (S.cat === 'Effect') renderLibrary(); });
  return false;
}
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
  return S.lib.filter(i => sideOk(i) && i.cat === S.cat && (!S.rigFilter || i.rig === S.rigFilter) && (!q || i.name.toLowerCase().includes(q) || i.rig.toLowerCase().includes(q)));
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
  if (S.cat === 'Effect' && !ensureEffects()) {
    $('grid').replaceChildren(h('p', { class: 'dim' }, 'Loading the game\'s effects… (the first time takes a little while)'));
    $('libCount').textContent = '';
    return;
  }
  const rigs = [...new Set(S.lib.filter(i => sideOk(i) && i.cat === S.cat).map(i => i.rig))].sort();
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
    // its drop targets: the attacks (Abilities tab), or in the Projectiles section the components (Visuals tab)
    if (S.mode === 'projectile') { if ($('tab-slots').hidden) setTab('slots'); }
    else setTab('abilities');
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
  // over a layer in the inspector: that layer's picture is swapped (its placement stays)
  const under = document.elementFromPoint(e.clientX, e.clientY);
  const layerRow = under && under.closest && under.closest('.layerRow[data-layer]');
  S.hoverLayer = layerRow ? { slot: layerRow.dataset.slot, uid: layerRow.dataset.layer } : null;
  document.querySelectorAll('.layerRow').forEach(r => r.classList.toggle('target', r === layerRow));
  S.hoverSlot = layerRow ? null : slotUnderPointer(e);
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
  const slot = S.hoverSlot, onLayer = S.hoverLayer;
  S.drag = null;
  S.hoverSlot = null;
  S.hoverLayer = null;
  $('dragGhost').hidden = true;
  document.querySelectorAll('.slotRow.target, .layerRow.target').forEach(r => r.classList.remove('target'));
  if (onLayer) replaceLayerArt(onLayer.slot, onLayer.uid, item, part, joined);
  else if (slot) assign(item, part, slot, joined);
});
// Another picture in an existing layer: its size is the new art's, its placement (offset, rotation, scale, shear,
// pivot) stays. A front/back pair swaps both sides when the art has a back.
function replaceLayerArt(slotKey, uid, item, part, joined) {
  if (item.look) { toast('A spring can\'t go in a layer'); return; }
  const slot = S.project.slots[slotKey];
  const layer = slot && slot.layers.find(l => l.uid === uid);
  const sprite = item.parts[part];
  if (!layer || !sprite) return;
  const swap = (l, sp) => Object.assign(l, { src: sp.key, name: sp.name, item: item.name, w: sp.w, h: sp.h });
  swap(layer, sprite);
  if (joined && layer.pair && item.parts.back && part !== 'back') {
    for (const [k, other] of Object.entries(S.project.slots)) {
      if (k === slotKey) continue;
      const twin = other.layers.find(l => l.pair === layer.pair);
      if (twin) swap(twin, item.parts.back);
    }
  }
  slot.active = uid;
  selectSlot(slotKey);
  touch();
  toast(`Swapped the picture in ${slotKey}; its placement stayed`);
}
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
  if (item.look || slotKey === SPRING_SLOT) {
    if (!item.look) { toast('Only a spring goes in the Spring slot'); return; }
    if (!rigLooks()[item.look]) { toast(`${S.rig.id} can't ride a spring`); return; }
    setLook(item.look);
    return;
  }
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
  if (hasLooks()) {
    const look = rigLooks()[S.project.variant] ? S.project.variant : '';
    features.unshift(h('div', { class: 'slotRow' + (S.selected === SPRING_SLOT ? ' sel' : '') + (look ? '' : ' off'), 'data-slot': SPRING_SLOT, onclick: () => selectSlot(SPRING_SLOT) },
      h('div', { class: 'slotThumb', style: look ? `background-image:url("${spriteUrl(lookSpriteKey(look))}")` : '' }),
      h('div', {}, h('div', { class: 'slotName' }, SPRING_SLOT), h('div', { class: 'slotInfo' }, look ? lookLabel(look) : 'None: it flies')),
      h('div', { class: 'slotActions' }, look ? h('button', { title: 'Take the spring off: the drone flies again', onclick: ev => { ev.stopPropagation(); setLook(''); } }, 'Clear') : null)));
  }
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
function pivotRow(key) {
  const pv = (S.project.bonePivots || {})[key];
  const set = (i, v) => { const cur = (S.project.bonePivots || {})[key] || [0, 0]; const next = cur.slice(); next[i] = v; S.project.bonePivots = { ...(S.project.bonePivots || {}), [key]: next }; touch(); };
  const input = i => h('input', { type: 'number', step: 1, value: pv ? pv[i] : 0, 'aria-label': `Pivot ${'xy'[i]}`, oninput: ev => set(i, num(ev.target.value, 0)) });
  return h('div', { class: 'pivotRow' },
    h('div', { class: 'row between' }, h('h4', {}, 'Rotation pivot'),
      h('span', { class: 'dim' }, pv ? 'its bone turns around this point' : 'the bone\'s own origin')),
    h('div', { class: 'row' },
      h('label', {}, 'X', input(0)), h('label', {}, 'Y', input(1)),
      h('button', { class: S.pivotEdit ? 'on' : '', title: 'Click or drag on the character to place the point this slot turns around',
        onclick: () => { S.pivotEdit = !S.pivotEdit; renderInspector(); } }, S.pivotEdit ? 'Done placing' : 'Place on canvas'),
      pv ? h('button', { title: 'Turn around the bone\'s own origin again', onclick: () => { const n = { ...S.project.bonePivots }; delete n[key]; S.project.bonePivots = Object.keys(n).length ? n : undefined; if (!S.project.bonePivots) delete S.project.bonePivots; touch(); renderInspector(); } }, 'Reset') : null));
}
function layerList(key, slot) {
  // Top of the list draws on top.
  return h('div', { class: 'layers' }, [...slot.layers].reverse().map(layer => h('div', { class: 'layerRow' + (layer === activeLayer(key) ? ' on' : '') + (layer.hidden ? ' off' : ''), 'data-slot': key, 'data-layer': layer.uid,
    title: 'Drop art here to swap this layer\'s picture and keep its placement', onclick: () => { slot.active = layer.uid; renderInspector(); } },
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
  if (S.selected === SPRING_SLOT && hasLooks()) {
    box.replaceChildren();
    const current = rigLooks()[S.project.variant] ? S.project.variant : '';
    box.append(h('div', { class: 'row between' }, h('h3', {}, SPRING_SLOT), h('span', { class: 'dim' }, 'Bounces on a spring instead of flying')));
    box.append(h('div', { class: 'row wrap' },
      ...Object.keys(rigLooks()).map(name => h('button', { class: name === current ? 'on' : '', title: 'Ride this spring', onclick: () => setLook(name) },
        h('img', { src: spriteUrl(lookSpriteKey(name)), alt: '', style: 'height:34px;vertical-align:middle;margin-right:6px' }), lookLabel(name))),
      h('button', { class: current ? '' : 'on', title: 'No spring: the drone flies', onclick: () => setLook('') }, 'None')));
    return;
  }
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
  if (slotBone(S.selected) && S.mode !== 'projectile') box.append(pivotRow(S.selected));
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
  const params = paramsOf(ability);
  const stats = levelStats();
  const combo = comboValues(ability, stats);
  return text => (text || '').replace(/\{[a-z0-9_]+\.[a-z0-9_]+\}/g, m => combo[m] !== undefined ? combo[m] : m).replace(/\{value_\d+\}/g, m => {
    const v = values[m];
    if (!v || !v.expr) return m;
    const result = evalFormula(v.expr, params, stats, durationOf(ability));
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
    if (num(paramsOf(a).charge) > 0) tip.append(h('div', { class: 'ttCharge', style: `left:${X(0) - 73}px;top:${Y(sy + 23) - 6}px;width:146px` }));
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
function pickProjectile(current, usual, onPick) {
  const modal = $('modal');
  modal.className = 'wide';
  const search = h('input', { type: 'search', class: 'grow', placeholder: 'Search projectiles', oninput: render });
  const grid = h('div', { class: 'movePick' });
  const choose = name => { modal.close(); onPick(name); };
  function render() {
    const q = search.value.trim().toLowerCase();
    const names = (S.projectiles || []).filter(n => !q || n.toLowerCase().includes(q));
    grid.replaceChildren(
      ...(!q ? [h('button', { class: !current ? 'on' : '', onclick: () => choose('') }, h('span'), h('span', {}, usual, h('small', {}, 'no projectile of its own')))] : []),
      ...names.map(n => h('button', { class: n === current ? 'on' : '', onclick: () => choose(n) },
        h('img', { src: `/projectileicon/${encodeURIComponent(n)}`, alt: '', loading: 'lazy' }), h('span', {}, projectileLabel(n), h('small', {}, projectileOwner(n))))));
    if (!names.length && q) grid.append(h('p', { class: 'dim' }, 'No projectile matches.'));
  }
  modal.replaceChildren(h('div', { class: 'row between' }, h('h3', {}, 'Choose a projectile'), h('button', { onclick: () => modal.close() }, 'Cancel')),
    h('div', { class: 'row', style: 'margin-top:8px' }, search), grid);
  render();
  modal.showModal();
  search.focus();
  const on = grid.querySelector('.on');
  if (on) on.scrollIntoView({ block: 'center' });
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
  // A picker dialog, not a native dropdown: 50+ entries opened low in the window ran off the screen.
  const select = h('button', { class: 'pickBtn grow', 'aria-label': 'Projectile for this ability', title: 'Choose the projectile',
    onclick: () => pickProjectile(current, usual, name => { a.projectile = name; touch(); renderAbilities(); playAbility(a); }) },
    current ? projectileLabel(current) : usual, h('span', { class: 'dim' }, ' ▾'));
  const shown = current || (w.isRanged && w.projectile && (S.projectiles || []).includes(w.projectile) ? w.projectile : '');
  rows.push(h('div', { class: 'row animRow projectileDrop', title: 'Drag a projectile from the library\'s Projectiles onto this attack' },
    shown ? h('img', { src: `/projectileicon/${encodeURIComponent(shown)}`, alt: '' }) : null,
    h('label', {}, 'Projectile', select)));
  const clip = abilityClip(a) || defaultClip(a);
  const tags = (S.rig.clips.find(c => c.name === clip) || { tags: [] }).tags || [];
  if (current) rows.push(h('span', { class: 'dim note' }, `Fires ${projectileLabel(current)} ${tags.includes('Projectile') ? `on ${clip}'s Projectile event` : `halfway through ${clip}, which has no Projectile event`}.`));
  if (shown) rows.push(h('label', { class: 'check', title: 'The ability\'s Impact effects (Effects section) play when the projectile reaches its target, instead of on the clip\'s Impact event' },
    h('input', { type: 'checkbox', checked: impactOnHit(a), onchange: ev => { a.impactOnHit = ev.target.checked; touch(); renderAbilities(); playAbility(a); } }),
    ' Projectile hit counts as the Impact event'));
  return rows;
}
const ABILITY_TYPES = ['Attack', 'Support', 'Passive', 'Combo'];

/* Combo moves (SkillTargetTypes.Combo): the move only signs up; at the end of the side's turn ComboMoveDirector
   resolves the moves into recipes, rows combo_<sorted combo ids joined with _>. The move's row AssetId is its combo id. */
function comboRecipes(id) {
  if (!id) return [];
  return S.skills.filter(s => s.recipe && s.id.slice('combo_'.length).split('_').includes(id))
    .map(s => ({ ...s, ids: s.id.slice('combo_'.length).split('_') }))
    .sort((x, y) => x.ids.length - y.ids.length || x.id.localeCompare(y.id));
}
function knownComboIds() {
  const ids = new Set();
  for (const s of S.skills) {
    if (s.type === 'Combo' && !s.recipe && s.asset) ids.add(s.asset.toLowerCase());
    if (s.recipe) s.id.slice('combo_'.length).split('_').forEach(x => ids.add(x));
  }
  return [...ids].sort();
}
// {<recipe ids>.<param>} and {<recipe ids>.damage} in a combo move's tooltip, as ComboRecipe fills them in.
function comboValues(ability, stats) {
  const out = {};
  if (ability.type !== 'Combo') return out;
  for (const r of comboRecipes(ability.comboId)) {
    const key = r.ids.join('_');
    for (const [k, v] of Object.entries(r.params || {})) out[`{${key}.${k}}`] = round(v);
    if (r.params && r.params.damage_in_percent !== undefined) out[`{${key}.damage}`] = Math.round(r.params.damage_in_percent / 100 * stats.attack);
  }
  return out;
}
// Every combo move a recipe can use: the game's (with icons) and this combatant's own, by combo id.
function comboMoves() {
  const out = new Map();
  for (const a of S.project.abilities) if (a.type === 'Combo' && a.comboId) out.set(a.comboId, { id: a.comboId, name: a.name || a.comboId, atlas: a.atlas, icon: a.icon, own: true });
  for (const s of S.skills) {
    if (s.type !== 'Combo' || s.recipe || !s.asset) continue;
    const id = s.asset.toLowerCase();
    if (!out.has(id)) out.set(id, { id, name: s.name, atlas: s.atlas, icon: s.icon });
  }
  return [...out.values()].sort((x, y) => x.name.localeCompare(y.name));
}
const recipeKey = ids => 'combo_' + [...ids].map(x => x.toLowerCase()).sort().join('_');
// A game recipe's description as plain text, numbers filled in.
function recipePlainText(row) {
  return (row.desc || '').replace(/\[[0-9a-fA-F]{6}\]|\[-\]/g, '').replace(/\{([a-z_]+)\}/g, (m, k) => (row.params || {})[k] !== undefined ? round(row.params[k]) : m);
}
// When a recipe's moves make a combination the game already has, that recipe is filled in: its description becomes the
// starting text for Claude. Only text filled in this way is replaced when the moves change again; your own stays.
function syncRecipe(r) {
  const row = S.skills.find(x => x.id === recipeKey(r.ids));
  const auto = !r.design || r.design === r.autoDesign;
  if (row) {
    r.source = row.id;
    if (auto) r.design = r.autoDesign = recipePlainText(row);
  } else {
    delete r.source;
    if (auto && r.autoDesign) { r.design = ''; delete r.autoDesign; }
  }
  delete r.written;
}
// The recipes a combo ability lists: what it was given, else the game's recipes that use its combo id.
function abilityRecipes(a) {
  if (!a.recipes) a.recipes = comboRecipes(a.comboId).map(r => { const x = { uid: newUid(), ids: [...r.ids], design: '' }; syncRecipe(x); return x; });
  return a.recipes;
}
function pickComboMove(onPick) {
  const modal = $('modal');
  modal.className = 'wide';
  const search = h('input', { type: 'search', class: 'grow', placeholder: 'Search combo moves', oninput: render });
  const grid = h('div', { class: 'movePick' });
  function render() {
    const q = search.value.trim().toLowerCase();
    const moves = comboMoves().filter(m => !q || m.name.toLowerCase().includes(q) || m.id.includes(q));
    grid.replaceChildren(...moves.map(m => h('button', { onclick: () => { modal.close(); onPick(m.id); } },
      m.atlas && m.icon ? h('img', { src: iconUrl(m.atlas, m.icon), alt: '' }) : h('span'),
      h('span', {}, m.name, m.own ? h('span', { class: 'dim' }, ' (this combatant)') : null, h('small', {}, m.id)))));
    if (!moves.length) grid.append(h('p', { class: 'dim' }, 'No combo move matches.'));
  }
  modal.replaceChildren(h('div', { class: 'row between' }, h('h3', {}, 'Add a combo move'), h('button', { onclick: () => modal.close() }, 'Cancel')),
    h('div', { class: 'row', style: 'margin-top:8px' }, search), grid);
  render();
  modal.showModal();
  search.focus();
}
function recipeCard(a, r) {
  const byId = Object.fromEntries(comboMoves().map(m => [m.id, m]));
  const rid = recipeKey(r.ids);
  const inGame = S.skills.find(s => s.id === rid);
  const dupe = abilityRecipes(a).filter(x => recipeKey(x.ids) === rid).length > 1;
  const changed = () => { touch(); renderAbilities(); renderTooltip(); };
  const chip = (id, k) => {
    const m = byId[id] || { id, name: id };
    return h('span', { class: 'moveChip' + (id === a.comboId ? ' own' : ''), title: m.name + ' (' + id + ')' },
      m.atlas && m.icon ? h('img', { src: iconUrl(m.atlas, m.icon), alt: '' }) : null, m.name,
      h('button', { title: 'Take this move out of the combination', 'aria-label': 'Remove ' + m.name, onclick: () => { r.ids.splice(k, 1); syncRecipe(r); changed(); } }, '×'));
  };
  const design = h('textarea', { 'aria-label': 'What this combination does', placeholder: 'What this combination does. Claude builds the recipe ' + rid + ' from it.', value: r.design || '',
    oninput: ev => { r.design = ev.target.value; if (r.design !== r.autoDesign) delete r.autoDesign; touch(); build.disabled = !r.design.trim() || !S.claude || !r.ids.length; } });
  const build = h('button', { disabled: !(r.design || '').trim() || !S.claude || !r.ids.length, title: S.claude ? 'Runs Claude Code to write this recipe' : 'Claude Code was not found on this machine',
    onclick: () => createRecipe(a, r) }, inGame ? 'Rewrite with Claude' : 'Create with Claude');
  const status = r.written ? ['done', 'Written by Claude'] : inGame ? ['', 'In the game: filled in below'] : ['pending', 'Not in the game yet: it does nothing until Claude writes it'];
  return h('div', { class: 'recipeCard' },
    h('div', { class: 'row between' }, h('strong', { class: 'mono' }, rid), h('span', { class: `status ${status[0]}` }, status[1]),
      h('button', { class: 'iconBtn', title: 'Take this recipe off the list (a recipe already in the game stays there)', 'aria-label': 'Remove recipe', onclick: () => { a.recipes = abilityRecipes(a).filter(x => x !== r); changed(); } }, '×')),
    h('div', { class: 'recipeMoves' }, ...r.ids.map(chip),
      h('button', { title: 'Add a combo move to this combination', onclick: () => pickComboMove(id => { r.ids.push(id); syncRecipe(r); changed(); }) }, '+ Move')),
    !r.ids.length ? h('span', { class: 'warnNote' }, 'Add the moves that make this combination.') : null,
    r.ids.length && a.comboId && !r.ids.includes(a.comboId) ? h('span', { class: 'warnNote' }, `This combination doesn't use ${a.name || a.comboId}.`) : null,
    dupe ? h('span', { class: 'warnNote' }, 'Another recipe here has the same moves: they are the same recipe in the game.') : null,
    design, h('div', { class: 'row' }, build));
}
function comboBox(a) {
  if (a.type !== 'Combo') return null;
  const listId = 'comboIds-' + a.uid;
  const recipes = abilityRecipes(a);
  return h('div', { class: 'comboBox' },
    h('div', { class: 'row' }, h('label', { class: 'grow' }, 'Combo id',
      h('input', { class: 'mono grow', value: a.comboId || '', list: listId, title: 'Moves with the same combo id combine the same way; recipes are named after the ids',
        onchange: ev => { a.comboId = slugify(ev.target.value); touch(); renderAbilities(); renderTooltip(); } }),
      h('datalist', { id: listId }, ...knownComboIds().map(id => h('option', { value: id }))))),
    h('span', { class: 'dim' }, 'Recipes: what happens when these moves are used in the same turn (alone, or with other pigs\' moves).'),
    h('div', { class: 'recipes' }, ...recipes.map(r => recipeCard(a, r))),
    h('div', { class: 'row' }, h('button', { onclick: () => { const r = { uid: newUid(), ids: a.comboId ? [a.comboId] : [], design: '' }; syncRecipe(r); recipes.push(r); touch(); renderAbilities(); } }, '+ Add recipe')));
}

// Animation, reach and projectile live in a section each ability can open or close; the summary keeps warnings in view.
/* An ability's attack effects (Root.unity VisualEffectsBalancing, keyed by the skill's AssetId, which other skills share).
   Editing them gives the ability its own list (a.effects), written to Resources/ForgeSkillEffects/<skill>.json on
   upload, which ForgeSkillEffects.cs uses in place of the AssetId's. */
const FX_TIMINGS = [['Start', 'When it starts'], ['Movement', 'Movement event'], ['Impact', 'Impact event'], ['Affected', 'A target is affected'], ['Triggered', 'Its effect triggers'],
  ['SlowMotion', 'Slow motion starts'], ['ProjectileLaunch', 'Projectile is fired'], ['ProjectileHit', 'Projectile hits'], ['ProjectileBounce', 'Projectile bounces'], ['ProjectileEnd', 'Projectile stops']];
const FX_TARGETS = [['Origin', 'The attacker'], ['Target', 'Each target'], ['OriginArea', 'Attacker\'s side'], ['TargetArea', 'Targets\' side'], ['CenterArea', 'Between the sides'],
  ['GlobalCamera', 'Camera'], ['GlobalScreen', 'Whole screen'], ['SlowMotion', 'Slow motion'], ['Projectile', 'At the projectile']];
const FX_ANCHORS = [['Bottom', 'Feet'], ['Center', 'Body centre'], ['MainHand', 'Main hand'], ['OffHand', 'Off hand']];
const abilityFxCache = new Map();
const abilityFxKey = a => `${a.nameId || a.source || ''}|${abilityAsset(a)}`;
function loadAbilityFx(a) {
  const key = abilityFxKey(a);
  if (!abilityFxCache.has(key)) {
    const entry = { list: null };
    abilityFxCache.set(key, entry);
    const [skill, asset] = key.split('|');
    api(`/api/abilityfx/${encodeURIComponent(skill || '_')}/${encodeURIComponent(asset || '_')}`)
      .then(list => { entry.list = list || []; if (!$('tab-abilities').hidden) renderAbilities(); })
      .catch(() => { entry.list = []; });
  }
  return abilityFxCache.get(key);
}
// The effects the ability plays: its own edited list, else its skill's (all loaded at start: S.allFx; null while loading).
function effectsFor(a) {
  if (a.effects) return a.effects;
  if (S.allFx) {
    const own = S.allFx.skills[a.nameId || a.source || ''];
    return own || S.allFx.assets[abilityAsset(a)] || [];
  }
  return loadAbilityFx(a).list;
}
// Fetch the pictures of an ability's effects ahead of a preview, so the first play shows them.
function prefetchFx(a) {
  for (const e of effectsFor(a) || []) if (e.prefab && !/^Camera_Shake/i.test(e.prefab)) loadFx(e.prefab);
}
let fxNames = null;
function loadFxNames() {
  if (!fxNames) { fxNames = []; api('/api/fx_names').then(list => { fxNames = list; }).catch(() => {}); }
  return fxNames;
}
const fxLabel = name => (name || '').replace(/^(SkillFX_|SKillFX_|ScreenFX_)/, '').replace(/_/g, ' ');
function pickEffect(current, onPick) {
  const modal = $('modal');
  modal.className = 'wide';
  const search = h('input', { type: 'search', class: 'grow', placeholder: 'Search effects', oninput: render });
  const list = h('div', { class: 'fxPick' });
  const choose = name => { modal.close(); onPick(name); };
  function render() {
    const q = search.value.trim().toLowerCase();
    const names = loadFxNames().filter(n => !q || n.name.toLowerCase().includes(q) || n.group.toLowerCase().includes(q));
    const groups = [...new Set(names.map(n => n.group))];
    const special = [['__slow', 'Slow motion', 'slows the whole battle down for a moment'], ['Forge_AfterImages', 'After-images', 'fading copies of the combatant'],
      ['SkillFX_Generic_Text', 'Text', 'a word that pops up (your own text, colour, size, arc)']].filter(([n, l, d]) => !q || (l + ' ' + d).toLowerCase().includes(q));
    list.replaceChildren(
      ...(special.length ? [h('h4', {}, 'Time, trails & text'), h('div', { class: 'fxPickGroup' },
        ...special.map(([n, l, d]) => h('button', { class: n === current ? 'on' : '', title: d, onclick: () => choose(n) }, l)))] : []),
      ...groups.flatMap(g => [h('h4', {}, g), h('div', { class: 'fxPickGroup' },
      ...names.filter(n => n.group === g).map(n => h('button', { class: n.name === current ? 'on' : '', title: n.name, onclick: () => choose(n.name) }, fxLabel(n.name))))]));
    if (!names.length) list.append(h('p', { class: 'dim' }, q ? 'No effect matches.' : 'Loading…'));
  }
  modal.replaceChildren(h('div', { class: 'row between' }, h('h3', {}, 'Choose an effect'), h('button', { onclick: () => modal.close() }, 'Cancel')),
    h('div', { class: 'row', style: 'margin-top:8px' }, search), list);
  render();
  if (!fxNames || !fxNames.length) setTimeout(render, 600);
  modal.showModal();
  search.focus();
  const on = list.querySelector('.on');
  if (on) on.scrollIntoView({ block: 'center' });
}
// What an effect can turn with: the bone it sits on (as the game does), nothing (stays upright), or any bone of the
// combatant (e.g. the flying pig's Body, so its airflow tilts with it).
function turnOptions() {
  const names = [...new Set((S.rig ? S.rig.paths : []).map(p => p.split('/').pop()).filter(n => n && !/_Sprite$/.test(n)))];
  return [['anchor', 'Its anchor bone'], ['upright', 'Nothing (stays upright)'], ...names.map(n => ['bone:' + n, 'Bone: ' + n])];
}
const fxRowLabel = e => e.onHit ? 'Weapon hit effect' : e.target === 'SlowMotion' ? 'Slow motion' : e.prefab === 'Forge_AfterImages' ? 'After-images'
  : e.prefab === 'SkillFX_Generic_Text' ? `Text: "${(e.text && e.text.text) ?? e.textDefault ?? 'Immune!'}"` : fxLabel(e.prefab) || '— choose —';
// What a picked name becomes (the picker's Time & trails entries aren't prefabs).
function fxFromPick(name, keep) {
  if (name === '__slow') return { ...keep, prefab: '', target: 'SlowMotion', onHit: false, slow: { scale: 0.3, duration: 0.5, ease: 0.1, easeOut: 0.1 }, text: undefined, after: undefined };
  if (name === 'Forge_AfterImages') return { ...keep, prefab: name, target: 'Origin', onHit: false, after: { ...AFTER_DEFAULTS }, text: undefined, slow: undefined };
  const out = { ...keep, prefab: name, onHit: false, slow: undefined, after: undefined };
  if (name === 'SkillFX_Generic_Text') out.text = { ...TEXT_DEFAULTS, text: keep.textDefault || 'Wow!' };
  else out.text = undefined;
  if (/^Camera_Shake/.test(name)) out.target = 'GlobalCamera';
  else if (out.target === 'GlobalCamera' || out.target === 'SlowMotion') out.target = 'Target';
  return out;
}
// The settings of text, slow motion and after-images.
function fxExtraFields(e, i, edit) {
  const numField = (label, obj, key, def, step, min = 0) => h('label', {}, label, h('input', { type: 'number', step, min, value: (e[obj] || {})[key] ?? def,
    onchange: ev => edit(l => { l[i][obj] = { ...(l[i][obj] || {}), [key]: Math.max(min, num(ev.target.value, def)) }; }) }));
  const colorField = (label, obj, key, def) => h('label', {}, label, h('input', { type: 'color', value: '#' + String((e[obj] || {})[key] || def).replace('#', ''),
    oninput: ev => edit.quiet(l => { l[i][obj] = { ...(l[i][obj] || {}), [key]: ev.target.value.slice(1) }; }) }));
  if (e.target === 'SlowMotion') {
    return h('div', { class: 'row wrap fxFields fxExtra' },
      numField('Time scale (1 = normal)', 'slow', 'scale', 0.3, 0.05, 0.01), numField('Lasts s', 'slow', 'duration', 0.5, 0.05), numField('Ease in s', 'slow', 'ease', 0.1, 0.05),
      numField('Ease out s', 'slow', 'easeOut', (e.slow || {}).ease ?? 0.1, 0.05));
  }
  if (e.prefab === 'Forge_AfterImages') {
    const af = { ...AFTER_DEFAULTS, ...(e.after || {}) };
    return h('div', { class: 'row wrap fxFields fxExtra' },
      numField('Leaves them for s', 'after', 'duration', AFTER_DEFAULTS.duration, 0.05), numField('One every s', 'after', 'interval', AFTER_DEFAULTS.interval, 0.01, 0.02),
      numField('Each lasts s', 'after', 'life', AFTER_DEFAULTS.life, 0.05, 0.02), numField('Opacity (0-1)', 'after', 'opacity', AFTER_DEFAULTS.opacity, 0.05),
      colorField('Tint', 'after', 'tint', AFTER_DEFAULTS.tint),
      numField('Spread', 'after', 'spread', 0, 1), numField('Moves forward /s', 'after', 'driftX', 0, 10, -9999), numField('Moves up /s', 'after', 'driftY', 0, 10, -9999),
      numField('Grows to ×', 'after', 'grow', 1, 0.05, 0.05),
      h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: af.behind !== false, onchange: ev => edit(l => { l[i].after = { ...af, behind: ev.target.checked }; }) }), ' Behind the combatant'));
  }
  if (e.prefab === 'SkillFX_Generic_Text') {
    const tx = { ...TEXT_DEFAULTS, ...(e.text || { text: e.textDefault || TEXT_DEFAULTS.text, outline: '' }) };
    return h('div', { class: 'row wrap fxFields fxExtra' },
      h('label', { class: 'grow' }, 'Text', h('input', { type: 'text', value: tx.text, maxlength: 40, onchange: ev => edit(l => { l[i].text = { ...tx, text: ev.target.value }; }) })),
      colorField('Colour', 'text', 'color', tx.color),
      h('label', {}, 'Outline', h('span', { class: 'row' },
        h('input', { type: 'color', value: '#' + (tx.outline || '000000'), disabled: !tx.outline, oninput: ev => edit.quiet(l => { l[i].text = { ...l[i].text, ...{ ...tx, ...l[i].text }, outline: ev.target.value.slice(1) }; }) }),
        h('input', { type: 'checkbox', checked: !!tx.outline, title: 'Draw an outline', onchange: ev => edit(l => { l[i].text = { ...tx, outline: ev.target.checked ? '000000' : '' }; }) }))),
      numField('Size ×', 'text', 'size', tx.size, 0.1, 0.1), numField('Rotation °', 'text', 'rotation', tx.rotation, 5, -360), numField('Arc °', 'text', 'arc', tx.arc, 5, -360));
  }
  return null;
}
function effectsSection(a) {
  if (a.type !== 'Attack' && a.type !== 'Support') return null;
  loadFxNames();
  const list = effectsFor(a);
  // the first edit gives the ability its own copy of the list
  const edit = fn => { if (!a.effects) a.effects = JSON.parse(JSON.stringify(effectsFor(a) || [])); fn(a.effects); touch(); renderAbilities(); };
  // colour pickers change without redrawing the list, which would close them
  edit.quiet = fn => { if (!a.effects) a.effects = JSON.parse(JSON.stringify(effectsFor(a) || [])); fn(a.effects); touch(); };
  const sel = (opts, value, label, onchange) => h('select', { 'aria-label': label, onchange: ev => onchange(ev.target.value) },
    ...opts.map(([v, l]) => h('option', { value: v, selected: v === value }, l)));
  const rows = (list || []).map((e, i) => h('div', { class: 'fxRow' },
    h('button', { class: 'pickBtn grow', title: e.onHit ? 'The weapon\'s hit effect (' + (e.onHitPrefix || '') + '…)' : e.prefab, onclick: () => pickEffect(e.target === 'SlowMotion' ? '__slow' : e.prefab, name => edit(l => {
      l[i] = fxFromPick(name, l[i]);
    })) }, fxRowLabel(e), h('span', { class: 'dim' }, ' ▾')),
    h('div', { class: 'row wrap fxFields' },
      h('label', {}, 'When', sel(FX_TIMINGS, e.timing, 'When it plays', v => edit(l => { l[i].timing = v; }))),
      h('label', {}, 'Delay s', h('input', { type: 'number', step: 0.05, min: 0, value: e.delay || 0, onchange: ev => edit(l => { l[i].delay = Math.max(0, num(ev.target.value, 0)); }) })),
      h('label', {}, 'Where', sel(FX_TARGETS, e.target, 'Where it plays', v => edit(l => { l[i].target = v; }))),
      h('label', {}, 'On', sel(FX_ANCHORS, e.anchor, 'Anchored on', v => edit(l => { l[i].anchor = v; }))),
      h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: !!e.doNotMirror, onchange: ev => edit(l => { l[i].doNotMirror = ev.target.checked; }) }), ' Not mirrored'),
      ...(e.target === 'SlowMotion' || e.prefab === 'Forge_AfterImages' ? [] : [
        h('label', {}, 'X offset', h('input', { type: 'number', step: 5, value: e.offsetX || 0, title: 'Moved this far; positive is toward the enemy', onchange: ev => edit(l => { l[i].offsetX = num(ev.target.value, 0); }) })),
        h('label', {}, 'Y offset', h('input', { type: 'number', step: 5, value: e.offsetY || 0, title: 'Moved this far up', onchange: ev => edit(l => { l[i].offsetY = num(ev.target.value, 0); }) })),
        h('label', {}, 'Turn °', h('input', { type: 'number', step: 5, value: e.turn || 0, onchange: ev => edit(l => { l[i].turn = num(ev.target.value, 0); }) })),
        h('label', {}, 'Turns with', sel(turnOptions(), e.turnWith === 'bone' ? 'bone:' + (e.bone || '') : (e.turnWith || 'anchor'), 'What it turns with',
          v => edit(l => { if (v.startsWith('bone:')) { l[i].turnWith = 'bone'; l[i].bone = v.slice(5); } else { l[i].turnWith = v; delete l[i].bone; } })))]),
      h('button', { class: 'iconBtn', title: 'Remove this effect', 'aria-label': 'Remove effect', onclick: () => edit(l => l.splice(i, 1)) }, '✕')),
    fxExtraFields(e, i, edit)));
  const own = !!a.effects;
  const usual = S.allFx ? (S.allFx.skills[a.nameId || a.source || ''] || S.allFx.assets[abilityAsset(a)] || []) : loadAbilityFx(a).list;
  if (S.fxOpen && S.fxOpen.has(a.uid)) prefetchFx(a);
  const sum = list ? (list.length ? list.map(fxRowLabel).join(', ') : 'none') : 'loading…';
  S.fxOpen = S.fxOpen || new Set();
  const box = h('details', { class: 'motion', open: S.fxOpen.has(a.uid), 'data-tongue': 'Effects' },
    h('summary', {}, h('span', {}, 'Effects'), h('span', { class: 'sumText dim' }, (own ? 'own · ' : '') + sum)),
    h('div', {},
      h('p', { class: 'dim note' }, own ? 'This ability\'s own effects: uploading gives it its own skill so others sharing its effects keep theirs.'
        : `The skill's effects (${abilityAsset(a) || 'none'}), shared with other skills. Editing one gives this ability its own list.`),
      ...rows,
      h('div', { class: 'row' },
        h('button', { onclick: () => pickEffect('', name => edit(l => l.push(fxFromPick(name, { timing: 'Impact', delay: 0, target: 'Target',
          anchor: 'Center', prefab: '', doNotMirror: false, onHit: false, onHitPrefix: '', raw: {} })))) }, '+ Add effect'),
        h('button', { onclick: () => playAbility(a), title: 'Play the ability with these effects' }, '▶ Preview'),
        own ? h('button', { onclick: () => { delete a.effects; touch(); renderAbilities(); }, title: 'Go back to the skill\'s usual effects' + (usual ? ` (${usual.length})` : '') }, 'Use the skill\'s usual effects') : null)));
  box.addEventListener('toggle', () => { if (box.open) { S.fxOpen.add(a.uid); prefetchFx(a); } else S.fxOpen.delete(a.uid); });
  return box;
}

/* An ability's numbers: its skill's parameters (damage %, chance, charge turns...) and how many turns its effect lasts.
   Edits sit in a.paramEdits / a.durationEdit over the skill's own (a.params, refreshed from the game on load), and
   go into its skill row on upload (publish.build_skills), making it this combatant's own copy of a shared skill. */
const HIDDEN_PARAMS = new Set(['ui_color_rgb', 'anim_clip', 'anim_projectile', 'anim_ranged', 'anim_impact_on_hit']);
// "Projectile hit counts as the Impact event": the skill's Impact effects play when its projectile lands (the game:
// skill parameter anim_impact_on_hit, AttackSkillTemplate / SkillBattleDataBase.SpawnProjectile).
const impactOnHit = a => a.impactOnHit ?? !!(a.params || {}).anim_impact_on_hit;
const paramsOf = a => { const out = { ...(a.params || {}), ...(a.paramEdits || {}) }; for (const k of Object.keys(out)) if (out[k] === null) delete out[k]; return out; };
const durationOf = a => a.durationEdit ?? a.duration;
const PARAM_LABELS = {
  damage_in_percent: 'Damage (% of attack)', chance: 'Chance %', charge: 'Charge turns', charge_min: 'Charge turns at least', all: 'Hits everyone (1 = yes)',
  attack_count: 'Hits (attack count)', heal_in_percent: 'Heal %', increase_in_percent: 'Increase %', decrease_in_percent: 'Decrease %', ragereduce: 'Rage reduced',
  extra_damage_in_percent_on_target: 'Extra damage % on the aimed target', additional_enemies_affected: 'More enemies hit', damage_in_percent_dot: 'Damage over time %',
};
const paramLabel = k => PARAM_LABELS[k] || k.replace(/_in_percent/g, ' %').replace(/_/g, ' ').replace(/^\w/, c => c.toUpperCase());
// Values every skill of a type can have, even when its skill doesn't set them (the game reads them for any skill:
// SkillBattleDataBase / AttackSkillTemplate). For these 0 means "not set": they are taken off the skill.
const BASIC_PARAMS = { Attack: ['charge', 'charge_min', 'all', 'attack_count', 'extra_damage_in_percent_on_target'], Support: ['charge', 'charge_min'] };
const PRESENCE_PARAMS = new Set(['charge', 'charge_min', 'all', 'attack_count', 'extra_damage_in_percent_on_target']);
function valuesSection(a) {
  const params = paramsOf(a), own = a.params || {};
  const keys = Object.keys(params).filter(k => !HIDDEN_PARAMS.has(k));
  for (const k of BASIC_PARAMS[a.type] || []) if (!keys.includes(k)) keys.push(k);
  const hasDuration = num(a.duration) > 0 || a.durationEdit != null;
  if (!keys.length && !hasDuration) return null;
  // which tooltip placeholders read each number
  const readers = {};
  for (const [ph, v] of Object.entries(a.values || {})) for (const m of (v.expr || '').matchAll(/p\("(\w+)"\)/g)) (readers[m[1]] = readers[m[1]] || []).push(ph);
  if (/\bD\b/.test(Object.values(a.values || {}).map(v => v.expr || '').join(' '))) readers.__duration = Object.entries(a.values || {}).filter(([, v]) => /\bD\b/.test(v.expr || '')).map(([k]) => k);
  const changed = () => { touch(); renderTooltip(); syncSummary(); };
  const edited = k => k === '__duration' ? a.durationEdit != null : !!a.paramEdits && k in a.paramEdits;
  const setParam = (k, v) => {
    const orig = own[k];
    a.paramEdits = a.paramEdits || {};
    if (PRESENCE_PARAMS.has(k) && !v) { if (orig === undefined) delete a.paramEdits[k]; else a.paramEdits[k] = null; }
    else if (v === orig) delete a.paramEdits[k];
    else a.paramEdits[k] = v;
    if (!Object.keys(a.paramEdits).length) delete a.paramEdits;
  };
  const field = (key, label, value, original, set, step) => {
    const notSet = original === undefined && !edited(key);
    const input = h('input', { type: 'number', step, value, 'aria-label': label, placeholder: notSet ? 'not set' : '',
      oninput: ev => { set(ev.target.value === '' ? 0 : num(ev.target.value, 0)); row.classList.toggle('edited', edited(key)); reset.hidden = !edited(key); changed(); } });
    const reset = h('button', { class: 'iconBtn', title: original === undefined ? 'Back to not set' : `Back to the skill's ${round(original)}`, 'aria-label': 'Reset ' + label, hidden: !edited(key),
      onclick: () => { if (key === '__duration') delete a.durationEdit; else { if (a.paramEdits) delete a.paramEdits[key]; if (a.paramEdits && !Object.keys(a.paramEdits).length) delete a.paramEdits; }
        input.value = original ?? 0; row.classList.remove('edited'); reset.hidden = true; changed(); } }, '↺');
    const used = readers[key];
    const row = h('label', { class: 'valueRow' + (edited(key) ? ' edited' : '') + (notSet ? ' unset' : ''), title: key + (used ? ' · shown in the tooltip as ' + used.join(', ') : '') + (PRESENCE_PARAMS.has(key) ? ' · 0 = not set' : '') },
      h('span', {}, label, used ? h('small', { class: 'dim' }, ' ' + used.join(' ')) : null), h('span', { class: 'row' }, input, reset));
    return row;
  };
  const rows = keys.map(k => field(k, paramLabel(k), params[k] ?? 0, own[k], v => setParam(k, v), Math.abs(params[k] ?? 0) >= 10 ? 1 : k.startsWith('charge') || k === 'all' || k === 'attack_count' ? 1 : 0.1));
  if (hasDuration) rows.push(field('__duration', 'Effect lasts (turns)', durationOf(a), a.duration, v => { if (v === a.duration) delete a.durationEdit; else a.durationEdit = Math.max(0, Math.round(v)); }, 1));
  const summaryText = () => {
    const n = Object.keys(a.paramEdits || {}).length + (a.durationEdit != null ? 1 : 0);
    const shown = keys.filter(k => paramsOf(a)[k] != null);
    return (n ? `${n} edited · ` : '') + shown.slice(0, 3).map(k => `${paramLabel(k)} ${round(paramsOf(a)[k])}`).join(' · ') + (shown.length > 3 ? ' …' : '');
  };
  const sum = h('span', { class: 'sumText dim' }, summaryText());
  const syncSummary = () => { sum.textContent = summaryText(); };
  S.valuesOpen = S.valuesOpen || new Set();
  const box = h('details', { class: 'motion', open: S.valuesOpen.has(a.uid), 'data-tongue': 'Values' },
    h('summary', {}, h('span', {}, 'Values'), sum),
    h('div', {},
      h('p', { class: 'dim note' }, 'The ability\'s numbers. Edited ones (marked) go into its skill when you upload; a skill other combatants share becomes this one\'s own copy. Charge and the like work on any skill: 0 leaves them off.'),
      h('div', { class: 'valueGrid' }, ...rows)));
  box.addEventListener('toggle', () => { if (box.open) S.valuesOpen.add(a.uid); else S.valuesOpen.delete(a.uid); });
  return box;
}

function motionSection(a) {
  if (a.type === 'Passive' || a.type === 'Combo' || !S.rig) return null;
  const rows = [animationRow(a), projectileRow(a)].flat().filter(Boolean);
  if (!rows.length) return null;
  const clip = abilityClip(a);
  const bits = [clip || `Usual clip (${defaultClip(a)})`];
  if (a.type === 'Attack') {
    bits.push(isRangedAttack(a) ? 'ranged' : 'melee');
    const proj = abilityProjectile(a);
    if (proj) bits.push(projectileLabel(proj));
  }
  const warn = clip && missingEvents(a, clip).length;
  S.motionOpen = S.motionOpen || new Set();
  const box = h('details', { class: 'motion', open: S.motionOpen.has(a.uid), 'data-tongue': 'Animation' },
    h('summary', {}, h('span', {}, a.type === 'Attack' ? 'Animation & reach' : 'Animation'), h('span', { class: 'sumText dim' }, bits.join(' · ')),
      warn ? h('span', { class: 'warnDot', title: 'Something in here needs a look' }, '⚠') : null),
    h('div', {}, ...rows));
  box.addEventListener('toggle', () => { if (box.open) S.motionOpen.add(a.uid); else S.motionOpen.delete(a.uid); });
  return box;
}

// Placeholders in the tooltip that read only parameters this ability doesn't set (they print 0), with the placeholders
// whose parameters it does set but the tooltip doesn't use: likely what was meant ({value_3} vs {value_5} on Lassie).
function unsetPlaceholders(a) {
  const values = a.values || {}, params = paramsOf(a);
  const refs = expr => [...new Set([...(expr || '').matchAll(/p\("(\w+)"\)/g)].map(m => m[1]))];
  const usesStat = expr => /\b[AHLDF]\b/.test(expr || '');
  const used = new Set((a.desc || '').match(/\{value_\d+\}/g) || []);
  const bad = [...used].filter(k => values[k] && values[k].expr && !usesStat(values[k].expr) && refs(values[k].expr).length && !refs(values[k].expr).some(r => r in params));
  if (!bad.length) return [];
  const unused = Object.keys(values).filter(k => !used.has(k) && refs(values[k].expr).some(r => r in params));
  return bad.map(k => ({ key: k, params: refs(values[k].expr), better: unused.map(u => ({ key: u, params: refs(values[u].expr).filter(r => r in params) })) }));
}
function placeholderWarnings(a) {
  return unsetPlaceholders(a).map(w => h('span', { class: 'warnNote' },
    `The tooltip's ${w.key} reads ${w.params.join(' / ')}, which this ability doesn't set, so it shows 0.`,
    w.better.length ? ` Did you mean ${w.better.map(b => `${b.key} (${b.params.join(', ')})`).join(' or ')}? ` : ' ',
    w.better.length === 1 ? h('button', { onclick: () => { a.desc = a.desc.split(w.key).join(w.better[0].key); touch(); renderAbilities(); renderTooltip(); } }, `Use ${w.better[0].key}`) : null));
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
        h('select', { 'aria-label': 'Ability type', onchange: ev => changeType(a, ev.target.value) }, ABILITY_TYPES.map(t => h('option', { selected: t === a.type }, t))),
        h('button', { class: 'danger', onclick: () => { S.project.abilities.splice(i, 1); touch(); renderAbilities(); renderTooltip(); renderStats(); } }, 'Remove')),
      h('span', { class: `status ${cls}` }, text),
      showsRawPercent(a) ? h('span', { class: 'warnNote' }, 'This tooltip prints damage as a percent. Rewrite it with Claude, or use the placeholder that holds the real number.') : null,
      ...placeholderWarnings(a),
      // A combo move's own Claude field comes first, then its recipes.
      ...(a.type === 'Combo'
        ? [valuesSection(a), design, h('div', { class: 'row' }, createBtn), comboBox(a)]
        : [valuesSection(a), motionSection(a), effectsSection(a), design, h('div', { class: 'row' }, createBtn)]));
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
  if (type === 'Combo' && !a.comboId) a.comboId = slugify(a.name) || 'combo';
  if (type !== 'Combo') delete a.comboId;
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
  const typeSel = h('select', { 'aria-label': 'Type', onchange: render }, h('option', { value: '' }, 'All types'), ...ABILITY_TYPES.map(t => h('option', {}, t)));
  function render() {
    const q = search.value.toLowerCase();
    const rows = S.skills.filter(s => !s.recipe && (!typeSel.value || s.type === typeSel.value) && (!q || s.id.includes(q) || s.name.toLowerCase().includes(q) || menuDescription(s).toLowerCase().includes(q))).slice(0, 120);
    listBox.replaceChildren(...rows.map(s => h('button', { class: 'pick', onclick: () => {
      addAbility({ uid: newUid(), type: s.type, source: s.id, name: s.name, desc: s.desc, color: s.color, atlas: s.atlas, icon: s.icon, params: s.params, values: s.values, template: s.template, duration: s.duration, design: '',
        ...(s.type === 'Combo' ? { comboId: (s.asset || '').toLowerCase() } : {}) });
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
  const presets = [['Attack', 'Attack'], ['Support', 'Support'], ['Passive', 'Passive'], ['Combo', 'Combo']].map(([k, label]) => h('button', { style: `background:#${S.colors[k]}`, onclick: () => { rgb = hexToRgb(S.colors[k]); hsv = rgbToHsv(rgb); paint(); } }, label));
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
  const attacks = actives.filter(a => a.type === 'Attack' || a.type === 'Combo'), supports = actives.filter(a => a.type === 'Support');
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
  box.append(h('div', { class: 'row' }, h('button', { onclick: () => openAddVariant(S.project.id) }, 'Add variant…'), h('button', { onclick: () => duplicateExisting(S.project.id) }, 'Duplicate')));
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
  setTimeout(syncExistingMode);
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
    const keep = $('grid').scrollTop; // opening redraws the library; the list stays where it was
    const { project, note } = await api('/api/existing_open', { id });
    await openProject(project);
    refreshCombatants(keep);
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
['existingBtn', 'importBtn', 'exportBtn', 'deleteBtn', 'duplicateBtn', 'variantBtn'].forEach(id => $(id).addEventListener('click', closeFileMenu));
$('duplicateBtn').addEventListener('click', () => { if (isExisting()) duplicateExisting(S.project.id); });
$('variantBtn').addEventListener('click', () => { if (isExisting()) openAddVariant(S.project.id); });

// A new variant: <parent>_<suffix>, a copy of the parent's row as the game has it, ticked for its edits. Its health,
// attack and scale are the parent's times the ratios; picking a suffix fills in what the game's variants of that kind
// usually have (bird3: 0.83x health, boss: 2.17x, ...).
async function openAddVariant(parentId) {
  if (!S.existing) await loadExisting();
  if (!S.variantPresets) { try { S.variantPresets = await api('/api/variant_presets'); } catch (e) { S.variantPresets = []; } }
  const parent = S.existing.pigs.find(x => x.id === parentId) || {};
  const original = (S.project.existing && S.project.existing.original && S.project.existing.original.stats) || S.project.stats || {};
  const base = { hp: parent.hp ?? original.hp, attack: parent.attack ?? original.attack, scale: original.sizeScale ?? 1 };
  const taken = new Set(S.existing.pigs.map(x => x.id));
  const free = sfx => !taken.has(`${parentId}_${sfx}`);
  const presets = S.variantPresets;
  const presetFor = sfx => presets.find(p => p.suffix === sfx);
  let n = 2;
  while (!free('v' + n)) n++;
  const first = (presets.find(p => free(p.suffix) && /^bird\d+$/.test(p.suffix)) || { suffix: 'v' + n }).suffix;
  let manual = false;
  const modal = $('modal');
  modal.className = '';
  const input = h('input', { class: 'mono grow', value: first, maxlength: 30, 'aria-label': 'Variant suffix', oninput: () => check(true) });
  const ratio = {}, result = {};
  const ratioField = (key, label) => {
    ratio[key] = h('input', { type: 'number', min: 0.01, step: 0.01, value: 1, 'aria-label': label + ' ratio', oninput: () => { manual = true; check(false); } });
    result[key] = h('span', { class: 'dim mono' });
    return h('label', {}, label + ' ×', ratio[key], result[key]);
  };
  const fields = h('div', { class: 'fields variantRatios' }, ratioField('hp', 'Health'), ratioField('attack', 'Attack'), ratioField('scale', 'Scale'));
  const typical = h('p', { class: 'dim note' });
  const preview = h('code', {});
  const problem = h('span', { class: 'warnNote' });
  const go = h('button', { class: 'primary', onclick: () => addVariant(parentId, input.value.trim(), Object.fromEntries(Object.entries(ratio).map(([k, el]) => [k, num(el.value, 1)]))) }, 'Add variant');
  const fill = p => {
    for (const k of ['hp', 'attack', 'scale']) ratio[k].value = p ? p[k] : 1;
    manual = false;
  };
  function check(suffixChanged) {
    input.value = input.value.toLowerCase().replace(/[^a-z0-9_]/g, '_');
    const p = presetFor(input.value);
    if (suffixChanged && !manual) fill(p);
    typical.textContent = p ? `Typical in the game for ${p.suffix} variants (median of ${p.count}): ${p.hp}× health, ${p.attack}× attack, ${p.scale}× scale.`
      : /^bird\d+$/.test(input.value) ? `No ${input.value} variants in the game yet: set the ratios yourself (bird1 ${(presetFor('bird1') || {}).hp ?? '?'}×, bird3 ${(presetFor('bird3') || {}).hp ?? '?'}× health).` : '';
    const show = (k, v, digits) => { result[k].textContent = `${v ?? '?'} → ${v == null ? '?' : digits ? Math.round(v * num(ratio[k].value, 1) * 1000) / 1000 : Math.max(1, Math.round(v * num(ratio[k].value, 1)))}`; };
    show('hp', base.hp); show('attack', base.attack); show('scale', base.scale, true);
    preview.textContent = `${parentId}_${input.value}`;
    const badRatio = Object.values(ratio).some(el => !(num(el.value, 0) > 0));
    const bad = !/^[a-z0-9][a-z0-9_]*$/.test(input.value) ? 'Use lowercase letters, digits and underscores' : !free(input.value) ? 'That one exists already' : badRatio ? 'Ratios must be above 0' : '';
    problem.textContent = bad;
    go.disabled = !!bad;
  }
  const chips = h('div', { class: 'suffixChips' }, ...presets.filter(p => free(p.suffix)).slice(0, 12).map(p => h('button', {
    title: `${p.hp}× health, ${p.attack}× attack, ${p.scale}× scale (median of ${p.count} game variants)`,
    onclick: () => { input.value = p.suffix; fill(p); check(false); } }, p.suffix)));
  modal.replaceChildren(
    h('h3', {}, `Add a variant of ${S.project.name || parentId}`),
    h('p', { class: 'dim' }, 'It starts as a copy of this combatant as the game has it now (unsaved edits aren\'t copied: update in game first), with its health, attack and scale multiplied by the ratios. It shares its name, gear and abilities until you change them, and gets this combatant\'s edits while it is ticked.'),
    h('div', { class: 'row', style: 'margin-top:6px' }, h('span', { class: 'mono dim' }, parentId + '_'), input), chips,
    fields, typical,
    h('p', {}, 'New combatant: ', preview, ' ', problem),
    h('div', { class: 'row', style: 'justify-content:flex-end;margin-top:8px' }, h('button', { onclick: () => modal.close() }, 'Cancel'), go));
  check(true);
  modal.showModal();
  input.focus(); input.select();
}
async function addVariant(parentId, suffix, ratios) {
  $('modal').close();
  try {
    const { job } = await api('/api/existing_variant', { parent: parentId, suffix, ratios });
    const result = await followJob(job, `Add variant ${parentId}_${suffix}`);
    if (result.error) { toast(result.error); return; }
    await loadExisting();
    refreshCombatants();
    if ($('logDialog').open && S.viewJob === job) $('logDialog').close();
    await openExisting(result.variant && result.variant.nameId || `${parentId}_${suffix}`);
    toast(`Added ${parentId}_${suffix}`);
  } catch (e) { toast(e.message); }
}

// Delete: the project file goes to projects/.trash; an uploaded combatant can also be taken out of the game.
const deletableVariant = () => isExisting() && !!(S.existingById && S.existingById[S.project.id] || {}).deletable;
$('deleteBtn').addEventListener('click', () => {
  const project = S.project;
  if (project && deletableVariant()) { openDeleteVariant(project); return; }
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
// A variant added in the Forge (Add variant) can be taken out of the game again: its pig row, and the Forge-made
// inventory, items, abilities, name and art only it used. The server refuses while anything still uses it.
function openDeleteVariant(project) {
  const me = S.existingById[project.id] || {};
  const kids = existingDescendants(project.id);
  const modal = $('modal');
  modal.className = '';
  const go = h('button', { class: 'danger', disabled: kids.length > 0, onclick: () => deleteVariant(project.id, me.parent) }, 'Delete');
  modal.replaceChildren(
    h('h3', {}, `Delete ${project.name || project.id}?`),
    h('p', {}, 'Removes ', h('code', {}, project.id), ' from the game, with any Forge-made inventory, items, abilities, name and art that only it uses. What it shares with ', h('code', {}, me.parent || 'its parent'), ' stays.'),
    kids.length ? h('p', { class: 'warnNote' }, `It has variants of its own (${kids.map(k => k.id).join(', ')}): delete those first.`)
      : h('p', { class: 'dim note' }, 'If a battle, another row or the game\'s code still uses it, nothing is removed. Its Forge project moves to projects/.trash.'),
    h('div', { class: 'row', style: 'justify-content:flex-end;margin-top:10px' }, h('button', { onclick: () => modal.close() }, 'Cancel'), go));
  modal.showModal();
  if (!go.disabled) go.focus();
}
async function deleteVariant(id, parent) {
  $('modal').close();
  clearTimeout(saveTimer);
  S.deleting = true;
  try {
    const { job } = await api('/api/existing_delete', { id });
    const result = await followJob(job, `Delete ${id}`);
    if (result.error) { toast(result.error); return; }
    S.skills = await api('/api/skills');
    await loadExisting();
    refreshCombatants();
    if ($('logDialog').open && S.viewJob === job) $('logDialog').close();
    S.savedId = null;
    if (parent && S.existingById[parent]) await openExisting(parent); else await openProject(blankProject(S.project.rig));
    toast(`Deleted ${id}`);
  } catch (e) {
    toast(e.message);
  } finally {
    S.deleting = false;
  }
}
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

// ◀ ▶ in the right panel: the open game combatant's family (its top-level pig and every variant), in list order.
function variantFamily() {
  if (!isExisting() || !S.existing) return [];
  const me = S.existingById && S.existingById[S.project.id];
  if (!me) return [];
  const root = S.existingById[me.root] || me;
  return [root, ...existingDescendants(root.id)];
}
function renderVariantNav() {
  const family = variantFamily();
  const nav = $('variantNav');
  nav.hidden = family.length < 2;
  if (nav.hidden) {
    if (isExisting() && !S.existing) loadExisting().then(renderVariantNav).catch(() => {});
    return;
  }
  const i = family.findIndex(x => x.id === S.project.id);
  const root = family[0];
  const label = x => x.id === root.id ? x.name : (x.name === root.name ? x.id.slice(root.id.length + 1) : x.name);
  $('variantLabel').replaceChildren(h('strong', {}, label(family[i])), ' ', h('span', { class: 'mono' }, `${i + 1} / ${family.length}`));
  $('variantLabel').title = family[i].id;
  const step = d => family[(i + d + family.length) % family.length];
  $('variantPrev').title = 'Previous: ' + step(-1).id;
  $('variantNext').title = 'Next: ' + step(1).id;
  $('variantPrev').onclick = () => openExisting(step(-1).id);
  $('variantNext').onclick = () => openExisting(step(1).id);
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
function refreshCombatants(keep = $('grid').scrollTop) {
  if (S.side !== 'game') return;
  const grid = $('grid');
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
    x.supported ? h('img', { class: 'cIcon', src: `/pigicon/${encodeURIComponent(x.id)}?v=${x.look || ''}`, alt: '', loading: 'lazy', draggable: false }) : h('div', { class: 'cNoIcon' }, x.rig),
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
  $('deleteBtn').disabled = ex && !deletableVariant();
  $('duplicateBtn').hidden = !ex;
  renderVariantNav();
  $('variantBtn').hidden = !ex;
  $('deleteBtn').title = !ex ? 'Delete this combatant' : deletableVariant() ? 'Take this Forge-added variant out of the game'
    : 'The game\'s own combatants can\'t be deleted here: battles use them';
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
  api('/api/allfx').then(all => { S.allFx = all; }).catch(() => {});
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
    // A recipe job decides only whether each recipe is written.
    const myRecipes = Object.fromEntries(((mine && mine.recipes) || []).map(r => [r.uid, r]));
    for (const r of a.recipes || []) if (myRecipes[r.uid]) for (const f of ['written', 'source']) if (f in r) myRecipes[r.uid][f] = r[f];
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
async function createRecipe(ability, recipe) {
  clearTimeout(saveTimer);
  if (!S.project.id) { toast('Give the combatant a name first'); return; }
  const rid = recipeKey(recipe.ids);
  if (!confirm(`Run Claude Code on this project now to write the recipe ${rid}? It can edit the skill data and scripts, and uses your Claude account.`)) return;
  try {
    await saveNow();
    const { job } = await api('/api/recipe', { uid: ability.uid, recipe: recipe.uid, project: S.project });
    const result = await followJob(job, `Claude: ${rid}`);
    await applyJobResult(result);
    if (!result.error && S.viewJob === job && $('logDialog').open) { $('logDialog').close(); toast(`${rid}: written`); }
  } catch (e) { toast(e.message); }
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
  // Picking the same base again keeps its spring (a feature, set in the Slots list).
  S.project.variant = !variant && sameRig && rigLooks()[S.project.variant] ? S.project.variant : variant;
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
  // A spring is a feature (Slots), not part of the base's name.
  $('baseName').textContent = S.project.rig + (rigLooks()[S.project.variant] ? '' : variantName(S.project.variant));
}
$('baseBtn').addEventListener('click', () => {
  const modal = $('modal');
  modal.className = '';
  const baseVariant = rigLooks()[S.project.variant] ? '' : S.project.variant || '';  // a spring doesn't change the base
  const tile = (rig, variant) => h('button', { class: 'baseTile' + (rig.id === S.project.rig && variant === baseVariant ? ' on' : ''), title: rig.id + variantName(variant), onclick: () => { modal.close(); setBase(rig.id, variant); } },
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
  // Ctrl+S saves now, even while typing (autosave still runs after each edit).
  if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 's') {
    e.preventDefault();
    if (!S.project || !S.project.id) { toast('Give the combatant a name first: it saves under its id'); return; }
    clearTimeout(saveTimer);
    saveNow().then(() => toast($('saveState').textContent.startsWith('Not saved') ? $('saveState').textContent : 'Saved'));
    return;
  }
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
  [S.lib, S.skills, S.icons, S.powerLevels, S.projectiles, S.allFx] = await Promise.all([api('/api/library'), api('/api/skills'), api('/api/icons'), api('/api/powerlevels'),
    api('/api/projectiles'), api('/api/allfx').catch(() => null)]);
  await openProject(blankProject('MinionPig'));
  requestAnimationFrame(tick);
}
window.forge = { S, assign, openProject, renderTooltip, setTab, setLevel, stepHistory, activeLayer, renderAbilities, generateAll, openExisting, duplicateExisting, loadExisting, setTick };
boot().catch(e => { document.body.prepend(h('pre', { style: 'color:#ef6b6b;padding:12px' }, 'Could not start: ' + e.message)); });
