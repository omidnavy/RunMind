'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');
const { TextEncoder } = require('node:util');

const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');

function createDevelopmentContext() {
  const values = new Map();
  let workspaceFingerprint = 'a'.repeat(64);
  const localStorage = {
    getItem(key) { return values.has(key) ? values.get(key) : null; },
    setItem(key, value) { values.set(key, String(value)); },
    removeItem(key) { values.delete(key); }
  };
  const context = vm.createContext({
    console,
    crypto: webcrypto,
    TextEncoder,
    Blob,
    localStorage,
    window: { chrome: { webview: {} } },
    workspaceContext: { id: 'workspace-context-1', folderName: 'fixture-project' },
    callAgentHost: async (route) => route === 'process.workspace.status'
      ? { selected: true, workspaceFingerprint, folder: 'fixture-project' }
      : { success: false, error: 'No Git fixture selected.' }
  });
  vm.runInContext(read('Shared/ChatWindow/scripts/agent/development-intelligence.js'), context, { filename: 'development-intelligence.js' });
  return { context, values, setWorkspaceFingerprint(value) { workspaceFingerprint = value; } };
}

function toPlain(value) { return JSON.parse(JSON.stringify(value)); }

test('P6 Git review parses staged, unstaged, untracked, renamed and conflicted paths', () => {
  const { context } = createDevelopmentContext();
  const result = toPlain(context.projectDevelopmentParseGitStatus({
    branch: 'feature/p6',
    status: '## feature/p6...origin/feature/p6 [ahead 1]\n M src/app.js\nA  src/new-module.js\n?? public/new.svg\nUU src/conflict.ts\nR  old-name.js -> src/new-name.js',
    untrackedPaths: ['public/new.svg', 'docs/new-note.md'],
    conflictPaths: ['src/conflict.ts']
  }));
  assert.equal(result.branch, 'feature/p6');
  assert.equal(result.stagedCount, 3);
  assert.equal(result.unstagedCount, 2);
  assert.equal(result.untrackedCount, 2);
  assert.equal(result.conflictCount, 1);
  assert.ok(result.changedPaths.includes('docs/new-note.md'));
  assert.ok(result.changedPaths.includes('old-name.js'), 'renamed source path should participate in impact analysis');
  assert.equal(result.files.find(item => item.path === 'src/new-name.js').originalPath, 'old-name.js');
  assert.deepEqual(result.conflictedPaths, ['src/conflict.ts']);
});

test('P6 build/test intelligence identifies scripts, missing tool declarations, flaky runs and recurring failures', async () => {
  const { context } = createDevelopmentContext();
  await context.projectDevelopmentEnsureIdentity();
  const index = {
    manifests: [{ path: 'package.json', text: JSON.stringify({ name: 'fixture', packageManager: 'npm@10.8.0', engines: { node: '>=24' }, scripts: { test: 'vitest run', build: 'vite build', lint: 'eslint .' }, devDependencies: { vitest: '^3.0.0', vite: '^6.0.0' } }) }],
    files: [
      { path: 'tests/app.test.js', text: '' },
      { path: '.github/workflows/ci.yml', text: 'name: CI\njobs:\n  test:\n    steps: []\n  build:\n    steps: []\n' }
    ]
  };
  for (const record of [
    { kind: 'test', command: 'npm run test', scriptName: 'test', success: false, failureSignature: 'assertion mismatch', excerpt: 'AssertionError: expected x', environment: { node: 'v24.0.0', packageManager: 'npm', packageManagerVersion: '10.8.0', os: 'Windows' } },
    { kind: 'test', command: 'npm run test', scriptName: 'test', success: false, failureSignature: 'assertion mismatch', excerpt: 'AssertionError: expected x', environment: { node: 'v24.0.0', packageManager: 'npm', packageManagerVersion: '10.8.0', os: 'Windows' } },
    { kind: 'test', command: 'npm run test', scriptName: 'test', success: true, exitCode: 0, environment: { node: 'v22.16.0', packageManager: 'npm', packageManagerVersion: '10.9.0', os: 'Linux' } },
    { kind: 'build', command: 'npm run build', scriptName: 'build', success: false, failureSignature: 'missing module', excerpt: 'Cannot resolve module', environment: { node: 'v24.0.0', packageManager: 'npm', packageManagerVersion: '10.8.0', os: 'Windows' } }
  ]) context.projectDevelopmentRecordRun(record);
  const memory = toPlain(context.projectDevelopmentLoadMemory());
  const report = toPlain(context.projectDevelopmentBuildTestAnalysis(index, memory, [
    { suite: 'test', status: 'passed', run_id: 'ci-42' }
  ]));
  assert.equal(report.scriptSummary.test, 1);
  assert.equal(report.scriptSummary.build, 1);
  assert.equal(report.testFiles[0], 'tests/app.test.js');
  assert.equal(report.ci.configured[0].provider, 'GitHub Actions');
  assert.equal(report.history.flakyCommands.some(item => item.command === 'npm run test'), true);
  assert.equal(report.history.recurringFailures.some(item => item.command === 'npm run test'), true);
  const testHistory = report.history.commands.find(item => item.command === 'npm run test');
  assert.equal(testHistory.environmentVaried, true);
  assert.equal(testHistory.environmentOutcomeDiffers, true);
  assert.match(testHistory.environmentSignal, /Possible environment-sensitive outcome/);
  assert.equal(report.history.environmentSensitiveCommands.some(item => item.command === 'npm run test'), true);
  assert.equal(testHistory.environmentVariants.length, 2);
  assert.equal(testHistory.latest.environment.node, 'v22.16.0');
  assert.equal(report.ci.comparisons[0].outcome, 'match');
  assert.equal(report.verification.buildsOrTestsExecutedByThisAnalysis, false);
});

test('P6 dependency inspection identifies exact lock mismatches and competing package-manager signals', () => {
  const { context } = createDevelopmentContext();
  const index = {
    manifests: [{ path: 'package.json', text: JSON.stringify({ packageManager: 'npm@10.0.0', dependencies: { vite: '6.0.0' }, devDependencies: { eslint: '^9.0.0' } }) }],
    files: [
      { path: 'package-lock.json', text: JSON.stringify({ lockfileVersion: 3, packages: { '': {}, 'node_modules/vite': { version: '6.1.0' }, 'node_modules/eslint': { version: '9.1.0' } } }) },
      { path: 'pnpm-lock.yaml', text: 'lockfileVersion: 9.0' }
    ],
    toolchains: { packageManager: 'npm', lockfiles: [{ manager: 'npm', file: 'package-lock.json' }, { manager: 'pnpm', file: 'pnpm-lock.yaml' }], conflictingPackageManagerSignals: false }
  };
  const report = toPlain(context.projectDevelopmentDependencyAnalysis(index));
  assert.equal(report.summary.exactLockMismatches, 1);
  assert.equal(report.audit.vulnerabilityAudit, 'not-run');
  assert.equal(report.audit.outdatedCheck, 'not-run');
  assert.equal(report.findings.some(item => /multiple package-manager lockfiles/i.test(item.message)), true);
});



test('P6 project memory switches identity when the selected native workspace changes within the same chat context', async () => {
  const { context, setWorkspaceFingerprint } = createDevelopmentContext();
  await context.projectDevelopmentEnsureIdentity();
  context.projectDevelopmentRecordDecision('Workspace A decision', 'Only applies to workspace A.');
  setWorkspaceFingerprint('b'.repeat(64));
  const identity = await context.projectDevelopmentEnsureIdentity();
  assert.equal(identity.key, 'b'.repeat(64));
  assert.equal(toPlain(context.projectDevelopmentReadMemory()).decisions.length, 0);
  setWorkspaceFingerprint('a'.repeat(64));
  await context.projectDevelopmentEnsureIdentity();
  assert.equal(toPlain(context.projectDevelopmentReadMemory()).decisions[0].title, 'Workspace A decision');
});

test('P6 file ownership hashing uses SHA-256 over exact file bytes', async () => {
  const { context } = createDevelopmentContext();
  const digest = await context.projectDevelopmentHashBlob(new Blob(['hello']));
  assert.equal(digest, '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824');
});

test('P6 ownership audit leaves external changes unattributed when no pre-write Git baseline exists', async () => {
  const { context } = createDevelopmentContext();
  await context.projectDevelopmentEnsureIdentity();
  await context.projectDevelopmentRecordOwnedChanges([
    { path: 'src/agent.js', operation: 'edit', backupPath: '.runmind-backups/agent.bak', previousSha256: '1'.repeat(64), currentSha256: '2'.repeat(64) }
  ], null);
  const report = toPlain(context.projectDevelopmentBuildOwnedChangeAudit({ status: '## main\n M src/agent.js\n M src/user-existing.js' }));
  assert.equal(report.attributionAvailable, false);
  assert.deepEqual(report.additionalChangesSinceRecordedBaseline, []);
  assert.deepEqual(report.unattributedChangedPaths, ['src/user-existing.js']);
});

test('P6 project memory is workspace-fingerprint-bound, redacted, bounded and restores only interrupted checkpoints', async () => {
  const { context, values } = createDevelopmentContext();
  const identity = await context.projectDevelopmentEnsureIdentity();
  assert.equal(identity.available, true);
  const decision = context.projectDevelopmentRecordDecision('Use Node 24', 'Keep Node 24 as the development baseline; token=not-a-real-secret');
  assert.equal(decision.saved, true);
  context.projectDevelopmentRecordRun({ kind: 'test', command: 'npm run test', scriptName: 'test', success: false, failureSignature: 'module missing', excerpt: 'Cannot find module', environment: { node: 'v24.0.0' } });
  context.projectDevelopmentPersistCheckpoints([
    { id: 'task-interrupted', task: 'Fix tests', status: 'interrupted', stage: 'test', summary: 'Need inspect result', plan: [{ step: 'Run tests', status: 'in_progress' }], operationIds: ['op-1'], createdAt: '2026-11-22T10:00:00Z', updatedAt: '2026-11-22T10:01:00Z' },
    { id: 'task-complete', task: 'Done', status: 'completed', stage: 'complete' }
  ], [{ checkpointId: 'task-interrupted', id: 'op-1', name: 'run tests', status: 'unknown', resultPreview: 'exit status unavailable' }]);
  const restored = [];
  const outcome = toPlain(context.projectDevelopmentRestoreCheckpoints(restored, 'workspace-context-1'));
  assert.equal(outcome.restored, 1);
  assert.equal(restored.length, 1);
  assert.equal(restored[0].id, 'task-interrupted');
  assert.equal(restored[0].recoveryOperations[0].status, 'unknown');
  const memoryRead = toPlain(context.projectDevelopmentReadMemory());
  assert.equal(memoryRead.recentRuns[0].failureSignature, 'module missing');
  assert.equal(memoryRead.recentRuns[0].environment.node, 'v24.0.0');
  assert.equal(memoryRead.checkpoints[0].operations[0].status, 'unknown');
  const serialized = [...values.values()].join('\n');
  assert.doesNotMatch(serialized, /not-a-real-secret/);
  assert.match(serialized, /Use Node 24/);
});

test('P6 Agent-owned change audit separates newly changed paths from its pre-write Git baseline', async () => {
  const { context } = createDevelopmentContext();
  await context.projectDevelopmentEnsureIdentity();
  await context.projectDevelopmentRecordOwnedChanges([
    { path: 'src/agent.js', operation: 'edit', backupPath: '.runmind-backups/agent.bak', previousSha256: '1'.repeat(64), currentSha256: '2'.repeat(64) }
  ], { changedPaths: ['src/user-existing.js'] });
  const report = toPlain(context.projectDevelopmentBuildOwnedChangeAudit({
    status: '## main\n M src/agent.js\n M src/user-existing.js\n?? docs/independent.md',
    untrackedPaths: ['docs/independent.md']
  }));
  assert.equal(report.summary.trackedAgentPaths, 1);
  assert.equal(report.ownedChanges[0].currentlyChangedInGit, true);
  assert.deepEqual(report.additionalChangesSinceRecordedBaseline, ['docs/independent.md']);
});

test('P6 tools, approval boundaries, ownership receipts and built-in workflows are wired into the shared Agent Core', () => {
  const html = read('Shared/ChatWindow.html');
  const providers = read('Shared/ChatWindow/scripts/providers.js');
  const tools = read('Shared/ChatWindow/scripts/agent/tools.js');
  const workflows = read('Shared/ChatWindow/scripts/agent/developer-workflows.js');
  const scheduler = read('Shared/ChatWindow/scripts/agent/workflow.js');
  const bridge = read('Shared/AgentHostBridge.cs');
  const runtime = read('Shared/ChatWindow/scripts/agent/runtime.js');
  assert.match(html, /ChatWindow\/scripts\/agent\/development-intelligence\.js/);
  for (const name of ['project_development_review','project_build_test_intelligence','project_dependency_health','project_environment_check','project_run_script','project_session_memory','project_agent_change_audit']) {
    assert.ok(providers.includes(`name: '${name}'`), `provider missing ${name}`);
    assert.ok(tools.includes(`case '${name}'`), `tool handler missing ${name}`);
  }
  for (const name of ['git-change-review','build-test-intelligence','dependency-environment-health','agent-change-review-recovery','development-session-continuity']) assert.ok(workflows.includes(`id:'${name}'`), `workflow missing ${name}`);
  assert.match(tools, /projectDevelopmentTrackMutation/);
  assert.match(tools, /restore_file_backup/);
  assert.match(tools, /alwaysAsk: true/);
  assert.match(tools, /failureCorrelationNote/);
  assert.match(bridge, /review_snapshot/);
  assert.match(bridge, /--find-renames/);
  assert.match(bridge, /untracked\.Output\.Contains/);
  assert.match(bridge, /workspaceFingerprint = selected \? GetWorkspaceFingerprint/);
  assert.match(scheduler, /project_development_review/);
  assert.match(scheduler, /project_agent_change_audit/);
  assert.match(runtime, /RunMind P6 Development Intelligence guidance/);
  assert.match(runtime, /project_session_memory/);
});

test('P6 release versions, shared native/JS assets and exact supported language scope are consistent', () => {
  const projects = [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj'
  ];
  for (const file of projects) assert.match(read(file), /<Version>3\.5\.0<\/Version>/, file);
  assert.match(read('RunMind.CommandPalette/RunMind.CommandPalette.csproj'), /<AppxPackageVersion>3\.5\.0\.0<\/AppxPackageVersion>/);
  assert.match(read('RunMind.CommandPalette/Package.appxmanifest'), /Version="3\.5\.0\.0"/);
  assert.match(read('Community.PowerToys.Run.Plugin.RunMind/plugin.json'), /"Version":\s*"3\.5\.0"/);
  assert.match(read('Shared/ChatWindow/scripts/agent/mcp.js'), /version: '3\.5\.0'/);
  assert.match(read('ROADMAP.md'), /JavaScript, TypeScript, JSX\/TSX, HTML and CSS/);
  assert.match(read('ROADMAP.md'), /SVG is a first-class vector asset/);
});
