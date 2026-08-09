# ADR-005: Hand-written family layout with an SVG renderer, no visualisation library

**Date:** 2026-08-09  
**Status:** Decided

## Context

The tree view (PR 10) is the project's largest technical unknown. It has to draw a directed acyclic graph — a person may have both biological and adoptive parents (US-039), so it is not a tree — with pan and zoom, five visually distinct edge styles (US-031), ~172×70px node cards, phantom nodes styled as dashed and hatched with no name (US-054), and a mini-map, at around 500 people. It has to run under Blazor WebAssembly, and whatever it downloads is spent from a budget the .NET runtime has already overdrawn: the published app is 3.7 MB gzipped on disk before any of this, of which roughly 3.0 MB is fetched on a cold load (three ICU data files ship, one is used).

Four options were prototyped against the same synthetic family graph and measured in Chromium. The prototype, the generator, the measurement scripts and the raw results are in [`spikes/tree-visualization/`](../../spikes/tree-visualization/).

1. **Hand-written family layout + hand-written SVG renderer** — no third-party code.
2. **dagre for layout + the same SVG renderer** — 30.6 kB gzipped.
3. **d3-dag + d3-selection + d3-zoom** — the plan's reference option, 54.3 kB gzipped.
4. **Cytoscape.js + cytoscape-dagre**, rendering to canvas — 170.9 kB gzipped, before a mini-map extension.

**ELK** (`elkjs`) was bundled and measured but not prototyped: at 430.2 kB gzipped it would add about 14% to a cold load for a layout algorithm alone, and no layout quality would justify that here.

Option 2 shares option 1's renderer exactly, so the comparison between those two isolates the layout algorithm and nothing else. Option 3 deliberately does not: it is written the way a d3 user would write it, with data joins and `d3-zoom`, because adopting d3 would commit us to the whole idiom and not just to its layout. It produces the same element structure to within one node.

## Decision

Write the layout and the renderer ourselves. No visualisation library.

Concretely, for PR 10: a four-pass layered layout (longest-path layering → couple grouping → barycentre crossing reduction → coordinate assignment with children centred under parents), rendering to SVG, with pan and zoom implemented as a single `transform` on one `<g>` driven by pointer and wheel events.

**Blazor hands the graph across the interop boundary once and is not involved in interaction at all.** The whole canvas lives on the JavaScript side.

## Reasoning

### A family tree is not a generic DAG drawing problem

This is the finding that decided it. Every general-purpose layout had to be told three things it does not know, and the workarounds are the bulk of the code in each candidate:

- **Marriage edges must be withheld from the layout.** To a layered layout an edge is a rank constraint, so handing it a marriage puts one spouse a generation below the other. All three library candidates exclude marriage edges and draw them afterwards.
- **Spouses must be adjacent**, which nothing enforces once marriage edges are withheld. Candidates 2 and 3 merge each couple into one double-width layout node and split it back afterwards. Cytoscape cannot do this without compound nodes, and the prototype does not — `results/cytoscape-small.png` shows the consequence: dozens of long diagonal marriage lines slashing across the diagram.
- **Children belong under the midpoint of their parents**, not at the barycentre of all their neighbours.

Once all three are bolted on, the library is doing layering and crossing reduction and nothing else — and layering is the part we already know the answer to, because generations come from the parent-child edges directly.

### Where the graph is drawn decides whether panning stutters

At 1000 nodes the hand candidate holds 19.1 ms frames while panning; dagre and d3-dag collapse to 111.0 ms and 107.4 ms. The hand and dagre candidates put an identical 8,578 SVG elements on the page through the very same renderer, and d3-dag puts up 8,579 through its own — so this is not a drawing-technique difference. It is where the layout put things.

The mechanism is that an SVG path's raster invalidation is bounded by its bounding box, not by its ink. A long edge is therefore expensive out of all proportion to what it draws.

Two explanations were tested rather than assumed (`diagnose-pan.mjs`), by re-running the pan at a fixed zoom instead of at fit-to-screen. They turn out to be different problems:

| At 1000 nodes | Extent | Median edge span | Worst edge span | Pan at fit | Pan at k=0.25 |
|---|---|---|---|---|---|
| Hand-written | 59,972px | 476px | 37,810px | 18.3 ms | 16.7 ms |
| dagre | 91,690px | 292px | 85,610px | 116.6 ms | 17.9 ms |
| d3-dag | 83,744px | 890px | 79,084px | 108.0 ms | 91.8 ms |

**dagre recovers completely when zoomed in** (116.6 → 17.9 ms). Its typical edge is short — shorter than ours — but it spreads the graph 53% wider than the hand layout and produces outlier edges spanning almost the entire canvas, so at fit-to-screen, when everything is on screen at once, those few enormous bounding boxes dominate every frame.

**d3-dag does not recover** (108.0 → 91.8 ms). Its problem is not a handful of outliers but a systematically longer typical edge — nearly double the hand layout's median — which stays with you at any zoom level.

Centring children under their parents is what keeps edges short and the extent compact. A generic layout has no reason to do it, so the family-aware layout is not merely tidier: it is the reason the tree stays interactive.

### Measurements at 500 nodes

Medians of three runs against a 500-node, 770-edge graph. Chromium at 1440×900. Full data in `results/summary.json`.

| Candidate | Payload (gzip) | Layout | Render | Pan p50 / p95 |
|---|---|---|---|---|
| Hand-written | **0 kB** | **14 ms** | 89 ms | **16.7 / 16.9 ms** |
| dagre + SVG | 30.6 kB | 219 ms | 92 ms | 23.7 / 47.5 ms |
| d3-dag + d3 | 54.3 kB | 428 ms | 101 ms | 31.6 / 34.1 ms |
| Cytoscape (canvas) | 170.9 kB | 568 ms | 257 ms | 16.7 / 46.7 ms |

At 500 nodes all four are usable and the choice is not forced by performance — the headroom is what differs, and at 1000 three of the four stop being viable. Layout cost is the clearest separation even here: 14 ms against 219–568 ms, because the hand layout reads the generations off the edges instead of searching for them.

### Cytoscape's visual vocabulary is too small

Canvas rendering makes Cytoscape the most obviously scalable option, and its pan, zoom, hit-testing and fit-to-screen are free and well-tested. It was still ruled out on requirements rather than on speed:

- **A node gets one label.** The design's card is an avatar, a name and a year range, independently positioned. Cytoscape can approximate that with a wrapped multi-line label or a per-node background image, neither of which is the card.
- **There is no double-line edge style.** `line-style` offers solid, dotted and dashed. Marriage was faked with `line-outline-width`, which leaves "marriage ended" nothing distinct — it falls back to dashed, colliding with adoptive. Five distinguishable styles do not fit.
- **Styling is a second, duplicate stylesheet.** Cytoscape cannot read the app's CSS, so every design token has to be restated as a literal in its own style language, where it can drift from `tokens.css` silently.

In SVG all five styles are ordinary CSS classes against the real tokens, and the legend and the edges cannot disagree.

### The WebAssembly boundary is not the bottleneck

A standalone Blazor WASM probe (`spikes/tree-visualization/wasm-probe/`) builds the same 500-node graph in C#, marshals it across `IJSRuntime`, and renders it with the chosen renderer. Its C# generator is a port of the harness's JavaScript one, and the probe asserts on every run that the two produce an identical graph — otherwise its timings would not be comparable with the harness's. Medians of three runs:

| Step | Cost |
|---|---|
| Build the DTO in C# | 28 ms |
| Interop, object argument (Blazor serialises) | 61 ms |
| Interop, JSON string, reflection | 28 ms |
| Interop, JSON string, source-generated context | 36 ms |
| Payload | 119 kB |
| **Pan under Blazor, p50 / p95** | **16.7 / 16.8 ms** |

Two things matter here. First, the expensive-looking route is still only 61 ms, paid once when the tree page opens — acceptable, so PR 10 should pass the DTO straight across and not complicate itself with hand-serialisation. Second, panning under Blazor is 60 fps and indistinguishable from the standalone harness, which is the direct evidence that interaction never re-enters .NET.

The source-generated context measuring consistently *slower* than reflection is reproducible across runs but unexplained. It is recorded because it contradicts the usual advice, and it changes nothing here since no route is a problem.

### Payload

Against a ~3.0 MB cold load, 30 kB or 55 kB is not itself disqualifying, and this decision is not primarily about download size — only ELK's 430 kB is ruled out on payload alone. It is worth noting simply that the option which measured fastest is also the one that costs nothing, so no trade-off had to be made.

A note on the numbers above: GitHub Pages negotiates gzip but not brotli, so gzip is the figure that decides anything today. All library sizes quoted here are tree-shaken bundles of only the imports each candidate uses, not the packages' advertised sizes — for d3 that distinction is more than fivefold.

## Consequences

**Easier:**

- The five edge styles, phantom hatching and node cards are ordinary SVG and CSS using `tokens.css` directly, so the tree obeys the design system by default.
- No third-party upgrade treadmill, and no library whose maintenance status becomes our problem.
- The layout knows what a spouse is, so remarriage, half-siblings and phantom parents are expressible rather than worked around.
- Zero added download.

**Harder:**

- We own the layout algorithm, including its bugs. The prototype already shows one: couple merging pairs each person with their *first* spouse only, so a remarriage leaves the second spouse non-adjacent and draws a long marriage edge across the diagram (visible in `results/hand-small.png` and `results/dagre-small.png`). **This is unsolved and PR 10 must address it** — a person in two couples cannot be adjacent to both, so it needs a deliberate rule, most likely ordering the ex-spouse, the person and the current spouse as a run of three.
- Crossing reduction is a barycentre heuristic with four sweeps. It is not optimal and will occasionally produce an avoidable crossing. d3-dag's better decrossers are not an escape route: `decrossOpt` is documented as exponential and is unusable at this scale.
- Accessibility, hit-testing and keyboard navigation are ours to build. Cytoscape would have given hit-testing for free.
- If the tree ever needs to render far beyond 1000 nodes, SVG's per-element cost becomes the ceiling and a canvas renderer would have to be written. The layout would survive that change; the renderer would not.

**If this turns out to be wrong**, dagre is the fallback and the reversal is cheap: it plugs into the same renderer behind the same `layout(graph)` signature, and the couple-merging adapter it needs is already written in `candidates/dagre.js`. Its weakness is the whole-tree view, which this spike has separately shown is not the view that matters. Rejecting it was not a judgement about its maintenance — `dagre` itself is frozen at 0.8.5 from 2019, but `@dagrejs/dagre` is actively released.

**Discovered along the way, and relevant to PR 10 regardless of this decision:**

- **Fit-to-screen on a full 500-person tree is useless.** Seven generations of ~100 people each is roughly 40,400 × 1,030px, so fitting it to a 1440px viewport scales it to about 3.5% — an unreadable grey smear. This is a property of family trees, not of any candidate. It means the focused view (US-029) and `generationDepth` are the primary experience and the whole-tree view is a navigational overview at best. PR 10 should treat "fit to screen" as a way to locate yourself in the mini-map, not as a way to read the tree.
- The zoom floor must sit below whatever fit-to-screen produces for the largest supported graph, or "fit" lands at a scale the zoom control cannot return to. A 1000-person tree fits at about 0.02.

## Reproducing

```bash
cd spikes/tree-visualization
npm install
npm run build            # bundles and sizes the candidate libraries
node verify-graph.mjs    # invariants on the generated graph
node measure.mjs         # timings + screenshots for all four candidates
node capture-detail.mjs  # readable-scale comparison screenshots
node diagnose-pan.mjs    # the edge-span / extent diagnosis

dotnet publish wasm-probe -c Release -o /tmp/probe
node run-wasm-probe.mjs /tmp/probe/wwwroot
```

The prototype is throwaway. It is kept in the repository because the measurements are the reasoning behind this decision, and a decision whose evidence cannot be re-run is a decision that has to be taken on trust.
