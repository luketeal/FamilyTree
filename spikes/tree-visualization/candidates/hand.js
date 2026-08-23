// Candidate: hand-written family-aware layout + the shared SVG renderer.
// Third-party download cost: zero.
//
// The reason this candidate exists at all is that a family tree is not a
// generic DAG drawing problem, and the generic libraries do not know that:
//
//   - generations are given, not inferred. A layered layout spends its time
//     deciding which layer a node belongs in; here the answer is already known
//     from the parent-child edges and never needs searching for.
//   - spouses must sit next to each other. To a generic layout a marriage edge
//     is just another edge, and it will happily place two spouses at opposite
//     ends of a row with a long line between them.
//   - children belong under the midpoint of their two parents. A generic
//     layout centres a child under the barycentre of *all* its neighbours,
//     which for a married adult with children pulls them off their own couple.
//
// So this candidate is a four-pass layered layout that hard-codes those three
// facts.

import { renderSvg, NODE_WIDTH, NODE_HEIGHT } from '../renderer-svg.js';

export const meta = {
    id: 'hand',
    label: 'Hand-written family layout + SVG',
    vendor: null,
};

const H_GAP = 28;
const V_GAP = 90;
const COUPLE_GAP = 12;

export function layout(graph) {
    const nodes = graph.nodes.map((n) => ({ ...n }));
    const byId = new Map(nodes.map((n) => [n.id, n]));

    const parentEdges = graph.edges.filter(
        (e) => e.kind === 'biological' || e.kind === 'adoptive',
    );
    const marriages = graph.edges.filter(
        (e) => e.kind === 'marriage' || e.kind === 'marriage-ended',
    );

    // Pass 1 — layering. Longest path from any root over parent-child edges,
    // which guarantees a child is always strictly below both its parents even
    // when one parent is many generations further up than the other (the
    // adoptive-grandparent case).
    const parentsOf = new Map();
    const childrenOf = new Map();
    for (const e of parentEdges) {
        if (!byId.has(e.source) || !byId.has(e.target)) {
            continue;
        }
        (parentsOf.get(e.target) ?? parentsOf.set(e.target, []).get(e.target)).push(e.source);
        (childrenOf.get(e.source) ?? childrenOf.set(e.source, []).get(e.source)).push(e.target);
    }

    const depth = new Map();
    const resolve = (id, seen = new Set()) => {
        if (depth.has(id)) {
            return depth.get(id);
        }
        // A cycle should be impossible — CircularReferenceChecker rejects them
        // on write — but a layout that infinite-loops on bad data is a worse
        // failure than one that draws it slightly wrong.
        if (seen.has(id)) {
            return 0;
        }
        seen.add(id);
        const parents = parentsOf.get(id) ?? [];
        const d = parents.length === 0
            ? 0
            : Math.max(...parents.map((p) => resolve(p, seen))) + 1;
        depth.set(id, d);
        seen.delete(id);
        return d;
    };
    for (const n of nodes) {
        n.depth = resolve(n.id);
    }

    // Spouses are pulled onto the same row. Without this a person who married
    // someone from a shallower branch ends up a row above their own spouse and
    // the marriage edge becomes a diagonal.
    for (let pass = 0; pass < 3; pass++) {
        for (const m of marriages) {
            const a = byId.get(m.source);
            const b = byId.get(m.target);
            if (!a || !b) {
                continue;
            }
            const row = Math.max(a.depth, b.depth);
            a.depth = row;
            b.depth = row;
        }
    }

    // Pass 2 — group each row into couples, so ordering moves a couple as one
    // unit and the two never get separated.
    const spouseOf = new Map();
    for (const m of marriages) {
        if (!spouseOf.has(m.source) && !spouseOf.has(m.target)) {
            spouseOf.set(m.source, m.target);
            spouseOf.set(m.target, m.source);
        }
    }

    const rows = new Map();
    for (const n of nodes) {
        (rows.get(n.depth) ?? rows.set(n.depth, []).get(n.depth)).push(n);
    }

    const rowUnits = new Map();
    for (const [d, members] of rows) {
        const placed = new Set();
        const units = [];
        for (const n of members) {
            if (placed.has(n.id)) {
                continue;
            }
            const spouseId = spouseOf.get(n.id);
            const spouse = spouseId ? byId.get(spouseId) : null;
            if (spouse && spouse.depth === d && !placed.has(spouse.id)) {
                units.push({ members: [n, spouse] });
                placed.add(n.id);
                placed.add(spouse.id);
            } else {
                units.push({ members: [n] });
                placed.add(n.id);
            }
        }
        rowUnits.set(d, units);
    }

    // Pass 3 — ordering by barycentre, sweeping down then up a few times. This
    // is the standard crossing-reduction heuristic; the only change is that the
    // barycentre of a couple is taken over both partners' parents together.
    const depths = [...rowUnits.keys()].sort((a, b) => a - b);
    const indexInRow = new Map();
    const reindex = () => {
        for (const d of depths) {
            rowUnits.get(d).forEach((u, i) => {
                for (const m of u.members) {
                    indexInRow.set(m.id, i);
                }
            });
        }
    };
    reindex();

    const barycentre = (unit, neighbourFor) => {
        const xs = [];
        for (const m of unit.members) {
            for (const nb of neighbourFor(m.id) ?? []) {
                const idx = indexInRow.get(nb);
                if (idx !== undefined) {
                    xs.push(idx);
                }
            }
        }
        return xs.length === 0 ? null : xs.reduce((a, b) => a + b, 0) / xs.length;
    };

    for (let sweep = 0; sweep < 4; sweep++) {
        const order = sweep % 2 === 0 ? depths : [...depths].reverse();
        const neighbourFor = sweep % 2 === 0
            ? (id) => parentsOf.get(id)
            : (id) => childrenOf.get(id);

        for (const d of order) {
            const units = rowUnits.get(d);
            const keyed = units.map((u, i) => ({
                u,
                i,
                b: barycentre(u, neighbourFor),
            }));
            // Units with no neighbours in the adjacent row keep their current
            // position rather than being swept to one end.
            keyed.sort((p, q) => {
                if (p.b === null && q.b === null) return p.i - q.i;
                if (p.b === null) return p.i - q.i;
                if (q.b === null) return p.i - q.i;
                return p.b - q.b || p.i - q.i;
            });
            rowUnits.set(d, keyed.map((k) => k.u));
            reindex();
        }
    }

    // Pass 4 — coordinates. Rows are packed left to right, then each parent
    // couple is nudged toward the centre of its children so the descent reads
    // as a family rather than as a grid.
    for (const d of depths) {
        let x = 0;
        for (const unit of rowUnits.get(d)) {
            for (const m of unit.members) {
                m.x = x + NODE_WIDTH / 2;
                x += NODE_WIDTH + COUPLE_GAP;
            }
            x += H_GAP - COUPLE_GAP;
            unit.x = unit.members.reduce((s, m) => s + m.x, 0) / unit.members.length;
        }
    }

    for (let pass = 0; pass < 2; pass++) {
        for (const d of [...depths].reverse()) {
            const units = rowUnits.get(d);
            for (const unit of units) {
                const kids = unit.members.flatMap((m) => childrenOf.get(m.id) ?? []);
                if (kids.length === 0) {
                    continue;
                }
                const target =
                    kids.reduce((s, k) => s + (byId.get(k)?.x ?? 0), 0) / kids.length;
                shiftUnit(unit, target - unit.x);
            }
            separate(units);
        }
    }

    for (const n of nodes) {
        n.y = n.depth * (NODE_HEIGHT + V_GAP) + NODE_HEIGHT / 2;
    }

    return { nodes, edges: graph.edges };
}

function shiftUnit(unit, dx) {
    for (const m of unit.members) {
        m.x += dx;
    }
    unit.x += dx;
}

// After nudging, units in a row can overlap. One left-to-right pass pushing
// each unit clear of the one before is enough, and unlike a full force
// relaxation it terminates.
function separate(units) {
    const sorted = [...units].sort((a, b) => a.x - b.x);
    for (let i = 1; i < sorted.length; i++) {
        const prev = sorted[i - 1];
        const cur = sorted[i];
        const prevRight = Math.max(...prev.members.map((m) => m.x)) + NODE_WIDTH / 2;
        const curLeft = Math.min(...cur.members.map((m) => m.x)) - NODE_WIDTH / 2;
        const overlap = prevRight + H_GAP - curLeft;
        if (overlap > 0) {
            shiftUnit(cur, overlap);
        }
    }
}

export function mount(container, graph, options) {
    const t0 = performance.now();
    const laidOut = layout(graph);
    const t1 = performance.now();
    const api = renderSvg(container, laidOut, options);
    const t2 = performance.now();

    // Assigned onto the renderer's object rather than spread into a new one:
    // `view` is a getter over the live transform, and spreading would capture
    // its value at mount time and hand every later reader a stale scale.
    api.timings = { layoutMs: t1 - t0, renderMs: t2 - t1 };
    return api;
}
