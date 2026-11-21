'use strict';
/* Turn Plan: the combatant's SkillCombos, drawn as a tree that grows downward from Start.
   (A chain layout, with moves that always follow each other on one level, is saved in layouts/decision_chains.)

   The game picks one combo at random (weighted by its Percentage) and then uses its chain of abilities one turn at a
   time; when the chain is used up, or a step can't be used, it picks again (BattleService.InitiateNewCombo). Picking a
   whole chain up front is the same as choosing a branch at every step, so chains that start the same are drawn as one
   path that branches: [Rally, Smash] 60% and [Rally, Slam] 40% is "Rally, then Smash 60% / Slam 40%".

   project.decisionOrder (absent = automatic) is { trees: [tree], active }, a tab each. A tree is { id, name, on, pct,
   kids: [node] }: only ticked (on) trees go to the game, and with more than one ticked the combatant picks one of them
   by pct at the start of every battle (each of its combos then starts with the marker "@tree<n>:<pct>", which
   ForgeDecisionTrees.cs reads; with one ticked the combos are written plain). A node is { id, ref, w, kids }:
     ref  an ability uid, 'keep:<skill id>' for a game skill the Forge keeps as it is, '@stop' (this path ends here,
          next to branches that go on), or '@decision': a step that takes one of its options (each a path of its
          own, in `options`, by w) and then carries on with its kids. A path inside a decision that ends goes on after
          the decision.
     w    its chance: a first turn's goes to the game as typed (the game takes each combo's share of their total); a
          later step's is its share of its siblings' total
   publish.decision_combos flattens it back into the game's combos. */
(() => {
  const STOP = '@stop', DECISION = '@decision';
  const NW = 150, NH = 50, GX = 14, GY = 26, DHEAD = 28, DPAD = 8;
  const newId = () => Math.random().toString(36).slice(2, 10);
  const keepList = () => (S.project.existing && S.project.existing.keepSkills) || S.project.keepSkills || [];

  // The abilities a decision can use: the combatant's non-passive ones, then game skills it keeps as they are.
  function choices() {
    const out = S.project.abilities.filter(a => a.type !== 'Passive').map(a => ({ ref: a.uid, name: a.name || 'Unnamed', type: a.type, color: a.color }));
    for (const k of keepList()) if (!k.passive) {
      const s = S.skills.find(x => x.id === k.id);
      out.push({ ref: 'keep:' + k.id, name: (s && s.name) || k.id, type: (s && s.type) || 'Attack', color: (s && s.color) || '888888' });
    }
    return out;
  }
  const choiceOf = ref => choices().find(c => c.ref === ref);

  /* ---------------------------------------------------------------- from and to the game's combos */

  // The game's own combos, while they still fit the abilities (as the Forge keeps them on upload); null otherwise.
  function gameCombos() {
    const P = S.project, live = P.abilities.filter(a => a.type !== 'Passive');
    if (isExisting()) {
      const ex = P.existing, origins = new Set(((ex.original || {}).abilities || []).map(a => a.origin));
      if (!ex.combos || !ex.combos.length || live.some(a => !origins.has(a.origin))) return null;
      return { combos: ex.combos, key: 'origin' };
    }
    if (P.combos && P.combos.length && !live.some(a => !a.source)) return { combos: P.combos, key: 'source' };
    return null;
  }
  // "@tree<n>:<pct>" at the start of a combo: which of the trees picked between at the start of a battle it is in
  const MARK = /^@tree(\w+):([\d.]+)$/;
  const markOf = c => ((c.ComboChain || [])[0] || '').match(MARK);
  // What the Forge writes when the order is left automatic: the game's own trees, or its supports then one attack.
  // The same inputs give the same order (and ids), so a drag can start on it before it is copied.
  let autoCache = { key: '', order: null };
  function autoOrder() {
    const game = gameCombos();
    const key = JSON.stringify([choices().map(c => [c.ref, c.type]), game]);
    if (autoCache.key === key) return autoCache.order;
    let seq = 0;
    const ids = () => 'auto' + (seq++);
    const trees = [];
    if (game) {
      const refOf = id => {
        const a = S.project.abilities.find(x => x.type !== 'Passive' && x[game.key] === id);
        if (a) return a.uid;
        return keepList().some(k => k.id === id && !k.passive) ? 'keep:' + id : null;
      };
      const groups = new Map();
      for (const c of game.combos) {
        const m = markOf(c), gid = m ? m[1] : '';
        if (!groups.has(gid)) groups.set(gid, { pct: m ? parseFloat(m[2]) : 100, list: [] });
        groups.get(gid).list.push({ pct: c.Percentage || 0, chain: (c.ComboChain || []).slice(m ? 1 : 0).map(refOf).filter(Boolean) });
      }
      for (const [, g] of groups) {
        const t = fromCombos(g.list, ids);
        if (t.kids.length) trees.push({ id: ids(), name: 'Tree ' + (trees.length + 1), on: true, pct: g.pct, kids: t.kids });
      }
    }
    if (!trees.length) {
      // publish.combos_for: the supports, then one of the attacks (each equally often)
      const all = choices(), supports = all.filter(c => c.type === 'Support'), attacks = all.filter(c => c.type === 'Attack' || c.type === 'Combo');
      const lists = attacks.length ? attacks.map(a => ({ pct: 100 / attacks.length, chain: [...supports, a].map(c => c.ref) }))
        : supports.length ? [{ pct: 100, chain: supports.map(c => c.ref) }] : [];
      trees.push({ id: ids(), name: 'Tree 1', on: true, pct: 100, kids: fromCombos(lists, ids).kids });
    }
    if (trees.length === 1) trees[0].name = 'Main';
    autoCache = { key, order: { trees, active: trees[0].id } };
    return autoCache.order;
  }
  // Chains that start the same share their first steps, and branch where they differ.
  function fromCombos(list, ids = newId) {
    const root = { kids: [] };
    for (const { pct, chain } of list) {
      if (!chain.length) continue;
      let node = root;
      for (const ref of chain) {
        let kid = node.kids.find(k => k.ref === ref);
        if (!kid) { kid = { id: ids(), ref, w: 0, kids: [], ends: 0, endW: 0 }; node.kids.push(kid); }
        kid.w += Math.max(0, pct);
        node = kid;
      }
      node.ends++;
      node.endW += Math.max(0, pct);
    }
    const tidy = n => {
      for (const k of n.kids) tidy(k);
      if (n.ends && n.kids.length) n.kids.push({ id: ids(), ref: STOP, w: n.endW, kids: [] });
      delete n.ends; delete n.endW;
    };
    root.kids.forEach(tidy);
    return root;
  }
  // A tree saved in the chain layout ({ chains: [seq] }, seq = { w, moves, branches }) as steps.
  function chainsToKids(seqs) {
    const out = [];
    for (const s of seqs || []) {
      const moves = s.moves || [], branches = s.branches || [];
      if (!moves.length) {
        if (!branches.length) out.push({ id: newId(), ref: STOP, w: s.w || 0, kids: [] });
        else { const total = branches.reduce((a, b) => a + Math.max(0, b.w || 0), 0); for (const k of chainsToKids(branches)) { k.w = total > 0 ? (s.w || 0) * Math.max(0, k.w || 0) / total : 0; out.push(k); } }
        continue;
      }
      const first = { id: newId(), ref: moves[0].ref, w: s.w || 0, kids: [] };
      let last = first;
      for (const m of moves.slice(1)) { const n = { id: newId(), ref: m.ref, w: 100, kids: [] }; last.kids.push(n); last = n; }
      last.kids = chainsToKids(branches);
      out.push(first);
    }
    return out;
  }
  const weight = n => Math.max(0, n.w || 0);
  // The game's combos of one tree: a first turn's chance as it is (the game takes each combo's share of their total),
  // times the shares of the steps on the way (each one's share of its siblings' total). A decision takes each of its
  // options by its share, and every path through it goes on with what comes after the decision.
  const MAX_COMBOS = 4000;
  function treeCombos(tree) {
    const out = [], known = new Set(choices().map(c => c.ref));
    const end = (chain, s, after) => {
      const i = after.findIndex(l => l && l.length);
      if (i >= 0) walk(after[i], chain, s, false, after.slice(i + 1));
      else if (chain.length && out.length < MAX_COMBOS) out.push({ pct: s, chain });
    };
    const walk = (nodes, chain, share, top, after) => {
      const total = nodes.reduce((a, n) => a + weight(n), 0);
      for (const n of nodes) {
        const s = top ? weight(n) : total > 0 ? share * weight(n) / total : 0;
        if (n.ref === STOP) { end(chain, s, after); continue; }
        if (n.ref === DECISION) {
          const next = [n.kids || [], ...after];
          if ((n.options || []).length) walk(n.options, chain, s, false, next); else end(chain, s, next);
          continue;
        }
        const c = known.has(n.ref) ? [...chain, n.ref] : chain;
        if (n.kids && n.kids.length) walk(n.kids, c, s, false, after); else end(c, s, after);
      }
    };
    walk(tree.kids || [], [], 100, true, []);
    return out;
  }
  // The ticked trees with their share of battles (they add up to 100).
  function onTrees(order) {
    const on = order.trees.filter(t => t.on), total = on.reduce((s, t) => s + Math.max(0, t.pct || 0), 0);
    return on.map(t => ({ t, share: on.length === 1 ? 100 : total > 0 ? 100 * Math.max(0, t.pct || 0) / total : 100 / on.length }));
  }
  // Everything that goes to the game (publish.decision_combos does the same): with one ticked tree its combos, with
  // more each tree's combos marked with the tree and its chance.
  function toCombos(order) {
    order = normal(order);
    const on = onTrees(order);
    return on.flatMap(({ t, share }, i) => treeCombos(t).map(c => ({ ...c, tree: t, treeShare: share, mark: on.length > 1 ? `@tree${i + 1}:${round(share, 2)}` : '' })));
  }
  // Older saves: an order before there were tabs is one tree; a tree saved in the chain layout becomes steps.
  function normal(order) {
    if (order && !order.trees) {
      const t = { id: newId(), name: 'Main', on: true, pct: 100, kids: order.kids || [] };
      for (const k of Object.keys(order)) delete order[k];
      Object.assign(order, { trees: [t], active: t.id });
    }
    if (order) for (const t of order.trees) if (!t.kids) { t.kids = chainsToKids(t.chains); delete t.chains; }
    return order;
  }

  /* ---------------------------------------------------------------- editing */

  const order = () => normal(S.project.decisionOrder) || autoOrder();
  const tree = () => { const o = order(); return o.trees.find(t => t.id === o.active) || o.trees[0]; };
  // Any edit makes the order the combatant's own (the automatic one is copied first).
  function editOrder(fn) {
    if (!S.project.decisionOrder) S.project.decisionOrder = JSON.parse(JSON.stringify(autoOrder()));
    const o = normal(S.project.decisionOrder);
    fn(o);
    o.trees.forEach(tidyTree);
    touch();
    render();
    if (typeof renderStats === 'function') renderStats();
  }
  // An edit of the tree on the open tab.
  const edit = fn => editOrder(o => fn(o.trees.find(t => t.id === o.active) || o.trees[0]));
  // A pick that only "ends here" is the same as a step with nothing after it.
  function tidyTree(n) {
    for (const k of n.kids || []) tidyTree(k);
    for (const k of n.options || []) tidyTree(k);
    if (n.kids && n.kids.length && n.kids.every(k => k.ref === STOP)) n.kids = [];
  }
  // Where a step is: the step, the list it is in (a step's kids or a decision's options), and that list's owner.
  function find(root, id) {
    const walk = (list, owner) => {
      for (const k of list) {
        if (k.id === id) return { node: k, list, owner };
        const r = walk(k.options || [], k) || walk(k.kids || [], k);
        if (r) return r;
      }
      return null;
    };
    return walk(root.kids || [], root);
  }
  const contains = (n, id) => n.id === id || (n.kids || []).some(k => contains(k, id)) || (n.options || []).some(k => contains(k, id));
  const shareIn = (n, list) => { const t = list.reduce((s, k) => s + weight(k), 0); return t > 0 ? 100 * weight(n) / t : 0; };
  // A new branch starts as often as the others on average (the only one: always).
  const freshWeight = ks => { if (!ks.length) return 100; const t = ks.reduce((s, k) => s + weight(k), 0); return t > 0 ? t / ks.length : 100; };
  function addTo(list, node) { node.w = freshWeight(list); list.push(node); }
  const addAfter = (parent, node) => addTo(parent.kids, node);
  // Setting one chance keeps the others in proportion, so they add up to 100%.
  function setShare(list, item, pct) {
    pct = Math.max(0, Math.min(100, pct));
    const shares = list.map(k => shareIn(k, list));
    list.forEach((k, i) => { k.w = shares[i]; });
    const others = list.filter(k => k !== item), was = others.reduce((a, k) => a + k.w, 0);
    item.w = others.length ? pct : 100;
    for (const k of others) k.w = was > 0 ? k.w * (100 - pct) / was : (100 - pct) / others.length;
  }
  // Whether editing one chance rebalances the others so they add up to 100% (a toggle; off, each is edited freely).
  const balancing = () => !!S.project.decisionBalance;
  function setChance(list, item, pct) {
    if (balancing()) setShare(list, item, pct);
    else item.w = Math.max(0, pct);
  }
  // Turning balancing on brings every set of chances to 100%, keeping their proportions.
  function balanceAll(o) {
    const fix = list => {
      const t = list.reduce((a, k) => a + weight(k), 0);
      for (const k of list) { k.w = t > 0 ? 100 * weight(k) / t : 100 / list.length; fix(k.kids || []); fix(k.options || []); }
    };
    for (const t of o.trees) fix(t.kids);
    const on = o.trees.filter(t => t.on), tot = on.reduce((a, t) => a + Math.max(0, t.pct || 0), 0);
    for (const t of on) t.pct = tot > 0 ? 100 * Math.max(0, t.pct || 0) / tot : 100 / on.length;
  }
  const sumOf = list => list.reduce((a, k) => a + weight(k), 0);
  // "Σ 130%" beside a set of chances that doesn't make 100.
  const totalBadge = (list, what) => list.length > 1 && Math.abs(sumOf(list) - 100) > 0.05
    ? h('span', { class: 'decTotal', title: `These ${what} add up to ${round(sumOf(list), 1)}%. In battle each counts as its share of that total.` }, `Σ ${round(sumOf(list), 1)}%`) : null;
  const clone = n => ({ ...n, id: newId(), kids: (n.kids || []).map(clone), ...(n.options ? { options: n.options.map(clone) } : {}) });
  const newNode = ref => ref === DECISION ? { id: newId(), ref, w: 0, options: [], kids: [] } : { id: newId(), ref, w: 0, kids: [] };

  // A drop: an ability (or a decision) from the palette, or a step (with what follows it) moved, or copied with Ctrl
  // held. 'after' a step adds it as a branch after that step; 'before' puts it in the step's place, with the step after
  // it; 'option' (on a decision) adds it as one more of the decision's options.
  function drop(payload, targetId, where, copy) {
    edit(root => {
      let node;
      if (payload.ref) node = newNode(payload.ref);
      else {
        const from = find(root, payload.id);
        if (!from) return;
        if (!copy && targetId && contains(from.node, targetId)) return;  // not into its own branches
        if (copy) node = clone(from.node);
        else { from.list.splice(from.list.indexOf(from.node), 1); node = from.node; }
      }
      if (!targetId) { addTo(root.kids, node); return; }
      const at = find(root, targetId);
      if (!at) return;
      if (where === 'option' && at.node.ref === DECISION) addTo(at.node.options, node);
      else if (where === 'before') {
        node.w = at.node.w;
        at.list.splice(at.list.indexOf(at.node), 1, node);
        addTo(node.kids, at.node);
      } else addTo(at.node.kids, node);
    });
  }

  /* ---------------------------------------------------------------- drawing */

  let menu = null;
  const closeMenu = () => { if (menu) { menu.remove(); menu = null; } };
  document.addEventListener('pointerdown', ev => { if (menu && !menu.contains(ev.target)) closeMenu(); }, true);
  // "+" on a step (what comes after it), on Start (a new first step) or on a decision (one more option).
  function openMenu(btn, targetId, where, extra) {
    closeMenu();
    const go = ref => () => { closeMenu(); drop({ ref }, targetId, where); };
    const opts = choices().map(c => h('button', { style: `--strip:#${c.color}`, onclick: go(c.ref) }, c.name, h('span', { class: 'dim' }, ' ' + c.type)));
    opts.push(h('button', { class: 'decisionOpt', title: 'A step that takes one of its options (each its own path) by chance, then carries on', onclick: go(DECISION) }, '◆ Decision'));
    if (extra) opts.push(h('button', { class: 'stopOpt', title: extra.title, onclick: go(STOP) }, extra.label));
    menu = h('div', { class: 'decMenu' }, opts);
    document.body.append(menu);
    const r = btn.getBoundingClientRect();
    menu.style.left = Math.min(r.left, innerWidth - 220) + 'px';
    menu.style.top = Math.max(8, Math.min(r.bottom + 2, innerHeight - menu.offsetHeight - 8)) + 'px';
  }

  let dragging = null;  // { id } or { ref } while a drag is going on
  // zones(ev, el) says where a drop on el lands: 'before', 'after' or 'option'
  function dropZone(el, targetId, zones) {
    const zone = ev => zones ? zones(ev, el) : 'after';
    const mark = z => { el.classList.toggle('dropBefore', z === 'before'); el.classList.toggle('dropAfter', z === 'after'); el.classList.toggle('dropOption', z === 'option'); };
    el.addEventListener('dragover', ev => {
      if (!dragging) return;
      if (dragging.id && !(ev.ctrlKey || ev.altKey) && targetId) {
        const f = find(tree(), dragging.id);
        if (f && contains(f.node, targetId)) return;
      }
      ev.preventDefault();
      ev.dataTransfer.dropEffect = dragging.ref || ev.ctrlKey || ev.altKey ? 'copy' : 'move';
      mark(zone(ev));
    });
    el.addEventListener('dragleave', () => mark(null));
    el.addEventListener('drop', ev => {
      ev.preventDefault();
      mark(null);
      const d = dragging;
      dragging = null;
      if (d) drop(d, targetId, zone(ev), ev.ctrlKey || ev.altKey);
    });
  }
  // a step: its top part puts the drop before it, the rest after it
  const stepZones = (ev, el) => ev.clientY - el.getBoundingClientRect().top < el.offsetHeight * 0.3 ? 'before' : 'after';
  // a decision: the top of its header is before it, the bottom strip after it, the rest a new option
  const decisionZones = (ev, el) => {
    const y = ev.clientY - el.getBoundingClientRect().top;
    return y < DHEAD * 0.35 ? 'before' : y > el.offsetHeight - 12 ? 'after' : 'option';
  };
  const dragSource = (el, payload) => {
    el.draggable = true;
    // pressing in a text box selects its text: the step isn't dragged from there
    el.addEventListener('pointerdown', ev => {
      if (!ev.target.closest('input, textarea, select')) return;
      el.draggable = false;
      const back = () => { el.draggable = true; removeEventListener('pointerup', back, true); };
      addEventListener('pointerup', back, true);
    });
    el.addEventListener('dragstart', ev => { ev.stopPropagation(); dragging = payload; ev.dataTransfer.effectAllowed = 'copyMove'; ev.dataTransfer.setData('text/plain', payload.ref || payload.id); el.classList.add('dragging'); });
    el.addEventListener('dragend', () => { dragging = null; el.classList.remove('dragging'); });
  };

  // Sizes, for the top-down layout: a set of branches side by side, each a step with what follows it underneath (a
  // decision is a box holding its options side by side, with what follows it under the box).
  function sizeSet(list) {
    const parts = list.map(n => ({ n, s: sizeNode(n) }));
    return { parts, w: parts.reduce((a, p) => a + p.s.w, 0) + GX * Math.max(0, parts.length - 1), h: parts.reduce((a, p) => Math.max(a, p.s.h), 0) };
  }
  function sizeNode(n) {
    let cw = NW, ch = NH, inner = null;
    if (n.ref === DECISION) {
      inner = sizeSet(n.options || []);
      cw = Math.max(NW + 110, inner.w + 2 * DPAD);
      ch = DHEAD + (inner.parts.length ? GY / 2 + inner.h + DPAD : 34);
    }
    const kids = n.kids && n.kids.length ? sizeSet(n.kids) : null;
    return { cw, ch, inner, kids, w: Math.max(cw, kids ? kids.w : 0), h: ch + (kids ? GY + kids.h : 0) };
  }

  // A redraw keeps where everything was scrolled to and which chance box had the focus (so its arrows can be clicked
  // again and again).
  function keepView(box) {
    const scrolled = [];
    for (let el = box.querySelector('.decScroll') || box; el; el = el.parentElement) if (el.scrollTop || el.scrollLeft) scrolled.push([el, el.scrollLeft, el.scrollTop]);
    const scroll = box.querySelector('.decScroll'), a = document.activeElement;
    const fk = a && box.contains(a) && a.dataset ? a.dataset.fk : null;
    return { scrolled, inner: scroll ? [scroll.scrollLeft, scroll.scrollTop] : null, fk };
  }
  function restoreView(box, v) {
    const scroll = box.querySelector('.decScroll');
    if (scroll && v.inner) [scroll.scrollLeft, scroll.scrollTop] = v.inner;
    for (const [el, l, t] of v.scrolled) if (el.isConnected) { el.scrollLeft = l; el.scrollTop = t; }
    if (v.fk) { const el = [...box.querySelectorAll('[data-fk]')].find(x => x.dataset.fk === v.fk); if (el) el.focus({ preventScroll: true }); }
  }
  function render() {
    const box = $('decisionOrder');
    if (!box) return;
    const view = keepView(box);
    try { draw(box); } finally { restoreView(box, view); }
  }
  // Shown or hidden: the viewer's choice, the same for every combatant (kept in this browser only).
  const HIDE_KEY = 'forge.turnPlanHidden';
  const isHidden = () => { try { return localStorage.getItem(HIDE_KEY) === '1'; } catch (e) { return false; } };
  const setHidden = on => { try { localStorage.setItem(HIDE_KEY, on ? '1' : '0'); } catch (e) { /* not kept */ } render(); };
  const toggleTitle = hidden => h('h3', { class: 'decToggle', role: 'button', tabindex: 0, 'aria-expanded': String(!hidden), title: hidden ? 'Show the Turn Plan' : 'Hide the Turn Plan',
    onclick: () => setHidden(!hidden), onkeydown: ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); setHidden(!hidden); } } },
    h('span', { class: 'decCaret' }, hidden ? '▸' : '▾'), ' Turn Plan');
  function draw(box) {
    closeMenu();
    if (!S.project || S.mode === 'projectile') { box.replaceChildren(); return; }
    box.classList.toggle('collapsed', isHidden());
    if (isHidden()) {
      const n = toCombos(order()).length;
      box.replaceChildren(h('div', { class: 'row between abilityHead' }, toggleTitle(true),
        h('span', { class: 'dim' }, `${S.project.decisionOrder ? 'Its own plan' : 'Automatic'} · ${n} combo${n === 1 ? '' : 's'}`)));
      return;
    }
    const own = !!S.project.decisionOrder, t = tree();
    const top = sizeSet(t.kids);
    const width = Math.max(NW, top.w) + 2, height = NH + (top.parts.length ? GY + top.h : 0) + 2;
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('width', width); svg.setAttribute('height', height); svg.setAttribute('class', 'decLinks');
    const canvasEl = h('div', { class: 'decCanvas', style: `width:${width}px;height:${height}px` }, svg);
    const link = (x1, y1, x2, y2, never) => {
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      const my = (y1 + y2) / 2;
      path.setAttribute('d', `M${x1},${y1} C${x1},${my} ${x2},${my} ${x2},${y2}`);
      if (never) path.setAttribute('class', 'never');
      svg.append(path);
    };
    // ctx: { overall: % of picks reaching this set, first: the tree's first steps, cont: something follows a path that
    // ends here (we're inside a decision with steps after it) }
    const placeSet = (set, list, x, y, from, ctx) => {
      for (const { n, s } of set.parts) {
        const sh = shareIn(n, list), all = ctx.first ? sh : ctx.overall * sh / 100;
        const bx = x + (s.w - s.cw) / 2;
        link(from[0], from[1], bx + s.cw / 2, y, all <= 0);
        const it = { n, list, share: sh, overall: all, first: ctx.first, cont: ctx.cont };
        if (n.ref === DECISION) {
          canvasEl.append(decisionEl(it, bx, y, s.cw, s.ch));
          if (s.inner.parts.length) placeSet(s.inner, n.options, bx + (s.cw - s.inner.w) / 2, y + DHEAD + GY / 2, [bx + s.cw / 2, y + DHEAD], { overall: all, first: false, cont: ctx.cont || !!(n.kids && n.kids.length), inside: true });
        } else canvasEl.append(nodeEl(it, bx, y));
        if (s.kids) placeSet(s.kids, n.kids, x + (s.w - s.kids.w) / 2, y + s.ch + GY, [bx + s.cw / 2, y + s.ch], { overall: all, first: false, cont: ctx.cont, inside: ctx.inside });
        x += s.w + GX;
      }
    };
    const sx = (width - NW) / 2;
    canvasEl.append(startEl(t, sx, 0));
    placeSet(top, t.kids, (width - top.w) / 2, NH + GY, [sx + NW / 2, NH], { overall: 100, first: true, cont: false, inside: false });
    const o = order(), combos = toCombos(o);
    const nameOf = ref => (choiceOf(ref) || {}).name || ref;
    const chip = (label, ref, color, title, cls = '') => { const c = h('span', { class: 'decChip ' + cls, style: `--strip:#${color}`, title }, label); dragSource(c, { ref }); return c; };
    const palette = h('div', { class: 'decPalette' },
      ...choices().map(c => chip(c.name, c.ref, c.color, 'Drag onto a step: its top part puts it before that step, the rest after it. Onto a decision box: a new option. Onto Start: a new first step')),
      chip('◆ Decision', DECISION, 'f0b23c', 'A step that takes one of its options (each its own path) by chance, then carries on with what comes after it', 'decisionChip'));
    box.replaceChildren(...[
      h('div', { class: 'row between abilityHead' }, toggleTitle(false),
        h('div', { class: 'row' },
          h('label', { class: 'check', title: 'On: changing one chance adjusts the others so they add up to 100%. Off: each chance is edited on its own, and in battle each counts as its share of their total.' },
            h('input', { type: 'checkbox', checked: balancing(), onchange: ev => {
              S.project.decisionBalance = ev.target.checked;
              if (ev.target.checked && S.project.decisionOrder) editOrder(balanceAll); else { touch(); render(); }
            } }), ' Keep chances at 100%'),
          own ? h('button', { class: 'danger', title: 'Delete all its trees and go back to the automatic plan', onclick: () => {
            if (!confirm('Reset the Turn Plan?\n\nThis deletes every tree on its tabs (ticked or not) and goes back to the automatic plan: ' + (gameCombos() ? 'the game\'s own plan for this combatant.' : 'its supports, then one of its attacks, each equally often.') + '\n\nYou can undo it with Ctrl+Z.')) return;
            delete S.project.decisionOrder; touch(); render(); if (typeof renderStats === 'function') renderStats();
          } }, 'Reset') : null)),
      h('p', { class: 'note' }, own
        ? 'This combatant\'s own plan. Each pick goes down one path from Start, a turn per step; where a step branches, it takes one branch by its chance. A decision takes one of its options by chance, then carries on below it. At the end of a path, or when a step can\'t be used, it attacks with its first ability instead and the next turn picks again from the start.'
        : (gameCombos() ? 'Automatic: the game\'s own plan for this combatant. ' : 'Automatic: its supports, then one of its attacks, each equally often. ') + 'Change anything here to make the plan its own.'),
      tabsBar(o),
      treeNote(o, t),
      palette,
      unusedNote(own, o),
      h('p', { class: 'dim note' }, 'Drag abilities and decisions from here onto the plan, and drag steps (with what follows them) to rearrange them. Hold Ctrl while dropping to copy.'),
      h('div', { class: 'decScroll' }, canvasEl),
      h('details', { class: 'decGame' }, h('summary', {}, `Saved as ${combos.length}${combos.length >= MAX_COMBOS ? '+' : ''} combo${combos.length === 1 ? '' : 's'} (the game's SkillCombos)`),
        ...combos.slice(0, 300).map(c => h('div', {}, c.mark ? h('span', { class: 'dim' }, `${c.tree.name || 'Tree'} (${round(c.treeShare, 1)}% of battles): `) : null,
          h('b', {}, round(c.pct, 2) + '%'), ' ' + c.chain.map(nameOf).join(' → '))))].filter(Boolean));
  }

  function startEl(t, x, y) {
    const el = h('div', { class: 'decNode root', style: `left:${x}px;top:${y}px;width:${NW}px;height:${NH}px` },
      h('div', { class: 'decName' }, 'Start'),
      h('div', { class: 'decRow' }, h('span', { class: 'dim' }, t.kids.length ? `${t.kids.length} first step${t.kids.length === 1 ? '' : 's'}` : 'Nothing to pick'),
        totalBadge(t.kids, 'first steps'),
        h('button', { class: 'iconBtn', title: 'Add a first step', 'aria-label': 'Add a first step', onclick: ev => openMenu(ev.currentTarget, null, 'after') }, '+')));
    dropZone(el, null, null);
    return el;
  }
  // A branch's chance (or "always" when it is the only one).
  function chanceField(it) {
    const { n, list } = it;
    if (list.length <= 1) return h('span', { class: 'dim' }, 'always');
    return h('span', { class: 'decPct' }, h('input', { type: 'number', min: 0, max: balancing() ? 100 : null, step: 5, 'data-fk': 'pct:' + n.id, value: round(balancing() ? it.share : weight(n), 1), 'aria-label': 'Chance of this branch',
      title: (it.first ? 'Chance of this first step at each pick' : 'Chance of this branch here') + (balancing() ? '; the others keep their proportions' : ''),
      onchange: ev => edit(root => { const f = find(root, n.id); if (f) setChance(f.list, f.node, num(ev.target.value, 0)); }) }), '%');
  }
  const overallOf = it => !it.first && Math.abs(it.overall - it.share) > 0.05 ? h('span', { class: 'dim', title: 'How often a pick goes this way, of all picks' }, `${round(it.overall, 1)}% of picks`) : null;
  const removeBtn = (n, title) => h('button', { class: 'iconBtn', title, 'aria-label': 'Remove',
    onclick: () => edit(root => { const f = find(root, n.id); if (f) f.list.splice(f.list.indexOf(f.node), 1); }) }, '×');
  // what a path ending here does: ends the pick, or (inside a decision with steps after it) goes on after the decision
  const endsLabel = it => it.cont ? { label: '↷ Nothing (goes on)', title: 'This path does nothing more here and goes on after the decision' } : { label: '↻ Ends here', title: 'A branch where the pick ends, and the next turn picks again from the start' };

  function nodeEl(it, x, y) {
    const { n } = it;
    const style = `left:${x}px;top:${y}px;width:${NW}px;height:${NH}px`;
    let el;
    if (n.ref === STOP) {
      const e = endsLabel(it);
      el = h('div', { class: 'decNode stop', style, title: e.title },
        h('div', { class: 'decName' }, e.label), h('div', { class: 'decRow' }, chanceField(it), overallOf(it), removeBtn(n, 'Remove this branch')));
    } else {
      const c = choiceOf(n.ref);
      const leaf = !n.kids || !n.kids.length;
      el = h('div', { class: 'decNode' + (c ? '' : ' missing') + (it.overall > 0 ? '' : ' never'), style: `${style};--strip:#${c ? c.color : 'ef6b6b'}`,
        title: c ? `${c.name} (${c.type})${leaf ? (it.cont ? '\nThen it goes on after the decision' : '\nThen the next turn picks again from the start') : ''}` : 'This ability was removed: the step is skipped' },
        h('div', { class: 'decName' }, c ? c.name : 'Removed ability', leaf ? h('span', { class: 'decAgain', 'aria-hidden': 'true' }, it.cont ? ' ↷' : ' ↻') : null),
        h('div', { class: 'decRow' }, chanceField(it), overallOf(it), leaf ? null : totalBadge(n.kids, 'branches'),
          h('button', { class: 'iconBtn', title: 'Add what comes next', 'aria-label': 'Add what comes next', onclick: ev => openMenu(ev.currentTarget, n.id, 'after', leaf ? null : endsLabel(it)) }, '+'),
          removeBtn(n, 'Remove this step and its branches')));
    }
    dropZone(el, n.id, n.ref === STOP ? null : stepZones);
    dragSource(el, { id: n.id });
    return el;
  }
  // A decision: a box holding its options side by side; what comes after it hangs below the box.
  function decisionEl(it, x, y, w, hgt) {
    const { n } = it;
    const opts = n.options || [];
    const el = h('div', { class: 'decNode decision' + (it.overall > 0 ? '' : ' never'), style: `left:${x}px;top:${y}px;width:${w}px;height:${hgt}px`,
      title: 'A decision: takes one of its options by chance, then carries on with what comes after it' },
      h('div', { class: 'decRow decHead' },
        h('span', { class: 'decName' }, '◆ Decision'),
        totalBadge(opts, 'options'),
        it.list.length > 1 ? chanceField(it) : null, overallOf(it),
        h('button', { class: 'iconBtn', title: 'Add an option', 'aria-label': 'Add an option', onclick: ev => openMenu(ev.currentTarget, n.id, 'option', { label: '↷ Nothing', title: 'An option that does nothing and goes on after the decision' }) }, '+ option'),
        h('button', { class: 'iconBtn', title: 'Add what comes after the decision', 'aria-label': 'Add what comes after the decision', onclick: ev => openMenu(ev.currentTarget, n.id, 'after', n.kids && n.kids.length ? endsLabel(it) : null) }, '+ after'),
        removeBtn(n, 'Remove this decision, its options and what follows it')),
      opts.length ? null : h('div', { class: 'dim decEmpty' }, 'Drop abilities here as its options'));
    dropZone(el, n.id, decisionZones);
    dragSource(el, { id: n.id });
    return el;
  }

  // The tabs: each a whole tree. Ticked ones go to the game; with more than one ticked, a battle picks one by its chance.
  function tabsBar(o) {
    const on = onTrees(o), shareOf = new Map(on.map(x => [x.t, x.share]));
    const tabs = o.trees.map(t => {
      const sel = t.id === o.active;
      const tick = h('input', { type: 'checkbox', checked: !!t.on, title: t.on ? 'Goes to the game (untick to keep it in the project only)' : 'Project only: not taken to the game. Tick it to send it',
        'aria-label': 'Send this tree to the game', onclick: ev => ev.stopPropagation(),
        onchange: ev => editOrder(x => { const tt = x.trees.find(y => y.id === t.id); tt.on = ev.target.checked; if (tt.on) { const others = x.trees.filter(y => y.on && y !== tt); tt.pct = others.length ? others.reduce((a, y) => a + Math.max(0, y.pct || 0), 0) / others.length || 100 : 100; } }) });
      const name = sel
        ? h('input', { class: 'decTabName', value: t.name || '', 'aria-label': 'Tree name', size: Math.max(4, (t.name || '').length), onclick: ev => ev.stopPropagation(),
            onchange: ev => editOrder(x => { x.trees.find(y => y.id === t.id).name = ev.target.value; }) })
        : h('span', {}, t.name || 'Tree');
      const pct = t.on && on.length > 1
        ? h('span', { class: 'decPct', title: 'Chance of this tree at the start of a battle' + (balancing() ? '; the other ticked trees keep their proportions' : '') },
            h('input', { type: 'number', min: 0, max: balancing() ? 100 : null, step: 5, 'data-fk': 'tree:' + t.id, value: round(balancing() ? shareOf.get(t) : Math.max(0, t.pct || 0), 1), 'aria-label': 'Chance of this tree', onclick: ev => ev.stopPropagation(),
              onchange: ev => editOrder(x => { if (balancing()) setTreeShare(x, t.id, num(ev.target.value, 0)); else x.trees.find(y => y.id === t.id).pct = Math.max(0, num(ev.target.value, 0)); }) }), '%')
        : t.on ? null : h('span', { class: 'dim' }, 'project only');
      const del = o.trees.length > 1 ? h('button', { class: 'iconBtn', title: 'Delete this tree', 'aria-label': 'Delete this tree',
        onclick: ev => { ev.stopPropagation(); if (!confirm(`Delete the tree "${t.name || 'Tree'}"?`)) return; editOrder(x => { x.trees = x.trees.filter(y => y.id !== t.id); if (x.active === t.id) x.active = x.trees[0].id; }); } }, '×') : null;
      const el = h('div', { class: 'decTab' + (sel ? ' on' : '') + (t.on ? '' : ' off'), role: 'tab', 'aria-selected': sel ? 'true' : 'false', tabindex: 0,
        onclick: () => { if (!sel) editOrderView(t.id); } }, tick, name, pct, del);
      // a step dragged onto a tab goes into that tree as a new first turn
      el.addEventListener('dragover', ev => { if (dragging && !sel) { ev.preventDefault(); el.classList.add('dropAfter'); } });
      el.addEventListener('dragleave', () => el.classList.remove('dropAfter'));
      el.addEventListener('drop', ev => {
        ev.preventDefault(); el.classList.remove('dropAfter');
        const d = dragging; dragging = null;
        if (!d) return;
        editOrder(x => {
          const src = x.trees.find(y => y.id === x.active), dst = x.trees.find(y => y.id === t.id);
          let node;
          if (d.ref) node = newNode(d.ref);
          else { const f = find(src, d.id); if (!f) return; node = clone(f.node); if (!(ev.ctrlKey || ev.altKey)) f.list.splice(f.list.indexOf(f.node), 1); }
          addAfter(dst, node);
        });
      });
      return el;
    });
    const add = copy => editOrder(x => {
      const cur = x.trees.find(y => y.id === x.active) || x.trees[0];
      const t = { id: newId(), name: copy ? (cur.name || 'Tree') + ' copy' : 'Tree ' + (x.trees.length + 1), on: false, pct: 100, kids: copy ? cur.kids.map(clone) : [] };
      x.trees.push(t);
      x.active = t.id;
    });
    // every ability its own one-step first turn, all equally likely
    const even = () => editOrder(x => {
      const cs = choices();
      const t = { id: newId(), name: 'Even', on: false, pct: 100, kids: cs.map(c => ({ id: newId(), ref: c.ref, w: round(100 / cs.length, 4), kids: [] })) };
      x.trees.push(t);
      x.active = t.id;
    });
    return h('div', { class: 'decTabs', role: 'tablist' }, ...tabs,
      h('button', { title: 'A new, empty tree (project only until you tick it)', onclick: () => add(false) }, '+ Tree'),
      h('button', { title: 'A new tree where every ability is its own first turn, all equally likely (project only until you tick it)', onclick: even }, '⚖ Even tree'),
      h('button', { title: 'A copy of this tree (project only until you tick it)', onclick: () => add(true) }, '⧉ Copy'));
  }
  // Switching tabs is a view change, not an edit (but the automatic order is never edited by it).
  function editOrderView(id) {
    if (S.project.decisionOrder) { normal(S.project.decisionOrder).active = id; queueSave(); }
    else autoOrder().active = id;
    render();
  }
  function setTreeShare(o, id, pct) {
    const on = o.trees.filter(t => t.on), shares = new Map(onTrees(o).map(x => [x.t, x.share]));
    const list = on.map(t => ({ t, w: shares.get(t) }));
    setShare(list, list.find(k => k.t.id === id), pct);
    for (const k of list) k.t.pct = round(k.w, 4);
  }
  function treeNote(o, t) {
    const on = onTrees(o);
    if (!on.length) return h('p', { class: 'warnNote' }, 'No tree is ticked, so the game gets no order: it always uses its first ability.');
    if (!t.on) return h('p', { class: 'dim note' }, 'This tree is project only: it isn\'t taken to the game. Tick its tab to send it.');
    if (on.length > 1) {
      const tot = on.reduce((a, x) => a + Math.max(0, x.t.pct || 0), 0);
      return h('p', { class: 'dim note' }, `At the start of every battle it picks one of the ${on.length} ticked trees by its chance, and follows only that tree for the whole battle.`,
        Math.abs(tot - 100) > 0.05 ? h('span', { class: 'decTotal' }, ` The ticked trees add up to ${round(tot, 1)}%: each counts as its share of that.`) : null);
    }
    return null;
  }

  // An ability left out of its own order is never used in battle.
  function unusedNote(own, o) {
    if (!own) return null;
    const used = new Set(), walk = n => { used.add(n.ref); (n.kids || []).forEach(walk); (n.options || []).forEach(walk); };
    onTrees(o).forEach(({ t }) => t.kids.forEach(walk));
    const left = choices().filter(c => !used.has(c.ref));
    return left.length ? h('p', { class: 'warnNote' }, `Not in the order, so never used: ${left.map(c => c.name).join(', ')}.`) : null;
  }

  // Drawn under the abilities, whenever they are.
  const orig = window.renderAbilities;
  window.renderAbilities = function (...args) { const r = orig.apply(this, args); render(); return r; };
  // The order in a line, for the Stats tab.
  function planText() {
    const names = ref => (choiceOf(ref) || {}).name || ref.replace(/^keep:/, '');
    const on = onTrees(order());
    if (!on.length) return 'Its Turn Plan has no ticked tree, so it always uses its first ability.';
    const line = t => treeCombos(t).map(c => `${round(c.pct, 1)}% ${c.chain.map(names).join(', then ')}`).join(' · ');
    if (!on.some(({ t }) => treeCombos(t).length)) return S.project.decisionOrder ? 'Its Turn Plan is empty, so it always uses its first ability.' : null;
    if (on.length === 1) return 'Each pick, by its Turn Plan (Abilities tab): ' + line(on[0].t) + '.';
    return 'Each battle it follows one of its Turn Plan trees (Abilities tab): ' + on.map(({ t, share }) => `${round(share, 1)}% ${t.name || 'Tree'} (${line(t)})`).join('; ') + '.';
  }
  window.forgeDecisions = { render, autoOrder, toCombos, fromCombos, tree, order, planText };
})();
