'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');

const bridge = read('Shared/AgentHostBridge.cs');
const workspaceBridge = read('Shared/WorkspaceFileBridge.cs');
const workspaceAdapter = read('Shared/ChatWindow/scripts/agent/workspace-native.js');
const core = read('Shared/ChatWindow/scripts/core.js');
const mcp = read('Shared/ChatWindow/scripts/agent/mcp.js');
const tools = read('Shared/ChatWindow/scripts/agent/tools.js');
const runtime = read('Shared/ChatWindow/scripts/agent/runtime.js');
const providers = read('Shared/ChatWindow/scripts/providers.js');
const serverManager = read('Shared/DevelopmentServerManager.cs');
const playwright = read('Shared/PlaywrightRunner.cjs');
const workflows = read('Shared/ChatWindow/scripts/agent/developer-workflows.js');

test('one native workspace is the canonical root for Agent file access and native tools in all hosts', () => {
  for (const project of [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj'
  ]) assert.match(read(project), /WorkspaceFileBridge\.cs/);
  assert.match(read('Shared/ChatWindow.html'), /ChatWindow\/scripts\/agent\/workspace-native\.js/);
  assert.match(core, /process\.workspace\.status/);
  assert.match(core, /createNativeWorkspaceDirectoryHandle/);
  assert.match(mcp, /if \(!forceChoose\).*process\.workspace\.status/s);
  assert.match(tools, /already selected for this chat[\s\S]*no second folder picker was opened/i);
  assert.match(tools, /no second folder picker was opened/i);
  assert.match(bridge, /process\.selectWorkspace/);
  assert.match(bridge, /SetProcessWorkspace\(contextId, root\)/);
});

test('native workspace file access rejects stale handles after the selected root changes', () => {
  assert.match(workspaceBridge, /private string RequireWorkspaceRoot\(string contextId, string workspaceId\)/);
  assert.match(workspaceBridge, /currentWorkspaceId, workspaceId/);
  assert.match(workspaceBridge, /stale or belongs to a different selected folder/);
  assert.match(workspaceBridge, /public required string WorkspaceId/);
  assert.match(workspaceBridge, /RequireWorkspaceRoot\(contextId, session\.WorkspaceId\)/);
  assert.match(workspaceAdapter, /workspaceId: this\.workspaceId/);
  assert.match(workspaceAdapter, /workspaceId,\s*writeId: session\.writeId/);
});

test('native workspace bridge has path containment, reparse-point, bounded transfer and atomic-write safeguards', () => {
  assert.match(workspaceBridge, /private static string\[\] WorkspaceSegments/);
  assert.match(workspaceBridge, /part is "\." or "\.\."/);
  assert.match(workspaceBridge, /Path\.IsPathRooted\(raw\)/);
  assert.match(workspaceBridge, /EnsureNoReparseAncestors\(candidate\)/);
  assert.match(workspaceBridge, /MaximumWorkspaceTransferChunkBytes|WorkspaceTransferChunkBytes/);
  assert.match(workspaceBridge, /MaximumWorkspaceWriteBytes/);
  assert.match(workspaceBridge, /stream\.ReadExactly\(buffer\)/);
  assert.match(workspaceBridge, /File\.Move\(session\.TemporaryPath, session\.DestinationPath, session\.Overwrite\)/);
  assert.match(workspaceBridge, /AbortWorkspaceWritesForContext/);
});

test('development-server ownership is persisted and only exact identity matches can be re-adopted or stopped', () => {
  assert.match(serverManager, /ProcessStartedAtUtcTicks/);
  assert.match(serverManager, /ExecutablePath/);
  assert.match(serverManager, /PersistDevelopmentServerMetadata\(session\)/);
  assert.match(serverManager, /VerifyDevelopmentServerIdentity\(session\)/);
  assert.match(serverManager, /Process\.GetProcessById\(metadata\.ProcessId\)/);
  assert.match(serverManager, /ProcessStartedAtUtcTicks/);
  assert.match(serverManager, /process\.ProcessName, "node"/);
  assert.match(serverManager, /start\.lock/);
  assert.match(serverManager, /\.stop\.lock/);
  assert.match(serverManager, /session\.Process\.Kill\(true\)/);
  assert.match(serverManager, /Stale or unknown processes were never terminated/);
});

test('development-server reconciliation is exposed without requiring a project workspace', () => {
  const serverTool = providers.slice(providers.indexOf("name: 'development_server_manage'"), providers.indexOf("name: 'api_test_run'"));
  assert.match(serverTool, /'reconcile'/);
  assert.match(tools, /needsWorkspace = action === 'discover' \|\| action === 'start'/);
  assert.match(tools, /!\['discover','start','list','reconcile','logs','health','stop'\]\.includes\(action\)/);
});

test('targeted port-owner lookup is exposed and broad diagnostics explicitly discourage port-specific use', () => {
  assert.match(providers, /\(groupOn\('processes'\) && capOn\('listProcesses'\)\) \|\| \(groupOn\('diagnostics'\) && capOn\('networkInformation'\)\)/);
  assert.match(providers, /addSystemTool\('system_manager_find_port_owner'/);
  assert.match(providers, /instead of system_manager_diagnostics/);
  assert.match(providers, /For a specific TCP\/UDP port or port conflict, use system_manager_find_port_owner/);
  assert.match(tools, /systemManager\.port\.owner/);
  assert.match(bridge, /systemManager\.port\.owner/);
  assert.match(read('Shared/SystemManager.cs'), /RequirePortOwnerCapability/);
  assert.match(read('Shared/SystemManager.cs'), /networkDiagnostics =/);
});

test('Agent records tool outcomes and always creates an evidence-based report after empty output, repeated failure, or step limit', () => {
  assert.match(runtime, /const toolExecutionLedger = \[\]/);
  assert.match(runtime, /toolExecutionLedger\.push\(item\)/);
  assert.match(runtime, /buildDeterministicFinalReport/);
  assert.match(runtime, /empty-final-response-after-tool-execution/);
  assert.match(runtime, /model-generation-failed-after-tool-execution/);
  assert.match(runtime, /tool-batch-failed-after-tool-execution/);
  assert.match(runtime, /ensureFinalAgentReport\('repeated-tool-failure'/);
  assert.match(runtime, /ensureFinalAgentReport\('tool-step-limit'/);
  assert.match(runtime, /tools: \[\]/);
});

test('P0 Agent guidance preserves JavaScript web-stack scope and directs port conflicts to targeted diagnostics', () => {
  assert.match(runtime, /JavaScript, TypeScript, JSX\/TSX, HTML, CSS, and SVG only/);
  assert.match(runtime, /system_manager_find_port_owner/);
  assert.match(runtime, /never the broad system_manager_diagnostics tool/);
  assert.match(runtime, /never open a second picker/);
});

test('Project Understanding only performs language-aware analysis for the agreed JS/TS/HTML/CSS/SVG scope', () => {
  const projectIndex = read('Shared/ChatWindow/scripts/agent/project-index.js');
  assert.match(projectIndex, /const map = \{[^}]*'\.js':'javascript'[^}]*'\.ts':'typescript'[^}]*'\.html':'html'[^}]*'\.svg':'svg'[^}]*'\.css':'css'/);
  assert.doesNotMatch(projectIndex, /'\.py':'python'|'\.cs':'csharp'|'\.rs':'rust'|'\.go':'go'/);
  assert.match(projectIndex, /if \(!\['javascript','typescript'\]\.includes\(file\.language\)\) continue;/);
  assert.doesNotMatch(projectIndex, /language==='python'|language==='rust'|language==='csharp'|language==='go'/);
  const importGraph = projectIndex.slice(projectIndex.indexOf('function projectIndexLocalImportGraph'), projectIndex.indexOf('function projectIndexExtract'));
  assert.doesNotMatch(importGraph, /\.py|__init__\.py/, 'JS/TS import resolution must not resolve into Python modules');
  assert.match(projectIndex, /ecosystems: packageManifest \? \['node'\] : \[\]/);
});

test('Project diagnostics and build workflows do not introduce other project-language toolchains', () => {
  for (const phrase of ['Python', 'Rust', 'Go', 'Ruff', 'mypy', 'clippy', 'dotnet analyzers', 'pytest', 'cargo test'])
    assert.equal(workflows.includes(phrase), false, `unexpected out-of-scope language/toolchain reference: ${phrase}`);
  assert.match(workflows, /JavaScript\/TypeScript\/JSX\/TSX\/HTML\/CSS tooling/);
});

test('Playwright reports browser policy events separately and only suppresses correlated policy transport failures', () => {
  assert.match(playwright, /Cross-Origin Request Blocked/);
  assert.match(playwright, /policyBlocks/);
  assert.match(playwright, /expectedPolicyBlocks/);
  assert.match(playwright, /unexpectedPolicyBlocks/);
  assert.match(playwright, /block\.expectedRules \|\| \[\]/);
  assert.match(playwright, /const success = !failed && missingExpectedPolicyBlocks\.length === 0/);
  assert.doesNotMatch(playwright, /expected\.url_contains && failedRequest\.url\.includes\(expected\.url_contains\) && block\.type/);
});

test('workspace identity is revalidated across hosts and metadata updates use an exclusive cross-process lock', () => {
  assert.match(bridge, /private readonly object _workspaceStateLock/);
  assert.match(bridge, /private string\? _processWorkspaceId/);
  assert.match(bridge, /AcquireProcessWorkspaceMetadataLock\(contextId\)/);
  assert.match(bridge, /FileShare\.None/);
  assert.match(bridge, /EnsureProcessWorkspaceForContext\(contextId\)/);
  assert.match(bridge, /latestWorkspaceId/);
  assert.match(bridge, /string\.Equals\(latestWorkspaceId, _processWorkspaceId/);
  assert.match(bridge, /AbortWorkspaceWritesForContext\(contextId\)/);
  assert.match(bridge, /selectedWorkspaceId/);
});

test('development-server metadata writes are serialized and orphan reconciliation is identity-conservative', () => {
  assert.match(serverManager, /metadataLockPath = target \+ "\.metadata\.lock"/);
  assert.match(serverManager, /new FileStream\(metadataLockPath, FileMode\.OpenOrCreate, FileAccess\.ReadWrite, FileShare\.None\)/);
  assert.match(serverManager, /start\.lock/);
  assert.match(serverManager, /reconcile\.lock/);
  assert.match(serverManager, /\.stop\.lock/);
  assert.match(serverManager, /ProcessStartedAtUtcTicks/);
  assert.match(serverManager, /ExecutablePath/);
  assert.match(serverManager, /process\.ProcessName, "node"/);
  assert.match(serverManager, /Stale or unknown processes were never terminated/);
  assert.match(serverManager, /readiness_status = exited \? "exited" : readinessUrl\.Length == 0 \? "not_configured"/);
  assert.match(serverManager, /ready = null/);
});

test('all published host package and Command Palette manifest versions agree at 3.1.0', () => {
  for (const project of [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj'
  ]) assert.match(read(project), /<Version>3\.1\.0<\/Version>/);
  assert.match(read('RunMind.CommandPalette/RunMind.CommandPalette.csproj'), /<AppxPackageVersion>3\.1\.0\.0<\/AppxPackageVersion>/);
  assert.match(read('RunMind.CommandPalette/Package.appxmanifest'), /Version="3\.1\.0\.0"/);
  assert.match(read('Community.PowerToys.Run.Plugin.RunMind/plugin.json'), /"Version":\s*"3\.1\.0"/);
});
