'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const { run, validateInput, operationSamples } = require('../Shared/OpenApiTestRunner.cjs');

async function withApi(handler, action) {
  const server = http.createServer(handler);
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  try { await action(base); }
  finally { await new Promise(resolve => server.close(resolve)); }
}
function createSpec(directory, extra = {}) {
  const spec = {
    openapi: '3.0.3',
    info: { title: 'RunMind fixture', version: '1.0.0' },
    servers: [{ url: 'https://must-not-be-selected.invalid' }],
    paths: {
      '/widgets/{id}': { get: {
        operationId: 'getWidget', parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer', example: 42 } }],
        responses: { '200': { description: 'Widget', content: { 'application/json': { schema: { $ref: '#/components/schemas/Widget' } } } } }
      } },
      '/widgets': {
        post: {
          operationId: 'createWidget',
          requestBody: { required: true, content: { 'application/json': { schema: { type: 'object', example: { name: 'New widget' } } } } },
          responses: { '201': { description: 'Created', content: { 'application/json': { schema: { $ref: '#/components/schemas/Widget' } } } } }
        }
      }
    },
    components: { schemas: { Widget: { type: 'object', required: ['id','name'], properties: { id: { type: 'integer' }, name: { type: 'string', minLength: 2 } }, additionalProperties: false } } },
    ...extra
  };
  fs.writeFileSync(path.join(directory, 'openapi.json'), JSON.stringify(spec));
  return spec;
}

test('OpenAPI request validation requires explicit HTTP(S) target and normalized spec path', () => {
  assert.equal(validateInput({ spec_path: 'docs/openapi.json', base_url: 'http://127.0.0.1:3000' }).includeMutations, false);
  for (const spec_path of ['../openapi.json', '/tmp/openapi.json', 'docs/../openapi.json']) assert.throws(() => validateInput({ spec_path, base_url: 'http://127.0.0.1:3000' }), /spec_path/);
  for (const base_url of ['file:///tmp/api', 'https://user:secret@example.test', 'https://example.test/?token=secret']) assert.throws(() => validateInput({ spec_path: 'openapi.json', base_url }), /base_url/);
});

test('OpenAPI generator uses local refs and safe examples, skips mutations by default', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-openapi-generate-'));
  try {
    const spec = createSpec(directory);
    const request = validateInput({ spec_path: 'openapi.json', base_url: 'http://127.0.0.1:3000' });
    const generated = operationSamples(spec, request, spec);
    assert.equal(generated.operations.length, 1);
    assert.equal(generated.operations[0].path, '/widgets/42');
    assert.equal(generated.operations[0].expect.statuses, undefined);
    assert.equal(generated.operations[0].responseSchemaChecked, true);
    assert.equal(generated.skipped.length, 1);
    assert.match(generated.skipped[0].reason, /excluded by default/);

    const mutating = operationSamples(spec, validateInput({ spec_path: 'openapi.json', base_url: 'http://127.0.0.1:3000', include_mutations: true }), spec);
    assert.equal(mutating.operations.length, 2);
    assert.deepEqual(mutating.operations[1].body_json, { name: 'New widget' });
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('OpenAPI runner validates response schema against an explicitly selected base URL', async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-openapi-run-'));
  try {
    createSpec(directory);
    await withApi((req, res) => {
      assert.match(req.url, /^\/widgets\/42$/);
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ id: 42, name: 'RunMind widget' }));
    }, async base => {
      const result = await run({ spec_path: 'openapi.json', base_url: base }, { workingDirectory: directory });
      assert.equal(result.success, true);
      assert.equal(result.operationsRun, 1);
      assert.equal(result.operationsWithResponseSchema, 1);
      assert.equal(result.results[0].assertions.find(item => item.kind === 'json_schema').passed, true);
      assert.equal(result.baseUrl, base + '/');
    });
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('OpenAPI runner reports a response schema mismatch as a failed contract', async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-openapi-fail-'));
  try {
    createSpec(directory);
    await withApi((req, res) => { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify({ id: 'not-an-integer', name: 'x', ignored: true })); }, async base => {
      const result = await run({ spec_path: 'openapi.json', base_url: base }, { workingDirectory: directory });
      assert.equal(result.success, false);
      const schema = result.results[0].assertions.find(item => item.kind === 'json_schema');
      assert.equal(schema.passed, false);
      assert.ok(schema.errors.some(item => /expected integer/.test(item)));
      assert.ok(schema.errors.some(item => /unexpected property/.test(item)));
    });
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('OpenAPI runner never follows remote references and rejects specification symlinks', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-openapi-ref-'));
  try {
    const spec = createSpec(directory);
    spec.paths['/remote'] = { get: { responses: { '200': { $ref: 'https://example.invalid/response.json' } } } };
    assert.throws(() => operationSamples(spec, validateInput({ spec_path: 'openapi.json', base_url: 'http://127.0.0.1:3000' }), spec), /Only local OpenAPI references/);
    if (process.platform !== 'win32') {
      fs.writeFileSync(path.join(directory, 'actual.json'), '{}');
      fs.symlinkSync(path.join(directory, 'actual.json'), path.join(directory, 'linked.json'));
      assert.throws(() => require('../Shared/OpenApiTestRunner.cjs').loadSpec('linked.json', directory), /symbolic links/);
    }
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});


test('OpenAPI response schemas are mapped to their declared status code, not mixed across 2xx variants', () => {
  const spec = {
    openapi: '3.1.0',
    paths: { '/resource': { get: { responses: {
      '200': { description: 'OK', content: { 'application/json': { schema: { type: 'object', properties: { value: { type: 'string' } }, required: ['value'] } } } },
      '202': { description: 'Accepted', content: { 'application/json': { schema: { type: 'object', properties: { jobId: { type: 'integer' } }, required: ['jobId'] } } } },
      '204': { description: 'No content' }
    } } } }
  };
  const generated = operationSamples(spec, validateInput({ spec_path: 'openapi.json', base_url: 'http://127.0.0.1:3000' }), spec);
  const op = generated.operations[0];
  assert.deepEqual(op.expect.status, [200, 202, 204]);
  assert.deepEqual(Object.keys(op.expect.responseSchemas).sort(), ['200','202']);
  assert.deepEqual(op.responseSchemaStatuses.sort(), [200, 202]);
});


test('OpenAPI contract validates the schema for the actual successful response status and permits schema-less 204', async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-openapi-status-'));
  try {
    fs.writeFileSync(path.join(directory, 'openapi.json'), JSON.stringify({
      openapi: '3.1.0',
      paths: { '/resource': { get: { responses: {
        '200': { description: 'OK', content: { 'application/json': { schema: { type: 'object', properties: { value: { type: 'string' } }, required: ['value'] } } } },
        '202': { description: 'Accepted', content: { 'application/json': { schema: { type: 'object', properties: { jobId: { type: 'integer' } }, required: ['jobId'] } } } },
        '204': { description: 'No content' }
      } } } }
    }));
    await withApi((req, res) => { res.writeHead(202, { 'content-type': 'application/json' }); res.end(JSON.stringify({ jobId: 17 })); }, async base => {
      const result = await run({ spec_path: 'openapi.json', base_url: base }, { workingDirectory: directory });
      assert.equal(result.success, true);
      assert.deepEqual(result.results[0].assertions.filter(item => item.kind === 'json_schema').map(item => item.status), [202]);
    });
    await withApi((req, res) => { res.writeHead(204); res.end(); }, async base => {
      const result = await run({ spec_path: 'openapi.json', base_url: base }, { workingDirectory: directory });
      assert.equal(result.success, true);
      assert.equal(result.results[0].assertions.some(item => item.kind === 'json_schema'), false);
    });
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
