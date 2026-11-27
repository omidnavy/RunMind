'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.resolve(__dirname, '..');
const source = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/toolsets.js'), 'utf8');

function createTool(name, description = name, group = '') {
  const tool = { type: 'function', function: { name, description, parameters: { type: 'object', properties: {} } } };
  if (group) Object.defineProperty(tool, '__runMindToolsetGroup', { value: group, enumerable: false });
  return tool;
}

function createContext() {
  const storage = new Map();
  const context = vm.createContext({
    localStorage: { getItem: key => storage.get(key) || null, setItem: (key, value) => storage.set(key, value) },
    systemManagerSettingsCache: { enabled: true },
    agentModeEnabled: true,
    agentDirectoryHandle: {},
    deepResearchEnabled: true,
    mcpToolRoutes: new Map([['mcp_server_tool', {}]]),
    activeToolDefinitions: [],
    document: { getElementById: () => null },
    window: { confirm: () => true },
    crypto: { randomUUID: () => '12345678-1234-1234-1234-123456789abc' },
    getAgentToolDefinitions: () => [
      createTool('current_datetime'), createTool('request_folder_access', 'request_folder_access', 'workspace'), createTool('read_file', 'read_file', 'workspace'), createTool('project_overview', 'project_overview', 'workspace'),
      createTool('system_manager_find_applications'), createTool('web_search'), createTool('create_docx', 'create_docx', 'workspace'),
      createTool('list_agent_skills')
    ],
    getMcpToolDefinitions: () => [createTool('mcp_server_tool')],
    researchToolDefinitions: () => [createTool('research_record_evidence')],
    rebuildActiveToolDefinitions: () => {}
  });
  vm.runInContext(source, context);
  return context;
}

test('toolsets classify generic RunMind capabilities without depending on provider or application names', () => {
  const context = createContext();
  context.getAgentToolsetCatalog();
  assert.equal(context.agentToolsetGroupForName('read_file'), 'workspace');
  assert.equal(context.agentToolsetGroupForName('request_folder_access'), 'workspace');
  assert.equal(context.agentToolsetGroupForName('project_overview'), 'workspace');
  assert.equal(context.agentToolsetGroupForName('system_manager_find_applications'), 'system-manager');
  assert.equal(context.agentToolsetGroupForName('mcp_server_tool'), 'integrations');
  assert.equal(context.agentToolsetGroupForName('research_record_evidence'), 'research');
  assert.equal(context.agentToolsetGroupForName('web_search'), 'web');
});

test('group preferences filter definitions and cannot be bypassed by an individual override', () => {
  const context = createContext();
  context.setAgentToolsetGroupPreference('workspace', false);
  context.setAgentToolPreference('read_file', true);
  const tools = [createTool('read_file', 'read_file', 'workspace'), createTool('current_datetime')];
  assert.deepEqual(Array.from(context.filterAgentToolDefinitions(tools), item => item.function.name), ['current_datetime']);
  assert.equal(context.isAgentToolEnabled('read_file'), false);
});

test('individual tool preferences filter only the selected tool', () => {
  const context = createContext();
  context.setAgentToolPreference('web_search', false);
  const tools = [createTool('web_search'), createTool('fetch_webpage'), createTool('current_datetime')];
  assert.deepEqual(Array.from(context.filterAgentToolDefinitions(tools), item => item.function.name), ['fetch_webpage', 'current_datetime']);
});

test('System Manager availability remains controlled by its existing master setting', () => {
  const context = createContext();
  context.systemManagerSettingsCache.enabled = false;
  assert.equal(context.isAgentToolEnabled('system_manager_find_applications'), false);
  context.systemManagerSettingsCache.enabled = true;
  assert.equal(context.isAgentToolEnabled('system_manager_find_applications'), true);
});

test('tool report distinguishes configured, available and actually exposed tools', () => {
  const context = createContext();
  context.setAgentToolPreference('web_search', false);
  const catalog = [createTool('web_search'), createTool('fetch_webpage'), createTool('read_file', 'read_file', 'workspace')];
  const exposed = [createTool('fetch_webpage')];
  const report = context.getAgentToolsetReport(catalog, exposed, [{ name: 'fetch_webpage', state: 'completed', message: 'Page fetched.' }]);
  const search = report.tools.find(item => item.name === 'web_search');
  const fetch = report.tools.find(item => item.name === 'fetch_webpage');
  assert.equal(search.configured, false);
  assert.equal(search.exposedNow, false);
  assert.equal(fetch.configured, true);
  assert.equal(fetch.available, true);
  assert.equal(fetch.exposedNow, true);
  assert.equal(fetch.executed, true);
  assert.equal(fetch.lastResult, 'completed');
  assert.equal(report.exposed, 1);
  assert.equal(report.executed, 1);
});

test('Agent Toolsets UI exposes editable presets, group controls and individual tool controls', () => {
  const html = fs.readFileSync(path.join(root, 'Shared/ChatWindow.html'), 'utf8');
  const application = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/application.js'), 'utf8');
  assert.match(html, /id="agentSettingsTabTools"/);
  assert.match(html, /id="agentSettingsPageTools"/);
  assert.match(html, /id="agentToolsetProfile"/);
  assert.match(html, /id="agentToolsetSaveProfile"/);
  assert.match(html, /id="agentToolsetList"/);
  assert.match(application, /initializeAgentToolsetsUi\(\)/);
  assert.match(source, /developer: \{ name: 'Developer'/);
  assert.match(source, /researcher: \{ name: 'Researcher'/);
  assert.match(source, /administrator: \{ name: 'System Administrator'/);
  assert.match(source, /control\.disabled = group\.id === 'system-manager'/);
});

test('Agent Toolsets remain provider-neutral and preserve the existing adapter-owned request pipeline', () => {
  const runtime = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/runtime.js'), 'utf8');
  const tools = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/tools.js'), 'utf8');
  const research = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/research.js'), 'utf8');
  assert.doesNotMatch(source, /LM Studio|Ollama|OpenAI/);
  assert.match(runtime, /filterAgentToolDefinitions\(definitions\)/);
  const providers = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/providers.js'), 'utf8');
  assert.match(providers, /__runMindToolsetGroup.*enumerable: false/);
  assert.match(runtime, /taskContext\.tools = toolsForTask/);
  assert.match(runtime, /tools: toolsForTask/);
  assert.match(tools, /isAgentToolEnabled\(name\)/);
  assert.match(tools, /was not exposed to this Agent task/);
  assert.match(research, /isAgentToolEnabled\(call\.name\)/);
  assert.match(research, /was not exposed to this Deep Research task/);
  assert.match(tools, /toolsets: report \?/);
  assert.match(tools, /offset, limit, count: tools\.length/);
  assert.match(tools, /lastResult/);
  assert.match(runtime, /state = cancelled \? 'cancelled' : unverified \? 'unverified'/);
  assert.match(runtime, /Application activation was verified; requested navigation was not verified/);
});


test('custom toolset profiles persist edits and can be deleted without touching other settings', () => {
  const context = createContext();
  context.setAgentToolsetGroupPreference('workspace', false);
  const profile = context.saveAgentToolsetProfile('Backend focus');
  assert.match(profile.id, /^custom-/);
  context.setAgentToolPreference('web_search', false);
  const settings = context.loadAgentToolsetSettings();
  const stored = settings.customProfiles.find(item => item.id === profile.id);
  assert.equal(settings.activeProfile, profile.id);
  assert.equal(stored.groupOverrides.workspace, false);
  assert.equal(stored.groupOverrides['system-manager'], true);
  assert.equal(stored.toolOverrides.web_search, false);
  assert.equal(context.deleteAgentToolsetProfile(), true);
  assert.equal(context.loadAgentToolsetSettings().activeProfile, 'general');
});


test('built-in profiles can hide System Manager tools but cannot enable its native master switch', () => {
  const context = createContext();
  assert.equal(context.isAgentToolEnabled('system_manager_find_applications'), true);
  context.applyAgentToolsetProfile('developer');
  assert.equal(context.isAgentToolEnabled('system_manager_find_applications'), false);
  context.systemManagerSettingsCache.enabled = false;
  context.applyAgentToolsetProfile('administrator');
  assert.equal(context.isAgentToolEnabled('system_manager_find_applications'), false);
});

test('custom profiles retain built-in profile restrictions when saved from a preset', () => {
  const context = createContext();
  context.applyAgentToolsetProfile('developer');
  assert.equal(context.isAgentToolEnabled('system_manager_find_applications'), false);
  const profile = context.saveAgentToolsetProfile('Developer custom');
  assert.equal(profile.groupOverrides['system-manager'], false);
  context.applyAgentToolsetProfile(profile.id);
  assert.equal(context.isAgentToolEnabled('system_manager_find_applications'), false);
});


test('workspace and research tools are rechecked against live runtime context', () => {
  const context = createContext();
  context.getAgentToolsetCatalog();
  assert.equal(context.isAgentToolEnabled('read_file'), true);
  assert.equal(context.isAgentToolEnabled('request_folder_access'), false);
  context.agentDirectoryHandle = null;
  assert.equal(context.isAgentToolEnabled('read_file'), false);
  assert.equal(context.isAgentToolEnabled('request_folder_access'), true);
  context.deepResearchEnabled = false;
  assert.equal(context.isAgentToolEnabled('research_record_evidence'), false);
  context.deepResearchEnabled = true;
  assert.equal(context.isAgentToolEnabled('research_record_evidence'), true);
});


test('tool execution reporting keeps accepted-but-unverified outcomes distinct from failures', () => {
  const context = createContext();
  const ledger = [
    { name: 'system_manager_launch_application', state: 'unverified', message: 'Window activation verified; navigation was not.' },
    { name: 'web_search', state: 'failed', message: 'Connection refused.' }
  ];
  const unverified = context.getAgentToolExecutionSummary('system_manager_launch_application', ledger);
  const failed = context.getAgentToolExecutionSummary('web_search', ledger);
  const untouched = context.getAgentToolExecutionSummary('fetch_webpage', ledger);
  assert.equal(unverified.executed, true);
  assert.equal(unverified.lastResult, 'unverified');
  const researchUnverified = context.getAgentToolExecutionSummary('research_record_evidence', [{ name: 'research_record_evidence', status: 'uncertain', resultError: 'The result could not be committed.' }]);
  assert.equal(researchUnverified.lastResult, 'unverified');
  assert.equal(failed.lastResult, 'failed');
  assert.equal(untouched.lastResult, 'not-executed');
});
