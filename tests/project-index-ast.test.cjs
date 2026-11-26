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

test('var declarations bind to the nearest function scope, including nested blocks and loops', () => {
  const result = context.__extract(`function run() {
    if (true) { var shared = 1; }
    for (var index = 0; index < 2; index++) { shared += index; }
    return shared;
  }`);
  const shared = result.symbolDetails.find(item => item.name === 'shared');
  const index = result.symbolDetails.find(item => item.name === 'index');
  assert.ok(shared && index);
  assert.equal(shared.scope, 'function');
  assert.equal(index.scope, 'function');
  assert.equal(shared.referenceCount, 2);
});

test('identifier references distinguish reads, writes, and read-write updates', () => {
  const result = context.__extract(`let count = 0;
count = 1;
count += 2;
count++;
const values = [0];
values[0] = count;
({ count } = source);
`);
  const count = result.symbolDetails.find(item => item.name === 'count');
  assert.ok(count);
  assert.equal(count.readCount, 3);
  assert.equal(count.writeCount, 4);
  assert.deepEqual(Array.from(result.references.filter(item => item.name === 'count').map(item => item.access)), ['write','read-write','read-write','read','write']);
});

test('parsed project files feed the real relative-import graph and resolve exports', () => {
  const consumer = context.__extract(`import { calculate as run } from './math.js';\nexport const start = run(2);`, 'src/main.js');
  const provider = context.__extract(`export function calculate(value) { return value * 2; }`, 'src/math.js');
  const graph = context.__graph([
    { path: 'src/main.js', language: 'javascript', ...consumer },
    { path: 'src/math.js', language: 'javascript', ...provider }
  ]);
  assert.equal(graph.localImportEdges.length, 1);
  assert.equal(graph.resolvedBindings.length, 1);
  assert.deepEqual(JSON.parse(JSON.stringify(graph.resolvedBindings[0])), {
    from: 'src/main.js', to: 'src/math.js', local: 'run', imported: 'calculate', exported: 'calculate'
  });
});


test('function declarations are visible before their source position and block scoped', () => {
  const result = context.__extract(`function outer() {
    callLater();
    function callLater() { return 1; }
    { function blockOnly() { return 2; } }
    return callLater;
  }`);
  const callLater = result.symbolDetails.find(item => item.name === 'callLater');
  const blockOnly = result.symbolDetails.find(item => item.name === 'blockOnly');
  assert.ok(callLater && blockOnly);
  assert.equal(callLater.scope, 'BlockStatement');
  assert.equal(callLater.referenceCount, 2);
  assert.equal(blockOnly.scope, 'BlockStatement');
});

test('AST records direct calls, member calls, constructors, and containing function names', () => {
  const result = context.__extract(`function build() { const worker = new Worker(); worker.run(1); execute(worker); }
const start = () => build();`);
  assert.ok(result.calls.some(item => item.kind === 'construct' && item.callee === 'Worker'));
  assert.ok(result.calls.some(item => item.kind === 'call' && item.callee === 'run' && item.receiver === 'worker' && item.caller === 'build'));
  assert.ok(result.calls.some(item => item.kind === 'call' && item.callee === 'execute' && item.caller === 'build' && item.resolution === 'unresolved'));
  assert.ok(result.calls.some(item => item.callee === 'build' && item.caller === 'start'));
});

test('AST call sites retain declaration links for locally declared functions', () => {
  const result = context.__extract(`function target() { return 1; } function caller() { return target(); }`);
  const call = result.calls.find(item => item.callee === 'target');
  const declaration = result.symbolDetails.find(item => item.name === 'target');
  assert.equal(call.resolution, 'local');
  assert.equal(call.declaration, declaration.start);
  assert.equal(call.caller, 'caller');
});

test('module graph detects bounded circular relative dependencies', () => {
  const graph = context.__graph([
    { path: 'src/a.js', language: 'javascript', imports: ['./b.js'], importBindings: [], exports: [] },
    { path: 'src/b.js', language: 'javascript', imports: ['./a.js'], importBindings: [], exports: [] }
  ]);
  assert.equal(graph.localImportEdges.length, 2);
  assert.equal(graph.circularDependencies.length, 1);
  assert.deepEqual(Array.from(graph.circularDependencies[0]), ['src/a.js','src/b.js','src/a.js']);
});

test('Node workspace package exports and package import aliases resolve to indexed modules', () => {
  const graph = context.__graph([
    { path: 'src/app.js', language: 'javascript', imports: ['@workspace/lib', '@workspace/lib/feature/one', '#internal/foo', 'fs/promises', 'react'], importBindings: [
      { source: '@workspace/lib', imported: 'default', local: 'lib' },
      { source: '@workspace/lib/feature/one', imported: 'feature', local: 'feature' },
      { source: '#internal/foo', imported: 'helper', local: 'helper' }
    ] },
    { path: 'packages/lib/src/index.js', language: 'javascript', exports: [{ exported: 'default', local: 'main' }] },
    { path: 'packages/lib/src/features/one.js', language: 'javascript', exports: [{ exported: 'feature', local: 'feature' }] },
    { path: 'src/internal/foo.js', language: 'javascript', exports: [{ exported: 'helper', local: 'helper' }] }
  ], [
    { path: 'package.json', text: JSON.stringify({ name: 'app', imports: { '#internal/*': './src/internal/*.js' } }) },
    { path: 'packages/lib/package.json', text: JSON.stringify({ name: '@workspace/lib', exports: { '.': { import: './src/index.js', require: './src/index.cjs' }, './feature/*': './src/features/*.js' } }) }
  ]);
  assert.equal(graph.localImportEdges.length, 3);
  assert.equal(graph.resolvedBindings.length, 3);
  assert.ok(graph.externalImports.some(item => item.specifier === 'react'));
  assert.ok(!graph.externalImports.some(item => item.specifier === 'fs/promises'));
});

test('AST records re-export bindings for cross-module symbol resolution', () => {
  const result = context.__extract(`export { value as publicValue } from './dep.js';`);
  assert.deepEqual(Array.from(result.importBindings, item => [item.source,item.imported,item.local,item.kind]), [['./dep.js','value','publicValue','re-export']]);
});

test('project index schema v4 preserves original manifest text across incremental reuse', () => {
  assert.match(source, /PROJECT_INDEX_PREFIX = 'runmind\.project-index\.v4\.'/);
  assert.match(source, /value\.version===4/);
  assert.match(source, /manifestText:isManifest\?text\.slice\(0,64000\):null/);
  assert.match(source, /prior\.manifestText\|\|prior\.text\.slice/);
});
