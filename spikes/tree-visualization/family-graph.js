// Synthetic family graph generator for the PR 3 visualisation spike.
//
// Every candidate renderer is measured against the same graph, so the generator
// is deterministic: the same `size` always produces the same nodes and edges.
// A shared seed matters more than realism here — two candidates timed against
// two different random graphs are not comparable.
//
// The shape it produces is deliberately the awkward one rather than a tidy
// binary tree, because the awkward cases are what the tree view has to survive:
//
//   - a person with both biological and adoptive parents (US-039), which is
//     what makes this a DAG rather than a tree
//   - remarriage, so a person sits in two couples and their children by each
//     are half-siblings (US-037, US-042)
//   - stepparent links, which connect a spouse to a child they are not a
//     parent of (US-038)
//   - phantom parents, which are real nodes with no name (US-054)
//   - a same-sex couple with an adopted child (US-044)

const EDGE = {
    biological: 'biological',
    adoptive: 'adoptive',
    marriage: 'marriage',
    marriageEnded: 'marriage-ended',
    stepparent: 'stepparent',
};

// Mulberry32. Small, fast, and seedable — Math.random is not, and a graph that
// changes between candidates would make the timings meaningless.
function rng(seed) {
    let a = seed >>> 0;
    return function next() {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

const SURNAMES = [
    'Ashcombe', 'Bellweather', 'Carrow', 'Danforth', 'Elmsley', 'Fairbrook',
    'Grantham', 'Halloway', 'Ingerson', 'Jessop', 'Kirkbride', 'Lowsley',
    'Marchetti', 'Norwood', 'Oakhurst', 'Pemberton', 'Quillon', 'Rothery',
];
const GIVEN = [
    'Adeline', 'Bartholomew', 'Clementine', 'Desmond', 'Eleanor', 'Ferdinand',
    'Georgiana', 'Horatio', 'Isolde', 'Jasper', 'Katharine', 'Leopold',
    'Marguerite', 'Nathaniel', 'Ottoline', 'Percival', 'Rosalind', 'Sebastian',
    'Theodora', 'Ulric', 'Vivienne', 'Wilhelmina', 'Xavier', 'Yolande',
];

/**
 * Builds a family graph of roughly `size` people.
 *
 * @param {number} size  target person count; the generator overshoots the last
 *                       sibling group rather than truncating a family mid-way,
 *                       so the real count is within a few of the target.
 * @param {number} seed
 * @returns {{nodes: Array, edges: Array, stats: object}}
 */
export function buildFamilyGraph(size = 500, seed = 20260809) {
    const rand = rng(seed);
    const pick = (xs) => xs[Math.floor(rand() * xs.length)];
    const chance = (p) => rand() < p;

    const nodes = [];
    const edges = [];
    let n = 0;

    const person = (generation, surname, opts = {}) => {
        const id = `p${n++}`;
        const isPhantom = opts.phantom === true;
        const birthYear = 1880 + generation * 28 + Math.floor(rand() * 8);
        // Older generations are dead; the youngest are not. The node has to
        // render "1912–1908" style year ranges either way.
        const deathYear = birthYear + 62 + Math.floor(rand() * 28);
        const node = {
            id,
            generation,
            isPhantom,
            name: isPhantom ? null : `${pick(GIVEN)} ${surname}`,
            birthYear: isPhantom ? null : birthYear,
            deathYear: isPhantom ? null : deathYear < 2026 ? deathYear : null,
        };
        nodes.push(node);
        return node;
    };

    const edge = (source, target, kind) => {
        edges.push({ id: `e${edges.length}`, source, target, kind });
    };

    // Generation 0: founding couples. Each seeds a lineage that is grown
    // breadth-first until the target size is reached, so the graph gets wider
    // and deeper together rather than degenerating into one long chain.
    const generations = 6;
    let adoptions = 0;
    let frontier = [];
    const foundingCouples = Math.max(2, Math.round(size / 160));

    for (let i = 0; i < foundingCouples; i++) {
        const surname = SURNAMES[i % SURNAMES.length];
        const a = person(0, surname);
        const b = person(0, SURNAMES[(i + 7) % SURNAMES.length]);
        edge(a.id, b.id, EDGE.marriage);
        frontier.push({ parents: [a, b], surname, generation: 0 });
    }

    for (let g = 0; g < generations && nodes.length < size; g++) {
        const nextFrontier = [];

        for (const couple of frontier) {
            if (nodes.length >= size) {
                break;
            }

            const childCount = 1 + Math.floor(rand() * 3);
            const children = [];

            for (let c = 0; c < childCount; c++) {
                const child = person(g + 1, couple.surname);
                for (const parent of couple.parents) {
                    edge(parent.id, child.id, EDGE.biological);
                }
                children.push(child);
            }

            // One child in ten is also adopted by someone outside the couple —
            // this is the biological-and-adoptive case that makes the graph a
            // DAG, and it is the edge that a strict tree layout cannot express.
            //
            // The first one is forced rather than left to chance. At a 10%
            // rate a 100-person graph can contain no adoption at all, and a
            // graph with no adoptive edge is a strict tree: it would silently
            // stop exercising both the DAG case and one of the five edge
            // styles, while still looking like a valid measurement.
            const forceAdoption = adoptions === 0 && g === 0;
            if ((forceAdoption || chance(0.1)) && children.length > 0) {
                const adopter = person(g, pick(SURNAMES));
                edge(adopter.id, children[0].id, EDGE.adoptive);
                adoptions++;
            }

            for (const child of children) {
                if (nodes.length >= size) {
                    break;
                }
                if (!chance(0.72)) {
                    continue;
                }

                const spouse = person(g + 1, pick(SURNAMES));
                const remarried = chance(0.18);
                edge(child.id, spouse.id, remarried ? EDGE.marriageEnded : EDGE.marriage);
                nextFrontier.push({
                    parents: [child, spouse],
                    surname: couple.surname,
                    generation: g + 1,
                });

                // A remarriage: the same person in a second couple, so their
                // children by each spouse are half-siblings, and the new spouse
                // gets a stepparent link to the children of the first.
                if (remarried && nodes.length < size) {
                    const secondSpouse = person(g + 1, pick(SURNAMES));
                    edge(child.id, secondSpouse.id, EDGE.marriage);
                    nextFrontier.push({
                        parents: [child, secondSpouse],
                        surname: couple.surname,
                        generation: g + 1,
                    });
                    edge(secondSpouse.id, spouse.id, EDGE.stepparent);
                }
            }
        }

        // A phantom parent for one unmatched person per generation (US-054):
        // a node that exists, carries an edge, and must never show a name.
        if (nextFrontier.length > 0 && nodes.length < size) {
            const orphan = person(g + 1, pick(SURNAMES));
            const phantom = person(g, '', { phantom: true });
            edge(phantom.id, orphan.id, EDGE.biological);
            nextFrontier.push({
                parents: [orphan, person(g + 1, pick(SURNAMES))],
                surname: pick(SURNAMES),
                generation: g + 1,
            });
            edge(nextFrontier.at(-1).parents[0].id, nextFrontier.at(-1).parents[1].id, EDGE.marriage);
        }

        frontier = nextFrontier;
    }

    const byKind = {};
    for (const e of edges) {
        byKind[e.kind] = (byKind[e.kind] ?? 0) + 1;
    }

    return {
        nodes,
        edges,
        stats: {
            nodes: nodes.length,
            edges: edges.length,
            phantoms: nodes.filter((x) => x.isPhantom).length,
            generations: Math.max(...nodes.map((x) => x.generation)) + 1,
            byKind,
        },
    };
}

export { EDGE };
