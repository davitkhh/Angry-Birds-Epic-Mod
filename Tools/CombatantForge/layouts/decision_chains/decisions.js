'use strict';
/* Ability Decision Order: the combatant's SkillCombos, at the bottom of the Abilities tab.

   The game picks one combo at random (weighted by its Percentage) and then uses its chain of abilities one turn at a
   time; when the chain is used up, or a step can't be used, it picks again (BattleService.InitiateNewCombo). Picking a
   whole chain up front is the same as choosing a branch wherever chains part ways, so combos that start the same are
   drawn as one chain that branches: [Rally, Smash] 60% and [Rally, Slam] 40% is "Rally, then Smash 60% / Slam 40%".

   project.decisionOrder (absent = automatic) is { trees: [tree], active }, a tab each. A tree is { id, name, on, pct,
   chains: [seq] }: only ticked (on) trees go to the game, and with more than one ticked the combatant picks one of them
   by pct at the start of every battle (each of its combos then starts with the marker "@tree<n>:<pct>", which
   ForgeDecisionTrees.cs reads; with one ticked the combos are written plain).
   A seq (a chain, or a branch of one) is { id, w, moves: [{ id, ref }], branches: [seq] }: its moves happen one after
   the other, then it takes one of its branches by w (their shares are w / the sum of w). A branch with no moves and no
   branches ends the pick there. A move's ref is an ability uid or 'keep:<skill id>' for a game skill the Forge keeps as
   it is. publish.decision_combos flattens it back into the game's combos. */
(() => {
  const H = 34, GY = 6, IND = 28;
  let W = 250, right = 250;  // the deepest row's width (the panel's, less its indent), and where every row ends
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
        const chains = fromCombos(g.list, ids);
        if (chains.length) trees.push({ id: ids(), name: 'Tree ' + (trees.length + 1), on: true, pct: g.pct, chains });
      }
    }
    if (!trees.length) {
      // publish.combos_for: the supports, then one of the attacks (each equally often)
      const all = choices(), supports = all.filter(c => c.type === 'Support'), attacks = all.filter(c => c.type === 'Attack' || c.type === 'Combo');
      const lists = attacks.length ? attacks.map(a => ({ pct: 100 / attacks.length, chain: [...supports, a].map(c => c.ref) }))
        : supports.length ? [{ pct: 100, chain: supports.map(c => c.ref) }] : [];
      trees.push({ id: ids(), name: 'Tree 1', on: true, pct: 100, chains: fromCombos(lists, ids) });
    }
    if (trees.length === 1) trees[0].name = 'Main';
    autoCache = { key, order: { trees, active: trees[0].id } };
    return autoCache.order;
  }
  // Combos that start the same share their first moves, and branch where they part ways.
  function fromCombos(list, ids = newId) {
    const root = { kids: [] };
    for (const { pct, chain } of list) {
      if (!chain.length) continue;
      let node = root;
      for (const ref of chain) {
        let kid = node.kids.find(k => k.ref === ref);
        if (!kid) { kid = { ref, w: 0, kids: [], ends: 0, endW: 0 }; node.kids.push(kid); }
        kid.w += Math.max(0, pct);
        node = kid;
      }
      node.ends++;
      node.endW += Math.max(0, pct);
    }
    const tidy = n => {
      for (const k of n.kids) tidy(k);
      if (n.ends && n.kids.length) n.kids.push({ ref: '@stop', w: n.endW, kids: [] });
    };
    root.kids.forEach(tidy);
    return root.kids.map(k => trieToSeq(k, ids));
  }
  // A step-by-step tree (one node per move, as orders were saved before chains) as chains: moves that always follow
  // each other become one chain, and it branches only where the path splits.
  function trieToSeq(node, ids = newId) {
    const seq = { id: ids(), w: node.w || 0, moves: [], branches: [] };
    let cur = node;
    for (;;) {
      seq.moves.push({ id: ids(), ref: cur.ref });
      const ks = cur.kids || [];
      if (ks.length === 1 && ks[0].ref !== '@stop') { cur = ks[0]; continue; }
      seq.branches = ks.map(k => k.ref === '@stop' ? { id: ids(), w: k.w || 0, moves: [], branches: [] } : trieToSeq(k, ids));
      break;
    }
    return seq;
  }
  const weight = s => Math.max(0, s.w || 0);
  // The game's combos of one tree: a chain's percentage as it is (the game takes each combo's share of their total),
  // times the shares of the branches on the way (each branch's share of its siblings' total).
  function treeCombos(tree) {
    const out = [], known = new Set(choices().map(c => c.ref));
    const walk = (list, chain, share, top) => {
      const total = list.reduce((a, s) => a + weight(s), 0);
      for (const s of list) {
        const sh = top ? weight(s) : total > 0 ? share * weight(s) / total : 0;
        const c = [...chain, ...s.moves.map(m => m.ref).filter(r => known.has(r))];
        if (s.branches.length) walk(s.branches, c, sh, false);
        else if (c.length) out.push({ pct: sh, chain: c });
      }
    };
    walk(tree.chains || [], [], 100, true);
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
  // Older saves: an order before there were tabs is one tree; a tree saved step by step becomes chains.
  function normal(order) {
    if (order && !order.trees) {
      const t = { id: newId(), name: 'Main', on: true, pct: 100, kids: order.kids || [] };
      for (const k of Object.keys(order)) delete order[k];
      Object.assign(order, { trees: [t], active: t.id });
    }
    if (order) for (const t of order.trees) if (!t.chains) {
      t.chains = (t.kids || []).filter(k => k.ref !== '@stop').map(k => trieToSeq(k));
      delete t.kids;
    }
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
    for (const t of o.trees) t.chains = tidyList(t.chains, true);
    touch();
    render();
    if (typeof renderStats === 'function') renderStats();
  }
  // An edit of the tree on the open tab.
  const edit = fn => editOrder(o => fn(o.trees.find(t => t.id === o.active) || o.trees[0]));
  const isEnd = s => !s.moves.length && !s.branches.length;
  // Keeps every chain in its simplest form: a lone branch is no branch (its moves just follow); branches that all end
  // are no branches; a branch (or chain) without moves of its own hands its branches up; an empty chain goes.
  function tidyList(list, top) {
    const out = [];
    for (const s of list) {
      s.branches = tidyList(s.branches, false);
      while (s.branches.length === 1) { const b = s.branches[0]; s.moves.push(...b.moves); s.branches = b.branches; }
      if (s.branches.length && s.branches.every(isEnd)) s.branches = [];
      if (!s.moves.length && s.branches.length) {
        const total = s.branches.reduce((a, b) => a + weight(b), 0);
        for (const b of s.branches) { b.w = total > 0 ? weight(s) * weight(b) / total : 0; out.push(b); }
        continue;
      }
      if (top && isEnd(s)) continue;
      out.push(s);
    }
    return out;
  }
  // Where an id is: a chain or branch (in its list, under its parent) or a move (in its seq).
  function locate(t, id) {
    const walk = (list, parent) => {
      for (const s of list) {
        if (s.id === id) return { kind: 'seq', seq: s, list, parent };
        const m = s.moves.find(x => x.id === id);
        if (m) return { kind: 'move', move: m, seq: s };
        const r = walk(s.branches, s);
        if (r) return r;
      }
      return null;
    };
    return walk(t.chains, null);
  }
  const containsId = (s, id) => s.id === id || s.moves.some(m => m.id === id) || s.branches.some(b => containsId(b, id));
  const shareIn = (s, list) => { const t = list.reduce((a, k) => a + weight(k), 0); return t > 0 ? 100 * weight(s) / t : 0; };
  // A new branch (or chain) starts as often as the others on average (the only one: always).
  const freshWeight = list => { if (!list.length) return 100; const t = list.reduce((a, k) => a + weight(k), 0); return t > 0 ? t / list.length : 100; };
  // Setting one branch's share keeps the others in proportion, so they always add up to 100%.
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
      for (const k of list) { k.w = t > 0 ? 100 * weight(k) / t : 100 / list.length; fix(k.branches || []); }
    };
    for (const t of o.trees) fix(t.chains);
    const on = o.trees.filter(t => t.on), tot = on.reduce((a, t) => a + Math.max(0, t.pct || 0), 0);
    for (const t of on) t.pct = tot > 0 ? 100 * Math.max(0, t.pct || 0) / tot : 100 / on.length;
  }
  const sumOf = list => list.reduce((a, k) => a + weight(k), 0);
  // "Adds up to 130%" beside a set of chances that doesn't make 100.
  const totalBadge = (list, what) => list.length > 1 && Math.abs(sumOf(list) - 100) > 0.05
    ? h('span', { class: 'decTotal', title: `These ${what} add up to ${round(sumOf(list), 1)}%. In battle each counts as its share of that total.` }, `Σ ${round(sumOf(list), 1)}%`) : null;
  const cloneSeq = s => ({ ...s, id: newId(), moves: s.moves.map(m => ({ ...m, id: newId() })), branches: s.branches.map(cloneSeq) });
  const newSeq = moves => ({ id: newId(), w: 0, moves, branches: [] });
  // Another way on after a chain's moves; the first one also keeps the old way (ending there) as a branch.
  function addBranch(seq, branch) {
    if (!seq.branches.length) seq.branches.push({ ...newSeq([]), w: 100 });
    branch.w = freshWeight(seq.branches);
    seq.branches.push(branch);
  }
  function addChain(t, chain) { chain.w = freshWeight(t.chains); t.chains.push(chain); }

  // A drop. What: an ability from the palette ({ ref }), or a move or a chain/branch ({ id }) moved, or copied with Ctrl
  // held. Where: on a move, it goes just before or after it in that chain (a chain dropped there becomes a branch of
  // it); on a chain or branch, a move goes at the end of its moves and a chain becomes one of its branches; anywhere
  // else, it starts a new chain.
  function drop(payload, targetId, where, copy, intoTree) {
    editOrder(o => {
      const src = o.trees.find(x => x.id === o.active) || o.trees[0];
      const t = intoTree ? o.trees.find(x => x.id === intoTree) : src;
      let item;  // { move } or { seq }
      if (payload.ref) item = { move: { id: newId(), ref: payload.ref } };
      else {
        const at = locate(src, payload.id);
        if (!at || payload.id === targetId) return;
        if (at.kind === 'seq' && targetId && !copy && containsId(at.seq, targetId)) return;  // not into itself
        if (at.kind === 'move') {
          item = { move: copy ? { ...at.move, id: newId() } : at.move };
          if (!copy) at.seq.moves.splice(at.seq.moves.indexOf(at.move), 1);
        } else {
          item = { seq: copy ? cloneSeq(at.seq) : at.seq };
          if (!copy) at.list.splice(at.list.indexOf(at.seq), 1);
        }
      }
      const target = targetId ? locate(t, targetId) : null;
      if (!target) { addChain(t, item.seq || newSeq([item.move])); return; }
      if (target.kind === 'move') {
        if (item.seq) { addBranch(target.seq, item.seq); return; }
        const i = target.seq.moves.indexOf(target.move);
        target.seq.moves.splice(where === 'before' ? i : i + 1, 0, item.move);
        return;
      }
      if (item.seq) addBranch(target.seq, item.seq);
      else target.seq.moves.push(item.move);
    });
  }

  /* ---------------------------------------------------------------- drawing */

  let menu = null;
  const closeMenu = () => { if (menu) { menu.remove(); menu = null; } };
  document.addEventListener('pointerdown', ev => { if (menu && !menu.contains(ev.target)) closeMenu(); }, true);
  // "+" on a chain or branch: a move at the end of its moves, or another branch; "+ Chain": a new chain.
  function openMenu(btn, seqId) {
    closeMenu();
    const cs = choices();
    const pick = (label, fn) => [h('div', { class: 'decMenuHead' }, label), ...cs.map(c => h('button', { style: `--strip:#${c.color}`, onclick: () => { closeMenu(); fn(c.ref); } }, c.name, h('span', { class: 'dim' }, ' ' + c.type)))];
    let opts;
    if (!seqId) opts = pick('A new chain starting with', ref => drop({ ref }, null, 'after'));
    else {
      const at = locate(tree(), seqId) || {};
      const seq = at.seq || { branches: [] };
      opts = [...pick('Add a move (always, after its moves)', ref => drop({ ref }, seqId, 'after')),
        ...pick('Add a branch starting with', ref => edit(t => { const a = locate(t, seqId); if (a) addBranch(a.seq, newSeq([{ id: newId(), ref }])); })),
        seq.branches.length && !seq.branches.some(isEnd) ? h('button', { class: 'stopOpt', title: 'A branch where the pick ends after these moves, and the next turn picks again from the start',
          onclick: () => { closeMenu(); edit(t => { const a = locate(t, seqId); if (a) addBranch(a.seq, newSeq([])); }); } }, '↻ Add a branch that ends here') : null].filter(Boolean);
    }
    menu = h('div', { class: 'decMenu' }, cs.length ? opts : h('span', { class: 'dim' }, 'No attack or support abilities yet.'));
    document.body.append(menu);
    const r = btn.getBoundingClientRect();
    menu.style.left = Math.min(r.left, innerWidth - 240) + 'px';
    menu.style.top = Math.max(8, Math.min(r.bottom + 2, innerHeight - menu.offsetHeight - 8)) + 'px';
  }

  let dragging = null;  // { id } or { ref } while a drag is going on
  function dropZone(el, targetId, allowBefore) {
    const zone = ev => !allowBefore || ev.clientX - el.getBoundingClientRect().left > el.offsetWidth * 0.3 ? 'after' : 'before';
    el.addEventListener('dragover', ev => {
      if (!dragging) return;
      ev.stopPropagation();
      if (dragging.id && targetId && !(ev.ctrlKey || ev.altKey)) {
        const at = locate(tree(), dragging.id);
        if (dragging.id === targetId || (at && at.kind === 'seq' && containsId(at.seq, targetId))) return;
      }
      ev.preventDefault();
      ev.dataTransfer.dropEffect = dragging.ref || ev.ctrlKey || ev.altKey ? 'copy' : 'move';
      el.classList.toggle('dropBefore', zone(ev) === 'before');
      el.classList.toggle('dropAfter', zone(ev) === 'after');
    });
    el.addEventListener('dragleave', () => el.classList.remove('dropBefore', 'dropAfter'));
    el.addEventListener('drop', ev => {
      ev.preventDefault();
      ev.stopPropagation();
      el.classList.remove('dropBefore', 'dropAfter');
      const d = dragging;
      dragging = null;
      if (d) drop(d, targetId, zone(ev), ev.ctrlKey || ev.altKey);
    });
  }
  const dragSource = (el, payload) => {
    el.draggable = true;
    // pressing in a text box selects its text: the block isn't dragged from there
    el.addEventListener('pointerdown', ev => {
      if (!ev.target.closest('input, textarea, select')) return;
      el.draggable = false;
      const back = () => { el.draggable = true; removeEventListener('pointerup', back, true); };
      addEventListener('pointerup', back, true);
    });
    el.addEventListener('dragstart', ev => { ev.stopPropagation(); dragging = payload; ev.dataTransfer.effectAllowed = 'copyMove'; ev.dataTransfer.setData('text/plain', payload.ref || payload.id); el.classList.add('dragging'); });
    el.addEventListener('dragend', () => { dragging = null; el.classList.remove('dragging'); });
  };

  function render() {
    const box = $('decisionOrder');
    if (!box) return;
    closeMenu();
    if (!S.project || S.mode === 'projectile') { box.replaceChildren(); return; }
    const own = !!S.project.decisionOrder, t = tree();
    // layout: an outline, top to bottom. A chain's moves sit one level in, in order (each always follows the one
    // before); its branches come after them on that level, each with its own moves one level further in.
    const rows = [];
    let depth = 0;
    const laySeq = (s, list, d, parentRow, overall, turn, top) => {
      const sh = shareIn(s, list), all = overall * sh / 100;  // (its share of the chains' or branches' total)
      const row = { kind: 'seq', s, list, d, parentRow, share: sh, overall: all, top, i: rows.length, turn };
      rows.push(row);
      depth = Math.max(depth, d);
      s.moves.forEach((m, k) => {
        rows.push({ kind: 'move', m, s, d: d + 1, parentRow: row, overall: all, turn: turn + k + 1, last: k === s.moves.length - 1 && !s.branches.length, i: rows.length });
        depth = Math.max(depth, d + 1);
      });
      for (const b of s.branches) laySeq(b, s.branches, d + 1, row, all, turn + s.moves.length, false);
    };
    for (const c of t.chains) laySeq(c, t.chains, 0, null, 100, 0, true);
    W = Math.max(220, Math.min(460, (box.clientWidth || 440) - 22 - depth * IND));
    right = W + depth * IND;  // every row ends at the same right edge, so the chances line up
    const px = d => d * IND, py = r => r * (H + GY);
    const width = right + 4, height = Math.max(H, rows.length * (H + GY) - GY);
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('width', width); svg.setAttribute('height', height); svg.setAttribute('class', 'decLinks');
    const canvasEl = h('div', { class: 'decCanvas', style: `width:${width}px;height:${height}px` }, svg);
    for (const row of rows) {
      const p = row.parentRow;
      if (p) {
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', `M${px(p.d) + 12},${py(p.i) + H} V${py(row.i) + H / 2} H${px(row.d)}`);
        path.setAttribute('class', row.overall > 0 ? '' : 'never');
        svg.append(path);
      }
      canvasEl.append(row.kind === 'seq' ? seqEl(row, px(row.d), py(row.i)) : moveEl(row, px(row.d), py(row.i)));
    }
    if (!rows.length) canvasEl.append(h('p', { class: 'dim', style: 'margin:0' }, 'Empty: drag an ability here, or use + Chain.'));
    const scroll = h('div', { class: 'decScroll' }, canvasEl);
    dropZone(scroll, null, false);
    const o = order(), combos = toCombos(o);
    const nameOf = ref => (choiceOf(ref) || {}).name || ref;
    const palette = h('div', { class: 'decPalette' }, ...choices().map(c => {
      const chip = h('span', { class: 'decChip', style: `--strip:#${c.color}`, title: 'Drag onto a move to put it after that move (or onto its left edge for before), onto a chain or branch to add it at the end of its moves, or onto an empty spot for a new chain' }, c.name);
      dragSource(chip, { ref: c.ref });
      return chip;
    }));
    box.replaceChildren(...[
      h('div', { class: 'row between abilityHead' }, h('h3', {}, 'Ability Decision Order'),
        h('div', { class: 'row' },
          h('label', { class: 'check', title: 'On: changing one chance adjusts the others so they add up to 100%. Off: each chance is edited on its own, and in battle each counts as its share of their total.' },
            h('input', { type: 'checkbox', checked: balancing(), onchange: ev => {
              S.project.decisionBalance = ev.target.checked;
              if (ev.target.checked && S.project.decisionOrder) editOrder(balanceAll); else { touch(); render(); }
            } }), ' Keep chances at 100%'),
          own ? h('button', { class: 'danger', title: 'Delete all its trees and go back to the automatic order', onclick: () => {
            if (!confirm('Reset the Ability Decision Order?\n\nThis deletes every tree on its tabs (ticked or not) and goes back to the automatic order: ' + (gameCombos() ? 'the game\'s own order for this combatant.' : 'its supports, then one of its attacks, each equally often.') + '\n\nYou can undo it with Ctrl+Z.')) return;
            delete S.project.decisionOrder; touch(); render(); if (typeof renderStats === 'function') renderStats();
          } }, 'Reset') : null)),
      h('p', { class: 'note' }, own
        ? 'This combatant\'s own order. Each pick takes one chain by its chance and uses its moves in order, one a turn; where a chain branches, it then takes one branch by its chance. At the end, or when a move can\'t be used, it attacks with its first ability instead and the next turn picks again.'
        : (gameCombos() ? 'Automatic: the game\'s own order for this combatant. ' : 'Automatic: its supports, then one of its attacks, each equally often. ') + 'Change anything here to make the order its own.'),
      tabsBar(o),
      treeNote(o, t),
      palette,
      unusedNote(own, o),
      h('p', { class: 'dim note' }, 'Drag abilities from here into a chain. Moves, chains and branches can all be dragged to rearrange them; hold Ctrl while dropping to copy.'),
      h('div', { class: 'row' }, h('button', { title: 'A new chain: one more way a pick can go', onclick: ev => openMenu(ev.currentTarget, null) }, '+ Chain'), totalBadge(t.chains, 'chains')),
      scroll,
      h('details', { class: 'decGame' }, h('summary', {}, `Saved as ${combos.length} combo${combos.length === 1 ? '' : 's'} (the game's SkillCombos)`),
        ...combos.map(c => h('div', {}, c.mark ? h('span', { class: 'dim' }, `${c.tree.name || 'Tree'} (${round(c.treeShare, 1)}% of battles): `) : null,
          h('b', {}, round(c.pct, 2) + '%'), ' ' + c.chain.map(nameOf).join(' → '))))].filter(Boolean));
  }

  const rowStyle = (x, y) => `left:${x}px;top:${y}px;width:${right - x}px;height:${H}px`;
  // A chain (top level) or a branch: its chance among its siblings, and its own + and ×.
  function seqEl(row, x, y) {
    const { s, list, top } = row;
    const pctField = list.length > 1
      ? h('span', { class: 'decPct' }, h('input', { type: 'number', min: 0, max: balancing() ? 100 : null, step: 1, value: round(balancing() ? row.share : weight(s), 1), 'aria-label': top ? 'Chance of this chain' : 'Chance of this branch',
          title: (top ? 'Chance of this chain at each pick' : 'Chance of this branch here') + (balancing() ? '; the others keep their proportions' : ''),
          onchange: ev => edit(t => { const a = locate(t, s.id); if (a) setChance(a.list, a.seq, num(ev.target.value, 0)); }) }), '%')
      : h('span', { class: 'decAlways' }, 'always');
    const overall = !top && Math.abs(row.overall - row.share) > 0.05 ? h('span', { class: 'decOverall', title: 'How often a pick goes this way, of all picks' }, `${round(row.overall, 1)}% overall`) : null;
    const remove = h('button', { class: 'iconBtn', title: top ? 'Remove this chain' : 'Remove this branch', 'aria-label': 'Remove',
      onclick: () => edit(t => { const a = locate(t, s.id); if (a) a.list.splice(a.list.indexOf(a.seq), 1); }) }, '×');
    const end = isEnd(s);
    const label = top ? 'Chain' : end ? '↻ Ends here' : 'Branch';
    const sub = end ? null : h('span', { class: 'decSub' }, [s.moves.length ? `${s.moves.length} move${s.moves.length === 1 ? '' : 's'}` : '', s.branches.length ? `${s.branches.length} branches` : ''].filter(Boolean).join(', '));
    const el = h('div', { class: 'decNode block' + (top ? ' chain' : end ? ' stop' : ' branch') + (row.overall > 0 ? '' : ' never'), style: rowStyle(x, y),
      title: end ? 'The pick ends here: the next turn picks again from the start' : top ? 'A chain: its moves happen one after the other' : 'A branch: taken by its chance, then its moves happen one after the other' },
      h('div', { class: 'decName' }, label, sub ? ' ' : null, sub), totalBadge(s.branches, 'branches'), overall,
      end ? null : h('button', { class: 'iconBtn', title: 'Add a move or a branch', 'aria-label': 'Add a move or a branch', onclick: ev => openMenu(ev.currentTarget, s.id) }, '+'),
      remove, pctField);
    if (!end) dropZone(el, s.id, false);
    dragSource(el, { id: s.id });
    return el;
  }
  // A move: always happens after the one above it in its chain.
  function moveEl(row, x, y) {
    const { m } = row;
    const c = choiceOf(m.ref);
    const remove = h('button', { class: 'iconBtn', title: 'Remove this move', 'aria-label': 'Remove this move',
      onclick: () => edit(t => { const a = locate(t, m.id); if (a) a.seq.moves.splice(a.seq.moves.indexOf(a.move), 1); }) }, '×');
    const el = h('div', { class: 'decNode move' + (c ? '' : ' missing') + (row.overall > 0 ? '' : ' never'), style: `${rowStyle(x, y)};--strip:#${c ? c.color : 'ef6b6b'}`,
      title: c ? `Turn ${row.turn}: ${c.name} (${c.type})${row.last ? '\nThen the next turn picks again from the start' : ''}` : 'This ability was removed: the move is skipped' },
      h('span', { class: 'decTurnNo', title: 'Turn ' + row.turn }, String(row.turn)),
      h('div', { class: 'decName' }, c ? c.name : 'Removed ability', row.last ? h('span', { class: 'decAgain', 'aria-hidden': 'true' }, ' ↻') : null),
      remove);
    dropZone(el, m.id, true);
    dragSource(el, { id: m.id });
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
        ? h('span', { class: 'decPct', title: 'Chance of this tree at the start of a battle; the other ticked trees keep their proportions' },
            h('input', { type: 'number', min: 0, max: balancing() ? 100 : null, step: 1, value: round(balancing() ? shareOf.get(t) : Math.max(0, t.pct || 0), 1), 'aria-label': 'Chance of this tree', onclick: ev => ev.stopPropagation(),
              onchange: ev => editOrder(x => { if (balancing()) setTreeShare(x, t.id, num(ev.target.value, 0)); else x.trees.find(y => y.id === t.id).pct = Math.max(0, num(ev.target.value, 0)); }) }), '%')
        : t.on ? null : h('span', { class: 'dim' }, 'project only');
      const del = o.trees.length > 1 ? h('button', { class: 'iconBtn', title: 'Delete this tree', 'aria-label': 'Delete this tree',
        onclick: ev => { ev.stopPropagation(); if (!confirm(`Delete the tree "${t.name || 'Tree'}"?`)) return; editOrder(x => { x.trees = x.trees.filter(y => y.id !== t.id); if (x.active === t.id) x.active = x.trees[0].id; }); } }, '×') : null;
      const el = h('div', { class: 'decTab' + (sel ? ' on' : '') + (t.on ? '' : ' off'), role: 'tab', 'aria-selected': sel ? 'true' : 'false', tabindex: 0,
        onclick: () => { if (!sel) editOrderView(t.id); } }, tick, name, pct, del);
      // anything dragged onto another tab goes into that tree as a new chain
      el.addEventListener('dragover', ev => { if (dragging && !sel) { ev.preventDefault(); el.classList.add('dropAfter'); } });
      el.addEventListener('dragleave', () => el.classList.remove('dropAfter'));
      el.addEventListener('drop', ev => {
        ev.preventDefault(); el.classList.remove('dropAfter');
        const d = dragging; dragging = null;
        if (d) drop(d, null, 'after', ev.ctrlKey || ev.altKey, t.id);
      });
      return el;
    });
    const add = copy => editOrder(x => {
      const cur = x.trees.find(y => y.id === x.active) || x.trees[0];
      const t = { id: newId(), name: copy ? (cur.name || 'Tree') + ' copy' : 'Tree ' + (x.trees.length + 1), on: false, pct: 100, chains: copy ? cur.chains.map(cloneSeq) : [] };
      x.trees.push(t);
      x.active = t.id;
    });
    // every ability its own one-move chain, all equally likely
    const even = () => editOrder(x => {
      const cs = choices();
      const t = { id: newId(), name: 'Even', on: false, pct: 100, chains: cs.map(c => ({ ...newSeq([{ id: newId(), ref: c.ref }]), w: round(100 / cs.length, 4) })) };
      x.trees.push(t);
      x.active = t.id;
    });
    return h('div', { class: 'decTabs', role: 'tablist' }, ...tabs,
      h('button', { title: 'A new, empty tree (project only until you tick it)', onclick: () => add(false) }, '+ Tree'),
      h('button', { title: 'A new tree where every ability is its own chain, all equally likely (project only until you tick it)', onclick: even }, '⚖ Even tree'),
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
    const used = new Set(), walk = s => { s.moves.forEach(m => used.add(m.ref)); s.branches.forEach(walk); };
    onTrees(o).forEach(({ t }) => t.chains.forEach(walk));
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
    if (!on.length) return 'Its Ability Decision Order has no ticked tree, so it always uses its first ability.';
    const line = t => treeCombos(t).map(c => `${round(c.pct, 1)}% ${c.chain.map(names).join(', then ')}`).join(' · ');
    if (!on.some(({ t }) => treeCombos(t).length)) return S.project.decisionOrder ? 'Its Ability Decision Order is empty, so it always uses its first ability.' : null;
    if (on.length === 1) return 'Each pick, by its Ability Decision Order (Abilities tab): ' + line(on[0].t) + '.';
    return 'Each battle it follows one of its Ability Decision Order trees (Abilities tab): ' + on.map(({ t, share }) => `${round(share, 1)}% ${t.name || 'Tree'} (${line(t)})`).join('; ') + '.';
  }
  window.forgeDecisions = { render, autoOrder, toCombos, fromCombos, tree, order, planText };
})();
