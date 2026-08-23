// Drives every candidate through the same browser and writes results/summary.json
// plus a screenshot per candidate per viewport.
//
// The measurement that matters is not "how long did it take to draw once" — a
// one-off cost on a page the user opens deliberately is forgivable. It is the
// frame time while panning, because that is the difference between a tree that
// feels attached to the cursor and one that smears. So the pan loop below runs
// inside requestAnimationFrame and records the interval between frames, rather
// than timing a synchronous loop that never yields to the compositor.

import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join, extname, normalize } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const resultsDir = join(here, 'results');

const MIME = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
};

const server = createServer(async (req, res) => {
    try {
        const url = new URL(req.url, 'http://localhost');
        const rel = normalize(decodeURIComponent(url.pathname)).replace(/^(\.\.[/\\])+/, '');
        const path = join(here, rel === '/' ? 'harness.html' : rel);
        const body = await readFile(path);
        res.writeHead(200, { 'content-type': MIME[extname(path)] ?? 'application/octet-stream' });
        res.end(body);
    } catch {
        res.writeHead(404).end('not found');
    }
});

await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const port = server.address().port;
const base = `http://127.0.0.1:${port}`;

const CANDIDATES = ['hand', 'dagre', 'd3dag', 'cytoscape'];
const SIZES = [100, 500, 1000];
const VIEWPORTS = {
    desktop: { width: 1440, height: 900 },
    mobile: { width: 390, height: 844 },
};

await mkdir(resultsDir, { recursive: true });

const executablePath = process.env.PLAYWRIGHT_CHROMIUM ?? '/opt/pw-browsers/chromium';
const browser = await chromium.launch({
    executablePath,
    args: ['--no-sandbox', '--disable-dev-shm-usage'],
});

const payloadSizes = JSON.parse(
    await readFile(join(resultsDir, 'payload-sizes.json'), 'utf8'),
);

const summary = { generatedAt: new Date().toISOString(), payloadSizes, runs: [] };

// Each configuration is run more than once and the median taken. A single run
// picks up JIT warm-up and whatever else the machine was doing, and the first
// pass of this spike reported one candidate as twice as slow as another that
// shares its renderer purely on that basis.
const REPEATS = 3;

for (const candidate of CANDIDATES) {
    for (const size of SIZES) {
      for (let rep = 0; rep < REPEATS; rep++) {
        const context = await browser.newContext({ viewport: VIEWPORTS.desktop });
        const page = await context.newPage();

        const errors = [];
        page.on('pageerror', (e) => errors.push(String(e)));
        page.on('console', (m) => {
            if (m.type() === 'error') {
                errors.push(m.text());
            }
        });

        const url = `${base}/harness.html?candidate=${candidate}&size=${size}`;
        await page.goto(url, { waitUntil: 'load' });

        let ok = true;
        try {
            await page.waitForFunction('window.__spikeReady === true', null, { timeout: 30000 });
        } catch {
            ok = false;
        }

        if (!ok) {
            summary.runs.push({ candidate, size, rep, ok: false, errors });
            await context.close();
            console.log(`${candidate} @ ${size}: FAILED — ${errors[0] ?? 'no ready signal'}`);
            continue;
        }

        const timings = await page.evaluate(() => ({
            layoutMs: window.__spike.api.timings.layoutMs,
            renderMs: window.__spike.api.timings.renderMs,
            domNodes: window.__spike.domNodes(),
            edges: window.__spike.graph.stats.edges,
            nodes: window.__spike.graph.stats.nodes,
            extent: window.__spike.extent(),
        }));

        // Pan for 90 animation frames, one small nudge per frame, recording the
        // gap between frames. A candidate that cannot keep up shows here and
        // nowhere else.
        const pan = await page.evaluate(async () => {
            const gaps = [];
            let last = performance.now();
            let n = 0;
            await new Promise((resolve) => {
                const step = () => {
                    const now = performance.now();
                    if (n > 0) {
                        gaps.push(now - last);
                    }
                    last = now;
                    window.__spike.api.panBy(6, 2);
                    if (++n >= 90) {
                        resolve();
                        return;
                    }
                    requestAnimationFrame(step);
                };
                requestAnimationFrame(step);
            });
            gaps.sort((a, b) => a - b);
            const pct = (p) => gaps[Math.min(gaps.length - 1, Math.floor(gaps.length * p))];
            return {
                frames: gaps.length,
                medianMs: pct(0.5),
                p95Ms: pct(0.95),
                worstMs: gaps.at(-1),
            };
        });

        const zoom = await page.evaluate(async () => {
            const t0 = performance.now();
            for (let i = 0; i < 20; i++) {
                window.__spike.api.zoomBy(i % 2 === 0 ? 1.1 : 1 / 1.1);
            }
            return { twentyStepsMs: performance.now() - t0 };
        });

        await page.evaluate(() => window.__spike.api.fitToScreen());

        if (size === 500 && rep === 0) {
            for (const [name, viewport] of Object.entries(VIEWPORTS)) {
                await page.setViewportSize(viewport);
                await page.evaluate(() => window.__spike.api.fitToScreen());
                await page.waitForTimeout(120);
                await page.screenshot({
                    path: join(resultsDir, `${candidate}-${name}.png`),
                });
            }
            await page.setViewportSize(VIEWPORTS.desktop);
        }

        summary.runs.push({ candidate, size, rep, ok: true, ...timings, pan, zoom, errors });

        await context.close();
      }
    }
}

// Aggregate to medians across repeats — this is what the ADR quotes.
const median = (xs) => {
    const s = [...xs].sort((a, b) => a - b);
    return s.length === 0 ? null : s[Math.floor(s.length / 2)];
};

summary.medians = [];
for (const candidate of CANDIDATES) {
    for (const size of SIZES) {
        const runs = summary.runs.filter((r) => r.candidate === candidate && r.size === size && r.ok);
        if (runs.length === 0) {
            summary.medians.push({ candidate, size, ok: false });
            console.log(`${candidate} @ ${size}: FAILED`);
            continue;
        }
        const row = {
            candidate,
            size,
            ok: true,
            nodes: runs[0].nodes,
            edges: runs[0].edges,
            domNodes: runs[0].domNodes,
            extent: runs[0].extent,
            layoutMs: median(runs.map((r) => r.layoutMs)),
            renderMs: median(runs.map((r) => r.renderMs)),
            panMedianMs: median(runs.map((r) => r.pan.medianMs)),
            panP95Ms: median(runs.map((r) => r.pan.p95Ms)),
            zoom20Ms: median(runs.map((r) => r.zoom.twentyStepsMs)),
        };
        summary.medians.push(row);
        console.log(
            `${candidate} @ ${size}: layout ${row.layoutMs.toFixed(1)}ms  ` +
            `render ${row.renderMs.toFixed(1)}ms  dom ${row.domNodes}  ` +
            `extent ${row.extent.width}x${row.extent.height}  ` +
            `pan p50 ${row.panMedianMs.toFixed(1)}ms p95 ${row.panP95Ms.toFixed(1)}ms`,
        );
    }
}

await browser.close();
server.close();

await writeFile(
    join(resultsDir, 'summary.json'),
    JSON.stringify(summary, null, 2) + '\n',
);

console.log(`\nwrote ${join(resultsDir, 'summary.json')}`);
