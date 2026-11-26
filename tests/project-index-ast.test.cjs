const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const acorn = require('../Shared/ChatWindow/vendor/acorn/acorn.js');
const ts = require('../Shared/ChatWindow/vendor/typescript/typescript.js');

const projectIndexPath = path.join(__dirname, '..', 'Shared', 'ChatWindow', 'scripts', 'agent', 'project-index.js');
const source = fs.readFileSync(projectIndexPath, 'utf8');
const context = { acorn, ts, console, AbortController, DOMException, setInterval, clearInterval };
context.globalThis = context;
vm.runInNewContext(`${source}\nglobalThis.__extract = projectIndexExtractJavaScript; globalThis.__extractFile = projectIndexExtract; globalThis.__graph = projectIndexLocalImportGraph; globalThis.__refactor = projectIndexRefactorJavaScript; globalThis.__crossRefs = projectIndexCrossFileReferences; globalThis.__extractTS = projectIndexExtractTypeScript; globalThis.__refactorAPI = projectIndexRefactorPublicApi; globalThis.__program = projectIndexAnalyzeTypeScriptProgram;`, context);

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

test('TypeScript files use the bundled compiler API and expose typed declarations', () => {
  const result = context.__extractFile('export interface Thing { value: number }\nexport const createThing = (value: number): Thing => ({ value });', 'typescript', 'Thing.ts');
  assert.equal(result.engine, 'typescript-compiler-api');
  assert.equal(result.parserVersion, ts.version);
  assert.ok(result.symbols.includes('Thing'));
  assert.ok(result.symbols.includes('createThing'));
  assert.ok(result.exports.some(item => item.exported === 'Thing'));
  assert.ok(result.exports.some(item => item.exported === 'createThing'));
});

test('JSX and TSX use compiler parser modes and extract JSX component references', () => {
  const jsx = context.__extractFile(`import { Panel } from './Panel'; const App = () => <Panel title="Hello" />;`, 'javascript', 'App.jsx');
  const tsx = context.__extractFile(`type Props = { title: string }; export const Panel = ({title}: Props) => <section>{title}</section>;`, 'typescript', 'Panel.tsx');
  assert.equal(jsx.engine, 'typescript-compiler-api');
  assert.equal(jsx.parseError, null);
  assert.ok(jsx.calls.some(item => item.kind === 'jsx' && item.callee === 'Panel'));
  assert.equal(tsx.engine, 'typescript-compiler-api');
  assert.equal(tsx.parseError, null);
  assert.ok(tsx.exports.some(item => item.exported === 'Panel'));
  assert.ok(tsx.symbols.includes('Props'));
});

test('TypeScript compiler parser reports malformed TSX with source coordinates', () => {
  const result = context.__extractFile('export const View = <div>;', 'typescript', 'View.tsx');
  assert.equal(result.engine, 'typescript-compiler-api');
  assert.ok(result.parseError);
  assert.equal(result.parseError.line, 1);
  assert.equal(typeof result.parseError.column, 'number');
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
  assert.deepEqual(JSON.parse(JSON.stringify(graph.resolvedBindings)), [{ from: 'src/main.js', to: 'src/dep.js', source: './dep.js', local: 'renamed', imported: 'value', exported: 'value' }]);
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
    from: 'src/main.js', to: 'src/math.js', source: './math.js', local: 'run', imported: 'calculate', exported: 'calculate'
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

test('project index schema v6 preserves original manifest text across incremental reuse', () => {
  assert.match(source, /PROJECT_INDEX_PREFIX = 'runmind\.project-index\.v6\.'/);
  assert.match(source, /value\.version===6/);
  assert.match(source, /manifestText:isManifest\?text\.slice\(0,64000\):null/);
  assert.match(source, /prior\.manifestText\|\|prior\.text\.slice/);
});


test('AST rename changes only the selected lexical binding and its references', () => {
  const code = `const value = 1;\nfunction outer() { const value = 2; return value; }\nconsole.log(value);`;
  const analysis = context.__extract(code);
  const inner = analysis.symbolDetails.filter(item => item.name === 'value').sort((a, b) => a.start - b.start).at(-1);
  const result = context.__refactor(code, { action: 'rename-symbol', oldName: 'value', newName: 'innerValue', declarationStart: inner.start });
  assert.equal(result.changed, true);
  assert.match(result.content, /const innerValue = 2; return innerValue;/);
  assert.match(result.content, /const value = 1;/);
  assert.match(result.content, /console\.log\(value\)/);
  assert.equal(context.__extract(result.content).parseError, null);
});

test('AST rename requires a declaration selector when names are ambiguous', () => {
  const code = `const item = 1; function run() { const item = 2; return item; }`;
  assert.throws(() => context.__refactor(code, { action: 'rename-symbol', oldName: 'item', newName: 'entry' }), /declarations in this file/);
});

test('AST rename rejects invalid identifiers and collisions', () => {
  const code = `const value = 1; const other = 2; console.log(value, other);`;
  const value = context.__extract(code).symbolDetails.find(item => item.name === 'value');
  assert.throws(() => context.__refactor(code, { action: 'rename-symbol', oldName: 'value', newName: 'other', declarationStart: value.start }), /already exists/);
  assert.throws(() => context.__refactor(code, { action: 'rename-symbol', oldName: 'value', newName: 'not-valid!', declarationStart: value.start }), /valid JavaScript identifier/);
});

test('AST import organization sorts only independent contiguous groups', () => {
  const code = `import z from 'zeta';\nimport a from 'alpha';\n\n// Keep this comment attached to the following import.\nimport c from 'charlie';\nimport b from 'bravo';\n`;
  const result = context.__refactor(code, { action: 'organize-imports' });
  assert.equal(result.changed, true);
  assert.ok(result.content.indexOf("import a from 'alpha'") < result.content.indexOf("import z from 'zeta'"));
  assert.ok(result.content.indexOf('// Keep this comment') < result.content.indexOf("import c from 'charlie'"));
  assert.ok(result.content.indexOf("import c from 'charlie'") < result.content.indexOf("import b from 'bravo'")); // The comment pins this group; leave its order unchanged.
  assert.equal(context.__extract(result.content).parseError, null);
});


test('AST rename preserves object property keys for shorthand references and destructuring', () => {
  const code = `const value = 1; const object = { value }; const { value: copied } = object; const { value: extracted } = object; console.log(value, copied, extracted);`;
  const binding = context.__extract(code).symbolDetails.find(item => item.name === 'value');
  const result = context.__refactor(code, { action: 'rename-symbol', oldName: 'value', newName: 'renamed', declarationStart: binding.start });
  assert.match(result.content, /const object = \{ value: renamed \}/);
  assert.match(result.content, /const \{ value: copied \}/);
  assert.match(result.content, /const \{ value: extracted \}/);
  assert.match(result.content, /console\.log\(renamed, copied, extracted\)/);
  assert.equal(context.__extract(result.content).parseError, null);
});


test('current RunMind JavaScript repository files parse through the semantic engine', () => {
  for (const relative of [
    'Shared/ChatWindow/scripts/agent/project-index.js',
    'Shared/ChatWindow/scripts/agent/tools.js',
    'Shared/ChatWindow/scripts/providers.js',
    'Shared/ChatWindow/scripts/core.js'
  ]) {
    const sourceText = fs.readFileSync(path.join(__dirname, '..', relative), 'utf8');
    const parsed = context.__extract(sourceText, relative);
    assert.equal(parsed.parseError, null, `${relative}: ${parsed.parseError?.message || 'parse failed'}`);
    assert.ok(parsed.symbolDetails.length > 0, `${relative}: no symbols extracted`);
  }
});


test('AST extract-function moves complete safe statements and captures outer bindings', () => {
  const code = `const value = 3;\nconsole.log(value);\nconsole.log('done');\n`;
  const start = code.indexOf('console.log(value)');
  const end = code.indexOf(';', code.indexOf("console.log('done')")) + 1;
  const result = context.__refactor(code, { action: 'extract-function', newName: 'reportResult', declarationStart: start, selectionEnd: end });
  assert.match(result.content, /function reportResult\(\)/);
  assert.match(result.content, /console\.log\(value\);/);
  assert.match(result.content, /reportResult\(\);/);
  assert.equal(context.__extract(result.content).parseError, null);
});

test('AST extract-function rejects partial statements and control-flow-sensitive selections', () => {
  const code = `function run() { if (ready) { return; } }`;
  const start = code.indexOf('return');
  const end = code.indexOf(';', start) + 1;
  assert.throws(() => context.__refactor(code, { action: 'extract-function', newName: 'doReturn', declarationStart: start, selectionEnd: end }), /control-flow or binding semantics/);
  const wholeStart = code.indexOf('if (ready)');
  const wholeEnd = code.indexOf('}', wholeStart) + 1;
  assert.throws(() => context.__refactor(code, { action: 'extract-function', newName: 'checkReady', declarationStart: wholeStart, selectionEnd: wholeEnd }), /control-flow or binding semantics/);
});


test('import organization does not reorder side-effect imports or cross blank-line groups', () => {
  const code = `import z from 'zeta';\nimport a from 'alpha';\n\nimport 'polyfill';\nimport d from 'delta';\nimport c from 'charlie';\n`;
  const result = context.__refactor(code, { action: 'organize-imports' });
  assert.ok(result.content.indexOf("import a from 'alpha'") < result.content.indexOf("import z from 'zeta'"));
  assert.ok(result.content.indexOf("import 'polyfill'") < result.content.indexOf("import d from 'delta'"));
  assert.ok(result.content.indexOf("import d from 'delta'") < result.content.indexOf("import c from 'charlie'"));
});

test('AST rename supports CommonJS script files with top-level return fallback', () => {
  const code = `const value = 1; if (value) return value;`;
  const binding = context.__extract(code).symbolDetails.find(item => item.name === 'value');
  const result = context.__refactor(code, { action: 'rename-symbol', oldName: 'value', newName: 'result', declarationStart: binding.start });
  assert.equal(result.changed, true);
  assert.match(result.content, /const result = 1; if \(result\) return result/);
});


test('AST extract-function preserves nested block indentation', () => {
  const code = `function run() {\n  if (ready) {\n    console.log(value);\n  }\n}\n`;
  const start = code.indexOf('if (ready)');
  const end = code.indexOf('}', code.indexOf('console.log(value)')) + 1;
  const result = context.__refactor(code, { action: 'extract-function', newName: 'logWhenReady', declarationStart: start, selectionEnd: end });
  assert.match(result.content, /function logWhenReady\(\) \{\n    if \(ready\) \{\n      console\.log\(value\);\n    \}\n  \}/);
  assert.equal(context.__extract(result.content).parseError, null);
});


test('cross-file symbol graph maps imported local references back to the exporting module', () => {
  const files = [
    { path: 'src/lib.js', exports: [{ exported: 'greet', local: 'greet' }] },
    { path: 'src/main.js', symbolDetails: [{ name: 'greetLocal', kind: 'import', start: 12 }], references: [
      { name: 'greetLocal', declaration: 12, start: 60, end: 70, access: 'read', read: true, write: false },
      { name: 'greetLocal', declaration: 12, start: 90, end: 100, access: 'read', read: true, write: false }
    ] }
  ];
  const graph = { resolvedBindings: [{ from: 'src/main.js', to: 'src/lib.js', local: 'greetLocal', imported: 'greet', exported: 'greet' }] };
  const result = context.__crossRefs(files, graph, { name: 'greet' });
  assert.equal(result.results.length, 1);
  assert.equal(result.results[0].from, 'src/main.js');
  assert.equal(result.results[0].to, 'src/lib.js');
  assert.equal(result.results[0].referenceCount, 2);
});


test('public API rename updates declaration, resolved importers, and re-exports while preserving local names', () => {
  const target = `export function oldName(value: number) { return value + 1; }`;
  const result = context.__refactorAPI('src/api.ts', target, [
    { path: 'src/app.ts', text: `import { oldName } from './api'; const value = oldName(2);`, sources: ['./api'] },
    { path: 'src/reexport.ts', text: `export { oldName } from './api';`, sources: ['./api'] }
  ], 'oldName', 'newName');
  assert.equal(result.changes.length, 3);
  assert.match(result.changes.find(item => item.path === 'src/api.ts').content, /export function newName/);
  assert.match(result.changes.find(item => item.path === 'src/app.ts').content, /import \{ newName as oldName \}/);
  assert.match(result.changes.find(item => item.path === 'src/app.ts').content, /oldName\(2\)/);
  assert.match(result.changes.find(item => item.path === 'src/reexport.ts').content, /export \{ newName as oldName \}/);
});

test('public API rename supports explicit export aliases without renaming the local implementation', () => {
  const result = context.__refactorAPI('src/api.ts', `const internal = () => 1; export { internal as oldName };`, [], 'oldName', 'newName');
  assert.match(result.changes[0].content, /const internal = \(\) => 1; export \{ internal as newName \}/);
});

test('public API rename rejects default exports and invalid output identifiers', () => {
  assert.throws(() => context.__refactorAPI('src/api.ts', `export default function oldName() {}`, [], 'oldName', 'newName'), /No direct named export/);
  assert.throws(() => context.__refactorAPI('src/api.ts', `export const oldName = 1;`, [], 'oldName', 'not-valid'), /valid old and new exported identifiers/);
});


test('TypeScript compiler Program resolves imported symbols and cross-file references across workspace files', () => {
  const result = context.__program([
    { path: 'src/api.ts', language: 'typescript', analysisSource: `export function calculate(value: number) { return value * 2; }` },
    { path: 'src/main.ts', language: 'typescript', analysisSource: `import { calculate as run } from './api'; export const result = run(2);` }
  ], []);
  assert.equal(result.compilerVersion, ts.version);
  assert.equal(result.filesAnalyzed, 2);
  assert.ok(result.resolvedBindings.some(item => item.from === 'src/main.ts' && item.to === 'src/api.ts' && item.imported === 'calculate' && item.local === 'run'));
  assert.ok(result.crossFileReferences.some(item => item.path === 'src/main.ts' && item.targetPath === 'src/api.ts' && item.targetName === 'calculate'));
});

test('TypeScript compiler Program resolves JSX component usage across TSX modules', () => {
  const result = context.__program([
    { path: 'src/Panel.tsx', language: 'typescript', analysisSource: `export const Panel = () => <section />;` },
    { path: 'src/App.tsx', language: 'typescript', analysisSource: `import { Panel } from './Panel'; export const App = () => <Panel />;` }
  ], []);
  assert.ok(result.crossFileReferences.some(item => item.path === 'src/App.tsx' && item.targetPath === 'src/Panel.tsx' && item.targetName === 'Panel'));
});


test('TypeScript compiler Program honors tsconfig paths for workspace module resolution', () => {
  const result = context.__program([
    { path: 'src/api.ts', language: 'typescript', analysisSource: `export const api = 1;` },
    { path: 'src/main.ts', language: 'typescript', analysisSource: `import { api } from '@/api'; console.log(api);` }
  ], [{ path: 'tsconfig.json', text: JSON.stringify({ compilerOptions: { baseUrl: '.', paths: { '@/*': ['src/*'] } } }) }]);
  assert.ok(result.resolvedBindings.some(item => item.from === 'src/main.ts' && item.to === 'src/api.ts' && item.source === '@/api'));
});

test('static project graph maps JavaScript extension imports to indexed TypeScript source candidates', () => {
  const graph = context.__graph([
    { path: 'src/main.js', language: 'javascript', imports: ['./service.js'], importBindings: [{ source: './service.js', imported: 'run', local: 'run' }] },
    { path: 'src/service.ts', language: 'typescript', exports: [{ exported: 'run', local: 'run' }] }
  ]);
  assert.ok(graph.localImportEdges.some(item => item.from === 'src/main.js' && item.to === 'src/service.ts'));
  assert.ok(graph.resolvedBindings.some(item => item.imported === 'run' && item.to === 'src/service.ts'));
});


test('public API rename preserves comments inside import and re-export specifiers', () => {
  const result = context.__refactorAPI('src/api.ts', `export function oldName() {}`, [
    { path: 'src/app.ts', text: `import { /* keep this */ oldName } from './api';`, sources: ['./api'] },
    { path: 'src/reexport.ts', text: `export { /* keep export note */ oldName } from './api';`, sources: ['./api'] }
  ], 'oldName', 'newName');
  assert.match(result.changes.find(item => item.path === 'src/app.ts').content, /\/\* keep this \*\/ newName as oldName/);
  assert.match(result.changes.find(item => item.path === 'src/reexport.ts').content, /\/\* keep export note \*\/ newName as oldName/);
});


test('public API rename preserves the local name when an unaliased local export is renamed', () => {
  const result = context.__refactorAPI('src/api.ts', `const oldName = () => 1; export { oldName };`, [], 'oldName', 'newName');
  assert.match(result.changes[0].content, /const oldName = \(\) => 1; export \{ oldName as newName \}/);
});
