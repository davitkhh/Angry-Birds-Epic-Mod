'use strict';
/* Combatant Forge, second section: projectiles (projectiles.py).

   A projectile is a list of components: the body that is fired, air trails, impact effects, marks left where it
   bounces... Each component is a stack of layers with the same asset system as a combatant's slots (library drag and
   drop, transforms, gizmo, undo): for that, the project keeps them in `slots` keyed by component uid, and the section
   gives the editor a stand-in rig with one bone per component, laid out side by side in the Visuals tab.

   The Physics tab sets how each component comes into being (fired from the launcher, trailing behind another, or
   spawned where another hits, bounces or ends) and how it moves. Firing is simulated from the time since the shot, so
   the preview is the same every time: fired components arc from the launcher to the targets, bounce on, fade out;
   trails and spawned parts live for a while, scale, fade, scatter, fall and squash. */
(() => {
  S.mode = 'combatant';
  const P = { projects: [], combatant: null, origin: { x: 0, y: 60 }, t0: performance.now(), seed: 0 };
  // Each shot re-rolls the ± variations.
  const refire = (now = performance.now()) => { P.t0 = now; P.seed = Math.floor(Math.random() * 100000); };
  // In this section with a projectile open (while switching, the old combatant is briefly still loaded).
  const isProj = () => S.mode === 'projectile' && !!(S.project && S.project.components);
  const KINDS = { body: 'Body', fired: 'Fired effect', trail: 'Air trail', impact: 'Impact effect', bounce: 'Bounce mark', slowmo: 'Slow motion' };
  const SPACING = 260;

  /* ---------------------------------------------------------------- the stand-in rig: one bone per component */

  function projectileRig(project) {
    const comps = project.components;
    const row = [0, '__none', 0, -40, 40, 40, -40];
    const slots = comps.map(c => ({ key: c.uid, kind: 'gear', bones: [c.uid], textures: [] }));
    return {
      id: '__projectile', side: 'pig', bodyCenter: [0, 45], textures: {}, variants: { pirate: {} }, base: {},
      paths: comps.map((c, i) => 'C' + i), parents: comps.map(() => -1),
      bones: comps.map((c, i) => ({ node: i, name: c.uid, slot: c.uid, bone: i, sprite: i })),
      clips: [{ name: 'Edit', fps: 24, wrap: 2, length: 1, steps: 1, tags: [], events: [],
        tracks: Object.fromEntries(comps.map((c, i) => [i, { p: [i * SPACING, 0] }])), tex: Object.fromEntries(comps.map((c, i) => [String(i), [row]])) }],
      slots, slotByKey: Object.fromEntries(slots.map(s => [s.key, s])), restClip: 0, anyRow: Object.fromEntries(comps.map((c, i) => [i, row])),
    };
  }
  function rebuild() {
    S.rig = projectileRig(S.project);
    if (!S.rig.slotByKey[S.selected]) S.selected = (S.project.components.find(c => c.type === 'body') || S.project.components[0] || {}).uid || null;
  }
  const comp = uid => S.project.components.find(c => c.uid === uid);

  /* ---------------------------------------------------------------- opening, saving, switching sections */

  async function refreshList() {
    try { P.projects = await api('/api/projectiles_list'); } catch (e) { P.projects = []; }
    renderProjectList();
  }
  async function openProjectile(project) {
    for (const c of project.components) if (!project.slots[c.uid]) project.slots[c.uid] = { layers: [], hidden: false };
    S.project = project;
    const saved = !!project.id && P.projects.some(p => p.id === project.id);
    S.savedId = saved ? project.id : null;
    // An unsaved copy (from the game, an import) takes its id from its name until saved, like a new combatant.
    S.idTouched = saved;
    S.selected = (project.components.find(c => c.type === 'body') || project.components[0] || {}).uid || null;
    S.clip = 0; S.time = 0;
    $('nameInput').value = project.name || '';
    $('idInput').value = project.id || '';
    $('idInput').disabled = false;
    rebuild();
    resetHistory();
    renderSlots(); renderInspector(); renderPhysics(); renderProjectList();
    $('saveState').textContent = saved ? (project.publishedAt ? `Saved · uploaded ${project.publishedAt}` : 'Saved') : '';
    uploadLabel();
    P.t0 = performance.now();
    fitView();
  }
  // Upload to game: the projectile goes to Resources/ForgeProjectiles (ForgeProjectiles.cs flies it), and combatant
  // attacks can pick it from the library's Projectiles like the game's own.
  function uploadLabel() {
    const up = !!S.project.published;
    $('uploadBtn').disabled = false;
    $('uploadBtn').textContent = up ? 'Update in game' : 'Upload to game';
    $('uploadBtn').title = up ? `Rewrite ${S.project.published} in the game with these edits` : 'Put this projectile into the game so combatant attacks can fire it';
  }
  $('uploadBtn').addEventListener('click', async e => {
    if (!isProj()) return;
    e.stopImmediatePropagation();
    if (!S.project.id) { toast('Give the projectile a name first'); return; }
    if (!S.project.components.some(c => c.physics.spawn === 'fire' && (S.project.slots[c.uid] || { layers: [] }).layers.length)) { toast('Give a fired component some art first'); return; }
    $('uploadBtn').disabled = true;
    try {
      await leave();
      const result = await api('/api/projectile_publish', { id: S.project.id });
      S.project.published = result.project.published;
      S.project.publishedAt = result.project.publishedAt;
      P.uploaded = Date.now();
      if (typeof projectileInfo !== 'undefined') projectileInfo.delete(result.name);
      S.projectiles = await api('/api/projectiles');
      if (S.cat === 'Projectile') renderLibrary();
      $('saveState').textContent = `Saved · uploaded ${S.project.publishedAt}`;
      toast(`Uploaded as ${result.name}: combatant attacks can now fire it (library → Projectiles). Unity picks it up when it next refreshes.`);
    } catch (err) { toast(err.message); }
    uploadLabel();
  }, true);
  async function leave() {
    clearTimeout(saveTimer);
    await saveNow();
  }
  async function setMode(mode) {
    if (mode === S.mode) return;
    await leave();
    document.querySelectorAll('#modeTabs button').forEach(b => b.classList.toggle('on', b.dataset.mode === mode));
    if (mode === 'projectile') {
      P.combatant = S.project;
      S.mode = 'projectile';
      document.body.classList.add('projMode');
      $('projectSelectLabel').textContent = 'Projectile';
      document.querySelector('#sideNav button[data-tab=slots]').textContent = 'Visuals';
      setTab('slots');
      await refreshList();
      const last = P.lastProjectile;
      await openProjectile(last ? (P.projects.some(p => p.id === last.id) ? await api('/api/projectile_project/' + last.id) : last) : await api('/api/projectile_new'));
    } else {
      P.lastProjectile = S.project;
      S.mode = 'combatant';
      document.body.classList.remove('projMode');
      $('projectSelectLabel').textContent = 'Combatant';
      document.querySelector('#sideNav button[data-tab=slots]').textContent = 'Slots';
      $('uploadBtn').disabled = false;
      setTab('slots');
      await openProject(P.combatant || blankProject('MinionPig'));
    }
  }
  document.querySelectorAll('#modeTabs button').forEach(b => b.addEventListener('click', () => setMode(b.dataset.mode)));

  // The shared editor calls these; in this section they work on projectiles.
  const wrap = (name, fn) => { const orig = window[name]; window[name] = function (...args) { return fn.call(this, orig, ...args); }; };
  for (const name of ['renderTooltip', 'renderAbilities', 'renderStats', 'renderBase']) wrap(name, (orig, ...args) => isProj() ? undefined : orig(...args));
  wrap('neededTargets', (orig, ...args) => isProj() ? 1 : orig(...args));
  wrap('saveNow', async (orig, ...args) => {
    await orig(...args);
    if (isProj() && S.project.id) await refreshList();
  });
  wrap('renderProjectList', (orig, ...args) => {
    if (!isProj()) return orig(...args);
    const saved = P.projects.some(p => p.id === S.project.id);
    $('projectSelect').replaceChildren(h('option', { value: '', selected: !saved }, saved ? '—' : 'Unsaved'),
      ...P.projects.map(p => h('option', { value: p.id, selected: p.id === S.project.id }, p.name || p.id)));
  });
  wrap('setTab', (orig, tab) => {
    if (!isProj()) { $('tab-pphysics').hidden = true; return orig(tab); }
    if (tab === 'abilities' || tab === 'stats') tab = 'slots';
    document.querySelectorAll('#sideNav button').forEach(b => b.classList.toggle('on', b.dataset.tab === tab));
    for (const t of ['slots', 'abilities', 'stats', 'pphysics']) $('tab-' + t).hidden = t !== tab;
    if (tab === 'pphysics') { renderPhysics(); P.t0 = performance.now(); if (!targetOnScreen()) fitView(); } else fitView();
  });
  wrap('stepHistory', async (orig, ...args) => {
    await orig(...args);
    if (isProj()) { rebuild(); renderSlots(); renderInspector(); renderPhysics(); }
  });
  // While firing (Physics tab) the side-by-side edit layout is replaced by the shot.
  wrap('collectDraw', (orig, ...args) => {
    if (isProj() && !$('tab-pphysics').hidden) { S.anchors = []; return []; }
    return orig(...args);
  });
  wrap('fitView', (orig, ...args) => {
    orig(...args);
    if (!isProj() || $('tab-pphysics').hidden) return;
    // keep the launcher in view with the targets
    const r = canvas.getBoundingClientRect();
    const [x, y] = apply(worldMatrix(), P.origin.x, P.origin.y);
    if (x < 30 || x > r.width - 30 || y < 30 || y > r.height - 30) { S.view.x += r.width * 0.65 - x; S.view.y += r.height * 0.55 - y; }
  });
  wrap('renderInspector', (orig, ...args) => {
    orig(...args);
    if (!isProj()) return;
    const c = comp(S.selected), head = document.querySelector('#inspector h3');
    if (c && head) head.textContent = c.name + (S.project.slots[c.uid] && S.project.slots[c.uid].hidden ? ' (hidden)' : '');
  });
  // Front/back pairs and weapon art belong to combatants: art dropped here is one layer.
  wrap('assign', (orig, item, part, slotKey, joined) => orig(item, part, slotKey, isProj() ? false : joined));

  /* ---------------------------------------------------------------- the components list (Visuals tab) */

  wrap('renderSlots', (orig, ...args) => {
    if (!isProj()) return orig(...args);
    const list = S.project.components;
    const add = kind => {
      const fired = list.find(c => c.physics.spawn === 'fire');
      {
        const base = { body: { spawn: 'fire' }, fired: { spawn: 'launch', life: 0.25, scaleStart: 0.8, scaleEnd: 1.3 }, trail: { spawn: 'trail', life: 0.3, scaleEnd: 0.4 }, impact: { spawn: 'hit', life: 0.4, scaleStart: 0.6, scaleEnd: 1.2 }, bounce: { spawn: 'bounce', life: 0.5, squash: 0.5 }, slowmo: { spawn: 'hit' } }[kind];
        const uid = newUid();
        const physics = { ...defaultPhysics(), ...base, of: base.spawn === 'fire' ? '' : (fired ? fired.uid : '') };
        list.push({ uid, name: KINDS[kind] + ' ' + (list.filter(c => c.type === kind).length + 1), type: kind, physics });
        S.project.slots[uid] = { layers: [], hidden: false };
        S.selected = uid;
        rebuild(); touch(); renderSlots(); renderInspector(); renderPhysics(); fitView();
      }
    };
    const row = (c, i) => {
      const slot = S.project.slots[c.uid] || { layers: [] };
      const shown = slot.layers.filter(l => !l.hidden).slice(-1)[0];
      return h('div', { class: 'compRow slotRow' + (S.selected === c.uid ? ' sel' : '') + (slot.hidden ? ' off' : ''), 'data-slot': c.uid, onclick: () => selectSlot(c.uid) },
        h('div', { class: 'slotThumb', style: shown ? `background-image:url("${spriteUrl(shown.src)}")` : '' }),
        h('div', {}, h('input', { value: c.name, 'aria-label': 'Component name', onclick: ev => ev.stopPropagation(), oninput: ev => { c.name = ev.target.value; touch(); renderPhysics(); } }),
          h('div', { class: 'kind' }, `${KINDS[c.type] || c.type} · ${slot.layers.length ? slot.layers.length + ' layer' + (slot.layers.length > 1 ? 's' : '') : 'no art yet: drop some here'}`)),
        h('div', { class: 'slotActions' },
          h('button', { title: 'Move up', disabled: i === 0, onclick: ev => { ev.stopPropagation(); list.splice(i - 1, 0, list.splice(i, 1)[0]); rebuild(); touch(); renderSlots(); renderPhysics(); } }, '↑'),
          h('button', { title: slot.hidden ? 'Show it' : 'Hide it (it isn\'t drawn or fired)', onclick: ev => { ev.stopPropagation(); slot.hidden = !slot.hidden; touch(); renderSlots(); renderInspector(); } }, slot.hidden ? 'Show' : 'Hide'),
          list.length > 1 ? h('button', { class: 'danger', title: 'Remove this component', onclick: ev => {
            ev.stopPropagation();
            list.splice(i, 1);
            delete S.project.slots[c.uid];
            for (const o of list) if (o.physics.of === c.uid) o.physics.of = '';
            rebuild(); touch(); renderSlots(); renderInspector(); renderPhysics();
          } }, '×') : null));
    };
    $('projComponents').replaceChildren(
      h('div', { class: 'row between' }, h('h3', {}, 'Components'), h('span', { class: 'dim' }, 'Drop art on a component, here or on the canvas')),
      ...list.map(row),
      h('div', { class: 'addRow' }, ...Object.entries(KINDS).map(([k, label]) => h('button', { onclick: () => add(k) }, '+ ' + label))));
    $('slotList').replaceChildren();
    $('emptyHint').textContent = list.some(c => (S.project.slots[c.uid] || { layers: [] }).layers.length) ? '' : 'Drag art from the library onto a component. The Physics tab sets how it flies and fires it.';
  });
  const DEFAULT_PHYSICS = { spawn: 'fire', of: '', count: 1, interval: 0.08, spread: 0, flightTime: 0.25, easing: 'even', arc: 0, hops: 0, hopHeight: 30,
    align: false, spin: 0, fadeFrom: 0, fadeTo: 0, bounces: 0, bounceArc: 60, bounceDistance: 140, bounceDecay: 0.6, afterSpeed: 1, afterDirection: 'onward',
    afterAlign: false, afterSpin: 0, linger: 0, rate: 30, life: 0.4, scaleStart: 1, scaleEnd: 1, fade: true, squash: 0, scatter: 0, gravity: 0,
    trailAlign: false, randomTurn: false, accel: 0, afterAccel: 0, style: 'pieces', textureMode: 'stretch', alphaKeys: null,
    slowScale: 0.3, slowDuration: 0.5, slowEase: 0.1, slowEaseOut: 0.1, slowScope: 'game' };
  function defaultPhysics() { return { ...DEFAULT_PHYSICS }; }

  /* ---------------------------------------------------------------- the Physics tab */

  const SPAWNS = [['fire', 'Fired from the launcher'], ['launch', 'Where it is fired from:'], ['trail', 'Trails behind'], ['hit', 'Where it hits its target:'], ['bounce', 'Where it bounces:'], ['end', 'Where it stops:']];
  // A fired component's settings come in groups: how it's fired, its flight up to the target, and after the impact.
  const EASES = [['even', 'Even'], ['accel', 'Speeds up'], ['decel', 'Slows down'], ['ease', 'Eases in and out']];
  const DIRS = [['onward', 'Carries on past it'], ['back', 'Rebounds toward the launcher']];
  const STYLES = [['pieces', 'Separate pieces'], ['ribbon', 'Ribbon (like the game\'s trails)']];
  const TEXMODES = [['stretch', 'Stretched along it'], ['tile', 'Repeats along it']];
  const SCOPES = [['game', 'The whole battle'], ['projectile', 'Only this projectile']];
  const FIELDS = {
    // nothing drawn: time slows when its shot fires, hits, bounces or stops (ForgeSlowMotion / the runner's own clock)
    slowmo: [[null, [['slowScope', 'Slows', SCOPES], ['slowScale', 'Time scale (1 = normal)', 0.05], ['slowDuration', 'Lasts s', 0.05], ['slowEase', 'Ease in s', 0.05], ['slowEaseOut', 'Ease out s', 0.05]]]],
    fire: [
      ['Firing', [['count', 'How many', 1], ['interval', 'Seconds between', 0.01], ['spread', 'Spread °', 1]]],
      ['Before impact', [['flightTime', 'Flight time s', 0.01], ['easing', 'Speed', EASES], ['accel', 'Acceleration /s²', 50], ['arc', 'Arc height', 1],
        ['hops', 'Bounces in flight', 1], ['hopHeight', 'In-flight bounce height', 1], ['align', 'Turns along its path', 'check'], ['spin', 'Spin °/s', 10],
        ['fadeFrom', 'Fade from (0-1 of flight)', 0.05], ['fadeTo', 'Gone at (0-1, 0 = reaches target)', 0.05]]],
      ['After impact', [['bounces', 'Bounces', 1], ['bounceArc', 'Bounce height', 1], ['bounceDistance', 'Bounce distance', 1], ['bounceDecay', 'Each bounce ×', 0.05],
        ['afterSpeed', 'Speed × flight speed', 0.05], ['afterAccel', 'Acceleration /s²', 50], ['afterDirection', 'Direction', DIRS], ['afterAlign', 'Turns along its bounces', 'check'],
        ['afterSpin', 'Spin °/s', 10], ['linger', 'Stays for s', 0.05]]]],
    ribbon: [[null, [['style', 'Look', STYLES], ['life', 'Lasts s', 0.05], ['scaleStart', 'Width at the shot ×', 0.05], ['scaleEnd', 'Width at the tail ×', 0.05],
      ['fade', 'Fades out', 'check'], ['textureMode', 'Texture', TEXMODES]]]],
    trail: [[null, [['style', 'Look', STYLES], ['rate', 'Per second', 1], ['life', 'Lasts s', 0.05], ['scaleStart', 'Start size', 0.05], ['scaleEnd', 'End size', 0.05], ['fade', 'Fades out', 'check'],
      ['trailAlign', 'Turns along the path', 'check'], ['randomTurn', 'Random rotation', 'check'], ['scatter', 'Scatter', 1], ['gravity', 'Gravity', 10], ['spin', 'Spin °/s', 10]]]],
    spawned: [[null, [['count', 'How many', 1], ['life', 'Lasts s', 0.05], ['scaleStart', 'Start size', 0.05], ['scaleEnd', 'End size', 0.05], ['fade', 'Fades out', 'check'],
      ['squash', 'Squash (0-1)', 0.05], ['randomTurn', 'Random rotation', 'check'], ['scatter', 'Scatter', 1], ['gravity', 'Gravity', 10], ['spin', 'Spin °/s', 10]]]],
  };
  // Values that can vary between shots (and between bounces / particles): a ± range beside the value.
  const VARY = new Set(['flightTime', 'arc', 'hops', 'hopHeight', 'spin', 'spread', 'bounces', 'bounceArc', 'bounceDistance', 'bounceDecay',
    'afterSpeed', 'afterSpin', 'linger', 'accel', 'afterAccel', 'life', 'scaleStart', 'scaleEnd', 'scatter', 'gravity', 'count']);
  function renderPhysics() {
    if (!isProj()) return;
    const list = S.project.components;
    const fired = list.filter(c => c.physics.spawn === 'fire');
    $('physicsForm').replaceChildren(
      h('p', { class: 'dim note' }, 'Press Fire (above the canvas). Drag the launcher and the targets to aim; the Targets buttons set how many there are. A fired component with "How many" above 1 aims at the targets in turn.'),
      ...list.map(c => {
        const p = c.physics;
        const changed = () => { touch(); P.t0 = performance.now(); };
        const spawnSel = h('select', { 'aria-label': 'Where it comes from', onchange: ev => { p.spawn = ev.target.value; if (p.spawn !== 'fire' && !p.of && fired[0]) p.of = fired[0].uid; changed(); renderPhysics(); } },
          ...SPAWNS.filter(([k]) => c.type !== 'slowmo' || (k !== 'fire' && k !== 'trail')).map(([k, label]) => h('option', { value: k, selected: p.spawn === k }, label)));
        const ofSel = p.spawn === 'fire' ? null : h('select', { 'aria-label': 'Of which component', onchange: ev => { p.of = ev.target.value; changed(); } },
          h('option', { value: '' }, '— pick a fired component —'), ...fired.filter(f => f.uid !== c.uid).map(f => h('option', { value: f.uid, selected: p.of === f.uid }, f.name)));
        const field = ([key, label, step]) => step === 'check'
          ? h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: !!p[key], onchange: ev => { p[key] = ev.target.checked; changed(); } }), ' ' + label)
          : Array.isArray(step)
            ? h('label', {}, label, h('select', { onchange: ev => { p[key] = ev.target.value; changed(); if (key === 'style') renderPhysics(); } }, ...step.map(([v, l]) => h('option', { value: v, selected: (p[key] || step[0][0]) === v }, l))))
            : h('label', {}, label, h('span', { class: 'varyRow' },
                h('input', { type: 'number', step, value: p[key] ?? DEFAULT_PHYSICS[key] ?? 0, oninput: ev => { p[key] = num(ev.target.value, p[key]); changed(); } }),
                VARY.has(key) ? h('span', { class: 'pm' }, '±') : null, VARY.has(key) ? h('input', { type: 'number', class: 'vary', step, min: 0, value: (p.vary || {})[key] || 0, title: '± random range: each shot (and each bounce) picks a value this far either side',
                  'aria-label': label + ' ± range', oninput: ev => { p.vary = { ...(p.vary || {}) }; const v = Math.abs(num(ev.target.value, 0)); if (v) p.vary[key] = v; else delete p.vary[key]; changed(); } }) : null));
        const groups = FIELDS[c.type === 'slowmo' ? 'slowmo' : p.spawn === 'fire' ? 'fire' : p.spawn === 'trail' ? (p.style === 'ribbon' ? 'ribbon' : 'trail') : 'spawned'];
        return h('div', { class: 'physCard' },
          h('h3', {}, c.name, h('span', { class: 'dim' }, KINDS[c.type] || c.type)),
          h('div', { class: 'row wrap' }, spawnSel, ofSel),
          p.spawn !== 'fire' && !p.of ? h('span', { class: 'warnNote' }, 'Pick which fired component it comes from.') : null,
          ...groups.flatMap(([title, list]) => [title ? h('h4', { class: 'physGroup' }, title) : null, h('div', { class: 'fields' }, ...list.map(field))]));
      }));
  }

  /* ---------------------------------------------------------------- the simulation */

  // A tiny repeatable random number per (component, shot, particle), so scatter looks the same every time.
  const rand = (a, b, c) => { const x = Math.sin(a * 127.1 + b * 311.7 + c * 74.7) * 43758.5453; return x - Math.floor(x); };
  const lerp = (a, b, t) => a + (b - a) * t;
  // A physics value with its ± variation picked for one shot (k) and sub-part (i, e.g. the bounce), repeatable per Fire.
  const KEYSEED = {};
  const keySeed = key => KEYSEED[key] ??= [...key].reduce((a, ch) => (a * 31 + ch.charCodeAt(0)) % 9973, 7);
  function pv(p, key, ci, k, i = 0) {
    const base = p[key] ?? DEFAULT_PHYSICS[key] ?? 0, r = (p.vary || {})[key] || 0;
    return r ? base + (rand(ci * 13.1 + P.seed * 0.0137, k * 7.3 + i * 1.91, keySeed(key)) * 2 - 1) * r : base;
  }
  // Every fired shot of a component: its path through time, and its hit, bounce and end events.
  const EASE = {
    even: u => u, accel: u => u * u, decel: u => 1 - (1 - u) * (1 - u), ease: u => u * u * (3 - 2 * u),
  };
  const ease = (p, u) => (EASE[p.easing] || EASE.even)(Math.max(0, Math.min(1, u)));
  // The time (0-1 of the flight) at which the eased flight reaches a point (0-1 of the path).
  // Constant acceleration over a fixed flight time: the shot starts slower and ends faster (or the reverse), k = aT²/2D.
  const accelMap = (f, s) => f.accelK ? (1 - f.accelK) * s + f.accelK * s * s : s;
  const progress = (f, u) => accelMap(f, ease(f.c.physics, u));
  const uneased = (f, s) => { let lo = 0, hi = 1; for (let i = 0; i < 30; i++) { const m = (lo + hi) / 2; if (progress(f, m) < s) lo = m; else hi = m; } return (lo + hi) / 2; };
  // A point s (0-1) along the flight: the straight line, the arc, and the in-flight bounces (it touches down between hops).
  function flightPoint(f, s) {
    const hops = f.hopsN;
    const hop = hops > 0 ? f.hopHeight * Math.abs(Math.sin(Math.PI * hops * s)) : 0;
    return { x: lerp(f.origin.x, f.end.x, s), y: lerp(f.origin.y, f.end.y, s) + f.arc * 4 * s * (1 - s) + hop };
  }
  // Every fired shot of a component: its flight, its bounces after the impact, and its events (launch, in-flight bounces,
  // hit, bounces after, end).
  let noLaunch = false;  // set by simulateWith for ricochet hops
  function flights(c, ci, list, origin) {
    const p = c.physics, out = [];
    const n = Math.max(1, Math.round(pv(p, 'count', ci, 0)));
    for (let k = 0; k < n; k++) {
      const v = (key, i) => pv(p, key, ci, k, i);
      const tgt = list[k % list.length];
      // spread fans the shots out around the line to their target; its ± range aims each shot off by up to that many degrees
      const jitter = (p.vary || {}).spread ? v('spread') - (p.spread || 0) : 0;
      const ang = ((k - (n - 1) / 2) * (p.spread || 0) + jitter) * Math.PI / 180;
      const dx = tgt.x - origin.x, dy = tgt.y - origin.y;
      const end = { x: origin.x + dx * Math.cos(ang) - dy * Math.sin(ang), y: origin.y + dx * Math.sin(ang) + dy * Math.cos(ang) };
      const t0 = k * (p.interval || 0), T = Math.max(0.02, v('flightTime') || 0.25);
      const dir = Math.sign(end.x - origin.x) || -1;
      const speed = Math.hypot(end.x - origin.x, end.y - origin.y) / T;
      const gone = p.fadeTo > 0 ? Math.min(1, p.fadeTo) : 0;
      const f = { c, ci, k, origin, end, t0, T, dir, gone, hops: [], arc: v('arc'), hopHeight: v('hopHeight'), hopsN: Math.max(0, Math.round(v('hops'))),
        spin: v('spin'), afterSpin: v('afterSpin') };
      const dist = Math.max(1, Math.hypot(end.x - origin.x, end.y - origin.y));
      f.accelK = Math.max(-1, Math.min(1, v('accel') * T * T / (2 * dist)));
      // after the impact: on past the target, or back toward the launcher, at its own speed
      const away = p.afterDirection === 'back' ? -dir : dir;
      const after = Math.max(0.05, v('afterSpeed')) * speed;
      let at = { ...end }, t = t0 + T, decay = 1;
      const afterAccel = v('afterAccel');
      if (!gone) {
        for (let i = 0, nb = Math.max(0, Math.round(v('bounces'))); i < nb; i++) {
          // each bounce picks its own distance and height within their ranges
          const d = Math.max(0, v('bounceDistance', i + 1)) * decay, hgt = Math.max(0, v('bounceArc', i + 1)) * decay;
          decay *= Math.max(0, v('bounceDecay', i + 1));
          // speeding up or slowing down after the hit (never below a twentieth of its speed)
          const sp = Math.max(after * 0.05, after + afterAccel * (t - t0 - T));
          const dur = Math.max(0.06, d / sp * 1.6);
          f.hops.push({ from: { ...at }, to: { x: at.x + away * d, y: at.y }, h: hgt, t, dur });
          at = { x: at.x + away * d, y: at.y };
          t += dur;
        }
      }
      // the fired effect faces the shot, like the body art (drawn for flying toward the enemy side)
      // a ricochet hop (Ricochet.Launch) is the same shot flying on, so it has no fired (launch) pieces
      f.events = noLaunch ? [] : [{ kind: 'launch', x: origin.x, y: origin.y, t: t0, rot: Math.atan2(end.y - origin.y, end.x - origin.x) - Math.atan2(0, dir) }];
      const hopsInFlight = f.hopsN;
      for (let j = 1; j < hopsInFlight; j++) {
        const sj = j / hopsInFlight;
        if (gone && sj >= gone) break;
        const q = flightPoint(f, sj);
        f.events.push({ kind: 'bounce', x: q.x, y: q.y, t: t0 + uneased(f, sj) * T });
      }
      if (!gone) f.events.push({ kind: 'hit', x: end.x, y: end.y, t: t0 + T });
      f.hops.forEach(hp => f.events.push({ kind: 'bounce', x: hp.to.x, y: hp.to.y, t: hp.t + hp.dur }));
      f.stopT = gone ? t0 + uneased(f, gone) * T : t;
      const stopAt = gone ? flightPoint(f, gone) : at;
      f.events.push({ kind: 'end', x: stopAt.x, y: stopAt.y, t: f.stopT });
      f.stopAt = stopAt;
      // it stays where it stopped for a while (not when it faded out in flight)
      f.lingerT = gone ? 0 : Math.max(0, v('linger'));
      out.push(f);
    }
    return out;
  }
  // Its rotation in flight: along its path and/or spinning.
  function flightRot(f, t, vel) {
    const p = f.c.physics;
    let rot = f.spin * (t - f.t0) * Math.PI / 180;
    if (p.align) rot += Math.atan2(vel[1], vel[0]) - Math.atan2(0, f.dir);
    return rot;
  }
  function flightAt(f, t) {
    const p = f.c.physics;
    const u = Math.min(1, Math.max(0, (t - f.t0) / f.T)), s = progress(f, u), s2 = progress(f, Math.min(1, u + 0.01));
    const pos = flightPoint(f, s), nx = flightPoint(f, Math.max(s2, s + 1e-4));
    return { pos, vel: [nx.x - pos.x || (f.end.x - f.origin.x), nx.y - pos.y], u };
  }
  // Where a shot is at time t, its rotation and opacity; null when it isn't there.
  function shotAt(f, t) {
    const p = f.c.physics;
    if (t < f.t0 || t > f.stopT + f.lingerT) return null;
    const hitT = f.t0 + f.T;
    if (t <= hitT || f.gone) {
      const { pos, vel, u } = flightAt(f, Math.min(t, f.stopT));
      let alpha = 1;
      if (p.fadeTo > 0 && u > (p.fadeFrom || 0)) alpha = Math.max(0, 1 - (u - (p.fadeFrom || 0)) / Math.max(0.001, p.fadeTo - (p.fadeFrom || 0)));
      return { x: pos.x, y: pos.y, rot: flightRot(f, Math.min(t, f.stopT), vel), sx: 1, sy: 1, alpha };
    }
    // after the impact: its rotation carries on from the moment of the hit
    const atHit = flightAt(f, hitT);
    const rot0 = flightRot(f, hitT, atHit.vel);
    let pos, vel = [f.dir, 0], alpha = 1;
    if (t <= f.stopT && f.hops.length) {
      const hp = f.hops.find(x => t <= x.t + x.dur) || f.hops[f.hops.length - 1];
      const v = Math.min(1, (t - hp.t) / hp.dur);
      pos = { x: lerp(hp.from.x, hp.to.x, v), y: lerp(hp.from.y, hp.to.y, v) + hp.h * 4 * v * (1 - v) };
      vel = [hp.to.x - hp.from.x, hp.h * 4 * (1 - 2 * v)];
    } else {
      pos = f.stopAt;
      if (f.lingerT > 0) alpha = Math.min(1, 2.5 * (1 - (t - f.stopT) / f.lingerT));  // fades over the last part of its stay
    }
    const spun = f.afterSpin * (Math.min(t, f.stopT) - hitT) * Math.PI / 180;
    const rot = p.afterAlign && t <= f.stopT ? Math.atan2(vel[1], vel[0]) - Math.atan2(0, f.dir) + spun : rot0 + spun;
    return { x: pos.x, y: pos.y, rot, sx: 1, sy: 1, alpha };
  }
  // Everything to draw at time t (seconds since the shot), and when the whole shot is over.
  function simulate(t) {
    return simulateWith(S.project, P.origin, targets(), null, t, P.seed);
  }
  // The same for any projectile, launcher and targets (the combatant preview fires uploaded projectiles with it). With a
  // centre, a fired component that fires once at several targets flies at their centre, as ForgeProjectiles.cs does.
  function simulateWith(project, origin, list, centre, t, seed, ricochet) {
    const saved = P.seed, savedNoLaunch = noLaunch;
    P.seed = seed;
    noLaunch = !!ricochet;
    try { return simulateBody(project, origin, list, centre, t); } finally { P.seed = saved; noLaunch = savedNoLaunch; }
  }
  function simulateBody(project, origin, list, centre, t) {
    const draws = [], slows = [], events = [];
    const comps = project.components.filter(c => !(project.slots[c.uid] || {}).hidden);
    const shots = {};
    let over = 0;
    comps.forEach((c, ci) => {
      if (c.physics.spawn !== 'fire') return;
      const n = Math.max(1, Math.round(pv(c.physics, 'count', ci, 0)));
      shots[c.uid] = flights(c, ci, centre && n === 1 && list.length > 1 ? [centre] : list, origin);
    });
    for (const c of comps) {
      const p = c.physics;
      if (p.spawn === 'fire') {
        for (const f of shots[c.uid]) {
          over = Math.max(over, f.stopT + f.lingerT);
          const s = shotAt(f, t);
          if (s) draws.push({ c, ...s });
        }
        continue;
      }
      const parents = shots[p.of] || [];
      if (c.type === 'slowmo') {
        for (const f of parents) f.events.forEach((ev, idx) => {
          if (ev.kind !== p.spawn) return;
          over = Math.max(over, ev.t);
          slows.push({ key: `${c.uid}:${f.k}:${idx}`, t: ev.t, x: ev.x, y: ev.y, scale: Math.max(0.01, p.slowScale ?? 0.3), duration: p.slowDuration ?? 0.5, ease: p.slowEase ?? 0.1, easeOut: p.slowEaseOut ?? p.slowEase ?? 0.1, scope: p.slowScope || 'game' });
        });
        continue;
      }
      const vr = p.vary || {};
      const life = Math.max(0.02, (p.life || 0.4) + (vr.life || 0));  // the longest a piece can last
      // a piece's own value within its ± range
      const pick = (key, seed, k, salt) => (p[key] ?? DEFAULT_PHYSICS[key] ?? 0) + (vr[key] ? (rand(seed + P.seed * 0.0137, k, salt) * 2 - 1) * vr[key] : 0);
      const piece = (x, y, age, seed, k, turn = 0) => {
        const own = Math.max(0.02, pick('life', seed, k, 5));
        if (age > own) return;
        const a = Math.min(1, age / own);
        const ang = rand(seed, k, 1) * Math.PI * 2, sp = Math.max(0, pick('scatter', seed, k, 6)) * (vr.scatter ? 1 : 0.5 + rand(seed, k, 2));
        const px = x + Math.cos(ang) * sp * age, py = y + Math.sin(ang) * sp * age - 0.5 * pick('gravity', seed, k, 7) * age * age;
        const sc = lerp(pick('scaleStart', seed, k, 8), pick('scaleEnd', seed, k, 9), a);
        // squash: flattened when it lands, then springing back with a wobble
        const q = (p.squash || 0) * Math.exp(-5 * a) * Math.cos(14 * a);
        const randomTurn = p.randomTurn ? rand(seed, k, 3) * Math.PI * 2 : 0;
        draws.push({ c, x: px, y: py, rot: turn + randomTurn + pick('spin', seed, k, 10) * age * Math.PI / 180, sx: sc * (1 + q), sy: sc * (1 - q), alpha: p.fade ? 1 - a : 1 });
      };
      if (p.spawn === 'trail' && p.style === 'ribbon') {
        // one continuous strip through where the shot was over the last `life` seconds, like Unity's TrailRenderer
        const span = Math.max(0.02, p.life || 0.5);
        for (const f of parents) {
          over = Math.max(over, f.stopT + span);
          const from = Math.max(f.t0, t - span), to = Math.min(f.stopT, t);
          if (to <= from) continue;
          const pts = [], step = 1 / 90;
          for (let ts = to; ts > from - 1e-6; ts -= step) {
            const sh = shotAt(f, Math.max(from, ts));
            if (sh) pts.push({ x: sh.x, y: sh.y, age: t - Math.max(from, ts) });
            if (ts - step < from && ts > from) { const e = shotAt(f, from); if (e) pts.push({ x: e.x, y: e.y, age: t - from }); break; }
          }
          if (pts.length > 1) draws.push({ c, ribbon: pts, span, x: 0, y: 0, rot: 0, sx: 1, sy: 1, alpha: 1 });
        }
        continue;
      }
      if (p.spawn === 'trail') {
        const rate = Math.max(1, p.rate || 30);
        for (const f of parents) {
          over = Math.max(over, f.stopT + life);
          const first = Math.ceil(Math.max(f.t0, t - life) * rate), last = Math.floor(Math.min(f.stopT, t) * rate);
          for (let j = first; j <= last; j++) {
            const s = shotAt(f, j / rate);
            if (!s) continue;
            let turn = 0;
            if (p.trailAlign) {
              // along the direction the shot was travelling when this piece was left
              const s2 = shotAt(f, j / rate + 0.004) || s;
              turn = Math.atan2(s2.y - s.y, s2.x - s.x || f.dir) - Math.atan2(0, f.dir);
            }
            piece(s.x, s.y, t - j / rate, f.ci * 31 + f.k, j, turn);
          }
        }
        continue;
      }
      for (const f of parents) {
        for (const ev of f.events) {
          if (ev.kind !== p.spawn) continue;
          over = Math.max(over, ev.t + life);
          const age = t - ev.t;
          if (age < 0 || age > life) continue;
          const many = Math.max(1, Math.round(pick('count', f.ci * 31 + f.k + ev.t * 7, 0, 11)));
          for (let k = 0; k < many; k++) piece(ev.x, ev.y, age, f.ci * 31 + f.k + ev.t * 7, k, ev.rot || 0);
        }
      }
    }
    for (const c of comps) for (const f of shots[c.uid] || []) f.events.forEach((ev, idx) => events.push({ key: `${c.uid}:${f.k}:${idx}`, kind: ev.kind, t: ev.t, x: ev.x, y: ev.y }));
    return { draws, over, slows, events };
  }
  // How fast time runs under the slow-downs going on (each eases in, holds, eases out; the slowest wins).
  function slowFactor(list, real) {
    let k = 1;
    for (let i = list.length - 1; i >= 0; i--) {
      const s = list[i], e = (real - s.start) / 1000, ease = Math.max(1e-4, s.ease), out = s.easeOut ?? s.ease, easeOut = Math.max(1e-4, out);
      let v;
      if (e < 0) v = 1;
      else if (e < s.ease) v = 1 + (s.scale - 1) * (e / ease);
      else if (e < s.ease + s.duration) v = s.scale;
      else if (e < s.ease + s.duration + out) v = s.scale + (1 - s.scale) * ((e - s.ease - s.duration) / easeOut);
      else { list.splice(i, 1); continue; }
      k = Math.min(k, v);
    }
    return k;
  }

  /* ---------------------------------------------------------------- drawing the shot, the launcher, the edit labels */

  // The trail's alpha along it (0 = at the shot, 1 = the tail end): the game's gradient keys, or a plain fade.
  function ribbonAlpha(p, a) {
    const keys = p.alphaKeys && p.alphaKeys.length ? p.alphaKeys : p.fade ? [[0, 1], [1, 0]] : [[0, 1], [1, 1]];
    if (a <= keys[0][0]) return keys[0][1];
    for (let i = 1; i < keys.length; i++) if (a <= keys[i][0]) return lerp(keys[i - 1][1], keys[i][1], (a - keys[i - 1][0]) / Math.max(1e-6, keys[i][0] - keys[i - 1][0]));
    return keys[keys.length - 1][1];
  }
  // Trails are a textured triangle strip (as the game's TrailRenderer builds them), rasterised here in plain JS: a 2D
  // canvas can't draw one without seams, and WebGL made the desktop app's window (WebKitGTK) flash black when it
  // switched compositing modes. Each pixel belongs to exactly one triangle, so there are no seams or doubled edges.
  const texCache = new Map();
  function texData(image) {
    let t = texCache.get(image);
    if (!t) {
      const c = document.createElement('canvas');
      c.width = image.naturalWidth; c.height = image.naturalHeight;
      const x = c.getContext('2d', { willReadFrequently: true });
      x.drawImage(image, 0, 0);
      t = { w: c.width, h: c.height, d: x.getImageData(0, 0, c.width, c.height).data };
      texCache.set(image, t);
    }
    return t;
  }
  const stripCanvas = document.createElement('canvas');
  // verts: [x, y, u, v, alpha] per vertex in canvas pixels, a triangle strip. Draws it onto ctx (identity transform).
  function drawStrip(image, verts, tile) {
    const n = verts.length / 5;
    if (n < 3) return;
    const tex = texData(image);
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (let i = 0; i < n; i++) { const x = verts[i * 5], y = verts[i * 5 + 1]; x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
    x0 = Math.max(0, Math.floor(x0)); y0 = Math.max(0, Math.floor(y0));
    x1 = Math.min(ctx.canvas.width, Math.ceil(x1)); y1 = Math.min(ctx.canvas.height, Math.ceil(y1));
    const bw = x1 - x0, bh = y1 - y0;
    if (bw <= 0 || bh <= 0 || bw * bh > 4e6) return;
    const out = new Float32Array(bw * bh * 4);  // premultiplied rgba
    const td = tex.d, tw = tex.w, th = tex.h;
    const sample = (u, v, o) => {
      u = tile ? u - Math.floor(u) : Math.min(1, Math.max(0, u));
      v = Math.min(1, Math.max(0, v));
      const fx = u * (tw - 1), fy = v * (th - 1);
      const ix = Math.floor(fx), iy = Math.floor(fy), ax = fx - ix, ay = fy - iy;
      const ix1 = Math.min(tw - 1, ix + 1), iy1 = Math.min(th - 1, iy + 1);
      for (let k = 0; k < 4; k++) {
        const a = td[(iy * tw + ix) * 4 + k], b = td[(iy * tw + ix1) * 4 + k], c = td[(iy1 * tw + ix) * 4 + k], d = td[(iy1 * tw + ix1) * 4 + k];
        o[k] = (a * (1 - ax) + b * ax) * (1 - ay) + (c * (1 - ax) + d * ax) * ay;
      }
    };
    const px = [0, 0, 0, 0];
    for (let t = 0; t + 2 < n; t++) {
      const A = t * 5, B = (t + 1) * 5, C = (t + 2) * 5;
      const ax = verts[A], ay = verts[A + 1], bx = verts[B], by = verts[B + 1], cx = verts[C], cy = verts[C + 1];
      const area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
      if (Math.abs(area) < 1e-6) continue;
      const tx0 = Math.max(x0, Math.floor(Math.min(ax, bx, cx))), tx1 = Math.min(x1 - 1, Math.ceil(Math.max(ax, bx, cx)));
      const ty0 = Math.max(y0, Math.floor(Math.min(ay, by, cy))), ty1 = Math.min(y1 - 1, Math.ceil(Math.max(ay, by, cy)));
      for (let y = ty0; y <= ty1; y++) {
        const sy = y + 0.5;
        for (let x = tx0; x <= tx1; x++) {
          const sx = x + 0.5;
          // barycentric weights; a pixel centre on a shared edge goes to one side only (half-open test)
          let w0 = ((bx - sx) * (cy - sy) - (by - sy) * (cx - sx)) / area;
          let w1 = ((cx - sx) * (ay - sy) - (cy - sy) * (ax - sx)) / area;
          const w2 = 1 - w0 - w1;
          if (w0 < 0 || w1 < 0 || w2 <= 0) continue;
          sample(w0 * verts[A + 2] + w1 * verts[B + 2] + w2 * verts[C + 2], w0 * verts[A + 3] + w1 * verts[B + 3] + w2 * verts[C + 3], px);
          const alpha = (w0 * verts[A + 4] + w1 * verts[B + 4] + w2 * verts[C + 4]) * px[3] / 255;
          if (alpha <= 0) continue;
          const o = ((y - y0) * bw + (x - x0)) * 4, keep = 1 - alpha;
          out[o] = px[0] * alpha + out[o] * keep; out[o + 1] = px[1] * alpha + out[o + 1] * keep;
          out[o + 2] = px[2] * alpha + out[o + 2] * keep; out[o + 3] = alpha + out[o + 3] * keep;
        }
      }
    }
    const img = new ImageData(bw, bh), d = img.data;
    for (let i = 0; i < bw * bh; i++) {
      const a = out[i * 4 + 3];
      if (a <= 0) continue;
      d[i * 4] = out[i * 4] / a; d[i * 4 + 1] = out[i * 4 + 1] / a; d[i * 4 + 2] = out[i * 4 + 2] / a; d[i * 4 + 3] = a * 255;
    }
    stripCanvas.width = bw; stripCanvas.height = bh;
    stripCanvas.getContext('2d').putImageData(img, 0, 0);
    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.drawImage(stripCanvas, x0, y0);
    ctx.restore();
  }
  function drawRibbon(d, base, W) {
    const slot = (d.slots || S.project.slots)[d.c.uid], p = d.c.physics;
    const layer = slot.layers.find(l => !l.hidden);
    if (!layer) return;
    const image = img(layer.src);
    if (!image.complete || !image.naturalWidth) return;
    const pts = d.ribbon, iw = image.naturalWidth, ih = image.naturalHeight;
    const lens = [0];
    for (let i = 1; i < pts.length; i++) lens.push(lens[i - 1] + Math.hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y));
    const total = Math.max(1e-3, lens[lens.length - 1]);
    const width = layer.h * (layer.sy ?? 1);
    // each point's half width and the normal there (averaged between its segments, mitred), as the game builds its trail mesh
    const half = pts.map(q => width / 2 * lerp(p.scaleStart ?? 1, p.scaleEnd ?? 1, Math.min(1, q.age / d.span)));
    const dirs = [];
    for (let i = 0; i < pts.length - 1; i++) { const dx = pts[i + 1].x - pts[i].x, dy = pts[i + 1].y - pts[i].y, l = Math.hypot(dx, dy) || 1; dirs.push([dx / l, dy / l]); }
    const M = mul(base, W), verts = [];
    pts.forEach((q, i) => {
      const d0 = dirs[Math.max(0, i - 1)], d1 = dirs[Math.min(dirs.length - 1, i)];
      let nx = -(d0[1] + d1[1]), ny = d0[0] + d1[0];
      const l = Math.hypot(nx, ny) || 1; nx /= l; ny /= l;
      const miter = Math.min(2, 1 / Math.max(0.5, nx * -d1[1] + ny * d1[0])), w = half[i] * miter;
      const u = p.textureMode === 'tile' ? lens[i] / Math.max(1, layer.w || iw) : lens[i] / total;
      const alpha = Math.max(0, Math.min(1, ribbonAlpha(p, Math.min(1, q.age / d.span))));
      const [x0, y0] = apply(M, q.x + nx * w, q.y + ny * w), [x1, y1] = apply(M, q.x - nx * w, q.y - ny * w);
      verts.push(x0, y0, u, 0, alpha, x1, y1, u, 1, alpha);
    });
    drawStrip(image, verts, p.textureMode === 'tile');
    ctx.globalAlpha = 1;
  }
  function drawPiece(d, base, W) {
    const slot = (d.slots || S.project.slots)[d.c.uid];
    if (!slot || slot.hidden) return;
    if (d.ribbon) return drawRibbon(d, base, W);
    const c = Math.cos(d.rot), s = Math.sin(d.rot);
    let m = mul(mul(W, [1, 0, 0, 1, d.x, d.y]), [c * d.sx, s * d.sx, -s * d.sy, c * d.sy, 0, 0]);
    if (d.mirror) m = mul(m, [-1, 0, 0, 1, 0, 0]);  // drawn for the shooter's facing, as the game scales it
    ctx.globalAlpha = Math.max(0, Math.min(1, d.alpha));
    for (const layer of slot.layers) {
      if (layer.hidden) continue;
      const image = img(layer.src);
      if (!image.complete || !image.naturalWidth) continue;
      ctx.setTransform(...mul(base, mul(mul(m, slotMatrix(layer)), [1, 0, 0, -1, 0, 0])));
      ctx.drawImage(image, -layer.w / 2, -layer.h / 2, layer.w, layer.h);
    }
    ctx.globalAlpha = 1;
  }
  function drawLauncher(base) {
    const [x, y] = apply(worldMatrix(), P.origin.x, P.origin.y);
    const t = targets()[0];
    const [tx, ty] = t ? apply(worldMatrix(), t.x, t.y) : [x - 1, y];
    const a = Math.atan2(ty - y, tx - x);
    ctx.save();
    ctx.setTransform(...base);
    ctx.strokeStyle = ctx.fillStyle = '#f0b23c';
    ctx.lineWidth = 2;
    ctx.globalAlpha = 0.25; ctx.beginPath(); ctx.arc(x, y, 16, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = 1; ctx.beginPath(); ctx.arc(x, y, 16, 0, Math.PI * 2); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(x + Math.cos(a) * 26, y + Math.sin(a) * 26);
    ctx.lineTo(x + Math.cos(a + 2.5) * 10, y + Math.sin(a + 2.5) * 10); ctx.lineTo(x + Math.cos(a - 2.5) * 10, y + Math.sin(a - 2.5) * 10); ctx.closePath(); ctx.fill();
    ctx.font = '11px system-ui, sans-serif'; ctx.textAlign = 'center'; ctx.fillText('Launcher', x, y + 32);
    ctx.restore();
  }
  // With Projectile paths on: each fired component's path, its bounces, and where it fades out.
  function drawShotPaths(base) {
    const list = targets(), W = worldMatrix();
    ctx.save();
    ctx.setTransform(...base);
    ctx.lineWidth = 2;
    S.project.components.forEach((c, ci) => {
      if (c.physics.spawn !== 'fire' || (S.project.slots[c.uid] || {}).hidden) return;
      for (const f of flights(c, ci, list, P.origin)) {
        ctx.strokeStyle = targetColor(f.k % list.length);
        ctx.beginPath();
        const steps = 60, stop = f.gone || 1;
        for (let i = 0; i <= steps; i++) { const q = flightPoint(f, i / steps * stop), [sx, sy] = apply(W, q.x, q.y); if (i) ctx.lineTo(sx, sy); else ctx.moveTo(sx, sy); }
        for (const hp of f.hops) for (let i = 1; i <= 12; i++) { const v = i / 12, [sx, sy] = apply(W, lerp(hp.from.x, hp.to.x, v), lerp(hp.from.y, hp.to.y, v) + hp.h * 4 * v * (1 - v)); ctx.lineTo(sx, sy); }
        ctx.stroke();
      }
    });
    ctx.restore();
  }
  wrap('drawGizmo', (orig, ...args) => {
    if (!isProj()) return orig(...args);
    const dpr = window.devicePixelRatio || 1, base = [dpr, 0, 0, dpr, 0, 0];
    if (!$('tab-pphysics').hidden) {
      // the shot's own clock: restarted by Fire, slowed by its slow motion components
      const now = performance.now();
      if (P.seenT0 !== P.t0) { P.seenT0 = P.t0; P.vt = Math.max(0, (now - P.t0) / 1000 * S.speed); P.slows = []; P.slowDone = new Set(); P.lastReal = now; }
      P.vt += Math.max(0, now - P.lastReal) / 1000 * S.speed * slowFactor(P.slows, now);
      P.lastReal = now;
      const t = P.vt;
      const { draws, over, slows } = simulate(t);
      for (const s of slows) if (t >= s.t && !P.slowDone.has(s.key)) { P.slowDone.add(s.key); P.slows.push({ start: now, scale: s.scale, duration: s.duration / S.speed, ease: s.ease / S.speed, easeOut: s.easeOut / S.speed }); }
      if (t > over + 0.6 && $('fireLoop').checked) refire(now);
      if ($('showPaths').checked) drawShotPaths(base);
      const W = worldMatrix();
      for (const d of draws) drawPiece(d, base, W);
      ctx.setTransform(...base);
      drawLauncher(base);
      return;
    }
    orig(...args);
    // the edit layout: each component's name under its column
    const V = viewMatrix();
    ctx.save();
    ctx.setTransform(...base);
    ctx.fillStyle = '#9aa0ab'; ctx.font = '12px system-ui, sans-serif'; ctx.textAlign = 'center';
    S.project.components.forEach((c, i) => { const [x, y] = apply(V, i * SPACING, -70); ctx.fillStyle = c.uid === S.selected ? '#f0b23c' : '#9aa0ab'; ctx.fillText(c.name, x, y); });
    ctx.restore();
  });
  $('fireBtn').addEventListener('click', () => { if (!isProj()) return; if ($('tab-pphysics').hidden) setTab('pphysics'); refire(); });

  // Dragging the launcher (the targets drag as in the combatant section).
  canvas.addEventListener('pointerdown', e => {
    if (!isProj() || $('tab-pphysics').hidden || e.button !== 0) return;
    const p = pointerPos(e), [x, y] = apply(worldMatrix(), P.origin.x, P.origin.y);
    if (Math.hypot(p[0] - x, p[1] - y) > 22) return;
    e.stopImmediatePropagation();
    canvas.setPointerCapture(e.pointerId);
    S.gesture = { type: 'launcher', p, x: P.origin.x, y: P.origin.y };
  }, true);
  canvas.addEventListener('pointermove', e => {
    if (!S.gesture || S.gesture.type !== 'launcher') return;
    e.stopImmediatePropagation();
    const p = pointerPos(e), g = S.gesture;
    P.origin = { x: g.x + (p[0] - g.p[0]) / S.view.zoom, y: g.y - (p[1] - g.p[1]) / S.view.zoom };
  }, true);

  /* ---------------------------------------------------------------- library: game projectiles open or drop in */

  wrap('projectileCard', (orig, name) => {
    const el = orig(name);
    if (name.startsWith('ForgeProjectile_')) {
      // uploaded from this section: its name, and a fresh picture after each upload
      el.querySelector('.cName').textContent = name.slice('ForgeProjectile_'.length) + ' ★';
      el.querySelector('img').src = `/projectileicon/${encodeURIComponent(name)}?v=${P.uploaded || 0}`;
      el.title = `${name}: made in the Projectiles section. ` + el.title.split('. ').slice(1).join('. ');
    }
    if (isProj()) {
      el.append(h('button', { class: 'openBtn', title: 'Open a copy of this game projectile to edit', onpointerdown: ev => ev.stopPropagation(),
        onclick: async ev => {
          ev.stopPropagation(); await leave();
          try {
            if (name.startsWith('ForgeProjectile_')) {
              // one made here: its project (rebuilt from the upload if its file is gone)
              const project = await api('/api/projectile_uploaded/' + encodeURIComponent(name));
              await refreshList();
              await openProjectile(project);
              toast(project.recovered ? `Rebuilt ${project.name || project.id} from what was uploaded and saved it as a project` : `Opened ${project.name || project.id}`);
            } else {
              await openProjectile(await api('/api/projectile_from_game/' + encodeURIComponent(name)));
              toast('Opened a copy: save it with a name to keep it');
            }
          } catch (err) { toast(err.message); }
        } }, 'Open'));
      el.title = `${projectileLabel(name)}. Drag it onto a component to use its art, or Open a copy to edit.`;
    }
    return el;
  });
  // A game projectile dragged onto a component adds its art as layers.
  wrap('abilityUnderPointer', (orig, e) => {
    if (!isProj()) return orig(e);
    const el = document.elementFromPoint(e.clientX, e.clientY);
    const row = el && el.closest('.compRow');
    document.querySelectorAll('.compRow').forEach(r => r.classList.toggle('target', r === row));
    return row ? { projSlot: row.dataset.slot, uid: row.dataset.slot } : null;
  });
  wrap('setAbilityProjectile', async (orig, target, name) => {
    if (!isProj() || !target.projSlot) return orig(target, name);
    document.querySelectorAll('.compRow.target').forEach(r => r.classList.remove('target'));
    try {
      const layers = await api('/api/projectile_layers/' + encodeURIComponent(name));
      const slot = S.project.slots[target.projSlot];
      slot.layers.push(...layers);
      if (layers.length) slot.active = layers[layers.length - 1].uid;
      S.selected = target.projSlot;
      touch(); renderSlots(); renderInspector();
      toast(`Added ${projectileLabel(name)}'s art to ${comp(target.projSlot).name}`);
    } catch (err) { toast(err.message); }
  });

  /* ---------------------------------------------------------------- the File menu in this section */

  const intercept = (id, fn) => $(id).addEventListener('click', e => { if (!isProj()) return; e.stopImmediatePropagation(); closeFileMenu(); fn(e); }, true);
  intercept('newBtn', async () => { await leave(); await openProjectile(await api('/api/projectile_new')); });
  intercept('existingBtn', () => {
    // the game's projectiles, in the library: Open a copy, or drag one onto a component
    S.side = S.spriteSide || 'pig'; S.cat = 'Projectile'; S.search = ''; $('searchInput').value = '';
    renderLibrary();
    toast('The game\'s projectiles are in the library: Open a copy, or drag one onto a component');
  });
  intercept('exportBtn', async () => {
    if (!S.project.id) { toast('Give the projectile a name first'); return; }
    await leave();
    window.location.href = '/api/projectile_export/' + S.project.id;
  });
  intercept('importBtn', () => $('projImportInput').click());
  $('projImportInput').addEventListener('change', async e => {
    const file = e.target.files[0];
    e.target.value = '';
    if (!file) return;
    const data = await new Promise((res, rej) => { const r = new FileReader(); r.onload = () => res(r.result); r.onerror = rej; r.readAsDataURL(file); });
    try {
      await leave();
      const result = await api('/api/projectile_import', { data });
      S.lib = result.library;
      await refreshList();
      await openProjectile(result.project);
      toast(`Imported ${result.project.name || result.project.id}`);
    } catch (err) { toast(err.message); }
  });
  intercept('duplicateBtn', async () => {
    await leave();
    const copy = await api('/api/projectile_duplicate', S.project);
    await refreshList();
    await openProjectile(copy);
    toast(`Duplicated as ${copy.name}`);
  });
  intercept('deleteBtn', async () => {
    const saved = S.project.id && P.projects.some(p => p.id === S.project.id);
    if (!confirm(saved ? `Delete ${S.project.name || S.project.id}? Its file moves to projects/projectiles/.trash.` : 'Discard this unsaved projectile?')) return;
    clearTimeout(saveTimer);
    S.deleting = true;
    try {
      if (saved) await api('/api/projectile_delete', { id: S.project.id });
      await refreshList();
      await openProjectile(await api('/api/projectile_new'));
    } finally { S.deleting = false; }
  });
  $('projectSelect').addEventListener('change', async e => {
    if (!isProj()) return;
    e.stopImmediatePropagation();
    const id = e.target.value;
    if (!id) return;
    closeFileMenu();
    await leave();
    await openProjectile(await api('/api/projectile_project/' + id));
  }, true);
  // Duplicate is offered for every projectile; Add variant is for game combatants only.
  wrap('syncExistingMode', (orig, ...args) => {
    orig(...args);
    if (isProj()) { $('duplicateBtn').hidden = false; $('variantBtn').hidden = true; $('exportBtn').disabled = false; $('deleteBtn').disabled = false; }
  });
  $('fileBtn').addEventListener('click', () => { if (isProj()) { $('duplicateBtn').hidden = false; $('variantBtn').hidden = true; $('exportBtn').disabled = false; $('deleteBtn').disabled = false; } });

  // One fired uploaded projectile in the combatant preview: f = { start: [x, y], list: [[x, y]...], centre, seed, mirror }.
  // Returns false once it's over.
  function drawShot(project, f, elapsed, base, W) {
    const pt = q => ({ x: q[0], y: q[1] });
    // game-wide slow motion slows the whole preview (S.slowmo, app.js); projectile-wide only this shot's own clock
    const real = performance.now();
    f.localSlows = f.localSlows || []; f.slowDone = f.slowDone || new Set();
    f.simT = (f.simT ?? 0) + Math.max(0, elapsed - (f.lastElapsed ?? 0)) * slowFactor(f.localSlows, real);
    f.lastElapsed = elapsed;
    elapsed = f.simT;
    const { draws, over, slows, events } = simulateWith(project, pt(f.start), f.list.map(pt), f.centre ? pt(f.centre) : null, elapsed, f.seed, f.ricochet);
    // its events reach the ability's effects (fired / hits / bounces / stops, and slow motion)
    f.eventsDone = f.eventsDone || new Set();
    if (f.onEvent) for (const ev of events) if (elapsed >= ev.t && !f.eventsDone.has(ev.key)) { f.eventsDone.add(ev.key); f.onEvent(ev.kind, ev.x, ev.y); }
    for (const s of slows) {
      if (elapsed < s.t || f.slowDone.has(s.key)) continue;
      f.slowDone.add(s.key);
      if (f.onEvent) f.onEvent('slowmo', s.x, s.y);
      const sp = f.speed || 1, slow = { start: real, scale: s.scale, duration: s.duration / sp, ease: s.ease / sp, easeOut: s.easeOut / sp };
      if (s.scope === 'projectile') f.localSlows.push(slow); else S.slowmo = slow;
    }
    for (const d of draws) { d.slots = project.slots; d.mirror = f.mirror; drawPiece(d, base, W); }
    return elapsed <= over + 0.1;
  }
  // Seconds from launch until a fired shot first hits (the combatant preview's "projectile hit counts as Impact").
  function firstHit(project, f) {
    const pt = q => ({ x: q[0], y: q[1] });
    const saved = P.seed;
    P.seed = f.seed;
    try {
      let best = Infinity;
      const list = f.list.map(pt), centre = f.centre ? pt(f.centre) : null;
      project.components.filter(c => !(project.slots[c.uid] || {}).hidden).forEach((c, ci) => {
        if (c.physics.spawn !== 'fire') return;
        const n = Math.max(1, Math.round(pv(c.physics, 'count', ci, 0)));
        for (const fl of flights(c, ci, centre && n === 1 && list.length > 1 ? [centre] : list, pt(f.start))) if (!fl.gone) best = Math.min(best, fl.t0 + fl.T);
      });
      return best === Infinity ? 0 : best;
    } finally { P.seed = saved; }
  }
  window.forgeProjectiles = { P, setMode, openProjectile, simulate, simulateWith, drawShot, firstHit, renderPhysics };
})();
