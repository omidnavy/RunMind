'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const bridge = read('Shared/AgentHostBridge.cs');
const workspaceFs = read('Shared/WorkspaceFileSystem.cs');
const nativeAdapter = read('Shared/ChatWindow/scripts/agent/native-workspace.js');
const workspaceCore = read('Shared/ChatWindow/scripts/core.js');
const workspaceMcp = read('Shared/ChatWindow/scripts/agent/mcp.js');
const workspaceTools = read('Shared/ChatWindow/scripts/agent/tools.js');
const runtime = read('Shared/ChatWindow/scripts/agent/runtime.js');
const projectIndex = read('Shared/ChatWindow/scripts/agent/project-index.js');
const serverManager = read('Shared/DevelopmentServerManager.cs');
const serverRegistry = read('Shared/DevelopmentServerRegistry.cs');
const providers = read('Shared/ChatWindow/scripts/providers.js');
const chatHtml = read('Shared/ChatWindow.html');
const playwright = read('Shared/PlaywrightRunner.cjs');

function hostProjectFiles() {
  return [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj'
  ].map(read);
}

test('native host owns one persisted workspace root per conversation with a stable selection identity', () => {
  assert.match(workspaceFs, /PersistedWorkspaceEntry/);
  assert.match(workspaceFs, /SelectionId/);
  assert.match(workspaceFs, /WorkspaceRegistryMutexName/);
  assert.match(workspaceFs, /File\.Move\(temporary, path, true\)/);
  assert.match(workspaceFs, /ReadPersistedWorkspaceEntry\(contextId\)/);
  assert.match(workspaceFs, /entry\.SelectionId/);
  assert.match(bridge, /"process\.selectWorkspace"/);
  assert.match(bridge, /"process\.workspace\.status"/);
  assert.match(bridge, /"workspace\.fs"/);
  assert.doesNotMatch(bridge, /_processWorkspaceRoot|_gitWorkspaceRoot/);
});

test('native workspace file bridge accepts only contained relative paths and rechecks selection during atomic writes', () => {
  assert.match(workspaceFs, /ResolveWorkspaceFileSystemPath/);
  assert.match(workspaceFs, /Path\.IsPathRooted\(raw\)/);
  assert.match(workspaceFs, /\.\./);
  assert.match(workspaceFs, /EnsureWorkspaceNoReparse/);
  assert.match(workspaceFs, /WorkspaceReadChunkBytes = 256 \* 1024/);
  assert.match(workspaceFs, /WorkspaceWriteChunkBytes = 256 \* 1024/);
  assert.match(workspaceFs, /WorkspaceFileLimitBytes = 128L \* 1024 \* 1024/);
  assert.match(workspaceFs, /The workspace changed while this file write was in progress/);
  assert.match(workspaceFs, /current\.SelectionId, session\.SelectionId/);
  assert.match(workspaceFs, /File\.Move\(session\.TemporaryPath, session\.TargetPath, true\)/);
});

test('native directory/file handles expose a relative File System Access-compatible adapter', () => {
  assert.match(nativeAdapter, /class NativeDirectoryHandle/);
  assert.match(nativeAdapter, /class NativeFileHandle/);
  assert.match(nativeAdapter, /workspaceCall\(this\.contextId, 'list'/);
  assert.match(nativeAdapter, /workspaceCall\(this\.contextId, 'readChunk'/);
  assert.match(nativeAdapter, /workspaceCall\(contextId, 'writeChunk'/);
  assert.match(nativeAdapter, /createNativeWorkspaceHandle/);
  assert.match(nativeAdapter, /selectionId/);
  assert.match(nativeAdapter, /CHUNK_BYTES = 256 \* 1024/);
  assert.doesNotMatch(nativeAdapter, /showDirectoryPicker|absolutePath|rootPath/);
});

test('native desktop restore and selection reuse the persisted workspace without a second browser folder picker', () => {
  assert.match(workspaceCore, /if \(nativeHost && typeof callAgentHost === 'function'/);
  assert.match(workspaceCore, /process\.workspace\.status/);
  assert.match(workspaceCore, /createNativeWorkspaceHandle\(contextId/);
  assert.match(workspaceCore, /Remove a legacy browser FileSystemDirectoryHandle/);
  assert.match(workspaceCore, /if \(handle\?\.isNativeWorkspace === true\)/);
  assert.match(workspaceMcp, /if \(status\?\.selected && status\.selectionId\)/);
  assert.match(workspaceMcp, /alreadySelected: true/);
  assert.match(workspaceMcp, /process\.selectWorkspace/);
  assert.match(workspaceMcp, /forceChange: forceChoose/);
  assert.match(workspaceMcp, /Could not verify the selected native workspace/);
  assert.match(workspaceMcp, /Fail closed rather than prompting the user to pick the same folder again/);
});

test('Agent task preflight refreshes the native workspace identity even when a handle is cached', () => {
  assert.match(runtime, /nativeWorkspaceHost \|\| !agentDirectoryHandle/);
  assert.match(runtime, /requestFolderAccess\(signal\)/);
  assert.match(workspaceMcp, /Another RunMind host may have changed the workspace for this conversation/);
  assert.match(workspaceMcp, /current\.selectionId === status\.selectionId/);
  assert.match(workspaceTools, /const ensureNativeWorkspace = async/);
  assert.match(workspaceTools, /process\.workspace\.status/);
});

test('all three Windows hosts compile the shared workspace and persistent server registry', () => {
  for (const project of hostProjectFiles()) {
    assert.match(project, /Shared\\WorkspaceFileSystem\.cs/);
    assert.match(project, /Shared\\DevelopmentServerRegistry\.cs/);
  }
  assert.match(read('Shared/ChatWindow.html'), /ChatWindow\/scripts\/agent\/native-workspace\.js/);
});

test('workspace settings UI describes one shared selection and no second native picker', () => {
  assert.match(chatHtml, /Select the project folder once for this conversation/);
  assert.match(chatHtml, /Agent file access, project indexing, Git, Playwright, API tests, development servers, debugging, PowerShell and native commands all reuse the same workspace/);
  assert.doesNotMatch(chatHtml, /must be granted separately in each host|uses a separate OS folder picker/);
});

test('file tools, Git, native commands, API, Playwright, servers and debugger share the selected native workspace', () => {
  for (const marker of [
    "if (name === 'powershell_execute') {\n        await ensureNativeWorkspace();",
    "if (name === 'run_native_command') {\n        await ensureNativeWorkspace();",
    "if (name === 'development_server_manage')",
    "if (name === 'playwright_run')",
    "if (name === 'api_test_run')",
    "if (name === 'node_debugger_launch') {\n        await ensureNativeWorkspace();",
    "if (name === 'node_debugger_attach') {\n        await ensureNativeWorkspace();",
    "if (gitRoutes[name]) {\n        await ensureNativeWorkspace();"
  ]) assert.ok(workspaceTools.includes(marker), marker);
  assert.match(workspaceTools, /The cloned repository is now the single shared workspace/);
  assert.match(bridge, /PersistWorkspaceRoot\(contextId, destination\)/);
});

test('project index rejects stale workspace selections and stays in the agreed JS/TS/JSX/HTML/CSS scope', () => {
  assert.match(projectIndex, /PROJECT_INDEX_PREFIX = 'runmind\.project-index\.v7\.'/);
  assert.match(projectIndex, /workspaceSelectionId/);
  assert.match(projectIndex, /value\.version===7/);
  assert.match(projectIndex, /const map = \{[^}]*'\.js':'javascript'[^}]*'\.jsx':'javascript'[^}]*'\.ts':'typescript'[^}]*'\.tsx':'typescript'[^}]*'\.html':'html'[^}]*'\.css':'css'/s);
  assert.match(projectIndex, /Other file extensions can still be searched as generic text, without language parsers/);
  assert.doesNotMatch(projectIndex, /'\.py':'python'|'\.cs':'csharp'|'\.rs':'rust'|'\.go':'go'/);
  const projectTools = read('Shared/ChatWindow/scripts/agent/tools.js');
  assert.doesNotMatch(projectTools, /path === 'go\.mod'|path === 'cargo\.toml'|path === 'pyproject\.toml'|test_\[\^\/\]\+\.py/);
});

test('development-server ownership is persisted with workspace and operating-system process identity', () => {
  for (const field of ['ContextId','WorkspaceSelectionId','ServerId','ProcessId','ProcessStartTimeUtcTicks','ExecutablePath','WorkingDirectory'])
    assert.match(serverRegistry, new RegExp(`public (?:int|long|string) ${field} \\{ get; set; \\}`));
  assert.match(serverRegistry, /DevelopmentServerRegistryMutexName/);
  assert.match(serverRegistry, /WriteDevelopmentServerRecordFile/);
  assert.match(serverRegistry, /VerifyDevelopmentServerIdentity/);
  assert.match(serverRegistry, /processId != record\.ProcessId/);
  assert.match(serverRegistry, /ProcessStartTimeUtcTicks/);
  assert.match(serverRegistry, /normalizedExecutable, expectedExecutable/);
});

test('server reconciliation adopts only exact matches and marks recovered logs unavailable', () => {
  assert.match(serverRegistry, /ReconcileDevelopmentServerSessions/);
  assert.match(serverRegistry, /IsDevelopmentServerRecordForWorkspace\(record, contextId, workspaceRoot, selectionId\)/);
  assert.match(serverRegistry, /if \(!identity\.Verified\)/);
  assert.match(serverRegistry, /Recovered = true/);
  assert.match(serverRegistry, /LogsAvailable = false/);
  assert.match(serverRegistry, /no process was adopted/i);
  assert.match(serverManager, /ReconcileDevelopmentServerSessions\(contextId, workspaceRoot, selectionId\)/);
});

test('server termination requires an atomic ownership claim and re-verifies the original process', () => {
  assert.match(serverRegistry, /TryClaimDevelopmentServerStop/);
  assert.match(serverRegistry, /WithDevelopmentServerRegistryLock\(\(\) =>/);
  assert.match(serverRegistry, /VerifyDevelopmentServerIdentity\(session\.Process, record\)/);
  assert.match(serverRegistry, /StopRequestedAtUtc/);
  assert.match(serverManager, /TryClaimDevelopmentServerStop\(session, out var verifiedProcess, out var reason\)/);
  assert.match(serverManager, /verifiedProcess\.Kill\(true\)/);
  assert.match(workspaceTools, /Stop this RunMind development server/);
});

test('port conflict resolution directs the Agent to the narrow read-only port owner tool', () => {
  assert.match(providers, /addSystemTool\('system_manager_find_port_owner'/);
  assert.match(providers, /do not call broad system_manager_diagnostics for a port-specific question/);
  assert.match(runtime, /For a specific listener\/port conflict use system_manager_find_port_owner/);
  assert.match(runtime, /Do not call system_manager_diagnostics, powershell, or broad process inventory for a port-specific question/);
  assert.match(workspaceTools, /callAgentHost\('systemManager\.port\.owner'/);
});

test('all attempted tool calls are audited so a silent model cannot produce an empty final response', () => {
  assert.match(runtime, /Count every attempted tool call/);
  assert.match(runtime, /executedAnyTool = true/);
  assert.match(runtime, /renderToolExecutionAudit/);
  assert.match(runtime, /The model did not provide a final narrative/);
  assert.match(runtime, /Any requested actions not represented by confirmed tool results remain unverified/);
  assert.match(runtime, /configured tool-step limit reached/);
});

test('Playwright preserves raw CORS policy events and separates expected policy blocks from application/network failures', () => {
  assert.match(playwright, /expected_cors_origins/);
  assert.match(playwright, /browserPolicyBlocks/);
  assert.match(playwright, /consoleEvents/);
  assert.match(playwright, /expectedCorsBlockCount/);
  assert.match(playwright, /undeclaredCorsBlockCount/);
  assert.match(playwright, /unexpectedNetworkFailureCount/);
  assert.match(playwright, /applicationHttpErrorCount/);
  assert.match(playwright, /classification: 'browser_security_policy'/);
  assert.match(playwright, /applicationFailure: false/);
});
