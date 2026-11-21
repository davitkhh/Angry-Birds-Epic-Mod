# Ability Decision Order: chain layout (saved 2026-11-14, not in use)

The user asked for this layout to be remembered, then switched the live Forge back to the sideways-columns layout.

To bring it back:
1. Copy `decisions.js` over `web/decisions.js`.
2. Replace the decision-order block at the end of `web/style.css` with `decisions.css`. That file also holds the
   `html.noSelect` / `img` rules, which belong to app.js's drag guard: keep one copy of them.

## The layout

A vertical outline:
- **Chain** blocks are the top level, each with a chance on the right.
- A chain's **moves** sit one level in, in order, with turn numbers. Moves that always follow each other are siblings
  on the same level, not nested.
- **Branch** blocks appear after a chain's moves, only where the path splits. Each has its own chance, "% overall",
  and its own moves one level further in. Branches can split again.
- **↻ Ends here** is a branch with no moves.
- An elbow line runs down each block's left side to its children. Right edges are aligned.

## Data

Each tree tab is `{ chains: [seq] }`, and a seq is `{ id, w, moves: [{ id, ref }], branches: [seq] }`.
- `tidyList` keeps it canonical: a lone branch merges up, all-ending branches are dropped, a branch with no moves
  hands its branches up, and empty chains are removed.
- `publish.tree_combos` still reads `chains`. The columns layout's `normal()` converts chains back into its
  step-by-step `kids` tree.

## Editing

- + on a block: add a move (always), add a branch, or add an "ends here" branch.
- Drag a move onto a move to go before or after it (left 30% = before). Drag onto a block to append, or onto an
  empty spot for a new chain.
- Blocks dragged onto a block or a move become a branch. Ctrl copies. Dropping on a tab moves the item to that tree.
