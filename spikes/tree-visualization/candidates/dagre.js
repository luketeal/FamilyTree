// Candidate: dagre for layout + the shared SVG renderer.
// Third-party download cost: 30.6 kB gzipped.
//
// dagre is a layered (Sugiyama) layout — the same family of algorithm the hand
// candidate implements, but general-purpose and much more thorough about
// crossing reduction. Holding the renderer constant against the hand candidate
// isolates exactly what that thoroughness costs and buys.
//
// Two adaptations are needed to make a generic layout draw a family, and they
// are the interesting part of this candidate:
//
//  1. Marriage edges are NOT given to dagre. A marriage is symmetric and its
//     two ends belong on the same rank; handing dagre a marriage edge makes it
//     a rank constraint and pushes one spouse a whole generation below the
//     other. They are excluded from the layout graph and drawn afterwards.
//  2. Because they are excluded, nothing keeps spouses adjacent, so couples are
//     merged into a single wide layout node and split back apart afterwards.
//
// Without those two, dagre produces a technically-correct DAG drawing that
// nobody would recognise as a family tree.

import { dagreLayout, graphlib } from '../vendor/dagre.js';
import { renderSvg, NODE_WIDTH, NODE_HEIGHT } from '../renderer-svg.js';

export const meta = {
    id: 'dagre',
    label: 'dagre layout + SVG',
    vendor: 'dagre',
};

const COUPLE_GAP = 12;

export function layout(graph) {
    const nodes = graph.nodes.map((n) => ({ ...n }));
    const byId = new Map(nodes.map((n) => [n.id, n]));

    const marriages = graph.edges.filter(
        (e) => e.kind === 'marriage' || e.kind === 'marriage-ended',
    );

    // Merge each couple into one layout node, twice as wide.
    const spouseOf = new Map();
    for (const m of marriages) {
        if (!spouseOf.has(m.source) && !spouseOf.has(m.target)) {
            spouseOf.set(m.source, m.target);
            spouseOf.set(m.target, m.source);
        }
    }

    const unitOf = new Map();
    const units = [];
    for (const n of nodes) {
        if (unitOf.has(n.id)) {
            continue;
        }
        const spouse = spouseOf.get(n.id);
        const members = spouse && byId.has(spouse) ? [n, byId.get(spouse)] : [n];
        const unit = {
            id: `u${units.length}`,
            members,
            width: members.length * NODE_WIDTH + (members.length - 1) * COUPLE_GAP,
        };
        units.push(unit);
        for (const m of members) {
            unitOf.set(m.id, unit.id);
        }
    }

    const g = new graphlib.Graph({ multigraph: true, compound: false });
    g.setGraph({ rankdir: 'TB', nodesep: 28, ranksep: 90, marginx: 40, marginy: 40 });
    g.setDefaultEdgeLabel(() => ({}));

    for (const u of units) {
        g.setNode(u.id, { width: u.width, height: NODE_HEIGHT });
    }

    // Parent-child edges only, deduplicated: two parents in the same couple
    // both pointing at the same child collapse to one unit-level edge, and
    // dagre weights duplicate edges rather than ignoring them.
    const seen = new Set();
    for (const e of graph.edges) {
        if (e.kind !== 'biological' && e.kind !== 'adoptive') {
            continue;
        }
        const from = unitOf.get(e.source);
        const to = unitOf.get(e.target);
        if (!from || !to || from === to) {
            continue;
        }
        const key = `${from}->${to}`;
        if (seen.has(key)) {
            continue;
        }
        seen.add(key);
        g.setEdge(from, to);
    }

    dagreLayout(g);

    for (const u of units) {
        const pos = g.node(u.id);
        if (!pos) {
            continue;
        }
        let x = pos.x - u.width / 2;
        for (const m of u.members) {
            m.x = x + NODE_WIDTH / 2;
            m.y = pos.y;
            x += NODE_WIDTH + COUPLE_GAP;
        }
    }

    // Anything dagre never placed (a fully isolated person) still needs
    // coordinates, or the renderer produces NaN bounds and draws nothing.
    for (const n of nodes) {
        if (!Number.isFinite(n.x) || !Number.isFinite(n.y)) {
            n.x = 0;
            n.y = 0;
        }
    }

    return { nodes, edges: graph.edges };
}

export function mount(container, graph, options) {
    const t0 = performance.now();
    const laidOut = layout(graph);
    const t1 = performance.now();
    const api = renderSvg(container, laidOut, options);
    const t2 = performance.now();

    // See the note in hand.js: spreading would freeze the `view` getter.
    api.timings = { layoutMs: t1 - t0, renderMs: t2 - t1 };
    return api;
}
