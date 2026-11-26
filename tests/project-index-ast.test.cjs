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
vm.runInNewContext(`${source}\nglobalThis.__extract = projectIndexExtractJavaScript; globalThis.__extractFile = projectIndexExtract; globalThis.__graph = projectIndexLocalImportGraph;`, context);

 test('Acorn extracts declarations, imports, and identifier occurrence counts', () => {
  const code = `import thing, { value as alias } from './thing.js';
export { alias };
const count = 1;
function calculate(input = count) { const local = input + count; return local; }
class Worker { run() { return calculate(count); } }
const lazy = () => require('./lazy.cjs');`;
  const result = context.__extract(code);
  assert.equal(result.engine, 'acorn-semantic-local');
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
  assert.equal(result.engine, 'acorn-semantic-local');
  assert.ok(result.imports.includes('./dep'));
});

test('TypeScript remains explicitly on the heuristic fallback', () => {
  const result = context.__extractFile('export const Thing = 1;', 'typescript', 'Thing.ts');
  assert.equal(result.engine, undefined);
  assert.ok(result.symbols.includes('Thing'));
});


test('AST resolves references by lexical scope and distinguishes shadowed bindings', () => {
  const result = context.__extract(`const value = 1;
function outer(value) { const local = value; { const value = 2; console.log(value); } return local + value; }
console.log(value);`);
  const top = result.symbolDetails.find(item => item.name === 'value' && item.scope === 'module');
  const parameter = result.symbolDetails.find(item => item.name === 'value' && item.kind === 'parameter');
  const block = result.symbolDetails.find(item => item.name === 'value' && item.scope === 'BlockStatement');
  assert.ok(top && parameter && block);
  assert.equal(top.referenceCount, 1);
  assert.equal(parameter.referenceCount, 2);
  assert.equal(block.referenceCount, 1);
  assert.ok(result.references.some(item => item.name === 'console' && item.resolution === 'unresolved'));
});

test('AST records named exports and import bindings for cross-file resolution', () => {
  const result = context.__extract(`import main, { value as alias } from './dep.js';\nexport { alias as exposed };\nexport default main;`);
  assert.deepEqual(Array.from(result.importBindings, item => [item.source,item.imported,item.local]), [['./dep.js','default','main'],['./dep.js','value','alias']]);
  assert.ok(result.exports.some(item => item.exported === 'exposed' && item.local === 'alias'));
  assert.ok(result.exports.some(item => item.exported === 'default'));
});


test('project graph resolves relative named imports to indexed exports', () => {
  const graph = context.__graph([
    { path: 'src/main.js', language: 'javascript', imports: ['./dep.js'], importBindings: [{ source: './dep.js', imported: 'value', local: 'renamed' }] },
    { path: 'src/dep.js', language: 'javascript', imports: [], exports: [{ exported: 'value', local: 'value', kind: 'declaration' }] }
  ]);
  assert.equal(graph.localImportEdges.length, 1);
  assert.deepEqual(JSON.parse(JSON.stringify(graph.resolvedBindings)), [{ from: 'src/main.js', to: 'src/dep.js', local: 'renamed', imported: 'value', exported: 'value' }]);
  assert.equal(graph.unresolvedBindings.length, 0);
});
