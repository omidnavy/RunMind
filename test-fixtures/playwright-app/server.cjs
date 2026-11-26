'use strict';

const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');

const host = '127.0.0.1';
const port = Number(process.argv[2] || 4320);
const root = __dirname;
const files = new Map([
  ['/', ['index.html', 'text/html; charset=utf-8']],
  ['/index.html', ['index.html', 'text/html; charset=utf-8']],
  ['/styles.css', ['styles.css', 'text/css; charset=utf-8']],
  ['/app.js', ['app.js', 'text/javascript; charset=utf-8']]
]);

const server = http.createServer((request, response) => {
  const pathname = new URL(request.url || '/', `http://${host}:${port}`).pathname;
  if (pathname === '/health') {
    response.writeHead(200, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' });
    response.end(JSON.stringify({ status: 'ok', port }));
    return;
  }
  if (pathname === '/cors-probe') {
    // Intentionally omit Access-Control-Allow-Origin. A page on the other
    // fixture origin must be blocked by the browser's same-origin policy.
    response.writeHead(200, { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store' });
    response.end('CORS fixture response is intentionally not cross-origin readable.\n');
    return;
  }

  const entry = files.get(pathname);
  if (!entry) {
    response.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' });
    response.end('Not found\n');
    return;
  }

  try {
    const content = fs.readFileSync(path.join(root, entry[0]));
    response.writeHead(200, {
      'content-type': entry[1],
      'content-length': content.length,
      'cache-control': 'no-store',
      'x-content-type-options': 'nosniff'
    });
    response.end(content);
  } catch {
    response.writeHead(500, { 'content-type': 'text/plain; charset=utf-8' });
    response.end('Fixture file unavailable\n');
  }
});

server.listen(port, host, () => {
  console.log(`Playwright fixture listening on http://${host}:${port}`);
});

server.on('error', error => {
  console.error(`Playwright fixture startup failed: ${error.code || error.message}`);
  process.exitCode = 1;
});
