'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const workflows = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/developer-workflows.js'), 'utf8');
const providers = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/providers.js'), 'utf8');

test('Project Understanding is a distinct architecture/code intelligence workflow', () => {
  assert.match(workflows, /id:'understand-project',name:'Project Understanding'/);
  assert.match(workflows, /project_index_overview/);
  assert.match(workflows, /project_symbol_graph/);
  assert.match(workflows, /actual source files/);
  assert.match(workflows, /Do not execute commands or modify files/);
});

test('Project Diagnostics is a separate operational readiness workflow', () => {
  assert.match(workflows, /id:'diagnose-project',name:'Project Diagnostics'/);
  assert.match(workflows, /project_validate/);
  assert.match(workflows, /malformed manifests/);
  assert.match(workflows, /conflicting or ambiguous lockfiles/);
  assert.match(workflows, /fixed read-only version\/discovery probes/);
  assert.match(workflows, /fresh approval through powershell_execute/);
  assert.match(workflows, /Never install, update, repair, format, or modify project files/);
});

test('Project diagnostics reuse one shared workspace and avoid duplicate folder selection', () => {
  assert.match(workflows, /same conversation-scoped workspace identity/);
  assert.match(workflows, /Never ask the user to select the same folder a second time/);
  assert.match(workflows, /complete static diagnostics first/);
});

test('Playwright tool schema applies action-specific selector and value requirements', () => {
  const line = providers.split(/\r?\n/).find(value => value.includes("name: 'playwright_run'"));
  assert.ok(line);
  assert.match(line, /required: \['action','selector','value'\]/);
  assert.match(line, /Every action object must include action, selector, and value/);
  assert.match(line, /RunMind performs action-specific preflight validation/);
});
