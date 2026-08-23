// Why do two candidates that share a renderer and produce an identical DOM
// node count pan at very different speeds?
//
// The first measurement pass showed `hand` holding 16.7 ms frames at 1000 nodes
// while `dagre` and `d3dag` collapsed to 100+ ms, with the same 8579 SVG
// elements on screen. That difference cannot be the renderer, so it has to be
// the geometry the layout produced. This script tests two candidate
// explanations instead of assuming one:
//
//   A. Extent. A wider graph means a smaller fit-to-screen scale. Re-run the
//      pan at a FIXED scale for every candidate; if the gap closes, extent was
//      the cause.
//   B. Edge span. Long edges crossing the whole canvas have huge bounding
//      boxes, and an SVG path's raster invalidation is bounded by its box, not
//      by its ink. Measure the horizontal span of every parent-child edge.

import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join, extname, normalize } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const MIME = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
};

const server = createServer(async (req, res) => {
    try {
        const url = new URL(req.url, 'http://localhost');
        const rel = normalize(decodeURIComponent(url.pathname)).replace(/^(\.\.[/\\])+/, '');
        const body = await readFile(join(here, rel === '/' ? 'harness.html' : rel));
        res.writeHead(200, { 'content-type': MIME[extname(rel)] ?? 'application/octet-stream' });
        res.end(body);
    } catch {
        res.writeHead(404).end('not found');
    }
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const base = `http://127.0.0.1:${server.address().port}`;

const browser = await chromium.launch({
    executablePath: process.env.PLAYWRIGHT_CHROMIUM ?? '/opt/pw-browsers/chromium',
    args: ['--no-sandbox', '--disable-dev-shm-usage'],
});

const SIZE = 1000;
console.log(`graph size ${SIZE}\n`);
console.log(
    'candidate'.padEnd(11),
    'extent'.padStart(9),
    'edge span p50'.padStart(14),
    'edge span p95'.padStart(14),
    'edge span max'.padStart(14),
    'pan@fit'.padStart(9),
    'pan@k=0.25'.padStart(11),
);

for (const candidate of ['hand', 'dagre', 'd3dag']) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    await page.goto(`${base}/harness.html?candidate=${candidate}&size=${SIZE}`, { waitUntil: 'load' });
    await page.waitForFunction('window.__spikeReady === true', null, { timeout: 30000 });

    const geometry = await page.evaluate(() => {
        // Read the spans off the rendered paths rather than recomputing them
        // from the layout, so this measures what the browser was actually
        // asked to rasterise.
        const paths = [...document.querySelectorAll('.ft-edges path')];
        const spans = paths.map((p) => {
            const b = p.getBBox();
            return b.width;
        });
        spans.sort((a, b) => a - b);
        const pct = (q) => Math.round(spans[Math.min(spans.length - 1, Math.floor(spans.length * q))]);
        return {
            count: spans.length,
            p50: pct(0.5),
            p95: pct(0.95),
            max: Math.round(spans.at(-1)),
            extent: window.__spike.extent(),
        };
    });

    const panAt = async (fixedScale) => page.evaluate(async (k) => {
        const api = window.__spike.api;
        api.fitToScreen();
        if (k !== null) {
            // Drive to an identical scale for every candidate, so the only
            // remaining difference is where the layout put things.
            //
            // This throws rather than skipping when a candidate does not expose
            // its transform. The first version returned quietly, which meant
            // one candidate was silently re-measured at fit scale and reported
            // in the fixed-scale column — a wrong number is worse than a gap.
            if (!api.view) {
                throw new Error('candidate does not expose `view`; cannot set a fixed scale');
            }
            api.zoomBy(k / api.view.k);
            const reached = api.view.k;
            if (Math.abs(reached - k) / k > 0.01) {
                throw new Error(`wanted scale ${k}, reached ${reached}`);
            }
        }
        const gaps = [];
        let last = performance.now();
        let n = 0;
        await new Promise((resolve) => {
            const step = () => {
                const now = performance.now();
                if (n > 0) gaps.push(now - last);
                last = now;
                api.panBy(6, 2);
                if (++n >= 60) return resolve();
                requestAnimationFrame(step);
            };
            requestAnimationFrame(step);
        });
        gaps.sort((a, b) => a - b);
        return gaps[Math.floor(gaps.length / 2)];
    }, fixedScale);

    const atFit = await panAt(null);
    const atFixed = await panAt(0.25);

    console.log(
        candidate.padEnd(11),
        `${geometry.extent.width}`.padStart(9),
        `${geometry.p50}`.padStart(14),
        `${geometry.p95}`.padStart(14),
        `${geometry.max}`.padStart(14),
        `${atFit.toFixed(1)}ms`.padStart(9),
        `${atFixed.toFixed(1)}ms`.padStart(11),
    );

    await context.close();
}

await browser.close();
server.close();
