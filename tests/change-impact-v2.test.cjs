const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const acorn = require('../Shared/ChatWindow/vendor/acorn/acorn.js');
const ts = require('../Shared/ChatWindow/vendor/typescript/typescript.js');

const root = path.join(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const indexSource = read('Shared/ChatWindow/scripts/agent/project-index.js');

function extractFunction(source, startText, endText) {
  const start = source.indexOf(startText);
  const end = source.indexOf(endText, start);
  assert.ok(start >= 0 && end > start, `Missing function ${startText}`);
  return source.slice(start, end);
}

function createImpactContext() {
  const context = vm.createContext({ acorn, ts, console, AbortController, DOMException, setInterval, clearInterval });
  context.globalThis = context;
  const names = [
    ['projectIndexResolveLocalAsset', 'function projectIndexResolveLocalAsset(', '\n    function projectIndexBuildUnderstanding'],
    ['projectIndexPackageData', 'function projectIndexPackageData(', '\n    function projectIndexResolveLocalAsset'],
    ['projectIndexBuildUnderstanding', 'function projectIndexBuildUnderstanding(', '\n    function projectIndexBuildDiagnostics'],
    ['projectIndexParseUnifiedDiff', 'function projectIndexParseUnifiedDiff(', '\n    function projectIndexInferChangedSymbols'],
    ['projectIndexInferChangedSymbols', 'function projectIndexInferChangedSymbols(', '\n    function projectIndexBuildCoverageEvidence'],
    ['projectIndexBuildCoverageEvidence', 'function projectIndexBuildCoverageEvidence(', '\n    function projectIndexAnalyzeTestImpact'],
    ['projectIndexExtractJavaScript', 'function projectIndexExtractJavaScript(', '\n    function projectIndexExtractTypeScript'],
    ['projectIndexExtractTypeScript', 'function projectIndexExtractTypeScript(', '\n    function projectIndexRefactorJavaScript'],
    ['projectIndexAnalyzeTestImpact', 'function projectIndexAnalyzeTestImpact(', '\n    function projectIndexAnalyzeChangeImpact'],
    ['projectIndexAnalyzeChangeImpact', 'function projectIndexAnalyzeChangeImpact(', '\n    function projectIndexBuildReleaseReadiness']
  ];
  let source = '';
  for (const [name, start, end] of names) source += `${extractFunction(indexSource, start, end)}\nglobalThis.${name} = ${name};\n`;
  // The source declaration for language detection appears before the extracted helpers.
  source = `function projectIndexNormalizePath(value) { return String(value || '').replace(/\\\\/g, '/').replace(/^\\.\\//, '').replace(/^\\/+/, '').replace(/\\/\\/+/g, '/'); }\n` + source;
  vm.runInContext(source, context);
  return context;
}

test('unified diff parser captures add/delete/rename status and line ranges', () => {
  const context = createImpactContext();
  const diff = [
    'diff --git a/src/old.ts b/src/new.ts',
    'similarity index 80%',
    'rename from src/old.ts',
    'rename to src/new.ts',
    '--- a/src/old.ts',
    '+++ b/src/new.ts',
    '@@ -1,3 +1,4 @@',
    ' export const stable = 1;',
    '-export function removed() { return 0; }',
    '+export function changed() { return 1; }',
    '+export const added = true;',
    ' export default changed;'
  ].join('\n');
  const parsed = context.projectIndexParseUnifiedDiff(diff);
  assert.equal(parsed.files.length, 1);
  assert.equal(parsed.files[0].status, 'renamed');
  assert.equal(parsed.files[0].oldPath, 'src/old.ts');
  assert.equal(parsed.files[0].path, 'src/new.ts');
  assert.equal(parsed.files[0].removedLines[0].text, 'export function removed() { return 0; }');
  assert.deepEqual(Array.from(parsed.files[0].changedLineRanges.filter(row => row.kind === 'added'), row => row.start), [2, 3]);
  assert.ok(parsed.changedPaths.includes('src/old.ts'));
  assert.ok(parsed.changedPaths.includes('src/new.ts'));
});

test('AST declaration ranges map changed diff lines to JS/TS symbols automatically', () => {
  const context = createImpactContext();
  const parsed = context.projectIndexParseUnifiedDiff([
    'diff --git a/src/service.ts b/src/service.ts',
    '--- a/src/service.ts',
    '+++ b/src/service.ts',
    '@@ -1,2 +1,3 @@',
    '-export function execute() { return false; }',
    '+export function execute() { return true; }',
    '+export const status = "ready";'
  ].join('\n'));
  const extracted = context.projectIndexExtractTypeScript('export function execute() { return true; }\nexport const status = "ready";', 'src/service.ts');
  const index = { files: [{ path: 'src/service.ts', language: 'typescript', analysisSource: 'export function execute() { return true; }\nexport const status = "ready";', declarationRanges: extracted.declarationRanges }] };
  const inferred = context.projectIndexInferChangedSymbols(index, parsed);
  assert.ok(inferred.symbols.some(item => item.name === 'execute'));
  assert.ok(inferred.symbols.some(item => item.name === 'status'));
  assert.ok(inferred.removedCandidates.some(item => item.name === 'execute'));
});

test('JavaScript and TypeScript parsing reports non-literal dependency construction', () => {
  const context = createImpactContext();
  const js = context.projectIndexExtractJavaScript("const moduleName = getName(); import('./plugins/' + moduleName); require.context('./modules', true); import.meta.glob('./pages/*.js');", 'src/loader.js');
  assert.ok(js.dynamicDependencySignals.some(item => item.kind === 'dynamic-import'));
  assert.ok(js.dynamicDependencySignals.some(item => item.kind === 'require-context'));
  assert.ok(js.dynamicDependencySignals.some(item => item.kind === 'import-meta-glob'));
  const tsResult = context.projectIndexExtractTypeScript("const name = getName(); import('./routes/' + name); import.meta.glob('./views/*.tsx');", 'src/router.ts');
  assert.equal(tsResult.parseError, null);
  assert.ok(tsResult.dynamicDependencySignals.some(item => item.kind === 'dynamic-import'));
  assert.ok(tsResult.dynamicDependencySignals.some(item => item.kind === 'import-meta-glob'));
});

test('asset graph covers HTML srcset/script/media, CSS imports and URLs, SVG links and JS module-relative assets', () => {
  const context = createImpactContext();
  const files = [
    { path: 'index.html', language: 'html', text: '', analysisSource: '<img src="assets/a.webp" srcset="assets/a.webp 1x, assets/b.webp 2x"><img src="Assets/Logo.SVG"><script src="app.js"></script><video poster="media/poster.png"></video><link rel="preload" href="fonts/site.woff2"><div style="background:url(assets/inline.png)"></div><style>.icon{background:url(assets/embedded.png)}</style>' },
    { path: 'styles/site.css', language: 'css', text: '', analysisSource: '@import "./tokens.css"; .hero { background:url("../assets/hero.svg"); }' },
    { path: 'icons/symbols.svg', language: 'svg', text: '', analysisSource: '<svg><use href="./parts.svg#mark"/><image xlink:href="../assets/mark.png"/></svg>' },
    { path: 'src/worker.js', language: 'javascript', text: '', analysisSource: 'const worker = new URL("./worker.wasm", import.meta.url);' },
    { path: 'assets/a.webp', language: 'text', text: '' }, { path: 'assets/b.webp', language: 'text', text: '' }
  ];
  const understanding = context.projectIndexBuildUnderstanding({ rootName: 'fixture', updatedAt: '', files, manifests: [], importGraph: { localImportEdges: [], resolvedBindings: [], unresolvedBindings: [], circularDependencies: [] } });
  const targets = new Set(understanding.assetReferences.map(item => item.to));
  for (const expected of ['assets/a.webp','assets/b.webp','Assets/Logo.SVG','app.js','media/poster.png','fonts/site.woff2','styles/tokens.css','assets/hero.svg','icons/parts.svg','assets/mark.png','src/worker.wasm']) assert.ok(targets.has(expected), `Missing asset edge for ${expected}`);
  assert.ok(understanding.assetReferences.some(item => item.kind === 'HTML SVG asset reference'));
  assert.ok(understanding.assetReferences.some(item => item.to === 'assets/inline.png' && item.sourceKind === 'HTML inline style url() reference'));
  assert.ok(understanding.assetReferences.some(item => item.to === 'assets/embedded.png' && item.sourceKind === 'HTML embedded style url() reference'));
});

test('Istanbul and LCOV reports provide changed-line coverage evidence without claiming test attribution', () => {
  const context = createImpactContext();
  const index = { files: [
    { path: 'src/service.ts', language: 'typescript', text: '' },
    { path: 'coverage/lcov.info', coverageDataText: 'TN:\nSF:src/service.ts\nDA:10,4\nDA:11,0\nend_of_record\n' },
    { path: 'src/other.js', language: 'javascript', text: '' },
    { path: 'coverage/coverage-final.json', coverageDataText: JSON.stringify({ 'src/other.js': { path: 'src/other.js', s: { 0: 1, 1: 0 }, statementMap: { 0: { start: { line: 2 } }, 1: { start: { line: 3 } } }, f: {}, fnMap: {} } }) }
  ] };
  const evidence = context.projectIndexBuildCoverageEvidence(index, [{ path: 'src/service.ts', ranges: [{ start: 11, end: 11 }] }]);
  assert.equal(evidence.summary.coverageFiles, 2);
  const service = evidence.sources.find(item => item.path === 'src/service.ts');
  assert.equal(service.changedLinesMeasured, 1);
  assert.equal(service.changedLinesCovered, 0);
  assert.match(evidence.note, /do not identify which individual test/);
  const other = evidence.sources.find(item => item.path === 'src/other.js');
  assert.equal(other.coveredLines, 1);
});

test('impact report combines inferred symbols, symbol consumers, removed importers, dynamic signals, coverage and transparent risk/confidence', () => {
  const context = createImpactContext();
  const source = 'export function execute() { return true; }';
  const changedFile = { path: 'src/service.ts', language: 'typescript', text: source.toLowerCase(), analysisSource: source,
    declarationRanges: [{ name: 'execute', kind: 'function', start: 0, end: source.length }], imports: [], exports: [{ exported: 'execute' }], importBindings: [], dynamicDependencySignals: [{ kind: 'dynamic-import', expression: 'loadPlugin(name)', start: 20 }] };
  const consumer = { path: 'src/consumer.ts', language: 'typescript', text: '', imports: ['./service'], exports: [], importBindings: [{ source: './service', imported: 'execute', local: 'run' }] };
  const testFile = { path: 'tests/service.test.ts', language: 'typescript', text: '', imports: ['./service'], exports: [], importBindings: [{ source: '../src/service', imported: 'execute', local: 'execute' }] };
  const oldConsumer = { path: 'src/legacy-consumer.ts', language: 'typescript', text: '', imports: ['./old-service'], exports: [], importBindings: [{ source: './old-service', imported: 'old', local: 'old' }] };
  const coverage = { path: 'coverage/lcov.info', coverageDataText: 'TN:\nSF:src/service.ts\nDA:1,0\nend_of_record\n' };
  const index = { rootName: 'fixture', updatedAt: '2026-11-24T00:00:00Z', stats: { files: 5, scanned: 5, truncated: false },
    files: [changedFile, consumer, testFile, oldConsumer, coverage],
    manifests: [], importGraph: {
      localImportEdges: [{ from: 'src/consumer.ts', to: 'src/service.ts' }, { from: 'tests/service.test.ts', to: 'src/service.ts' }],
      resolvedBindings: [{ from: 'src/consumer.ts', to: 'src/service.ts', imported: 'execute', exported: 'execute', local: 'run', compiler: true }],
      unresolvedBindings: [{ from: 'src/legacy-consumer.ts', source: './old-service', reason: 'workspace-module-not-resolved' }],
      dynamicDependencySignals: [{ path: 'src/service.ts', kind: 'dynamic-import' }],
      compilerAnalysis: { supported: true, filesAnalyzed: 3, truncated: false }, note: 'static graph fixture'
    }
  };
  const diff = context.projectIndexParseUnifiedDiff(['diff --git a/src/service.ts b/src/service.ts','--- a/src/service.ts','+++ b/src/service.ts','@@ -1 +1 @@','-export function execute() { return false; }','+export function execute() { return true; }'].join('\n'));
  const result = context.projectIndexAnalyzeChangeImpact(index, ['src/service.ts', 'src/old-service.ts'], [], {
    source: 'git-working-tree', diffLineRanges: diff.files.map(file => ({ path: file.path, ranges: file.changedLineRanges, removedLines: file.removedLines })),
    git: { branch: 'feature/demo' }
  });
  assert.equal(result.schemaVersion, 2);
  assert.ok(result.changedSymbols.some(item => item.name === 'execute'));
  assert.ok(result.symbolConsumers.some(item => item.importer === 'src/consumer.ts' && item.confidence === 'high'));
  assert.ok(result.removedModuleConsumers.some(item => item.importer === 'src/legacy-consumer.ts'));
  assert.ok(result.dynamicDependencySignals.some(item => item.path === 'src/service.ts'));
  assert.ok(result.tests.some(item => item.path === 'tests/service.test.ts'));
  assert.equal(result.coverageEvidence.summary.changedLinesMeasured, 1);
  assert.ok(result.blastRadius && result.risk && result.confidence);
  assert.ok(result.risk.factors.some(item => item.id === 'changed-lines-without-coverage'));
  assert.ok(result.confidence.coverage.changedLinesMeasured >= 1);
  assert.match(result.risk.meaning, /not a probability/);
  assert.match(result.confidence.meaning, /not a statistically calibrated probability/);
});


test('unindexed changed image/font assets resolve to owners and flow into affected test recommendations', () => {
  const context = createImpactContext();
  const files = [
    { path: 'styles/site.css', language: 'css', text: '', analysisSource: '.hero{background:url(../assets/hero.webp)}' },
    { path: 'src/header.ts', language: 'typescript', text: '', imports: ['../styles/site.css'], importBindings: [{ source: '../styles/site.css', imported: '*', local: 'styles' }], analysisSource: "import '../styles/site.css';" },
    { path: 'tests/header.test.ts', language: 'typescript', text: '', imports: ['../src/header'], importBindings: [{ source: '../src/header', imported: '*', local: 'header' }], analysisSource: "import '../src/header';" }
  ];
  const index = { rootName: 'fixture', updatedAt: '', stats: { files: 3, scanned: 3, truncated: false }, files, manifests: [], importGraph: {
    localImportEdges: [{ from: 'src/header.ts', to: 'styles/site.css' }, { from: 'tests/header.test.ts', to: 'src/header.ts' }],
    resolvedBindings: [], unresolvedBindings: [], dynamicDependencySignals: [], compilerAnalysis: { supported: true, filesAnalyzed: 2, truncated: false }, note: 'fixture'
  } };
  const result = context.projectIndexAnalyzeChangeImpact(index, ['assets/hero.webp'], [], { source: 'git-working-tree', diffLineRanges: [] });
  assert.equal(result.unknownChangedPaths.length, 0);
  assert.deepEqual(Array.from(result.unindexedAssetChanges), ['assets/hero.webp']);
  assert.ok(result.assetConsumers.some(item => item.owner === 'styles/site.css'));
  assert.ok(result.tests.some(item => item.path === 'tests/header.test.ts'));
});

test('Git snapshot combines working tree and local branch diff, including untracked source declaration impact', () => {
  const context = createImpactContext();
  const developmentSource = read('Shared/ChatWindow/scripts/agent/development-intelligence.js');
  const helpers = [
    extractFunction(developmentSource, 'function projectDevelopmentParseGitStatus(', '\nfunction projectDevelopmentDiffPaths'),
    extractFunction(developmentSource, 'function projectDevelopmentAnalyzeGitSnapshot(', '\nfunction projectDevelopment')
  ].join('\n') + '\nglobalThis.projectDevelopmentAnalyzeGitSnapshot = projectDevelopmentAnalyzeGitSnapshot;\n';
  vm.runInContext(helpers, context);
  const code = 'export function addedFeature() { return true; }';
  const extracted = context.projectIndexExtractTypeScript(code, 'src/new-feature.ts');
  const index = { rootName: 'fixture', updatedAt: '', stats: { files: 2, scanned: 2, truncated: false }, files: [
    { path: 'src/new-feature.ts', language: 'typescript', text: code.toLowerCase(), analysisSource: code, declarationRanges: extracted.declarationRanges, imports: [], exports: [{ exported: 'addedFeature' }], importBindings: [], dynamicDependencySignals: [] },
    { path: 'tests/new-feature.test.ts', language: 'typescript', text: '', analysisSource: "import '../src/new-feature';", imports: ['../src/new-feature'], exports: [], importBindings: [{ source: '../src/new-feature', imported: 'addedFeature', local: 'addedFeature' }], dynamicDependencySignals: [] }
  ], manifests: [], importGraph: { localImportEdges: [{ from: 'tests/new-feature.test.ts', to: 'src/new-feature.ts' }], resolvedBindings: [], unresolvedBindings: [], dynamicDependencySignals: [], compilerAnalysis: { supported: true, filesAnalyzed: 2, truncated: false }, note: 'fixture' } };
  const snapshot = {
    current: { branch: 'feature/change-impact', status: '## feature/change-impact\n?? src/new-feature.ts', stagedDiff: '', unstagedDiff: '', diffCheck: { exitCode: 0, output: '' } },
    branchComparison: { baseRef: 'main', mergeBase: 'abc123', truncated: false, diff: 'diff --git a/src/old.ts b/src/old.ts\n--- a/src/old.ts\n+++ /dev/null\n@@ -1 +0,0 @@\n-export const old = true;' }
  };
  const result = context.projectDevelopmentAnalyzeGitSnapshot(index, snapshot);
  assert.equal(result.branch, 'feature/change-impact');
  assert.equal(result.baseRef, 'main');
  assert.ok(result.changedPaths.includes('src/old.ts'));
  assert.ok(result.changedPaths.includes('src/new-feature.ts'));
  assert.ok(result.changedSymbols.some(item => item.path === 'src/new-feature.ts' && item.name === 'addedFeature'));
  assert.ok(result.testImpact.tests.some(item => item.path === 'tests/new-feature.test.ts'));
  assert.equal(result.branchComparison.mergeBase, 'abc123');
});

test('Git-aware tool contract requires approval, refreshes the index and supports optional local base-ref comparison', () => {
  const tools = read('Shared/ChatWindow/scripts/agent/tools.js');
  const providers = read('Shared/ChatWindow/scripts/providers.js');
  const native = read('Shared/AgentHostBridge.cs');
  const workflow = read('Shared/ChatWindow/scripts/agent/developer-workflows.js');
  assert.match(tools, /case 'project_change_impact':[\s\S]*?requireAgentApproval\([\s\S]*?permissionCategory: 'gitRead'/);
  assert.match(tools, /action: 'impact_snapshot'/);
  assert.match(tools, /buildProjectIndex\(\{ force: false, signal \}\)/);
  assert.match(providers, /name: 'project_change_impact'[\s\S]*?base_ref/);
  const changeTool = providers.slice(providers.indexOf("name: 'project_change_impact'"), providers.indexOf("name: 'project_release_readiness'"));
  assert.match(changeTool, /changed_paths:[\s\S]*?maxItems: 1000/);
  assert.doesNotMatch(changeTool, /changed_paths: \{[^}]*minItems/);
  assert.match(read('Shared/ChatWindow/scripts/agent/project-index.js'), /analysisSource:\['javascript','typescript','html','css','svg'\]/);
  assert.match(native, /case "impact_snapshot": return await BuildGitImpactSnapshotAsync/);
  assert.match(native, /private static async Task<object> BuildGitImpactSnapshotAsync/);
  assert.match(native, /"diff", "--no-ext-diff", "--no-color", "--find-renames", "--unified=3", range/);
  assert.match(workflow, /Change Impact Explorer v2/);
  assert.match(workflow, /coverage|base_ref/);
});
