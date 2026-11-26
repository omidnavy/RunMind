'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadNativeWorkspaceAdapter(options = {}) {
  let selectedId = options.selectionId || '11111111111141118111111111111111';
  const calls = [];
  const files = new Map([['README.md', Buffer.from('hello')]]);
  let pendingWrite = null;
  const callAgentHost = async (operation, args) => {
    calls.push({ operation, args });
    assert.equal(operation, 'workspace.fs');
    if (args.selectionId !== selectedId) throw new Error('The selected workspace changed. Refresh the Agent workspace handle.');
    switch (args.action) {
      case 'stat': {
        const bytes = files.get(args.path);
        return bytes
          ? { exists: true, kind: 'file', name: path.posix.basename(args.path), size: bytes.length, lastWriteTicks: 638000000000000000, contentType: 'text/plain' }
          : { exists: false, kind: null, name: path.posix.basename(args.path), size: 0, lastWriteTicks: 0, contentType: 'application/octet-stream' };
      }
      case 'readChunk': {
        const bytes = files.get(args.path);
        return { offset: args.offset, bytesRead: bytes.length, totalLength: bytes.length, lastWriteTicks: args.expectedLastWriteTicks, base64: bytes.toString('base64'), isLast: true };
      }
      case 'list':
        return { entries: [{ name: 'README.md', kind: 'file', size: 5, lastWriteTicks: 638000000000000000 }, { name: 'src', kind: 'directory', size: 0, lastWriteTicks: 638000000000000000 }], truncated: false };
      case 'ensureDirectory': return { created: true, kind: 'directory', name: path.posix.basename(args.path) };
      case 'ensureFile': files.set(args.path, Buffer.alloc(0)); return { created: true, kind: 'file', name: path.posix.basename(args.path), size: 0 };
      case 'writeBegin': pendingWrite = { id: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', path: args.path, keepExistingData: args.keepExistingData, chunks: [] }; return { sessionId: pendingWrite.id, currentLength: args.keepExistingData ? (files.get(args.path)?.length || 0) : 0, maximumBytes: 128 * 1024 * 1024 };
      case 'writeChunk':
        assert.equal(args.sessionId, pendingWrite.id);
        pendingWrite.chunks.push({ position: args.position, bytes: Buffer.from(args.base64, 'base64') });
        return { written: Buffer.from(args.base64, 'base64').length };
      case 'writeTruncate': return { truncated: true, size: args.size };
      case 'writeCommit': {
        const length = pendingWrite.chunks.reduce((maximum, item) => Math.max(maximum, item.position + item.bytes.length), 0);
        const output = Buffer.alloc(length);
        for (const item of pendingWrite.chunks) item.bytes.copy(output, item.position);
        files.set(pendingWrite.path, output);
        const result = { written: true, path: pendingWrite.path, bytes: output.length };
        pendingWrite = null;
        return result;
      }
      case 'writeAbort': pendingWrite = null; return { aborted: true };
      case 'delete': files.delete(args.path); return { deleted: true, kind: 'file' };
      default: throw new Error(`Unexpected action ${args.action}`);
    }
  };
  const sandbox = {
    callAgentHost,
    TextEncoder,
    TextDecoder,
    Blob,
    File,
    ArrayBuffer,
    Uint8Array,
    DOMException,
    btoa,
    atob,
    console
  };
  sandbox.globalThis = sandbox;
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '..', 'Shared', 'ChatWindow', 'scripts', 'agent', 'native-workspace.js'), 'utf8'), sandbox, { timeout: 1000 });
  return { create: sandbox.createNativeWorkspaceHandle, calls, files, changeSelection(value) { selectedId = value; } };
}

const contextId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const selectionId = '11111111111141118111111111111111';

test('native workspace adapter implements directory enumeration and file reads through the host bridge', async () => {
  const adapter = loadNativeWorkspaceAdapter();
  const root = adapter.create(contextId, 'playwright-app', selectionId);
  assert.equal(root.kind, 'directory');
  assert.equal(await root.queryPermission({ mode: 'readwrite' }), 'granted');
  const entries = [];
  for await (const [name, handle] of root.entries()) entries.push([name, handle.kind]);
  assert.deepEqual(entries, [['README.md', 'file'], ['src', 'directory']]);
  const file = await root.getFileHandle('README.md');
  assert.equal(await (await file.getFile()).text(), 'hello');
  assert.equal(adapter.calls.filter(call => call.args.action === 'readChunk').length, 1);
});

test('native workspace adapter writes with bounded chunks and atomic host commit', async () => {
  const adapter = loadNativeWorkspaceAdapter();
  const root = adapter.create(contextId, 'playwright-app', selectionId);
  const file = await root.getFileHandle('output.txt', { create: true });
  const writer = await file.createWritable();
  await writer.write('RunMind P0');
  await writer.close();
  assert.equal(adapter.files.get('output.txt').toString('utf8'), 'RunMind P0');
  assert.ok(adapter.calls.some(call => call.args.action === 'writeBegin'));
  assert.ok(adapter.calls.some(call => call.args.action === 'writeCommit'));
});

test('native workspace adapter rejects traversal and stale selection identities', async () => {
  const adapter = loadNativeWorkspaceAdapter();
  const root = adapter.create(contextId, 'playwright-app', selectionId);
  await assert.rejects(() => root.getFileHandle('../outside.txt'), /single safe workspace entry name/);
  const file = await root.getFileHandle('README.md');
  adapter.changeSelection('22222222222242228222222222222222');
  await assert.rejects(() => file.getFile(), /workspace changed/);
});

test('native workspace adapter closes its writable session without exposing absolute paths', async () => {
  const adapter = loadNativeWorkspaceAdapter();
  const root = adapter.create(contextId, 'playwright-app', selectionId);
  const file = await root.getFileHandle('README.md');
  const writer = await file.createWritable({ keepExistingData: true });
  await writer.write('updated');
  await writer.close();
  const writeBegin = adapter.calls.find(call => call.args.action === 'writeBegin');
  assert.equal(writeBegin.args.keepExistingData, true);
  assert.ok(writeBegin.args.path === 'README.md');
  assert.equal(JSON.stringify(adapter.calls).includes('C:\\'), false);
});
