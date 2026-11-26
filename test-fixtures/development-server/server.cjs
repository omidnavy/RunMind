'use strict';

const http = require('node:http');
const port = Number(process.argv[2] || 4317);

const server = http.createServer((request, response) => {
  if (request.url === '/health') {
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ status: 'ok', port }));
    return;
  }
  response.writeHead(200, { 'content-type': 'text/plain; charset=utf-8' });
  response.end(`RunMind fixture server on ${port}\n`);
});

server.listen(port, '127.0.0.1', () => {
  console.log(`fixture server listening on http://127.0.0.1:${port}`);
  console.log('token=fixture-secret-value');
});

server.on('error', error => {
  console.error(`server startup failed: ${error.code || error.message}`);
  process.exitCode = 1;
});
