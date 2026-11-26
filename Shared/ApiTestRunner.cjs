'use strict';

const { performance } = require('node:perf_hooks');
const { isDeepStrictEqual } = require('node:util');

const MAX_REQUESTS = 20;
const MAX_INPUT_BYTES = 512 * 1024;

function parseInputJson(input) {
  const normalized = String(input || '').replace(/^\uFEFF/, '');
  return JSON.parse(normalized || '{}');
}
const MAX_RESPONSE_BYTES = 1024 * 1024;
const MAX_BODY_TEXT = 64 * 1024;
const MAX_OUTPUT_BYTES = 256 * 1024;
const METHODS = new Set(['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS']);
const SENSITIVE_KEY = /(?:authorization|proxy-authorization|cookie|set-cookie|token|api.?key|secret|credential)/i;
const SECRET_FIELD = /(?:password|secret|token|api.?key|credential)/i;

function validateRequest(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new Error('An API test request object is required.');
  let base;
  try { base = new URL(String(input.base_url || '')); } catch { throw new Error('base_url must be an absolute HTTP or HTTPS URL.'); }
  if (!['http:', 'https:'].includes(base.protocol) || base.username || base.password || base.hash || base.search)
    throw new Error('base_url must use HTTP/HTTPS and cannot contain credentials, a query, or a fragment.');
  if (!Array.isArray(input.requests) || input.requests.length < 1 || input.requests.length > MAX_REQUESTS)
    throw new Error(`Provide between 1 and ${MAX_REQUESTS} API requests.`);
  const timeoutSeconds = Math.max(5, Math.min(120, Number(input.timeout_seconds) || 60));
  const requests = input.requests.map((raw, index) => {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error(`Request ${index + 1} must be an object.`);
    const name = String(raw.name || `request-${index + 1}`);
    if (!/^[a-zA-Z0-9_-]{1,60}$/.test(name)) throw new Error(`Request ${index + 1} has an invalid name.`);
    const method = String(raw.method || 'GET').toUpperCase();
    if (!METHODS.has(method)) throw new Error(`Request ${index + 1} uses an unsupported HTTP method.`);
    const requestPath = String(raw.path || '');
    if (!requestPath || requestPath.length > 2000) throw new Error(`Request ${index + 1} requires a path up to 2,000 characters.`);
    const url = new URL(requestPath, base);
    if (url.origin !== base.origin || !['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.hash)
      throw new Error(`Request ${index + 1} path must remain on the base URL origin and cannot contain credentials or a fragment.`);
    if (raw.headers != null && (typeof raw.headers !== 'object' || Array.isArray(raw.headers))) throw new Error(`Request ${index + 1} headers must be an object.`);
    const headers = {};
    for (const [key, value] of Object.entries(raw.headers || {})) {
      if (!/^[!#$%&'*+.^_`|~0-9A-Za-z-]{1,100}$/.test(key) || typeof value !== 'string' || value.length > 8000)
        throw new Error(`Request ${index + 1} contains an invalid header.`);
      headers[key] = value;
    }
    if (raw.body_json !== undefined && raw.body_text !== undefined) throw new Error(`Request ${index + 1} cannot define both body_json and body_text.`);
    if (raw.body_text !== undefined && (typeof raw.body_text !== 'string' || raw.body_text.length > MAX_BODY_TEXT)) throw new Error(`Request ${index + 1} body_text exceeds 64 KB.`);
    if (['GET', 'HEAD'].includes(method) && (raw.body_json !== undefined || raw.body_text !== undefined)) throw new Error(`${method} requests cannot include a body.`);
    if (raw.expect !== undefined && (!raw.expect || typeof raw.expect !== 'object' || Array.isArray(raw.expect))) throw new Error(`Request ${index + 1} expect must be an object.`);
    const expect = raw.expect || {};
    const statuses = expect.status === undefined ? null : (Array.isArray(expect.status) ? expect.status : [expect.status]);
    if (statuses && (!statuses.length || statuses.length > 10 || statuses.some(status => !Number.isInteger(status) || status < 100 || status > 599)))
      throw new Error(`Request ${index + 1} has invalid expected HTTP status values.`);
    const json = Array.isArray(expect.json) ? expect.json : [];
    if (json.length > 10) throw new Error(`Request ${index + 1} may define at most 10 JSON assertions.`);
    for (const assertion of json) {
      if (!assertion || typeof assertion !== 'object' || !/^[A-Za-z0-9_$.[\]-]{1,300}$/.test(String(assertion.path || '')))
        throw new Error(`Request ${index + 1} has an invalid JSON assertion path.`);
      if (!['exists','equals','contains','type','one_of','greater_than'].includes(String(assertion.operator || 'exists')))
        throw new Error(`Request ${index + 1} has an unsupported JSON assertion operator.`);
    }
    const headerAssertions = Array.isArray(expect.headers) ? expect.headers : [];
    if (headerAssertions.length > 10) throw new Error(`Request ${index + 1} may define at most 10 header assertions.`);
    for (const assertion of headerAssertions) {
      if (!assertion || typeof assertion !== 'object' || !/^[!#$%&'*+.^_`|~0-9A-Za-z-]{1,100}$/.test(String(assertion.name || '')) || !['exists','equals','contains'].includes(String(assertion.operator || 'exists')))
        throw new Error(`Request ${index + 1} has an invalid response-header assertion.`);
    }
    if (expect.body_contains !== undefined && (typeof expect.body_contains !== 'string' || expect.body_contains.length > 4000))
      throw new Error(`Request ${index + 1} body_contains must be a string up to 4,000 characters.`);
    return { name, method, path: requestPath, headers, body_json: raw.body_json, body_text: raw.body_text, expect: { statuses, json, headers: headerAssertions, body_contains: expect.body_contains } };
  });
  return { baseUrl: base.href, timeoutSeconds, requests };
}

function resolveEnvironment(value, env) {
  if (typeof value === 'string') return value.replace(/\{\{env:([A-Za-z_][A-Za-z0-9_]*)\}\}/g, (match, name) => {
    if (!Object.prototype.hasOwnProperty.call(env, name)) throw new Error(`Required environment variable '${name}' is not defined.`);
    return String(env[name]);
  });
  if (Array.isArray(value)) return value.map(item => resolveEnvironment(item, env));
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, resolveEnvironment(item, env)]));
  return value;
}

function getJsonPath(value, expression) {
  const parts = String(expression).replace(/\[(\d+)\]/g, '.$1').split('.').filter(Boolean);
  let current = value;
  for (const part of parts) {
    if (part === '__proto__' || part === 'prototype' || part === 'constructor') return { exists: false };
    if (current === null || current === undefined || !Object.prototype.hasOwnProperty.call(Object(current), part)) return { exists: false };
    current = current[part];
  }
  return { exists: true, value: current };
}

function redact(value, key = '') {
  if (SECRET_FIELD.test(key)) return '[REDACTED]';
  if (Array.isArray(value)) return value.slice(0, 100).map(item => redact(item));
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).slice(0, 200).map(([name, item]) => [name, redact(item, name)]));
  return typeof value === 'string' ? value.slice(0, 4000) : value;
}


function collectSecrets(value, env, secrets) {
  if (typeof value === 'string') {
    for (const match of value.matchAll(/\{\{env:([A-Za-z_][A-Za-z0-9_]*)\}\}/g)) {
      const secret = env[match[1]];
      if (secret !== undefined && String(secret).length >= 4) secrets.add(String(secret));
    }
  } else if (Array.isArray(value)) {
    for (const item of value) collectSecrets(item, env, secrets);
  } else if (value && typeof value === 'object') {
    for (const item of Object.values(value)) collectSecrets(item, env, secrets);
  }
}

function scrubSecrets(value, secrets) {
  if (typeof value === 'string') {
    let output = value.replace(/([?&][^=&#\s]+)=([^&#\s]*)/g, '$1=[REDACTED]');
    for (const secret of secrets) output = output.split(secret).join('[REDACTED]');
    return output;
  }
  if (Array.isArray(value)) return value.map(item => scrubSecrets(item, secrets));
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, scrubSecrets(item, secrets)]));
  return value;
}

function safeRequestPath(value, base) {
  const url = new URL(value, base);
  const query = [...url.searchParams.keys()].map(key => `${encodeURIComponent(key)}=[REDACTED]`);
  return `${url.pathname}${query.length ? `?${query.join('&')}` : ''}`;
}

async function readResponseBounded(response) {
  const length = Number(response.headers.get('content-length'));
  if (Number.isFinite(length) && length > MAX_RESPONSE_BYTES) throw new Error(`Response exceeded the ${MAX_RESPONSE_BYTES} byte limit.`);
  if (!response.body) return Buffer.alloc(0);
  const reader = response.body.getReader();
  const chunks = [];
  let total = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      total += value.byteLength;
      if (total > MAX_RESPONSE_BYTES) { await reader.cancel(); throw new Error(`Response exceeded the ${MAX_RESPONSE_BYTES} byte limit.`); }
      chunks.push(Buffer.from(value));
    }
  } finally { try { reader.releaseLock(); } catch { } }
  return Buffer.concat(chunks, total);
}

async function fetchSameOrigin(url, options, baseOrigin, fetchImpl) {
  let current = url;
  let method = options.method;
  let body = options.body;
  let headers = options.headers;
  for (let redirects = 0; redirects <= 5; redirects++) {
    const response = await fetchImpl(current, { ...options, method, body, headers, redirect: 'manual' });
    if (![301,302,303,307,308].includes(response.status)) return response;
    const location = response.headers.get('location');
    if (!location) return response;
    const next = new URL(location, current);
    if (next.origin !== baseOrigin || !['http:','https:'].includes(next.protocol) || next.username || next.password)
      throw new Error('Redirect was blocked because it left the approved base URL origin.');
    if (redirects === 5) throw new Error('API request exceeded the five-redirect limit.');
    await response.body?.cancel().catch(() => {});
    if (response.status === 303 || ((response.status === 301 || response.status === 302) && method === 'POST')) {
      method = 'GET';
      body = undefined;
      const nextHeaders = new Headers(headers);
      nextHeaders.delete('content-type');
      nextHeaders.delete('content-length');
      headers = nextHeaders;
    }
    current = next.href;
  }
  throw new Error('API redirect handling failed.');
}

async function run(input, dependencies = {}) {
  const request = validateRequest(input);
  const env = dependencies.env || process.env;
  const fetchImpl = dependencies.fetch || globalThis.fetch;
  if (typeof fetchImpl !== 'function') throw new Error('This Node.js runtime does not provide fetch; use Node.js 24 or later.');
  const base = new URL(request.baseUrl);
  const startedAt = new Date().toISOString();
  const results = [];
  const secrets = new Set();
  for (const step of request.requests) { collectSecrets(step.headers, env, secrets); collectSecrets(step.body_json, env, secrets); collectSecrets(step.body_text, env, secrets); }
  const totalStarted = performance.now();
  let failed = false;
  for (const step of request.requests) {
    const started = performance.now();
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(new Error('Request timed out.')), request.timeoutSeconds * 1000);
    try {
      const url = new URL(step.path, base);
      const headers = resolveEnvironment(step.headers, env);
      let body;
      if (step.body_json !== undefined) {
        body = JSON.stringify(resolveEnvironment(step.body_json, env));
        if (!Object.keys(headers).some(name => name.toLowerCase() === 'content-type')) headers['Content-Type'] = 'application/json';
      } else if (step.body_text !== undefined) body = resolveEnvironment(step.body_text, env);
      const response = await fetchSameOrigin(url.href, { method: step.method, headers, body, signal: controller.signal }, base.origin, fetchImpl);
      const bytes = await readResponseBounded(response);
      const text = bytes.toString('utf8');
      const contentType = response.headers.get('content-type') || '';
      let json;
      let isJson = false;
      if (/\bjson\b/i.test(contentType) || /^[\s]*[\[{]/.test(text)) {
        try { json = JSON.parse(text); isJson = true; } catch { }
      }
      const assertions = [];
      const statuses = step.expect.statuses;
      const statusOk = statuses ? statuses.includes(response.status) : response.status >= 200 && response.status < 300;
      assertions.push({ kind: 'status', passed: statusOk, actual: response.status, expected: statuses || '2xx' });
      for (const check of step.expect.headers) {
        const actual = response.headers.get(check.name);
        const operator = check.operator || 'exists';
        const passed = operator === 'exists' ? actual !== null : operator === 'equals' ? actual === String(check.value ?? '') : actual !== null && actual.includes(String(check.value ?? ''));
        assertions.push({ kind: 'header', name: check.name, passed, actual: SENSITIVE_KEY.test(check.name) ? (actual === null ? null : '[REDACTED]') : actual, operator });
      }
      for (const check of step.expect.json) {
        const found = isJson ? getJsonPath(json, check.path) : { exists: false };
        const operator = check.operator || 'exists';
        let passed = false;
        if (operator === 'exists') passed = found.exists;
        else if (operator === 'equals') passed = found.exists && isDeepStrictEqual(found.value, check.value);
        else if (operator === 'contains') passed = found.exists && (typeof found.value === 'string' ? found.value.includes(String(check.value ?? '')) : Array.isArray(found.value) && found.value.some(item => JSON.stringify(item) === JSON.stringify(check.value)));
        else if (operator === 'type') passed = found.exists && (Array.isArray(found.value) ? 'array' : found.value === null ? 'null' : typeof found.value) === String(check.value);
        else if (operator === 'one_of') passed = found.exists && Array.isArray(check.values) && check.values.some(item => isDeepStrictEqual(item, found.value));
        else if (operator === 'greater_than') passed = found.exists && Number(found.value) > Number(check.value);
        assertions.push({ kind: 'json', path: check.path, operator, passed, actual: found.exists ? redact(found.value, check.path.split('.').at(-1)) : '[MISSING]' });
      }
      if (step.expect.body_contains !== undefined) assertions.push({ kind: 'body_contains', passed: text.includes(step.expect.body_contains), expected: step.expect.body_contains });
      const passed = assertions.every(item => item.passed);
      if (!passed) failed = true;
      const safeHeaders = {};
      for (const [key, value] of response.headers.entries()) safeHeaders[key] = SENSITIVE_KEY.test(key) ? '[REDACTED]' : value.slice(0, 1000);
      results.push({ name: step.name, method: step.method, path: safeRequestPath(step.path, base), status: response.status, ok: passed, durationMs: Math.round(performance.now() - started), responseBytes: bytes.length, contentType, headers: safeHeaders, isJson, bodyPreview: isJson ? JSON.stringify(redact(json)).slice(0, 6000) : text.slice(0, 3000), assertions });
    } catch (error) {
      failed = true;
      results.push({ name: step.name, method: step.method, path: safeRequestPath(step.path, base), ok: false, durationMs: Math.round(performance.now() - started), error: String(error.message || error).slice(0, 2000) });
    } finally { clearTimeout(timer); }
  }
  const output = scrubSecrets({ success: !failed, baseUrl: request.baseUrl, startedAt, durationMs: Math.round(performance.now() - totalStarted), passedRequests: results.filter(item => item.ok).length, failedRequests: results.filter(item => !item.ok).length, totalRequests: results.length, results }, secrets);
  if (Buffer.byteLength(JSON.stringify(output), 'utf8') > MAX_OUTPUT_BYTES) throw new Error('API test results exceeded the 256 KB output limit. Reduce the number or size of response bodies.');
  return output;
}

async function main() {
  try {
    let input = '';
    for await (const chunk of process.stdin) {
      input += chunk;
      if (Buffer.byteLength(input, 'utf8') > MAX_INPUT_BYTES) throw new Error('API test request exceeds the 512 KB input limit.');
    }
    const parsed = parseInputJson(input);
    const result = await run(parsed);
    process.stdout.write(`${JSON.stringify(result)}\n`);
    process.exitCode = result.success ? 0 : 2;
  } catch (error) {
    process.stdout.write(`${JSON.stringify({ success: false, error: String(error.message || error).slice(0, 3000) })}\n`);
    process.exitCode = 1;
  }
}

if (require.main === module) main();
module.exports = { validateRequest, parseInputJson, run, getJsonPath, MAX_REQUESTS, MAX_RESPONSE_BYTES, MAX_OUTPUT_BYTES };
