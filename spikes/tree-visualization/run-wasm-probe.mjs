// Publishes nothing itself — run `dotnet publish` on wasm-probe first, then
// point this at the output:
//
//   dotnet publish wasm-probe -c Release -o /tmp/probe
//   node run-wasm-probe.mjs /tmp/probe/wwwroot
//
// Serves the published site the way GitHub Pages does (static files, correct
// MIME types for .wasm and .dat) and reads the probe's results back out.

import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { join, extname, normalize, resolve } from 'node:path';

const root = resolve(process.argv[2] ?? './wasm-probe/bin/Release/net10.0/publish/wwwroot');

const MIME = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.wasm': 'application/wasm',
    '.dat': 'application/octet-stream',
    '.woff2': 'font/woff2',
    '.pdb': 'application/octet-stream',
    '.blat': 'application/octet-stream',
};

const server = createServer(async (req, res) => {
    try {
        const url = new URL(req.url, 'http://localhost');
        const rel = normalize(decodeURIComponent(url.pathname)).replace(/^(\.\.[/\\])+/, '');
        const path = join(root, rel === '/' ? 'index.html' : rel);
        const body = await readFile(path);
        res.writeHead(200, { 'content-type': MIME[extname(path)] ?? 'application/octet-stream' });
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
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await context.newPage();

const problems = [];
page.on('pageerror', (e) => problems.push(`pageerror: ${e}`));
page.on('console', (m) => {
    if (m.type() === 'error') {
        problems.push(`console: ${m.text()}`);
    }
});
page.on('requestfailed', (r) => problems.push(`requestfailed: ${r.url()}`));

await page.goto(base, { waitUntil: 'load' });

try {
    await page.waitForFunction('window.__probeReady === true', null, { timeout: 120000 });
} catch {
    console.error('probe never signalled ready');
    for (const p of problems.slice(0, 15)) {
        console.error('  ' + p);
    }
    await browser.close();
    server.close();
    process.exit(1);
}

const results = await page.evaluate(() => window.__probeResults);

const row = (k, v) => console.log(`  ${k.padEnd(38)} ${v}`);
console.log('\nBlazor WebAssembly interop probe');
row('graph', `${results.nodeCount} nodes, ${results.edgeCount} edges`);
row('build DTO in C#', `${results.buildMs.toFixed(1)} ms`);
row('interop — object argument', `${results.objectArgMs.toFixed(1)} ms`);
row('interop — JSON string (reflection)', `${results.jsonStringMs.toFixed(1)} ms`);
row('interop — JSON string (source-gen)', `${results.jsonSourceGenMs.toFixed(1)} ms`);
row('payload', `${(results.payloadBytes / 1024).toFixed(0)} kB`);
row('layout in JS', `${results.layoutMs.toFixed(1)} ms`);
row('render in JS', `${results.renderMs.toFixed(1)} ms`);

// Pan the tree that Blazor handed over, to confirm interaction stays on the JS
// side of the boundary and never re-enters .NET.
const pan = await page.evaluate(async () => {
    const gaps = [];
    let last = performance.now();
    let n = 0;
    await new Promise((resolve) => {
        const step = () => {
            const now = performance.now();
            if (n > 0) gaps.push(now - last);
            last = now;
            window.__probeApi.panBy(6, 2);
            if (++n >= 60) return resolve();
            requestAnimationFrame(step);
        };
        requestAnimationFrame(step);
    });
    gaps.sort((a, b) => a - b);
    return { p50: gaps[Math.floor(gaps.length / 2)], p95: gaps[Math.floor(gaps.length * 0.95)] };
});
row('pan under Blazor (p50 / p95)', `${pan.p50.toFixed(1)} / ${pan.p95.toFixed(1)} ms`);
row('C# generator matches JS generator', results.matchesJsGenerator ? 'yes' : 'NO');

if (!results.matchesJsGenerator) {
    // The probe's layout and render figures are only comparable with the
    // harness's if both drew the same graph, so this is a hard failure rather
    // than a warning.
    console.error('\n  generator mismatch — probe timings are NOT comparable to the harness:');
    for (const m of results.generatorMismatches) {
        console.error('    ' + m);
    }
}

await page.screenshot({ path: 'results/wasm-probe.png' });

const { writeFile } = await import('node:fs/promises');
await writeFile(
    'results/wasm-probe.json',
    JSON.stringify({ generatedAt: new Date().toISOString(), ...results, pan }, null, 2) + '\n',
);

if (problems.length > 0) {
    console.log('\n  browser problems:');
    for (const p of problems.slice(0, 10)) {
        console.log('    ' + p);
    }
}

await browser.close();
server.close();

process.exit(results.matchesJsGenerator ? 0 : 1);
