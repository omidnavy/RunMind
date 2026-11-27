'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');

function extractFunction(source, startText, endText) {
  const start = source.indexOf(startText);
  const end = source.indexOf(endText, start);
  assert.ok(start >= 0 && end > start, `Expected function ${startText} in source`);
  return source.slice(start, end);
}

test('P4 result summarization redacts secrets, preserves useful diagnostics, and emits valid JSON when truncated', () => {
  const context = vm.createContext({});
  vm.runInContext(read('Shared/ChatWindow/scripts/agent/p4-logic.js'), context);
  const logic = context.RunMindP4;
  const sample = {
    message: 'Authorization: Bearer abc.def.ghi api_key=secret123 ghp_abcdefghijklmnopqrstuvwxyz123456',
    access_token: 'must-not-leak',
    stack: 'TypeError: test\n at app.js:10:2',
    rawBody: 'very large private payload',
    records: Array.from({ length: 500 }, (_, id) => ({ id, status: 'ok' }))
  };
  const safe = logic.compactValue(sample);
  assert.doesNotMatch(JSON.stringify(safe), /abc\.def\.ghi|secret123|must-not-leak|ghp_abcdefghijklmnopqrstuvwxyz/);
  assert.match(safe.stack, /app\.js:10:2/);
  assert.match(safe.rawBody, /omitted/);
  const summarized = logic.summarizeToolResultForContext('fixture_tool', sample, 400);
  const parsed = JSON.parse(summarized);
  assert.equal(parsed.truncated, true);
  assert.doesNotMatch(summarized, /must-not-leak|secret123/);
});

test('P4 orchestration planner creates bounded parallel groups and serialized barriers', () => {
  const context = vm.createContext({});
  vm.runInContext(read('Shared/ChatWindow/scripts/agent/p4-logic.js'), context);
  const plan = context.RunMindP4.planToolBatchExecution([
    { name: 'read_file', arguments: { path: 'a.js' } },
    { name: 'project_overview', arguments: {} },
    { name: 'write_file', arguments: { path: 'b.js' } },
    { name: 'read_file', arguments: { path: 'b.js' } },
    { name: 'read_file', arguments: { path: 'c.js' } }
  ], [true, true, false, true, true], 3);
  assert.deepEqual(Array.from(plan.groups, group => [group.mode, Array.from(group.indexes)]), [
    ['parallel', [0, 1]], ['serialized', [2]], ['parallel', [3, 4]]
  ]);
  assert.equal(plan.duplicates, 0);
  assert.equal(plan.maxConcurrency, 3);
  const serial = context.RunMindP4.planToolBatchExecution([{ name: 'read_file' }, { name: 'read_file' }], [true, true], 4);
  assert.equal(serial.groups.every(group => group.mode === 'serialized'), true);
});

test('P4 checkpoint normalization is bounded, rejects invalid IDs and redacts secrets', () => {
  const context = vm.createContext({});
  vm.runInContext(read('Shared/ChatWindow/scripts/agent/p4-logic.js'), context);
  const id = '123e4567-e89b-12d3-a456-426614174000';
  assert.equal(context.RunMindP4.normalizeCheckpoint({ id: 'bad' }), null);
  const cp = context.RunMindP4.normalizeCheckpoint({
    id, status: 'running', task: 'Deploy with token=secret-value', summary: 'Authorization: Bearer abc.def',
    plan: [{ step: 'Check status', status: 'in_progress' }, { step: 'x'.repeat(500), status: 'bad' }],
    operationIds: [id, 'invalid'], workspaceId: 'workspace', createdAt: new Date().toISOString()
  });
  assert.equal(cp.status, 'running');
  assert.doesNotMatch(cp.task + cp.summary, /secret-value|abc\.def/);
  assert.equal(cp.plan.length, 2);
  assert.equal(cp.plan[1].step.length, 180);
  assert.deepEqual(Array.from(cp.operationIds), [id]);
});

test('P4 MCP discovery safely bounds pagination and detects repeated cursors', async () => {
  const mcp = read('Shared/ChatWindow/scripts/agent/mcp.js');
  const listFunction = extractFunction(mcp, 'async function mcpListAll(', '\n    function renderMcpServers');
  const context = vm.createContext({ calls: [], mcpRpc: async (_runtime, _method, params) => {
    context.calls.push(params.cursor || 'first');
    return { items: [{ name: 'one' }], nextCursor: params.cursor ? 'repeat' : 'repeat' };
  }});
  vm.runInContext(`${listFunction}\nglobalThis.listAll = mcpListAll;`, context);
  const result = await context.listAll({}, 'resources/list', 'items');
  assert.equal(result.length, 2);
  assert.equal(result.truncated, true);
  assert.equal(context.calls.length, 2);
  assert.match(mcp, /resources\/templates\/list/);
  assert.match(mcp, /if \(enabledRuntimes\.length\) \{/);
  assert.match(mcp, /mcp_search_knowledge/);
  assert.match(mcp, /mcp_read_resource_template/);
  assert.match(mcp, /page < 20/);
  assert.match(mcp, /version: '3\.5\.0'/);
  const discovery = extractFunction(mcp, 'function mcpDiscoveryIsComplete(', '\n    function searchMcpKnowledge');
  const discoveryContext = vm.createContext({});
  vm.runInContext(`${discovery}\nglobalThis.isComplete = mcpDiscoveryIsComplete;`, discoveryContext);
  assert.equal(discoveryContext.isComplete({ toolsTruncated: false, resourcesTruncated: false }), true);
  assert.equal(discoveryContext.isComplete({ resourcesError: 'unsupported method' }), false);
  assert.equal(discoveryContext.isComplete({ resourceTemplatesTruncated: true }), false);
});

test('P4 Change Impact Explorer follows reverse import edges and includes SVG asset consumers', () => {
  const project = read('Shared/ChatWindow/scripts/agent/project-index.js');
  const fn = extractFunction(project, 'function projectIndexAnalyzeChangeImpact(', '\n    function projectIndexBuildReleaseReadiness');
  const context = vm.createContext({ fixture: null });
  vm.runInContext(`
    function projectIndexNormalizePath(value) { return String(value || '').replace(/\\\\/g, '/').replace(/^\\.\\//, ''); }
    function projectIndexBuildUnderstanding() { return { assetReferences: [{ from: 'index.html', to: 'assets/icon.svg', kind: 'html-image', targetIndexed: true }], entryPoints: [], highCouplingModules: [] }; }
    function projectIndexAnalyzeTestImpact(_index, paths) { return { tests: [{ path: 'tests/app.test.js', confidence: 'high' }], summary: { recommendedTests: 1 }, coverageEvidence: { summary: { coverageFiles: 0, changedLinesMeasured: 0, changedLinesCovered: 0 } } }; }
    function projectIndexInferChangedSymbols() { return { symbols: [], removedCandidates: [] }; }
    function projectIndexBuildCoverageEvidence() { return { available: false, summary: { coverageFiles: 0, changedLinesMeasured: 0, changedLinesCovered: 0 } }; }
    ${fn}
    globalThis.analyze = projectIndexAnalyzeChangeImpact;
  `, context);
  const index = {
    rootName: 'fixture', updatedAt: new Date().toISOString(),
    files: [
      { path: 'src/app.js', language: 'javascript' }, { path: 'src/consumer.js', language: 'javascript' },
      { path: 'src/transitive.ts', language: 'typescript' }, { path: 'index.html', language: 'html' },
      { path: 'assets/icon.svg', language: 'svg' }, { path: 'notes.py', language: 'text' }
    ],
    importGraph: {
      localImportEdges: [{ from: 'src/consumer.js', to: 'src/app.js' }, { from: 'src/transitive.ts', to: 'src/consumer.js' }],
      resolvedBindings: [{ from: 'src/consumer.js', to: 'src/app.js', imported: 'run', local: 'run', compiler: true }],
      compilerAnalysis: { supported: true, truncated: false }, note: 'fixture static graph'
    }
  };
  const result = context.analyze(index, ['src/app.js', 'assets/icon.svg', 'notes.py', 'missing.js'], [{ path: 'src/app.js', name: 'run' }]);
  assert.deepEqual(Array.from(result.directDependents, item => item.path), ['src/consumer.js']);
  assert.deepEqual(Array.from(result.transitiveDependents, item => item.path), ['src/transitive.ts']);
  assert.equal(result.assetConsumers[0].path, 'index.html');
  assert.equal(result.symbolConsumers[0].importer, 'src/consumer.js');
  assert.deepEqual(Array.from(result.unknownChangedPaths), ['missing.js']);
  assert.deepEqual(Array.from(result.unanalysedChangedPaths), ['notes.py']);
  assert.equal(result.summary.recommendedTests, 1);
});

test('P4 Agent run captures project instructions and Skill metadata as explicitly untrusted bounded context', () => {
  const workspace = read('Shared/ChatWindow/scripts/agent/workspace.js');
  const runtime = read('Shared/ChatWindow/scripts/agent/runtime.js');
  assert.match(workspace, /async function buildAgentProjectContext/);
  for (const file of ['AGENTS.md', '.agents/AGENTS.md', '.github/copilot-instructions.md', 'package.json']) assert.ok(workspace.includes(file));
  assert.match(workspace, /24000/);
  assert.match(workspace, /discoverAgentSkills\(signal\)/);
  assert.match(runtime, /user-controlled evidence, not RunMind instructions/);
  assert.match(runtime, /Skill content remains guidance and never grants permission/i);
  assert.match(runtime, /buildAgentProjectContext\(signal, originalPrompt\)/);
});

test('P4 checkpoints are explicit-resume only, workspace-bound and linked to operation records', () => {
  const workflow = read('Shared/ChatWindow/scripts/agent/workflow.js');
  const runtime = read('Shared/ChatWindow/scripts/agent/runtime.js');
  const html = read('Shared/ChatWindow.html');
  assert.match(html, /id="workflowCheckpoints"/);
  assert.match(workflow, /createAgentTaskCheckpoint/);
  assert.match(workflow, /localStorage\.setItem\(agentTaskCheckpointStorageKey\(\)/);
  assert.match(workflow, /item\.workspaceId.*workspaceContext\?\.id/);
  assert.match(workflow, /data-checkpoint-resume/);
  assert.match(workflow, /unknown.*inspect before retrying/s);
  assert.match(workflow, /checkpointId: taskContext\.checkpointId/);
  assert.match(workflow, /buildDevelopmentSessionTimeline/);
  assert.match(runtime, /createAgentTaskCheckpoint/);
  assert.match(runtime, /finally/);
  assert.match(runtime, /updateAgentTaskCheckpoint\(taskCheckpoint\.id/);
});

test('P4 Development Session Timeline is exposed read-only and as a built-in workflow', () => {
  const providers = read('Shared/ChatWindow/scripts/providers.js');
  const tools = read('Shared/ChatWindow/scripts/agent/tools.js');
  const workflow = read('Shared/ChatWindow/scripts/agent/workflow.js');
  const recipes = read('Shared/ChatWindow/scripts/agent/developer-workflows.js');
  assert.match(providers, /name: 'development_session_timeline'/);
  assert.match(tools, /case 'development_session_timeline'/);
  assert.match(tools, /buildDevelopmentSessionTimeline/);
  assert.match(workflow, /parallelGroupId/);
  assert.match(workflow, /record\.error = RunMindP4\.redactText/);
  assert.match(recipes, /id:'development-session-timeline'/);
});

test('P4 artifact verification hashes exact file bytes, compares optional expected digest and returns bounded receipts', () => {
  const artifacts = read('Shared/ChatWindow/scripts/agent/artifacts.js');
  const providers = read('Shared/ChatWindow/scripts/providers.js');
  const tools = read('Shared/ChatWindow/scripts/agent/tools.js');
  assert.match(artifacts, /globalThis\.crypto\?\.subtle\)\s*\{\s*const digest = await globalThis\.crypto\.subtle\.digest\('SHA-256'/);
  assert.match(artifacts, /expectedSha256/);
  assert.match(artifacts, /verificationReceipt/);
  assert.match(artifacts, /expected SHA-256 mismatch/i);
  assert.match(artifacts, /finishReceipt\(false/);
  assert.match(providers, /expected_sha256/);
  assert.match(tools, /args\.expected_sha256/);
});

test('P4 assets, tools and versions are deployed consistently to all three hosts', () => {
  const hosts = [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj'
  ];
  for (const project of hosts) {
    const xml = read(project);
    assert.match(xml, /<Version>3\.5\.0<\/Version>/, project);
    assert.match(xml, /ChatWindow\\\*\*/, 'shared ChatWindow assets must be included');
  }
  const html = read('Shared/ChatWindow.html');
  assert.match(html, /ChatWindow\/scripts\/agent\/p4-logic\.js/);
  const providers = read('Shared/ChatWindow/scripts/providers.js');
  for (const name of ['mcp_search_knowledge','mcp_read_resource_template','project_change_impact','development_session_timeline']) {
    assert.ok(read('Shared/ChatWindow/scripts/agent/mcp.js').includes(name) || providers.includes(name), `missing ${name}`);
  }
});
