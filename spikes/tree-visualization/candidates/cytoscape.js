// Candidate: Cytoscape.js with the dagre layout extension.
// Third-party download cost: 170.9 kB gzipped, before a mini-map extension.
//
// Cytoscape is the "batteries included" option: it owns the canvas, and pan,
// zoom, hit-testing, fit-to-screen and selection all arrive for free and
// well-tested. It renders to <canvas> rather than SVG, which is the reason to
// take it seriously at 500+ nodes.
//
// It is also where the spike's requirements start pushing back, and the
// pushback is the finding rather than a detail of this file:
//
//  - A node gets ONE label. The design's node is a 172x70 card with an avatar,
//    a name, and a birth-death year range on separate lines. Cytoscape can do
//    that only via a per-node background image or a wrapped multi-line label
//    with no independent positioning, so the card is approximated here with a
//    two-line wrapped label.
//  - There is no double-line edge style. `line-style` offers solid, dotted and
//    dashed only. Marriage is faked with `line-outline-width`, which draws a
//    contrasting casing rather than two strokes, and "ended marriage" then has
//    nothing distinct left to use — so it reuses dashed, which collides with
//    adoptive. Five distinguishable styles do not fit in the vocabulary.
//  - The mini-map is `cytoscape-navigator`, a further dependency not counted
//    in the figure above.

import { cytoscape } from '../vendor/cytoscape.js';
import { NODE_WIDTH, NODE_HEIGHT } from '../renderer-svg.js';

export const meta = {
    id: 'cytoscape',
    label: 'Cytoscape.js + dagre (canvas)',
    vendor: 'cytoscape',
};

export function mount(container, source, options = {}) {
    const t0 = performance.now();

    const elements = [];
    for (const n of source.nodes) {
        elements.push({
            data: {
                id: n.id,
                label: n.isPhantom
                    ? 'Unknown'
                    : `${truncate(n.name, 15)}\n${n.birthYear ?? '?'}–${n.deathYear ?? ''}`,
                phantom: n.isPhantom ? 'yes' : 'no',
            },
        });
    }
    for (const e of source.edges) {
        // Marriage edges are withheld from the layout for the same reason as in
        // the other candidates — dagre would read them as rank constraints —
        // but Cytoscape has no way to draw an edge it was not given, so they go
        // in as elements and are excluded from the layout by selector instead.
        elements.push({
            data: { id: e.id, source: e.source, target: e.target, kind: e.kind },
            classes: e.kind,
        });
    }

    const cy = cytoscape({
        container,
        elements,
        // Cytoscape's own CSS-like language; it cannot read the app's
        // stylesheet, so every design token has to be duplicated here as a
        // literal. That duplication is a maintenance cost the SVG candidates
        // do not have.
        style: [
            {
                selector: 'node',
                style: {
                    width: NODE_WIDTH,
                    height: NODE_HEIGHT,
                    shape: 'round-rectangle',
                    'background-color': '#ffffff',
                    'border-width': 1,
                    'border-color': '#d8d2c4',
                    label: 'data(label)',
                    'text-wrap': 'wrap',
                    'text-valign': 'center',
                    'text-halign': 'center',
                    'font-family': 'Inter, system-ui, sans-serif',
                    'font-size': 13,
                    color: '#1c1a17',
                },
            },
            {
                selector: 'node[phantom = "yes"]',
                style: {
                    'background-color': '#f3f0e9',
                    'border-style': 'dashed',
                    'border-color': '#9a9387',
                    color: '#6f6a5e',
                },
            },
            {
                selector: 'edge',
                style: {
                    width: 1.5,
                    'curve-style': 'taxi',
                    'taxi-direction': 'downward',
                    'line-color': '#6f6a5e',
                },
            },
            { selector: 'edge.biological', style: { 'line-style': 'solid' } },
            {
                selector: 'edge.adoptive',
                style: { 'line-style': 'dashed', 'line-color': '#3f7d6e' },
            },
            {
                selector: 'edge.marriage',
                style: {
                    'curve-style': 'straight',
                    'line-color': '#c96442',
                    width: 3,
                    // Stands in for a double line, which Cytoscape cannot draw.
                    'line-outline-width': 1,
                    'line-outline-color': '#fbfaf7',
                },
            },
            {
                selector: 'edge.marriage-ended',
                style: {
                    'curve-style': 'straight',
                    'line-color': '#c96442',
                    'line-style': 'dashed',
                    width: 3,
                },
            },
            {
                selector: 'edge.stepparent',
                style: { 'line-style': 'dotted', 'line-color': '#8a8377' },
            },
        ],
        layout: { name: 'preset' },
        pixelRatio: 1,
        textureOnViewport: true,
    });

    const t1 = performance.now();

    const layoutEdges = cy.edges('.biological, .adoptive');
    const layoutGraph = cy.nodes().union(layoutEdges);
    const layout = layoutGraph.layout({
        name: 'dagre',
        rankDir: 'TB',
        nodeSep: 28,
        rankSep: 90,
        fit: true,
        padding: 40,
        animate: false,
    });

    const done = new Promise((resolve) => layout.one('layoutstop', resolve));
    layout.run();

    return done.then(() => {
        const t2 = performance.now();
        return {
            fitToScreen() {
                cy.fit(undefined, 48);
            },
            panBy(dx, dy) {
                cy.panBy({ x: dx, y: dy });
            },
            zoomBy(factor) {
                cy.zoom({ level: cy.zoom() * factor, renderedPosition: { x: 200, y: 200 } });
            },
            bounds: cy.elements().boundingBox(),
            // Cytoscape interleaves layout and rendering, so the split between
            // the two is not observable the way it is for the SVG candidates.
            // The layout figure below is layout-plus-first-paint; the render
            // figure is the graph construction that precedes it.
            timings: { layoutMs: t2 - t1, renderMs: t1 - t0 },
            destroy() {
                cy.destroy();
            },
        };
    });
}

function truncate(text, max) {
    if (!text) {
        return '';
    }
    return text.length <= max ? text : `${text.slice(0, max - 1)}…`;
}
