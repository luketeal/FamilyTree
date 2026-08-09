// Candidate: d3-dag for layout, d3-selection for rendering, d3-zoom for
// interaction. Third-party download cost: 54.3 kB gzipped.
//
// This is the plan's reference option, so it is written the way a d3 user would
// write it — data joins rather than manual DOM building, and d3-zoom rather
// than hand-rolled pointer handling — not as a thin wrapper over the shared
// renderer. Comparing it against the `dagre` candidate therefore measures the
// whole d3 idiom, rendering included, which is what adopting d3 would actually
// commit us to.
//
// It needs the same two family adaptations as the dagre candidate: marriage
// edges withheld from the layout, and couples merged into one layout node.
// d3-dag is a DAG layout, not a genealogy layout, and neither library knows
// what a spouse is.

import {
    select, zoom, zoomIdentity, line, curveLinear,
    graph as dagGraph, sugiyama, layeringLongestPath, decrossTwoLayer, coordGreedy,
} from '../vendor/d3-dag.js';
import { NODE_WIDTH, NODE_HEIGHT, computeBounds } from '../renderer-svg.js';

export const meta = {
    id: 'd3dag',
    label: 'd3-dag + d3-selection + d3-zoom',
    vendor: 'd3-dag',
};

const COUPLE_GAP = 12;

export function layout(source) {
    const nodes = source.nodes.map((n) => ({ ...n }));
    const byId = new Map(nodes.map((n) => [n.id, n]));

    const spouseOf = new Map();
    for (const e of source.edges) {
        if (e.kind !== 'marriage' && e.kind !== 'marriage-ended') {
            continue;
        }
        if (!spouseOf.has(e.source) && !spouseOf.has(e.target)) {
            spouseOf.set(e.source, e.target);
            spouseOf.set(e.target, e.source);
        }
    }

    const unitOf = new Map();
    const units = [];
    for (const n of nodes) {
        if (unitOf.has(n.id)) {
            continue;
        }
        const spouseId = spouseOf.get(n.id);
        const members = spouseId && byId.has(spouseId) ? [n, byId.get(spouseId)] : [n];
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

    const g = dagGraph();
    const handles = new Map();
    for (const u of units) {
        handles.set(u.id, g.node(u));
    }

    const seen = new Set();
    for (const e of source.edges) {
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
        g.link(handles.get(from), handles.get(to));
    }

    // layeringLongestPath and coordGreedy are the cheap end of d3-dag's menu.
    // The alternatives it offers — layeringSimplex, coordSimplex, decrossOpt —
    // solve linear programs, and decrossOpt is explicitly documented as
    // exponential. At 500 nodes those are not options, which is itself worth
    // recording: d3-dag's best-quality settings are unreachable at our scale.
    const runner = sugiyama()
        .layering(layeringLongestPath())
        .decross(decrossTwoLayer())
        .coord(coordGreedy())
        .nodeSize((node) => [node.data.width, NODE_HEIGHT])
        .gap([28, 90]);

    runner(g);

    for (const node of g.nodes()) {
        const u = node.data;
        let x = node.x - u.width / 2;
        for (const m of u.members) {
            m.x = x + NODE_WIDTH / 2;
            m.y = node.y;
            x += NODE_WIDTH + COUPLE_GAP;
        }
    }

    for (const n of nodes) {
        if (!Number.isFinite(n.x) || !Number.isFinite(n.y)) {
            n.x = 0;
            n.y = 0;
        }
    }

    return { nodes, edges: source.edges };
}

function edgePoints(kind, a, b) {
    if (kind === 'marriage' || kind === 'marriage-ended') {
        const y = (a.y + b.y) / 2;
        const [l, r] = a.x <= b.x ? [a, b] : [b, a];
        return [[l.x + NODE_WIDTH / 2, y], [r.x - NODE_WIDTH / 2, y]];
    }
    const y1 = a.y + NODE_HEIGHT / 2;
    const y2 = b.y - NODE_HEIGHT / 2;
    const mid = y1 + (y2 - y1) / 2;
    return [[a.x, y1], [a.x, mid], [b.x, mid], [b.x, y2]];
}

export function mount(container, source, options = {}) {
    const t0 = performance.now();
    const laidOut = layout(source);
    const t1 = performance.now();

    const byId = new Map(laidOut.nodes.map((n) => [n.id, n]));
    const bounds = computeBounds(laidOut.nodes);

    const root = select(container);
    root.selectAll('*').remove();

    const svg = root
        .append('svg')
        .attr('class', 'ft-canvas')
        .attr('width', '100%')
        .attr('height', '100%');

    const defs = svg.append('defs');
    const pattern = defs
        .append('pattern')
        .attr('id', 'ft-phantom-hatch-d3')
        .attr('width', 8)
        .attr('height', 8)
        .attr('patternUnits', 'userSpaceOnUse')
        .attr('patternTransform', 'rotate(45)');
    pattern.append('rect').attr('width', 8).attr('height', 8).attr('class', 'ft-hatch-bg');
    pattern
        .append('line')
        .attr('x1', 0).attr('y1', 0).attr('x2', 0).attr('y2', 8)
        .attr('class', 'ft-hatch-line');

    const viewport = svg.append('g').attr('class', 'ft-viewport');

    const pathOf = line().curve(curveLinear);

    viewport
        .append('g')
        .attr('class', 'ft-edges')
        .selectAll('path')
        .data(laidOut.edges.filter((e) => byId.has(e.source) && byId.has(e.target)))
        .join('path')
        .attr('class', (e) => `ft-edge ft-edge--${e.kind}`)
        .attr('d', (e) => pathOf(edgePoints(e.kind, byId.get(e.source), byId.get(e.target))));

    const nodeSel = viewport
        .append('g')
        .attr('class', 'ft-nodes')
        .selectAll('g')
        .data(laidOut.nodes)
        .join('g')
        .attr('class', (d) => (d.isPhantom ? 'ft-node ft-node--phantom' : 'ft-node'))
        .attr('transform', (d) => `translate(${d.x - NODE_WIDTH / 2} ${d.y - NODE_HEIGHT / 2})`);

    nodeSel
        .append('rect')
        .attr('class', 'ft-node__card')
        .attr('width', NODE_WIDTH)
        .attr('height', NODE_HEIGHT)
        .attr('rx', 10)
        .attr('fill', (d) => (d.isPhantom ? 'url(#ft-phantom-hatch-d3)' : null));

    nodeSel
        .append('circle')
        .attr('class', 'ft-node__avatar')
        .attr('cx', 26)
        .attr('cy', NODE_HEIGHT / 2)
        .attr('r', 16);

    nodeSel
        .append('text')
        .attr('class', 'ft-node__initial')
        .attr('x', 26)
        .attr('y', NODE_HEIGHT / 2 + 5)
        .attr('text-anchor', 'middle')
        .text((d) => (d.isPhantom ? '?' : (d.name?.[0] ?? '?')));

    nodeSel
        .append('text')
        .attr('class', 'ft-node__name')
        .attr('x', 50)
        .attr('y', 30)
        .text((d) => (d.isPhantom ? 'Unknown' : truncate(d.name, 15)));

    nodeSel
        .append('text')
        .attr('class', 'ft-node__years')
        .attr('x', 50)
        .attr('y', 48)
        .text((d) => (d.isPhantom ? '' : `${d.birthYear ?? '?'}–${d.deathYear ?? ''}`));

    // d3-zoom owns the transform, but the measurement harness needs to read the
    // current scale to drive every candidate to an identical zoom level. Track
    // it here rather than reaching into d3's internals.
    let current = { x: 0, y: 0, k: 1 };

    const zoomBehavior = zoom()
        .scaleExtent([0.05, 4])
        .on('zoom', (event) => {
            current = { x: event.transform.x, y: event.transform.y, k: event.transform.k };
            viewport.attr('transform', event.transform);
            if (minimap) {
                minimap.update(event.transform);
            }
        });

    svg.call(zoomBehavior);

    const minimap = options.minimap === false ? null : buildMinimap(container, laidOut.nodes, bounds, () => svg.node().getBoundingClientRect());

    const fitToScreen = () => {
        const rect = svg.node().getBoundingClientRect();
        const pad = 48;
        const k = Math.min(
            (rect.width - pad * 2) / Math.max(bounds.width, 1),
            (rect.height - pad * 2) / Math.max(bounds.height, 1),
            1.5,
        );
        const t = zoomIdentity
            .translate(
                rect.width / 2 - (bounds.minX + bounds.width / 2) * k,
                rect.height / 2 - (bounds.minY + bounds.height / 2) * k,
            )
            .scale(k);
        svg.call(zoomBehavior.transform, t);
    };

    fitToScreen();

    const t2 = performance.now();

    return {
        fitToScreen,
        panBy(dx, dy) {
            svg.call(zoomBehavior.translateBy, dx, dy);
        },
        zoomBy(factor) {
            svg.call(zoomBehavior.scaleBy, factor);
        },
        get view() {
            return { ...current };
        },
        bounds,
        timings: { layoutMs: t1 - t0, renderMs: t2 - t1 },
        destroy() {
            root.selectAll('*').remove();
        },
    };
}

function buildMinimap(container, nodes, bounds, getRect) {
    const W = 168;
    const H = 118;
    const wrap = select(container).append('div').attr('class', 'ft-minimap');
    const svg = wrap.append('svg').attr('width', W).attr('height', H);

    const scale = Math.min(W / Math.max(bounds.width, 1), H / Math.max(bounds.height, 1)) * 0.9;
    const offX = (W - bounds.width * scale) / 2 - bounds.minX * scale;
    const offY = (H - bounds.height * scale) / 2 - bounds.minY * scale;

    svg.append('g')
        .selectAll('rect')
        .data(nodes)
        .join('rect')
        .attr('class', (d) => (d.isPhantom ? 'ft-mini__node ft-mini__node--phantom' : 'ft-mini__node'))
        .attr('x', (d) => (d.x - NODE_WIDTH / 2) * scale + offX)
        .attr('y', (d) => (d.y - NODE_HEIGHT / 2) * scale + offY)
        .attr('width', Math.max(NODE_WIDTH * scale, 1.5))
        .attr('height', Math.max(NODE_HEIGHT * scale, 1));

    const viewRect = svg.append('rect').attr('class', 'ft-mini__view');

    return {
        update(t) {
            const rect = getRect();
            viewRect
                .attr('x', (-t.x / t.k) * scale + offX)
                .attr('y', (-t.y / t.k) * scale + offY)
                .attr('width', (rect.width / t.k) * scale)
                .attr('height', (rect.height / t.k) * scale);
        },
    };
}

function truncate(text, max) {
    if (!text) {
        return '';
    }
    return text.length <= max ? text : `${text.slice(0, max - 1)}…`;
}
