const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const acorn = require('../Shared/ChatWindow/vendor/acorn/acorn.js');

const projectIndexPath = path.join(__dirname, '..', 'Shared', 'ChatWindow', 'scripts', 'agent', 'project-index.js');
const source = fs.readFileSync(projectIndexPath, 'utf8');
const context = { acorn, console, AbortController, DOMException, setInterval, clearInterval };
context.globalThis = context;
vm.runInNewContext(`${source}\nglobalThis.__extract = projectIndexExtractJavaScript; globalThis.__extractFile = projectIndexExtract;`, context);

 test('Acorn extracts declarations, imports, and identifier occurrence counts', () => {
  const code = `import thing, { value as alias } from './thing.js';
export { alias };
const count = 1;
function calculate(input = count) { const local = input + count; return local; }
class Worker { run() { return calculate(count); } }
const lazy = () => require('./lazy.cjs');`;
  const result = context.__extract(code);
  assert.equal(result.engine, 'acorn');
  assert.equal(result.parseError, null);
  assert.deepEqual(Array.from(result.imports), ['./thing.js', './lazy.cjs']);
  for (const name of ['calculate', 'count', 'input', 'local', 'Worker']) assert.ok(result.symbols.includes(name), `missing ${name}`);
  assert.equal(result.symbolDetails.find(item => item.name === 'count').referenceOccurrences, 3);
});

test('Acorn records malformed JavaScript with a source location', () => {
  const result = context.__extract('function {');
  assert.ok(result.parseError);
  assert.equal(result.parseError.line, 1);
  assert.equal(typeof result.parseError.column, 'number');
});

test('CommonJS top-level return remains parseable through script fallback', () => {
  const result = context.__extract('const load = require("./dep"); return load;', 'module.cjs');
  assert.equal(result.engine, 'acorn');
  assert.ok(result.imports.includes('./dep'));
});

test('TypeScript remains explicitly on the heuristic fallback', () => {
  const result = context.__extractFile('export const Thing = 1;', 'typescript', 'Thing.ts');
  assert.equal(result.engine, undefined);
  assert.ok(result.symbols.includes('Thing'));
});
