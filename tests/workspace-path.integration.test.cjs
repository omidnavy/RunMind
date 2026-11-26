'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { resolve, safeSegments } = require('../Shared/ChatWindow/scripts/agent/workspace-paths.js');

function notFound(message) { const error = new Error(message); error.name = 'NotFoundError'; return error; }
function directory(name, childDirectories = {}, files = {}) {
  return {
    name,
    async getDirectoryHandle(key) {
      if (!Object.hasOwn(childDirectories, key)) throw notFound(`Missing directory ${key}`);
      return childDirectories[key];
    },
    async getFileHandle(key) {
      if (!Object.hasOwn(files, key)) throw notFound(`Missing file ${key}`);
      return { kind: 'file', name: key, content: files[key] };
    }
  };
}

const project = directory('playwright-app', {}, { 'package.json': '{"name":"fixture"}', 'index.html': '<main></main>' });
const fixtureRoot = directory('test-fixtures', { 'playwright-app': project });

test('workspace resolver reads paths relative to the selected parent folder', async () => {
  const result = await resolve(fixtureRoot, 'playwright-app/package.json', 'file');
  assert.equal(result.relativePath, 'playwright-app/package.json');
  assert.equal(result.handle.content, '{"name":"fixture"}');
});

test('workspace resolver removes a duplicated selected-workspace prefix only after direct lookup fails', async () => {
  const parentResult = await resolve(fixtureRoot, 'test-fixtures/playwright-app/package.json', 'file');
  assert.equal(parentResult.relativePath, 'playwright-app/package.json');
  const projectResult = await resolve(project, 'test-fixtures/playwright-app/package.json', 'file');
  assert.equal(projectResult.relativePath, 'package.json');
  assert.equal(projectResult.handle.content, '{"name":"fixture"}');
});

test('workspace resolver preserves a real nested directory with the same name as its workspace', async () => {
  const duplicate = directory('test-fixtures', {}, { 'nested.txt': 'keep-real-nested-path' });
  const root = directory('test-fixtures', { 'test-fixtures': duplicate });
  const result = await resolve(root, 'test-fixtures/nested.txt', 'file');
  assert.equal(result.handle.content, 'keep-real-nested-path');
});

test('workspace resolver rejects absolute and traversal paths', () => {
  assert.throws(() => safeSegments('../outside.txt'), /safe relative path/);
  assert.throws(() => safeSegments('C:\\\\Windows\\\\win.ini'), /safe relative path/);
  assert.throws(() => safeSegments('/etc/passwd'), /safe relative path/);
});
