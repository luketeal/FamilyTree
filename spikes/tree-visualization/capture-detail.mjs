// Fit-to-screen on a 500-person tree is a grey smear — the graph is ~34,000px
// wide and ~1,000px tall, so fitting it to a 1440px viewport scales it to about
// 4%. That is a real finding about the tree view, but it means the whole-graph
// screenshots say nothing about whether the five edge styles are actually
// distinguishable or whether a node card looks right.
//
// This captures each candidate twice more: a small graph at a readable scale,
// and a 500-person graph zoomed to 1:1 over a populated region.

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

for (const candidate of ['hand', 'dagre', 'd3dag', 'cytoscape']) {
    for (const [label, size, scale] of [['small', 60, null], ['detail', 500, 1]]) {
        const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
        const page = await context.newPage();
        await page.goto(`${base}/harness.html?candidate=${candidate}&size=${size}`, { waitUntil: 'load' });
        await page.waitForFunction('window.__spikeReady === true', null, { timeout: 30000 });

        if (scale !== null) {
            await page.evaluate((k) => {
                const api = window.__spike.api;
                api.fitToScreen();
                if (api.view) {
                    api.zoomBy(k / api.view.k);
                } else {
                    // Cytoscape exposes its own zoom rather than a `view`.
                    api.zoomBy(1);
                }
                // Walk into the middle generations, where couples, children,
                // remarriages and phantoms all appear together.
                api.panBy(-1200, -300);
            }, scale);
        }

        await page.waitForTimeout(200);
        await page.screenshot({ path: join(here, 'results', `${candidate}-${label}.png`) });
        await context.close();
    }
}

await browser.close();
server.close();
console.log('detail screenshots written to results/');
