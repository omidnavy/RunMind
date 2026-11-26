'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const workflows = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/developer-workflows.js'), 'utf8');
const providers = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/providers.js'), 'utf8');
const tools = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/tools.js'), 'utf8');
const index = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/project-index.js'), 'utf8');
const app = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/application.js'), 'utf8');
const html = fs.readFileSync(path.join(root, 'Shared/ChatWindow.html'), 'utf8');

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
  assert.match(workflows, /fixed read-only Node\.js\/npm and JS\/TS tooling version\/discovery probes/);
  assert.match(workflows, /fresh approval through powershell_execute/);
  assert.match(workflows, /Never install, update, repair, format, or modify project files/);
});

test('Project diagnostics use the unified workspace and do not request the same folder twice', () => {
  assert.match(workflows, /one selected native workspace shared by Agent file access, Git and native developer tools/);
  assert.match(workflows, /never ask the user to choose the same folder twice/);
});

test('Playwright tool schema applies action-specific selector and value requirements', () => {
  const line = providers.split(/\r?\n/).find(value => value.includes("name: 'playwright_run'"));
  assert.ok(line);
  assert.match(line, /required: \['action','selector','value'\]/);
  assert.match(line, /ALWAYS include this string property/);
  assert.match(line, /Use a non-empty Playwright selector/);
  assert.match(line, /Use an empty string for goto/);
});


test('P1 exposes all five project-intelligence capabilities as registered read-only tools', () => {
  for (const name of ['project_understanding','project_diagnostics','project_mission_control','project_test_impact','project_release_readiness']) {
    assert.match(providers, new RegExp(`name: '${name}'`));
    assert.match(tools, new RegExp(`case '${name}'`));
    assert.match(tools, new RegExp(`'${name}'`));
  }
  assert.match(index, /function projectIndexBuildUnderstanding/);
  assert.match(index, /function projectIndexBuildDiagnostics/);
  assert.match(index, /function projectIndexAnalyzeTestImpact/);
  assert.match(index, /function projectIndexBuildReleaseReadiness/);
  assert.match(index, /function getProjectMissionControl/);
});

test('P1 workflows keep test selection and release preparation non-destructive', () => {
  assert.match(workflows, /id:'project-mission-control',name:'Project Mission Control'/);
  assert.match(workflows, /id:'test-impact-analysis',name:'Test Impact Analysis'/);
  assert.match(workflows, /id:'release-readiness',name:'Release Readiness'/);
  assert.match(workflows, /Do not execute tests automatically/);
  assert.match(workflows, /Never tag, commit, publish, install or modify release metadata unless explicitly instructed/);
});


test('Project Mission Control has a visible settings entry point and renders a structured safe dashboard', () => {
  assert.match(html, /id="runProjectMissionControl"/);
  assert.match(html, /id="projectMissionControl"/);
  assert.match(app, /getProjectMissionControl\(\{ changedPaths \}\)/);
  assert.match(app, /projectMissionControlElement\.replaceChildren\(\)/);
  assert.match(app, /node\.textContent = String\(text\)/);
});

test('Project Mission Control returns a versioned evidence model instead of counters alone', () => {
  const start = index.indexOf('async function getProjectMissionControl(');
  assert.notEqual(start, -1);
  const body = index.slice(start, index.indexOf('\n    let projectIndexAutoRefreshTimer', start));
  for (const field of ['schemaVersion: 2','generatedAt:','statusExplanation:','index: indexCompleteness','projectUnderstanding: architecture','diagnostics: { status: diagnostics.status','releaseReadiness: { status: readiness.status','testImpact: impact ?','nextActions,','scope:','verification:','limitations:']) assert.ok(body.includes(field), `Missing Mission Control field: ${field}`);
  assert.match(body, /staleAfterMs = 5 \* 60 \* 1000/);
  assert.match(body, /buildProjectIndex\(\{ force: false \}\)/);
  assert.match(body, /commandsExecuted: false/);
});

test('Mission Control exposes severity-sorted recommendations and distinguishes incomplete evidence', () => {
  const start = index.indexOf('async function getProjectMissionControl(');
  const body = index.slice(start, index.indexOf('\n    let projectIndexAutoRefreshTimer', start));
  assert.match(body, /priorityRank = \{ critical: 0, high: 1, medium: 2, low: 3 \}/);
  assert.match(body, /status = 'blocked'/);
  assert.match(body, /status = 'incomplete'/);
  assert.match(body, /status = 'attention'/);
  assert.match(body, /index\.stats/);
});

test('Mission Control settings accepts explicit changed paths and presents structured evidence safely', () => {
  assert.match(html, /id="projectMissionControlChangedPaths"/);
  assert.match(html, /one workspace-relative path per line/);
  assert.match(app, /rawPaths\.split\(\/\\r\?\\n\//);
  assert.match(app, /missionControlCheckList\(/);
  assert.match(app, /missionControlMetric\(/);
  assert.match(app, /projectMissionControlElement\.replaceChildren\(\)/);
  assert.match(app, /node\.textContent = String\(text\)/);
  assert.doesNotMatch(app.slice(app.indexOf('function renderProjectMissionControl'), app.indexOf('function projectIndexStatus')), /\.innerHTML\s*=/);
});

test('Mission Control clearly marks release checks and test impact as analysis rather than execution', () => {
  assert.match(app, /Suggested build\/test commands \(not executed\)/);
  assert.match(app, /No changed paths were supplied/);
  assert.match(app, /No test candidates were found for these changed paths/);
  assert.match(app, /Verification performed: static project analysis only/);
  assert.match(workflows, /never infer Git changes or claim that all affected tests are known/);
});
