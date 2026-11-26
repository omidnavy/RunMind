'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const acorn = require('../Shared/ChatWindow/vendor/acorn/acorn.js');
const ts = require('../Shared/ChatWindow/vendor/typescript/typescript.js');

const source = fs.readFileSync(path.join(__dirname, '..', 'Shared/ChatWindow/scripts/agent/project-index.js'), 'utf8');
const context = { acorn, ts, console, AbortController, DOMException, setInterval, clearInterval };
context.globalThis = context;
vm.runInNewContext(`${source}\nglobalThis.__understanding = projectIndexBuildUnderstanding; globalThis.__diagnostics = projectIndexBuildDiagnostics; globalThis.__impact = projectIndexAnalyzeTestImpact; globalThis.__release = projectIndexBuildReleaseReadiness;`, context);

function fixture() {
  return {
    rootName: 'fixture', updatedAt: '2026-11-22T00:00:00.000Z',
    stats: { files: 12, truncated: false },
    files: [
      { path: 'src/app.js', language: 'javascript', symbols: ['start'], calls: [{ callee: 'boot' }] },
      { path: 'src/service.js', language: 'javascript', symbols: ['serve'], calls: [] },
      { path: 'src/util.js', language: 'javascript', symbols: ['format'], calls: [] },
      { path: 'tests/app.test.js', language: 'javascript', symbols: [], calls: [] },
      { path: 'tests/service.test.js', language: 'javascript', symbols: [], calls: [] },
      { path: 'tests/legacy.test.py', language: 'text', symbols: [], calls: [] },
      { path: 'tests/Windows.test.cs', language: 'text', symbols: [], calls: [] },
      { path: 'src/legacy.py', language: 'text', symbols: [], calls: [] },
      { path: 'README.md', language: 'markdown', symbols: [], calls: [] },
      { path: 'index.html', language: 'html', symbols: [], calls: [], text: '<html><script src="./src/app.js"></script></html>' },
      { path: 'CHANGELOG.md', language: 'markdown', symbols: [], calls: [], text: '## 1.2.3 release notes' },
      { path: '.github/workflows/ci.yml', language: 'yaml', symbols: [], calls: [] }
    ],
    manifests: [{ path: 'package.json', text: JSON.stringify({ name: 'fixture', version: '1.2.3', packageManager: 'npm@10.0.0', scripts: { build: 'vite build', test: 'node --test' }, dependencies: { vite: '^6.0.0' } }) }],
    toolchains: { packageManager: 'npm', lockfiles: [{ manager: 'npm', file: 'package-lock.json' }], conflictingPackageManagerSignals: false },
    importGraph: {
      localImportEdges: [
        { from: 'src/app.js', to: 'src/service.js' },
        { from: 'src/service.js', to: 'src/util.js' },
        { from: 'tests/app.test.js', to: 'src/app.js' },
        { from: 'tests/service.test.js', to: 'src/service.js' }
      ],
      resolvedBindings: [], unresolvedBindings: [], circularDependencies: [], note: 'fixture graph'
    }
  };
}

test('Project Understanding maps modules, entry points, coupling and supported-language coverage', () => {
  const result = context.__understanding(fixture());
  assert.equal(result.available, true);
  assert.ok(result.modules.some(item => item.directory === 'src'));
  assert.ok(result.entryPoints.some(item => item.path === './src/app.js' && item.confidence === 'heuristic-candidate'));
  assert.ok(result.highCouplingModules.some(item => item.path === 'src/service.js'));
  assert.equal(result.analysisCoverage.javascriptFiles, 5);
  assert.match(result.scope, /JavaScript, TypeScript, JSX\/TSX, HTML and CSS/);
});

test('Project Diagnostics detects invalid package manifests and lockfile conflicts', () => {
  const project = fixture();
  project.manifests[0].text = '{ invalid';
  project.toolchains = { packageManager: 'pnpm', lockfiles: [{ manager: 'npm', file: 'package-lock.json' }, { manager: 'pnpm', file: 'pnpm-lock.yaml' }], conflictingPackageManagerSignals: true };
  const result = context.__diagnostics(project);
  assert.equal(result.status, 'issues-found');
  assert.ok(result.checks.some(item => item.id === 'invalid-package-json' && item.severity === 'error'));
  assert.ok(result.checks.some(item => item.id === 'package-manager-conflict' && item.severity === 'error'));
});

test('Project Diagnostics detects package-lock metadata mismatch and malformed package scripts', () => {
  const project = fixture();
  project.manifests.push({ path: 'package-lock.json', text: JSON.stringify({ name: 'wrong-name', version: '9.9.9', lockfileVersion: 3 }) });
  project.manifests[0].text = JSON.stringify({ name: 'fixture', version: '1.2.3', scripts: ['not-an-object'] });
  const result = context.__diagnostics(project);
  assert.ok(result.checks.some(item => item.id === 'package-lock-name-mismatch' && item.severity === 'error'));
  assert.ok(result.checks.some(item => item.id === 'package-lock-version-mismatch' && item.severity === 'error'));
  assert.ok(result.checks.some(item => item.id === 'invalid-package-scripts' && item.severity === 'error'));
});

test('Test Impact Analysis selects direct and transitive dependent tests without executing them', () => {
  const result = context.__impact(fixture(), ['src/util.js']);
  assert.equal(result.executionPerformed, false);
  assert.deepEqual(Array.from(result.tests.map(item => item.path)).sort(), ['tests/app.test.js', 'tests/service.test.js']);
  assert.equal(result.summary.mediumConfidence, 2);
  assert.equal(result.summary.highConfidence, 0);
});

test('P1 test discovery and impact analysis exclude unsupported-language test files', () => {
  const project = fixture();
  const diagnostics = context.__diagnostics(project);
  const impact = context.__impact(project, ['src/util.js']);
  assert.equal(diagnostics.testFilesFound, 2);
  assert.ok(impact.tests.every(item => item.path.endsWith('.js')));
  assert.ok(!impact.tests.some(item => /\.(?:py|cs)$/i.test(item.path)));
});

test('Test Impact Analysis explicitly reports indexed non-JS/TS changes as unanalysed', () => {
  const result = context.__impact(fixture(), ['src/legacy.py']);
  assert.deepEqual(Array.from(result.unanalysedChangedPaths), ['src/legacy.py']);
  assert.equal(result.analyzedChangedPaths.length, 0);
  assert.equal(result.tests.length, 0);
  assert.match(result.note, /only for JavaScript\/TypeScript changed files/);
});

test('Test Impact Analysis reports unknown paths and rejects an empty change set', () => {
  const result = context.__impact(fixture(), ['missing/file.js']);
  assert.deepEqual(Array.from(result.unknownChangedPaths), ['missing/file.js']);
  assert.equal(result.tests.length, 0);
  assert.throws(() => context.__impact(fixture(), []), /Provide one or more/);
});

test('Release Readiness separates static readiness from execution and publishing gates', () => {
  const project = fixture();
  const diagnostics = context.__diagnostics(project);
  const result = context.__release(project, diagnostics);
  assert.equal(result.version, '1.2.3');
  assert.equal(result.status, 'static-checks-passed');
  assert.equal(result.suggestedCommands.build, 'npm run build');
  assert.equal(result.suggestedCommands.test, 'npm run test');
  assert.equal(result.executionPerformed, false);
  assert.equal(result.notVerified.length >= 4, true);
  assert.match(result.note, /does not create tags, commits, packages/);
});
