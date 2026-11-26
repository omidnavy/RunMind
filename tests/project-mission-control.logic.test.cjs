'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '..');
const source = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/project-index.js'), 'utf8');
const start = source.indexOf('async function getProjectMissionControl(');
const end = source.indexOf('\n    let projectIndexAutoRefreshTimer', start);
assert.ok(start >= 0 && end > start, 'Mission Control function source should be present');
const missionControlFunction = source.slice(start, end);

function createHarness({ index, diagnostics, readiness, understanding, impact } = {}) {
  const fixture = {
    index: index === undefined ? {
      rootName: 'fixture-project',
      updatedAt: new Date().toISOString(),
      stats: { files: 20, scanned: 20, readBytes: 4096, reused: 15, removed: 0, skipped: 2, truncated: false, reason: '' },
      files: []
    } : index,
    diagnostics: diagnostics || { status: 'healthy', summary: { errors: 0, warnings: 0, information: 0 }, checks: [], packageManager: 'npm', lockfiles: [{ manager: 'npm', file: 'package-lock.json' }], testFilesFound: 2, toolingConfigurations: ['tsconfig.json'], execution: { performed: false } },
    readiness: readiness || { status: 'static-checks-passed', summary: { blockers: 0, warnings: 0, passed: 8 }, checks: [], packageManager: 'npm', suggestedCommands: { build: 'npm run build', test: 'npm test' }, notVerified: ['Actual test execution'] },
    understanding: understanding || { modules: [{ directory: 'src', files: 10 }], entryPoints: [{ path: 'src/index.js' }], highCouplingModules: [{ path: 'src/core.js', incomingImports: 8, outgoingImports: 2 }], dependencyDirection: { circularDependencies: [], unresolvedBindings: 0, localImportEdges: 15, resolvedBindings: 14 }, analysisCoverage: { indexedFiles: 20, javascriptFiles: 12, typescriptFiles: 4, htmlFiles: 2, cssFiles: 2, parseErrors: [], indexTruncated: false } },
    impact: impact || { summary: { analyzedChanges: 1, unanalysedChanges: 0, unknownChanges: 0, highConfidence: 1, mediumConfidence: 0, lowConfidence: 0, recommendedTests: 1 }, changedPaths: ['src/core.js'], matchedChangedPaths: ['src/core.js'], analyzedChangedPaths: ['src/core.js'], unanalysedChangedPaths: [], unknownChangedPaths: [], impactedModuleCount: 3, tests: [{ path: 'tests/core.test.js', confidence: 'high', reason: 'Direct import' }], note: 'Static graph only.' },
    buildCount: 0,
    impactPaths: null,
    refreshError: null
  };
  const context = vm.createContext({ fixture, console });
  vm.runInContext(`
    let projectIndexAutoRefreshPromise = null;
    async function loadProjectIndex() { return fixture.index; }
    async function buildProjectIndex() {
      fixture.buildCount++;
      if (fixture.refreshError) throw new Error(fixture.refreshError);
      fixture.index = { ...fixture.index, updatedAt: new Date().toISOString() };
    }
    function projectIndexBuildDiagnostics() { return fixture.diagnostics; }
    function projectIndexBuildReleaseReadiness() { return fixture.readiness; }
    function projectIndexBuildUnderstanding() { return fixture.understanding; }
    function projectIndexAnalyzeTestImpact(index, changedPaths) { fixture.impactPaths = [...changedPaths]; return fixture.impact; }
    ${missionControlFunction}
    globalThis.runMissionControl = getProjectMissionControl;
  `, context);
  return { context, fixture };
}

test('Mission Control refreshes stale index data once and carries detailed evidence through its result', async () => {
  const staleIndex = {
    rootName: 'fixture-project',
    updatedAt: new Date(Date.now() - 6 * 60 * 1000).toISOString(),
    stats: { files: 20, scanned: 22, readBytes: 8192, reused: 15, removed: 0, skipped: 2, truncated: true, reason: 'aggregate byte budget' },
    files: []
  };
  const { context, fixture } = createHarness({ index: staleIndex });
  const result = await context.runMissionControl({ changedPaths: ['src/core.js', ' ', 'src/template.py'] });
  assert.equal(fixture.buildCount, 1);
  assert.equal(result.index.refresh.attempted, true);
  assert.equal(result.index.refresh.succeeded, true);
  assert.equal(result.index.freshness, 'fresh');
  assert.equal(result.index.truncated, true);
  assert.equal(result.status, 'incomplete');
  assert.deepEqual(Array.from(fixture.impactPaths), ['src/core.js', 'src/template.py']);
  assert.equal(result.testImpact.tests[0].path, 'tests/core.test.js');
  assert.ok(result.limitations.length >= 3);
  assert.equal(result.verification.commandsExecuted, false);
});

test('Mission Control does not claim fresh evidence when a stale refresh is disabled', async () => {
  const index = { rootName: 'fixture-project', updatedAt: new Date(Date.now() - 9 * 60 * 1000).toISOString(), stats: { files: 1 }, files: [] };
  const { context, fixture } = createHarness({ index });
  const result = await context.runMissionControl({ refreshIfStale: false });
  assert.equal(fixture.buildCount, 0);
  assert.equal(result.index.freshness, 'stale');
  assert.equal(result.status, 'incomplete');
  assert.ok(result.nextActions.some(item => item.id === 'index-freshness'));
});

test('Mission Control prioritizes errors and warnings while treating coupling as a heuristic', async () => {
  const diagnostics = {
    status: 'issues-found', summary: { errors: 1, warnings: 1, information: 0 },
    checks: [
      { id: 'warning-a', severity: 'warning', message: 'Investigate warning', evidence: ['src/a.js'] },
      { id: 'error-a', severity: 'error', message: 'Fix blocking error', evidence: ['package.json'] }
    ], packageManager: 'npm', lockfiles: [], testFilesFound: 0, toolingConfigurations: [], execution: { performed: false }
  };
  const { context } = createHarness({ diagnostics });
  const result = await context.runMissionControl();
  assert.equal(result.status, 'blocked');
  assert.equal(result.nextActions[0].priority, 'critical');
  assert.equal(result.nextActions[0].action, 'Fix blocking error');
  assert.equal(result.projectUnderstanding.highCouplingExamples[0].path, 'src/core.js');
});

test('Mission Control preserves stale status and reports a failed incremental refresh', async () => {
  const staleIndex = { rootName: 'fixture-project', updatedAt: new Date(Date.now() - 8 * 60 * 1000).toISOString(), stats: { files: 4, truncated: false }, files: [] };
  const { context, fixture } = createHarness({ index: staleIndex });
  fixture.refreshError = 'Workspace read permission was revoked.';
  const result = await context.runMissionControl();
  assert.equal(fixture.buildCount, 1);
  assert.equal(result.index.refresh.attempted, true);
  assert.equal(result.index.refresh.succeeded, false);
  assert.match(result.index.refresh.error, /permission was revoked/);
  assert.equal(result.index.freshness, 'stale');
  assert.equal(result.status, 'incomplete');
});

test('High coupling remains a review heuristic rather than an overall status failure', async () => {
  const { context } = createHarness();
  const result = await context.runMissionControl();
  assert.equal(result.status, 'static-checks-passed');
  assert.ok(result.nextActions.some(item => item.id === 'architecture-coupling' && item.priority === 'medium'));
});

test('Mission Control exposes impact-analysis limitations and never implies inferred Git changes', async () => {
  const { context } = createHarness();
  const result = await context.runMissionControl({ changedPaths: [] });
  assert.equal(result.testImpact.available, false);
  assert.match(result.testImpact.message, /Git changes are not inferred/);
  assert.equal(result.executionPerformed, false);
  assert.equal(result.releaseReadiness.executionPerformed, false);
});
