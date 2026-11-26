'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');
const apiRunner = require('./ApiTestRunner.cjs');

const MAX_SPEC_BYTES = 2 * 1024 * 1024;
const MAX_OPERATIONS = 20;
const METHODS = new Set(['get', 'head', 'options', 'post', 'put', 'patch', 'delete']);
const MUTATING = new Set(['post', 'put', 'patch', 'delete']);

function parseInputJson(input) { return JSON.parse(String(input || '').replace(/^\uFEFF/, '') || '{}'); }
function resolveLocalRef(root, ref) {
  if (typeof ref !== 'string' || !ref.startsWith('#/')) throw new Error(`Only local OpenAPI references are supported: ${String(ref).slice(0, 160)}`);
  let value = root;
  for (const raw of ref.slice(2).split('/')) {
    const key = raw.replace(/~1/g, '/').replace(/~0/g, '~');
    if (!value || typeof value !== 'object' || !Object.prototype.hasOwnProperty.call(value, key)) throw new Error(`OpenAPI reference could not be resolved: ${ref.slice(0, 200)}`);
    value = value[key];
  }
  return value;
}
function dereference(value, root, stack = new Set(), depth = 0) {
  if (depth > 24) throw new Error('OpenAPI schema/reference nesting exceeded 24 levels.');
  if (Array.isArray(value)) return value.map(item => dereference(item, root, stack, depth + 1));
  if (!value || typeof value !== 'object') return value;
  if (typeof value.$ref === 'string') {
    const ref = value.$ref;
    if (stack.has(ref)) throw new Error(`Circular OpenAPI reference is not supported: ${ref.slice(0, 160)}`);
    const next = new Set(stack); next.add(ref);
    const resolved = dereference(resolveLocalRef(root, ref), root, next, depth + 1);
    const siblings = Object.fromEntries(Object.entries(value).filter(([key]) => key !== '$ref'));
    return { ...resolved, ...dereference(siblings, root, next, depth + 1) };
  }
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, dereference(item, root, stack, depth + 1)]));
}
function sampleValue(rawSchema, root) {
  const schema = dereference(rawSchema || {}, root);
  if (Object.prototype.hasOwnProperty.call(schema, 'example')) return { found: true, value: schema.example };
  if (Object.prototype.hasOwnProperty.call(schema, 'default')) return { found: true, value: schema.default };
  if (Array.isArray(schema.enum) && schema.enum.length) return { found: true, value: schema.enum[0] };
  return { found: false };
}
function extractContentExample(content, root) {
  if (!content || typeof content !== 'object') return { found: false };
  const mediaType = Object.keys(content).find(key => key.toLowerCase() === 'application/json' || key.toLowerCase().endsWith('+json'));
  if (!mediaType) return { found: false };
  const media = content[mediaType];
  if (media?.example !== undefined) return { found: true, value: media.example };
  if (media?.examples && typeof media.examples === 'object') {
    const first = Object.values(media.examples)[0];
    if (first && Object.prototype.hasOwnProperty.call(first, 'value')) return { found: true, value: first.value };
  }
  return sampleValue(media?.schema, root);
}
function responseSchemas(responses, root) {
  const successfulStatuses = [];
  const schemasByStatus = {};
  for (const [status, rawResponse] of Object.entries(responses || {})) {
    if (!/^2\d\d$/.test(status)) continue;
    const numericStatus = Number(status);
    successfulStatuses.push(numericStatus);
    const response = dereference(rawResponse, root);
    const content = response?.content;
    const mediaType = content && Object.keys(content).find(key => key.toLowerCase() === 'application/json' || key.toLowerCase().endsWith('+json'));
    if (mediaType && content[mediaType]?.schema) schemasByStatus[String(numericStatus)] = dereference(content[mediaType].schema, root);
  }
  return { statuses: successfulStatuses.sort((a, b) => a - b).slice(0, 10), schemasByStatus };
}
function validateInput(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new Error('An OpenAPI contract test request is required.');
  const specPath = String(input.spec_path || '').trim();
  if (!specPath || specPath.length > 1024 || path.isAbsolute(specPath) || specPath.split(/[\\/]/).some(part => !part || part === '.' || part === '..')) throw new Error('spec_path must be a normalized workspace-relative JSON or YAML file path.');
  const base = new URL(String(input.base_url || ''));
  if (!['http:', 'https:'].includes(base.protocol) || base.username || base.password || base.search || base.hash) throw new Error('base_url must be an HTTP(S) URL without credentials, query, or fragment.');
  const headers = input.headers === undefined ? {} : input.headers;
  if (!headers || typeof headers !== 'object' || Array.isArray(headers) || Object.keys(headers).length > 30) throw new Error('headers must be an object with at most 30 entries.');
  for (const [name, value] of Object.entries(headers)) if (!/^[!#$%&'*+.^_`|~0-9A-Za-z-]{1,100}$/.test(name) || typeof value !== 'string' || value.length > 8000) throw new Error('OpenAPI test contains an invalid request header.');
  const maximum = Math.max(1, Math.min(MAX_OPERATIONS, Number(input.max_operations) || MAX_OPERATIONS));
  return { specPath, baseUrl: base.href, includeMutations: input.include_mutations === true, headers, timeoutSeconds: Math.max(5, Math.min(120, Number(input.timeout_seconds) || 30)), maximum };
}
function loadSpec(specPath, cwd, dependencies = {}) {
  const fullPath = path.resolve(cwd, specPath);
  const relative = path.relative(cwd, fullPath);
  if (!relative || relative.startsWith('..' + path.sep) || path.isAbsolute(relative)) throw new Error('OpenAPI specification must remain inside the selected workspace.');
  let current = cwd;
  for (const part of relative.split(path.sep)) { current = path.join(current, part); const stat = fs.lstatSync(current); if (stat.isSymbolicLink()) throw new Error('OpenAPI specification paths cannot contain symbolic links.'); }
  const stat = fs.statSync(fullPath);
  if (!stat.isFile() || stat.size > MAX_SPEC_BYTES) throw new Error('OpenAPI specification must be a regular file no larger than 2 MB.');
  const text = fs.readFileSync(fullPath, 'utf8').replace(/^\uFEFF/, '');
  if (/\.json$/i.test(fullPath) || /^[\s\uFEFF]*[\[{]/.test(text)) return JSON.parse(text);
  const requireFromWorkspace = dependencies.requireFromWorkspace || createRequire(path.join(cwd, '__runmind_openapi__.cjs'));
  try {
    const yaml = requireFromWorkspace('yaml');
    return yaml.parse(text, { maxAliasCount: 100 });
  } catch (first) {
    if (first?.code !== 'MODULE_NOT_FOUND') throw new Error(`OpenAPI YAML parsing failed: ${String(first.message || first).slice(0, 500)}`);
    try {
      const jsYaml = requireFromWorkspace('js-yaml');
      return jsYaml.load(text, { json: false, maxAliasCount: 100 });
    } catch (second) {
      if (second?.code === 'MODULE_NOT_FOUND') throw new Error('This is an OpenAPI YAML file, but neither `yaml` nor `js-yaml` is installed in the selected project. No dependency was installed. Add one to the project or provide an equivalent JSON OpenAPI document.');
      throw new Error(`OpenAPI YAML parsing failed: ${String(second.message || second).slice(0, 500)}`);
    }
  }
}
function operationSamples(spec, request, root) {
  const operations = [];
  const skipped = [];
  for (const [rawPath, pathItemRaw] of Object.entries(spec.paths || {})) {
    if (!rawPath.startsWith('/') || rawPath.length > 1000) { skipped.push({ path: String(rawPath).slice(0, 300), reason: 'Path must begin with / and be at most 1,000 characters.' }); continue; }
    const pathItem = dereference(pathItemRaw, root);
    for (const [method, rawOperation] of Object.entries(pathItem || {})) {
      if (!METHODS.has(method.toLowerCase())) continue;
      const verb = method.toLowerCase();
      if (MUTATING.has(verb) && !request.includeMutations) { skipped.push({ method: verb.toUpperCase(), path: rawPath, reason: 'Mutating operation excluded by default; set include_mutations=true to run with fresh approval.' }); continue; }
      const operation = dereference(rawOperation, root);
      const parameters = new Map();
      for (const item of [...(pathItem.parameters || []), ...(operation.parameters || [])]) {
        const parameter = dereference(item, root);
        if (parameter?.name && parameter?.in) parameters.set(`${parameter.in}:${parameter.name}`, parameter);
      }
      let pathValue = rawPath;
      const query = new URLSearchParams();
      const headers = { ...request.headers };
      let incomplete = '';
      for (const parameter of parameters.values()) {
        const sample = parameter.example !== undefined ? { found: true, value: parameter.example } : sampleValue(parameter.schema, root);
        if (!sample.found) { if (parameter.required) incomplete = `Required ${parameter.in} parameter '${parameter.name}' has no example/default/enum value.`; continue; }
        const value = String(sample.value);
        if (parameter.in === 'path') pathValue = pathValue.replaceAll(`{${parameter.name}}`, encodeURIComponent(value));
        else if (parameter.in === 'query') query.set(parameter.name, value);
        else if (parameter.in === 'header') headers[parameter.name] = value;
        else if (parameter.in === 'cookie' && parameter.required) incomplete = `Required cookie parameter '${parameter.name}' cannot be generated safely.`;
      }
      if (/\{[^}]+\}/.test(pathValue)) incomplete ||= 'Path contains a required placeholder without an example value.';
      if (incomplete) { skipped.push({ method: verb.toUpperCase(), path: rawPath, operationId: operation.operationId || null, reason: incomplete }); continue; }
      if (query.size) pathValue += `${pathValue.includes('?') ? '&' : '?'}${query.toString()}`;
      let body;
      const requestBody = operation.requestBody ? dereference(operation.requestBody, root) : null;
      if (requestBody) {
        body = extractContentExample(requestBody.content, root);
        if (!body.found && requestBody.required) { skipped.push({ method: verb.toUpperCase(), path: rawPath, operationId: operation.operationId || null, reason: 'Required requestBody has no JSON example/default/enum value.' }); continue; }
        if (body.found && !['post','put','patch','delete'].includes(verb)) { skipped.push({ method: verb.toUpperCase(), path: rawPath, operationId: operation.operationId || null, reason: `Request body generation is not enabled for ${verb.toUpperCase()}.` }); continue; }
      }
      const response = responseSchemas(operation.responses, root);
      const statuses = response.statuses.filter(status => status >= 200 && status < 300);
      if (!statuses.length) { skipped.push({ method: verb.toUpperCase(), path: rawPath, operationId: operation.operationId || null, reason: 'No successful 2xx response is declared for this operation.' }); continue; }
      const expectedStatus = statuses;
      const expect = { status: expectedStatus };
      if (verb !== 'head' && Object.keys(response.schemasByStatus).length) expect.responseSchemas = response.schemasByStatus;
      const name = String(operation.operationId || `${verb}-${rawPath}`).replace(/[^a-zA-Z0-9_-]/g, '-').replace(/-+/g, '-').slice(0, 60) || `operation-${operations.length + 1}`;
      operations.push({ name, method: verb.toUpperCase(), path: pathValue, headers, ...(body?.found ? { body_json: body.value } : {}), expect, operationId: operation.operationId || null, specificationPath: rawPath, responseSchemaChecked: !!expect.responseSchemas, responseSchemaStatuses: Object.keys(expect.responseSchemas || {}).map(Number) });
      if (operations.length >= request.maximum) break;
    }
    if (operations.length >= request.maximum) break;
  }
  return { operations, skipped };
}
async function run(input, dependencies = {}) {
  const request = validateInput(input);
  const cwd = path.resolve(dependencies.workingDirectory || process.cwd());
  const spec = loadSpec(request.specPath, cwd, dependencies);
  if (!spec || typeof spec !== 'object' || !/^3\.(?:0|1)\./.test(String(spec.openapi || ''))) throw new Error('Only OpenAPI 3.0.x and 3.1.x documents are supported.');
  if (!spec.paths || typeof spec.paths !== 'object') throw new Error('OpenAPI document has no paths object.');
  const generated = operationSamples(spec, request, spec);
  if (!generated.operations.length) return { success: false, specification: request.specPath, operationsRun: 0, skipped: generated.skipped.slice(0, 100), message: 'No executable operations could be generated. Required parameter/body examples may be missing, or all operations may be mutation methods excluded by default.' };
  const apiResult = await apiRunner.run({ base_url: request.baseUrl, requests: generated.operations, timeout_seconds: request.timeoutSeconds }, dependencies);
  const operationResults = apiResult.results.map((result, index) => ({ ...result, operationId: generated.operations[index]?.operationId || null, specificationPath: generated.operations[index]?.specificationPath || null, responseSchemaChecked: generated.operations[index]?.responseSchemaChecked || false, responseSchemaStatuses: generated.operations[index]?.responseSchemaStatuses || [] }));
  return { ...apiResult, runner: 'openapi-contract', specification: request.specPath, openapiVersion: spec.openapi, operationsRun: operationResults.length, operationsWithResponseSchema: operationResults.filter(item => item.responseSchemaChecked).length, responseSchemaChecks: operationResults.reduce((sum, item) => sum + (Array.isArray(item.assertions) ? item.assertions.filter(assertion => assertion.kind === 'json_schema').length : 0), 0), skipped: generated.skipped.slice(0, 100), results: operationResults };
}
async function main() {
  try { let text = ''; for await (const chunk of process.stdin) { text += chunk; if (Buffer.byteLength(text) > 256 * 1024) throw new Error('OpenAPI test request exceeds 256 KB.'); }
    const input = parseInputJson(text); input.working_directory = process.cwd(); const result = await run(input); process.stdout.write(`${JSON.stringify(result)}\n`); process.exitCode = result.success ? 0 : 2;
  } catch (error) { process.stdout.write(`${JSON.stringify({ success: false, error: String(error.message || error).slice(0, 3000) })}\n`); process.exitCode = 1; }
}
if (require.main === module) main();
module.exports = { parseInputJson, validateInput, loadSpec, resolveLocalRef, dereference, sampleValue, operationSamples, run, MAX_OPERATIONS };
