 'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');

test('P3 native infrastructure bridge is compiled and its runners are bundled by all three hosts', () => {
  const projects = [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj'
  ];
  for (const project of projects) {
    const xml = read(project);
    assert.match(xml, /Compile Include="\.\.\\Shared\\DeveloperInfrastructure\.cs"/i, project);
    assert.match(xml, /Content Include="\.\.\\Shared\\SqliteRunner\.cjs"/i, project);
    assert.match(xml, /Content Include="\.\.\\Shared\\OpenApiTestRunner\.cjs"/i, project);
    assert.match(xml, /<Version>3\.4\.1<\/Version>/, project);
  }
  const palette = read(projects[2]);
  assert.match(palette, /<AppxPackageVersion>3\.4\.1\.0<\/AppxPackageVersion>/);
  const bridge = read('Shared/AgentHostBridge.cs');
  for (const route of ['containers.workbench','sqlite.run','openapi.test.run']) assert.ok(bridge.includes(route), `Missing native route ${route}`);
});

test('P3 container operations use a finite allow-list and exact container identities', () => {
  const native = read('Shared/DeveloperInfrastructure.cs');
  const agent = read('Shared/ChatWindow/scripts/agent/tools.js');
  const providers = read('Shared/ChatWindow/scripts/providers.js');
  for (const action of ['container_inspect','container_logs','container_stats','container_start','container_stop','container_restart','compose_config','compose_ps','compose_up','compose_down','compose_build','compose_logs']) {
    assert.ok(native.includes(`"${action}"`), `Native action missing: ${action}`);
    assert.ok(agent.includes(`'${action}'`), `Agent action missing: ${action}`);
    assert.ok(providers.includes(`'${action}'`), `Provider schema action missing: ${action}`);
  }
  assert.match(native, /UseShellExecute\s*=\s*false/);
  assert.match(native, /ArgumentList\.Add\(argument\)/);
  assert.match(native, /container_id must be one exact container ID or name/);
  assert.match(agent, /alwaysAsk:\s*true/);
  assert.match(native, /compose_down.*down/s);
  assert.doesNotMatch(native, /remove-orphans|volume.*prune|system.*prune/i);
});

test('P3 SQLite bridge is SQLite-only and guards writes separately', () => {
  const runner = read('Shared/SqliteRunner.cjs');
  const agent = read('Shared/ChatWindow/scripts/agent/tools.js');
  const provider = read('Shared/ChatWindow/scripts/providers.js');
  assert.match(runner, /node:sqlite/);
  assert.match(runner, /DatabaseSync/);
  assert.match(runner, /readOnly:\s*request\.action !== 'execute'/);
  assert.match(runner, /ATTACH|DETACH/);
  assert.match(agent, /action === 'execute'/);
  assert.match(agent, /alwaysAsk:\s*action === 'execute'/);
  assert.match(provider, /SQLite/);
  assert.doesNotMatch(provider, /mysql_workbench|postgres_workbench/i);
});

test('P3 OpenAPI contract runner uses status-specific response schemas and explicit base URL', () => {
  const runner = read('Shared/OpenApiTestRunner.cjs');
  const api = read('Shared/ApiTestRunner.cjs');
  const agent = read('Shared/ChatWindow/scripts/agent/tools.js');
  assert.match(runner, /schemasByStatus/);
  assert.match(runner, /Only local OpenAPI references are supported/);
  assert.match(runner, /baseUrl/);
  assert.match(api, /expect\.responseSchemas/);
  assert.match(api, /responseSchemas\?\.\[String\(response\.status\)\]/);
  assert.match(agent, /const includeMutations = args\.include_mutations === true/);
  assert.match(agent, /alwaysAsk:\s*true/);
});

test('P3 runtime correlation sanitizes relative URL query credentials and log messages', () => {
  const agent = read('Shared/ChatWindow/scripts/agent/tools.js');
  assert.match(agent, /runmind-relative\.invalid/);
  assert.match(agent, /safeText = \(match\?\.\[3\] \|\| text\)/);
  assert.match(agent, /const safeOutput = output/);
  assert.match(agent, /message: safeOutput/);
  assert.match(agent, /addTokens\(`node-\$\{stream\}`, safeOutput, id\)/);
  assert.match(agent, /supplied-source-time/);
  assert.match(agent, /captured-at-read-not-source/);
  assert.match(agent, /Matching IDs|correlation identifiers/);
});

test('P3 advanced Playwright diagnostics include console, network timing and opt-in trace artifacts', () => {
  const runner = read('Shared/PlaywrightRunner.cjs');
  const agent = read('Shared/ChatWindow/scripts/agent/tools.js');
  const providers = read('Shared/ChatWindow/scripts/providers.js');
  assert.match(runner, /request\(\)\.timing\(\)/);
  assert.match(runner, /inspect_console/);
  assert.match(runner, /inspect_network/);
  assert.match(runner, /context\.tracing\.start/);
  assert.match(agent, /capture_trace/);
  assert.match(providers, /capture_trace/);
  assert.match(providers, /page exceptions/);
});

test('P3 developer workflows expose all five infrastructure workflows', () => {
  const workflows = read('Shared/ChatWindow/scripts/agent/developer-workflows.js');
  for (const id of ['container-development-workbench','sqlite-database-workbench','openapi-contract-testing','runtime-log-correlation','advanced-browser-debugging']) {
    assert.ok(workflows.includes(`id:'${id}'`), `Missing built-in workflow ${id}`);
  }
});
