const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('SVG is indexed as a first-class asset and local references are surfaced to Mission Control', () => {
  const index = read('Shared/ChatWindow/scripts/agent/project-index.js');
  const dashboard = read('Shared/ChatWindow/scripts/application.js');
  assert.match(index, /\.svg/);
  assert.match(index, /'\.svg':'svg'/);
  assert.match(index, /HTML SVG asset reference/);
  assert.match(index, /CSS SVG asset reference/);
  assert.match(index, /SVG local href reference/);
  assert.match(index, /xlink:href/);
  assert.match(index, /assetReferences/);
  assert.match(dashboard, /coverage\.svgFiles/);
  assert.match(dashboard, /architecture\.assetReferences/);
});

test('SVG may be attached as bounded text, never promoted to an active browser image attachment', () => {
  const html = read('Shared/ChatWindow.html');
  const core = read('Shared/ChatWindow/scripts/core.js');
  const ui = read('Shared/ChatWindow/scripts/chat-ui.js');
  assert.match(html, /image\/svg\+xml/);
  assert.match(html, /\.svg/);
  assert.match(core, /'svg'/);
  assert.match(ui, /declaredType\.startsWith\('image\/'\) && declaredType !== 'image\/svg\+xml'/);
  assert.match(ui, /SUPPORTED_TEXT_EXTENSIONS\.has\(extension\)/);
});
