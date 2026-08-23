# Tree visualisation spike (PR 3)

Throwaway prototype behind [ADR-005](../../docs/decisions/ADR-005-tree-visualization-library.md).
**This is not shipped code.** Nothing in `src/` references it, it is not in
`FamilyTree.slnx`, and CI never builds it.

It is kept in the repository so the measurements in the ADR can be re-run
rather than believed.

## What it measures

Four ways of drawing a ~500-person family DAG, all against the same generated
graph, all in the same browser:

| Candidate | Layout | Renderer | Third-party |
|---|---|---|---|
| `hand` | hand-written, family-aware | hand-written SVG | none |
| `dagre` | dagre | the same SVG renderer | 30.6 kB gz |
| `d3dag` | d3-dag sugiyama | d3-selection + d3-zoom | 54.3 kB gz |
| `cytoscape` | cytoscape-dagre | Cytoscape canvas | 170.9 kB gz |

`hand` and `dagre` deliberately share `renderer-svg.js`, so the difference
between them is the layout algorithm and nothing else.

## Layout

```
family-graph.js        deterministic generator — the same graph for every candidate
renderer-svg.js        shared SVG renderer: 5 edge styles, phantoms, pan/zoom, mini-map
candidates/*.js        one module per candidate, each exporting mount()
harness.html/.css      the page a candidate is loaded into
build-vendor.mjs       bundles + sizes the third-party libraries
measure.mjs            timings and screenshots for every candidate x size
capture-detail.mjs     readable-scale screenshots (fit-to-screen at 500 is a smear)
diagnose-pan.mjs       why two candidates sharing a renderer pan at different speeds
verify-graph.mjs       invariants on the generated graph — every ADR number rests on these
wasm-probe/            a standalone Blazor WASM app measuring the interop boundary
run-wasm-probe.mjs     serves and drives the published probe
results/               summary.json, payload-sizes.json, wasm-probe.json, screenshots
```

## Running it

```bash
npm install
npm run build            # writes vendor/ and results/payload-sizes.json
node measure.mjs         # writes results/summary.json and screenshots
node capture-detail.mjs
node diagnose-pan.mjs

dotnet publish wasm-probe -c Release -o /tmp/probe
node run-wasm-probe.mjs /tmp/probe/wwwroot
```

Chromium is expected at `/opt/pw-browsers/chromium`; override with
`PLAYWRIGHT_CHROMIUM`. Playwright's Firefox and WebKit builds cannot be
downloaded in this environment, so the measurements are Chromium-only — a
limitation worth remembering before treating any single figure as definitive.

To look at a candidate by hand, serve this directory and open
`harness.html?candidate=hand&size=500`. `candidate`, `size` and `minimap`
are the query parameters.

## What is committed

`vendor/` and `node_modules/` are build output and are ignored. Of the
screenshots only the four `-small` comparisons and the WASM probe shot are
committed — they are the visual evidence the ADR cites; the rest regenerate.
