// Invariant checks on the generated graph.
//
// Every number in ADR-005 is a measurement taken against this graph, so if the
// generator is wrong the ADR is wrong in a way no timing would reveal. These
// assert the properties the candidates are being judged on actually hold:
// all five edge styles are exercised, the biological two-parent cap is
// respected, the DAG really is a DAG, and the both-biological-and-adoptive case
// (US-039) — the reason this is not a tree — is actually present.
//
// Run: node verify-graph.mjs

import { buildFamilyGraph } from './family-graph.js';

let failures = 0;

function check(label, condition, detail = '') {
    if (condition) {
        console.log(`  ok    ${label}`);
    } else {
        console.log(`  FAIL  ${label}${detail ? ` — ${detail}` : ''}`);
        failures++;
    }
}

for (const size of [100, 500, 1000]) {
    console.log(`\ngraph size ${size}`);
    const { nodes, edges, stats } = buildFamilyGraph(size);
    const ids = new Set(nodes.map((n) => n.id));

    check('node count is close to the target', Math.abs(nodes.length - size) <= 5,
        `got ${nodes.length}`);

    check('no edge references a missing node',
        edges.every((e) => ids.has(e.source) && ids.has(e.target)));

    check('no self-edges', edges.every((e) => e.source !== e.target));

    const kinds = new Set(edges.map((e) => e.kind));
    check('all five edge styles are exercised', kinds.size === 5,
        `got ${[...kinds].sort().join(', ')}`);

    const bioParents = new Map();
    for (const e of edges) {
        if (e.kind === 'biological') {
            bioParents.set(e.target, (bioParents.get(e.target) ?? 0) + 1);
        }
    }
    const overCap = [...bioParents.values()].filter((c) => c > 2);
    check('no person has more than two biological parents', overCap.length === 0,
        `${overCap.length} over the cap`);

    const adoptedTargets = new Set(
        edges.filter((e) => e.kind === 'adoptive').map((e) => e.target),
    );
    const both = [...adoptedTargets].filter((t) => bioParents.has(t));
    check('someone has both biological and adoptive parents (US-039)', both.length > 0,
        `${both.length} such people`);

    const phantoms = nodes.filter((n) => n.isPhantom);
    check('phantom nodes exist and carry no name (US-054)',
        phantoms.length > 0 && phantoms.every((p) => p.name === null),
        `${phantoms.length} phantoms`);

    const phantomIds = new Set(phantoms.map((p) => p.id));
    check('every phantom is actually connected',
        phantoms.every((p) =>
            edges.some((e) => e.source === p.id || e.target === p.id)),
    );
    check('no phantom is a child of anyone',
        !edges.some((e) => phantomIds.has(e.target) && e.kind !== 'marriage'));

    // The parent-child graph must be acyclic, or a layered layout will not
    // terminate and CircularReferenceChecker's premise would be violated.
    const children = new Map();
    for (const e of edges) {
        if (e.kind === 'biological' || e.kind === 'adoptive') {
            (children.get(e.source) ?? children.set(e.source, []).get(e.source)).push(e.target);
        }
    }
    const WHITE = 0, GREY = 1, BLACK = 2;
    const colour = new Map(nodes.map((n) => [n.id, WHITE]));
    let cyclic = false;
    const visit = (id) => {
        if (cyclic) return;
        colour.set(id, GREY);
        for (const c of children.get(id) ?? []) {
            const state = colour.get(c);
            if (state === GREY) { cyclic = true; return; }
            if (state === WHITE) visit(c);
        }
        colour.set(id, BLACK);
    };
    for (const n of nodes) {
        if (colour.get(n.id) === WHITE) visit(n.id);
    }
    check('the parent-child graph is acyclic', !cyclic);

    check('generation count is plausible for a family tree',
        stats.generations >= 4 && stats.generations <= 8,
        `got ${stats.generations}`);
}

// Determinism: the candidates are compared against "the same graph", which is
// only true if the generator is reproducible.
console.log('\ndeterminism');
const a = JSON.stringify(buildFamilyGraph(500));
const b = JSON.stringify(buildFamilyGraph(500));
check('the same seed produces an identical graph', a === b);
check('a different seed produces a different graph',
    JSON.stringify(buildFamilyGraph(500, 1)) !== a);

console.log(failures === 0 ? '\nall checks passed' : `\n${failures} check(s) FAILED`);
process.exit(failures === 0 ? 0 : 1);
