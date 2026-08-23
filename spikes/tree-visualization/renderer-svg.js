// A hand-written SVG renderer, shared by the candidates that only differ in
// how they compute positions.
//
// Sharing it is the point: with the renderer held constant, the difference
// between the `hand` and `dagre` candidates is purely the layout algorithm,
// and the difference between them and `cytoscape` is purely that cytoscape
// brings its own renderer. Otherwise a slow candidate tells you nothing about
// whether the layout or the drawing was at fault.
//
// Three things here exist because of constraints the plan sets out, and they
// are the parts worth reading:
//
//  1. Pan and zoom write a single `transform` on one <g>. Nothing re-renders,
//     no attribute on any node changes, and — the part that matters for
//     Blazor — no component has to re-run. See NOTE-BLAZOR below.
//  2. The five edge styles are CSS classes on <path>, not per-edge inline
//     attributes, so the legend and the edges cannot drift apart.
//  3. Phantom nodes get a hatch <pattern> defined once in <defs> and referenced
//     by fill, rather than a per-node fill, which keeps 500 nodes cheap.

export const NODE_WIDTH = 172;
export const NODE_HEIGHT = 70;

const HATCH_ID = 'ft-phantom-hatch';

// The floor has to sit below whatever fit-to-screen produces for the largest
// graph we support, or "fit" lands at a scale the zoom control cannot return
// to. A 1000-person tree fits at about 0.02, so 0.05 — the value this started
// with — silently made fit-to-screen a one-way trip.
const ZOOM_MIN = 0.01;
const ZOOM_MAX = 4;

/**
 * Draws a laid-out graph and wires up interaction.
 *
 * @param {HTMLElement} container
 * @param {{nodes: Array, edges: Array}} graph  nodes carry `x`/`y` centres,
 *        already assigned by whichever layout the candidate uses.
 * @param {{minimap?: boolean}} options
 */
export function renderSvg(container, graph, options = {}) {
    const { nodes, edges } = graph;
    const byId = new Map(nodes.map((n) => [n.id, n]));

    const bounds = computeBounds(nodes);
    const svgns = 'http://www.w3.org/2000/svg';

    const svg = document.createElementNS(svgns, 'svg');
    svg.setAttribute('class', 'ft-canvas');
    svg.setAttribute('width', '100%');
    svg.setAttribute('height', '100%');

    svg.appendChild(buildDefs(svgns));

    // One transformed group. Every pan and zoom is one attribute write on this
    // element; the thousands of children below it never change.
    const viewport = document.createElementNS(svgns, 'g');
    viewport.setAttribute('class', 'ft-viewport');
    svg.appendChild(viewport);

    const edgeLayer = document.createElementNS(svgns, 'g');
    edgeLayer.setAttribute('class', 'ft-edges');
    viewport.appendChild(edgeLayer);

    const nodeLayer = document.createElementNS(svgns, 'g');
    nodeLayer.setAttribute('class', 'ft-nodes');
    viewport.appendChild(nodeLayer);

    // Edges are built into one document fragment and appended once. Appending
    // 777 paths individually costs a layout invalidation each time.
    const edgeFrag = document.createDocumentFragment();
    for (const e of edges) {
        const a = byId.get(e.source);
        const b = byId.get(e.target);
        if (!a || !b) {
            continue;
        }
        const path = document.createElementNS(svgns, 'path');
        path.setAttribute('class', `ft-edge ft-edge--${e.kind}`);
        path.setAttribute('d', edgePath(e.kind, a, b));
        edgeFrag.appendChild(path);
    }
    edgeLayer.appendChild(edgeFrag);

    const nodeFrag = document.createDocumentFragment();
    for (const n of nodes) {
        nodeFrag.appendChild(buildNode(svgns, n));
    }
    nodeLayer.appendChild(nodeFrag);

    container.replaceChildren(svg);

    const view = { x: 0, y: 0, k: 1 };
    const applyTransform = () => {
        viewport.setAttribute(
            'transform',
            `translate(${view.x} ${view.y}) scale(${view.k})`,
        );
        if (minimap) {
            minimap.update(view);
        }
    };

    const minimap = options.minimap === false
        ? null
        : buildMinimap(svgns, container, nodes, bounds, () => svg.getBoundingClientRect());

    attachPanZoom(svg, view, applyTransform);

    const fitToScreen = () => {
        const rect = svg.getBoundingClientRect();
        const pad = 48;
        const kx = (rect.width - pad * 2) / Math.max(bounds.width, 1);
        const ky = (rect.height - pad * 2) / Math.max(bounds.height, 1);
        view.k = clamp(Math.min(kx, ky, 1.5), ZOOM_MIN, ZOOM_MAX);
        view.x = rect.width / 2 - (bounds.minX + bounds.width / 2) * view.k;
        view.y = rect.height / 2 - (bounds.minY + bounds.height / 2) * view.k;
        applyTransform();
    };

    fitToScreen();

    return {
        fitToScreen,
        panBy(dx, dy) {
            view.x += dx;
            view.y += dy;
            applyTransform();
        },
        zoomBy(factor) {
            view.k = clamp(view.k * factor, ZOOM_MIN, ZOOM_MAX);
            applyTransform();
        },
        get view() {
            return { ...view };
        },
        bounds,
        destroy() {
            container.replaceChildren();
        },
    };
}

function buildDefs(svgns) {
    const defs = document.createElementNS(svgns, 'defs');

    // Phantom nodes are "dashed border, hatched background, ? avatar, never a
    // name" (US-054). Defining the hatch once and referencing it by fill keeps
    // that styling free no matter how many phantoms are on screen.
    const pattern = document.createElementNS(svgns, 'pattern');
    pattern.setAttribute('id', HATCH_ID);
    pattern.setAttribute('width', '8');
    pattern.setAttribute('height', '8');
    pattern.setAttribute('patternUnits', 'userSpaceOnUse');
    pattern.setAttribute('patternTransform', 'rotate(45)');
    const hatchBg = document.createElementNS(svgns, 'rect');
    hatchBg.setAttribute('width', '8');
    hatchBg.setAttribute('height', '8');
    hatchBg.setAttribute('class', 'ft-hatch-bg');
    const hatchLine = document.createElementNS(svgns, 'line');
    hatchLine.setAttribute('x1', '0');
    hatchLine.setAttribute('y1', '0');
    hatchLine.setAttribute('x2', '0');
    hatchLine.setAttribute('y2', '8');
    hatchLine.setAttribute('class', 'ft-hatch-line');
    pattern.append(hatchBg, hatchLine);
    defs.appendChild(pattern);

    return defs;
}

function buildNode(svgns, n) {
    const g = document.createElementNS(svgns, 'g');
    g.setAttribute(
        'class',
        n.isPhantom ? 'ft-node ft-node--phantom' : 'ft-node',
    );
    g.setAttribute('transform', `translate(${n.x - NODE_WIDTH / 2} ${n.y - NODE_HEIGHT / 2})`);
    g.dataset.personId = n.id;

    const rect = document.createElementNS(svgns, 'rect');
    rect.setAttribute('class', 'ft-node__card');
    rect.setAttribute('width', String(NODE_WIDTH));
    rect.setAttribute('height', String(NODE_HEIGHT));
    rect.setAttribute('rx', '10');
    if (n.isPhantom) {
        rect.setAttribute('fill', `url(#${HATCH_ID})`);
    }
    g.appendChild(rect);

    const avatar = document.createElementNS(svgns, 'circle');
    avatar.setAttribute('class', 'ft-node__avatar');
    avatar.setAttribute('cx', '26');
    avatar.setAttribute('cy', String(NODE_HEIGHT / 2));
    avatar.setAttribute('r', '16');
    g.appendChild(avatar);

    const initial = document.createElementNS(svgns, 'text');
    initial.setAttribute('class', 'ft-node__initial');
    initial.setAttribute('x', '26');
    initial.setAttribute('y', String(NODE_HEIGHT / 2 + 5));
    initial.setAttribute('text-anchor', 'middle');
    initial.textContent = n.isPhantom ? '?' : (n.name?.[0] ?? '?');
    g.appendChild(initial);

    const name = document.createElementNS(svgns, 'text');
    name.setAttribute('class', 'ft-node__name');
    name.setAttribute('x', '50');
    name.setAttribute('y', '30');
    // A phantom is never shown with a name — not even a placeholder one.
    name.textContent = n.isPhantom ? 'Unknown' : truncate(n.name, 15);
    g.appendChild(name);

    const years = document.createElementNS(svgns, 'text');
    years.setAttribute('class', 'ft-node__years');
    years.setAttribute('x', '50');
    years.setAttribute('y', '48');
    years.textContent = n.isPhantom
        ? ''
        : `${n.birthYear ?? '?'}–${n.deathYear ?? ''}`;
    g.appendChild(years);

    return g;
}

// Parent-child edges route orthogonally: down out of the parent, across, then
// down into the child. Marriage edges are a short horizontal run between two
// spouses sitting on the same row. Both are cheap `d` strings — no curve
// generator, and so no d3-shape dependency for the candidates that use this.
function edgePath(kind, a, b) {
    if (kind === 'marriage' || kind === 'marriage-ended') {
        const y = (a.y + b.y) / 2;
        const [left, right] = a.x <= b.x ? [a, b] : [b, a];
        const x1 = left.x + NODE_WIDTH / 2;
        const x2 = right.x - NODE_WIDTH / 2;
        return `M${x1},${y} L${x2},${y}`;
    }

    const startY = a.y + NODE_HEIGHT / 2;
    const endY = b.y - NODE_HEIGHT / 2;
    const midY = startY + (endY - startY) / 2;
    return `M${a.x},${startY} V${midY} H${b.x} V${endY}`;
}

function attachPanZoom(svg, view, applyTransform) {
    let dragging = false;
    let lastX = 0;
    let lastY = 0;

    svg.addEventListener('pointerdown', (e) => {
        dragging = true;
        lastX = e.clientX;
        lastY = e.clientY;
        svg.setPointerCapture(e.pointerId);
    });

    svg.addEventListener('pointermove', (e) => {
        if (!dragging) {
            return;
        }
        view.x += e.clientX - lastX;
        view.y += e.clientY - lastY;
        lastX = e.clientX;
        lastY = e.clientY;
        applyTransform();
    });

    const stop = (e) => {
        dragging = false;
        if (svg.hasPointerCapture?.(e.pointerId)) {
            svg.releasePointerCapture(e.pointerId);
        }
    };
    svg.addEventListener('pointerup', stop);
    svg.addEventListener('pointercancel', stop);

    svg.addEventListener(
        'wheel',
        (e) => {
            e.preventDefault();
            const rect = svg.getBoundingClientRect();
            const px = e.clientX - rect.left;
            const py = e.clientY - rect.top;
            const factor = Math.exp(-e.deltaY * 0.002);
            const next = clamp(view.k * factor, ZOOM_MIN, ZOOM_MAX);
            // Zoom about the cursor: the graph point under the pointer has to
            // stay under the pointer, which is what makes the gesture feel
            // attached to the canvas rather than to the viewport centre.
            view.x = px - ((px - view.x) / view.k) * next;
            view.y = py - ((py - view.y) / view.k) * next;
            view.k = next;
            applyTransform();
        },
        { passive: false },
    );
}

function buildMinimap(svgns, container, nodes, bounds, getViewportRect) {
    const W = 168;
    const H = 118;

    const wrap = document.createElement('div');
    wrap.className = 'ft-minimap';

    const svg = document.createElementNS(svgns, 'svg');
    svg.setAttribute('width', String(W));
    svg.setAttribute('height', String(H));

    const scale = Math.min(W / Math.max(bounds.width, 1), H / Math.max(bounds.height, 1)) * 0.9;
    const offX = (W - bounds.width * scale) / 2 - bounds.minX * scale;
    const offY = (H - bounds.height * scale) / 2 - bounds.minY * scale;

    // Nodes only, as bare rects with no text and no per-node classes. At 500
    // nodes this is the whole mini-map: edges add nothing legible at this scale
    // and would triple the element count.
    const frag = document.createDocumentFragment();
    for (const n of nodes) {
        const r = document.createElementNS(svgns, 'rect');
        r.setAttribute('x', String((n.x - NODE_WIDTH / 2) * scale + offX));
        r.setAttribute('y', String((n.y - NODE_HEIGHT / 2) * scale + offY));
        r.setAttribute('width', String(Math.max(NODE_WIDTH * scale, 1.5)));
        r.setAttribute('height', String(Math.max(NODE_HEIGHT * scale, 1)));
        r.setAttribute('class', n.isPhantom ? 'ft-mini__node ft-mini__node--phantom' : 'ft-mini__node');
        frag.appendChild(r);
    }
    svg.appendChild(frag);

    const viewRect = document.createElementNS(svgns, 'rect');
    viewRect.setAttribute('class', 'ft-mini__view');
    svg.appendChild(viewRect);

    wrap.appendChild(svg);
    container.appendChild(wrap);

    return {
        update(view) {
            const rect = getViewportRect();
            const vx = (-view.x / view.k) * scale + offX;
            const vy = (-view.y / view.k) * scale + offY;
            viewRect.setAttribute('x', String(vx));
            viewRect.setAttribute('y', String(vy));
            viewRect.setAttribute('width', String((rect.width / view.k) * scale));
            viewRect.setAttribute('height', String((rect.height / view.k) * scale));
        },
    };
}

export function computeBounds(nodes) {
    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;
    for (const n of nodes) {
        minX = Math.min(minX, n.x - NODE_WIDTH / 2);
        maxX = Math.max(maxX, n.x + NODE_WIDTH / 2);
        minY = Math.min(minY, n.y - NODE_HEIGHT / 2);
        maxY = Math.max(maxY, n.y + NODE_HEIGHT / 2);
    }
    return { minX, minY, maxX, maxY, width: maxX - minX, height: maxY - minY };
}

function truncate(text, max) {
    if (!text) {
        return '';
    }
    return text.length <= max ? text : `${text.slice(0, max - 1)}…`;
}

function clamp(v, lo, hi) {
    return Math.min(Math.max(v, lo), hi);
}

// NOTE-BLAZOR
// Nothing above touches Blazor, and that is the finding this file exists to
// support. If the nodes were Razor markup, every pan frame would either
// re-enter the renderer or require the transform to be written behind Blazor's
// back — and writing to the DOM behind Blazor's back is exactly what its
// diffing contract forbids. Keeping the whole canvas on the JS side of the
// interop boundary means Blazor hands over a DTO once and is not involved in
// interaction at all.
