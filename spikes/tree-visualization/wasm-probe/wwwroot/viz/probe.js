// JS side of the WebAssembly interop probe.
//
// `receiveObject` and `receiveJson` deliberately do almost nothing with the
// data. The cost being measured is Blazor marshalling the argument across the
// boundary, and any work done here would be added to it.
//
// They do have to touch the payload, though. An empty function body is exactly
// the sort of thing an engine can optimise away, and a measurement of a
// discarded argument would read as "interop is free" — which is the wrong
// answer arrived at convincingly.

let sink = 0;

export function receiveObject(graph) {
    sink += graph.nodes.length + graph.edges.length;
}

export function receiveJson(json) {
    const parsed = JSON.parse(json);
    sink += parsed.nodes.length + parsed.edges.length;
}

export async function draw(containerId, graph) {
    const { mount } = await import('./candidates/hand.js');
    const container = document.getElementById(containerId);

    // The renderer expects the field names the harness generator produces.
    // Blazor serialises the DTO as camelCase, which already matches for every
    // field except the phantom flag.
    const adapted = {
        nodes: graph.nodes.map((n) => ({
            id: n.id,
            generation: n.generation,
            isPhantom: n.isPhantom,
            name: n.name,
            birthYear: n.birthYear,
            deathYear: n.deathYear,
        })),
        edges: graph.edges,
    };

    const api = mount(container, adapted, { minimap: true });
    window.__probeApi = api;
    return { layoutMs: api.timings.layoutMs, renderMs: api.timings.renderMs };
}

// ADR-005 states that SyntheticGraph.cs is a bit-compatible port of
// family-graph.js, and leans on that when it compares the probe's layout and
// render timings against the browser harness's. This checks it rather than
// asserting it: same seed, same size, same graph, node for node and edge for
// edge.
export async function compareWithJs(graph, size) {
    const { buildFamilyGraph } = await import('./family-graph.js');
    const js = buildFamilyGraph(size);

    const mismatches = [];
    if (js.nodes.length !== graph.nodes.length) {
        mismatches.push(`node count: js ${js.nodes.length} vs c# ${graph.nodes.length}`);
    }
    if (js.edges.length !== graph.edges.length) {
        mismatches.push(`edge count: js ${js.edges.length} vs c# ${graph.edges.length}`);
    }

    const n = Math.min(js.nodes.length, graph.nodes.length);
    for (let i = 0; i < n && mismatches.length < 6; i++) {
        const a = js.nodes[i];
        const b = graph.nodes[i];
        if (a.id !== b.id || a.name !== b.name || a.isPhantom !== b.isPhantom
            || a.birthYear !== (b.birthYear ?? null) || a.deathYear !== (b.deathYear ?? null)) {
            mismatches.push(
                `node ${i}: js ${JSON.stringify(a)} vs c# ${JSON.stringify(b)}`);
        }
    }

    const m = Math.min(js.edges.length, graph.edges.length);
    for (let i = 0; i < m && mismatches.length < 10; i++) {
        const a = js.edges[i];
        const b = graph.edges[i];
        if (a.source !== b.source || a.target !== b.target || a.kind !== b.kind) {
            mismatches.push(
                `edge ${i}: js ${a.source}->${a.target} ${a.kind} vs c# ${b.source}->${b.target} ${b.kind}`);
        }
    }

    return { identical: mismatches.length === 0, mismatches };
}

export function publishResults(results) {
    window.__probeResults = results;
    window.__probeSink = sink;
    window.__probeReady = true;
}
