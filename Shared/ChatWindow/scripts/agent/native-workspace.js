'use strict';
(function () {
  const CHUNK_BYTES = 256 * 1024;
  const MAX_FILE_BYTES = 128 * 1024 * 1024;

  function safeName(value) {
    const name = String(value || '');
    if (!name || name === '.' || name === '..' || /[\\/:\0]/.test(name)) throw new TypeError('A single safe workspace entry name is required.');
    return name;
  }
  function joinPath(parent, name) { return parent ? `${parent}/${safeName(name)}` : safeName(name); }
  function domError(name, message) {
    try { return new DOMException(message, name); } catch (_) { const error = new Error(message); error.name = name; return error; }
  }
  function bytesToBase64(bytes) {
    let binary = '';
    for (let offset = 0; offset < bytes.length; offset += 0x8000) binary += String.fromCharCode(...bytes.subarray(offset, Math.min(offset + 0x8000, bytes.length)));
    return btoa(binary);
  }
  function base64ToBytes(value) {
    const binary = atob(String(value || ''));
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
    return bytes;
  }
  async function asBytes(value) {
    if (typeof value === 'string') return new TextEncoder().encode(value);
    if (value instanceof Blob) return new Uint8Array(await value.arrayBuffer());
    if (value instanceof ArrayBuffer) return new Uint8Array(value);
    if (ArrayBuffer.isView(value)) return new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
    if (value && typeof value === 'object' && value.type === 'write') return asBytes(value.data);
    throw new TypeError('Workspace writes support strings, Blob, ArrayBuffer and typed-array values.');
  }
  async function workspaceCall(contextId, action, values = {}) {
    if (typeof callAgentHost !== 'function') throw new Error('The native workspace bridge is unavailable.');
    return callAgentHost('workspace.fs', { contextId, selectionId: values.selectionId, action, ...values }, undefined, 120000);
  }

  class NativeDirectoryHandle {
    constructor(contextId, selectionId, relativePath, name) {
      this.contextId = contextId;
      this.selectionId = selectionId;
      this.relativePath = relativePath || '';
      this.name = name || 'workspace';
      this.kind = 'directory';
      this.isNativeWorkspace = true;
    }
    async queryPermission() { return 'granted'; }
    async requestPermission() { return 'granted'; }
    async isSameEntry(other) { return !!other?.isNativeWorkspace && other.contextId === this.contextId && other.selectionId === this.selectionId && other.relativePath === this.relativePath; }
    async getDirectoryHandle(name, options = {}) {
      const relativePath = joinPath(this.relativePath, name);
      let entry;
      try { entry = await workspaceCall(this.contextId, 'stat', { selectionId: this.selectionId, path: relativePath }); }
      catch (error) { if (error?.name !== 'NotFoundError') throw error; }
      if (!entry?.exists) {
        if (!options.create) throw domError('NotFoundError', `Directory '${relativePath}' does not exist.`);
        await workspaceCall(this.contextId, 'ensureDirectory', { selectionId: this.selectionId, path: relativePath });
        entry = { exists: true, kind: 'directory', name };
      }
      if (entry.kind !== 'directory') throw domError('TypeMismatchError', `'${relativePath}' is not a directory.`);
      return new NativeDirectoryHandle(this.contextId, this.selectionId, relativePath, entry.name || name);
    }
    async getFileHandle(name, options = {}) {
      const relativePath = joinPath(this.relativePath, name);
      let entry;
      try { entry = await workspaceCall(this.contextId, 'stat', { selectionId: this.selectionId, path: relativePath }); }
      catch (error) { if (error?.name !== 'NotFoundError') throw error; }
      if (!entry?.exists) {
        if (!options.create) throw domError('NotFoundError', `File '${relativePath}' does not exist.`);
        await workspaceCall(this.contextId, 'ensureFile', { selectionId: this.selectionId, path: relativePath });
        entry = { exists: true, kind: 'file', name };
      }
      if (entry.kind !== 'file') throw domError('TypeMismatchError', `'${relativePath}' is not a file.`);
      return new NativeFileHandle(this.contextId, this.selectionId, relativePath, entry.name || name);
    }
    async removeEntry(name, options = {}) {
      await workspaceCall(this.contextId, 'delete', { selectionId: this.selectionId, path: joinPath(this.relativePath, name), recursive: options.recursive === true });
    }
    async *entries() {
      const result = await workspaceCall(this.contextId, 'list', { selectionId: this.selectionId, path: this.relativePath });
      for (const item of result.entries || []) {
        if (!item || typeof item.name !== 'string') continue;
        const path = joinPath(this.relativePath, item.name);
        yield [item.name, item.kind === 'directory'
          ? new NativeDirectoryHandle(this.contextId, this.selectionId, path, item.name)
          : new NativeFileHandle(this.contextId, this.selectionId, path, item.name)];
      }
    }
    async *keys() { for await (const [name] of this.entries()) yield name; }
    async *values() { for await (const [, handle] of this.entries()) yield handle; }
  }

  class NativeFileHandle {
    constructor(contextId, selectionId, relativePath, name) {
      this.contextId = contextId;
      this.selectionId = selectionId;
      this.relativePath = relativePath;
      this.name = name;
      this.kind = 'file';
      this.isNativeWorkspace = true;
    }
    async queryPermission() { return 'granted'; }
    async requestPermission() { return 'granted'; }
    async isSameEntry(other) { return !!other?.isNativeWorkspace && other.contextId === this.contextId && other.selectionId === this.selectionId && other.relativePath === this.relativePath; }
    async getFile() {
      const stat = await workspaceCall(this.contextId, 'stat', { selectionId: this.selectionId, path: this.relativePath });
      if (!stat.exists || stat.kind !== 'file') throw domError('NotFoundError', `File '${this.relativePath}' does not exist.`);
      if (stat.size > MAX_FILE_BYTES) throw new Error(`File '${this.relativePath}' exceeds the native workspace read limit of 128 MB.`);
      const parts = [];
      let offset = 0;
      while (offset < stat.size) {
        const response = await workspaceCall(this.contextId, 'readChunk', { selectionId: this.selectionId, path: this.relativePath, offset, length: Math.min(CHUNK_BYTES, stat.size - offset), expectedLength: stat.size, expectedLastWriteTicks: stat.lastWriteTicks });
        const bytes = base64ToBytes(response.base64);
        if (!bytes.length && offset < stat.size) throw new Error(`Native workspace returned an empty chunk while reading '${this.relativePath}'.`);
        parts.push(bytes);
        offset += bytes.length;
      }
      const blob = new Blob(parts, { type: stat.contentType || 'application/octet-stream' });
      try { return new File([blob], this.name, { type: blob.type, lastModified: Math.floor(Number(stat.lastWriteTicks) / 10000 - 62135596800000) }); }
      catch (_) { Object.defineProperties(blob, { name: { value: this.name }, lastModified: { value: Date.now() } }); return blob; }
    }
    async createWritable(options = {}) {
      const contextId = this.contextId;
      const selectionId = this.selectionId;
      const start = await workspaceCall(contextId, 'writeBegin', { selectionId: this.selectionId, path: this.relativePath, keepExistingData: options.keepExistingData === true });
      let closed = false;
      let position = 0;
      const failIfClosed = () => { if (closed) throw new Error('The workspace writable stream is already closed.'); };
      const sendBytes = async (bytes, at) => {
        if (at + bytes.byteLength > MAX_FILE_BYTES) throw new Error('Workspace file writes are limited to 128 MB.');
        for (let offset = 0; offset < bytes.length; offset += CHUNK_BYTES) {
          const chunk = bytes.subarray(offset, Math.min(offset + CHUNK_BYTES, bytes.length));
          await workspaceCall(contextId, 'writeChunk', { selectionId, sessionId: start.sessionId, position: at + offset, base64: bytesToBase64(chunk) });
        }
      };
      const write = async value => {
        failIfClosed();
        if (value && typeof value === 'object' && value.type === 'seek') { position = Number(value.position); if (!Number.isSafeInteger(position) || position < 0 || position > MAX_FILE_BYTES) throw new TypeError('Invalid writable seek position.'); return; }
        if (value && typeof value === 'object' && value.type === 'truncate') { const size = Number(value.size); if (!Number.isSafeInteger(size) || size < 0 || size > MAX_FILE_BYTES) throw new TypeError('Invalid writable truncate size.'); await workspaceCall(contextId, 'writeTruncate', { selectionId, sessionId: start.sessionId, size }); position = Math.min(position, size); return; }
        let data = value;
        let at = position;
        if (value && typeof value === 'object' && value.type === 'write') { data = value.data; if (value.position != null) { at = Number(value.position); if (!Number.isSafeInteger(at) || at < 0 || at > MAX_FILE_BYTES) throw new TypeError('Invalid writable write position.'); } }
        const bytes = await asBytes(data);
        await sendBytes(bytes, at);
        position = at + bytes.byteLength;
      };
      return {
        write,
        async seek(offset) { failIfClosed(); const value = Number(offset); if (!Number.isSafeInteger(value) || value < 0 || value > MAX_FILE_BYTES) throw new TypeError('Invalid writable seek position.'); position = value; },
        async truncate(size) { failIfClosed(); const value = Number(size); if (!Number.isSafeInteger(value) || value < 0 || value > MAX_FILE_BYTES) throw new TypeError('Invalid writable truncate size.'); await workspaceCall(contextId, 'writeTruncate', { selectionId, sessionId: start.sessionId, size: value }); position = Math.min(position, value); },
        async close() { if (closed) return; await workspaceCall(contextId, 'writeCommit', { selectionId, sessionId: start.sessionId }); closed = true; },
        async abort() { if (closed) return; await workspaceCall(contextId, 'writeAbort', { selectionId, sessionId: start.sessionId }); closed = true; }
      };
    }
  }

  function createNativeWorkspaceHandle(contextId, folderName, selectionId) {
    if (!/^[0-9a-f-]{36}$/i.test(String(contextId || ''))) throw new TypeError('A valid workspace context ID is required.');
    if (!/^[0-9a-f]{32}$/i.test(String(selectionId || ''))) throw new TypeError('A valid workspace selection identity is required.');
    return new NativeDirectoryHandle(contextId, selectionId, '', String(folderName || 'workspace'));
  }
  async function activateNativeWorkspaceHandle(contextId, folderName, selectionId) {
    const handle = createNativeWorkspaceHandle(contextId, folderName, selectionId);
    await persistAgentFolderHandle(handle);
    return handle;
  }
  Object.assign(globalThis, { createNativeWorkspaceHandle, activateNativeWorkspaceHandle });
})();
