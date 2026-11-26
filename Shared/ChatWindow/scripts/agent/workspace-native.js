(function (global) {
  'use strict';

  const CHUNK_BYTES = 256 * 1024;
  const MAX_FILE_BYTES = 100 * 1024 * 1024;
  const missing = () => {
    const error = new Error('The requested workspace entry does not exist.');
    error.name = 'NotFoundError';
    return error;
  };
  const invalid = message => {
    const error = new Error(message);
    error.name = 'TypeError';
    return error;
  };
  const safeName = value => {
    const name = String(value || '');
    if (!name || name === '.' || name === '..' || /[ .]$/.test(name) || /[\\/:\x00-\x1f]/.test(name) || name.length > 255 || /^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?$/i.test(name))
      throw invalid('Workspace entry names must be one safe path segment.');
    return name;
  };
  const join = (base, name) => base ? `${base}/${safeName(name)}` : safeName(name);
  const decodeBase64 = value => {
    const binary = atob(String(value || ''));
    const output = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) output[index] = binary.charCodeAt(index);
    return output;
  };
  const encodeBase64 = bytes => {
    let binary = '';
    for (let offset = 0; offset < bytes.length; offset += 0x8000)
      binary += String.fromCharCode(...bytes.subarray(offset, Math.min(bytes.length, offset + 0x8000)));
    return btoa(binary);
  };
  const toBytes = async value => {
    if (value instanceof Blob) return new Uint8Array(await value.arrayBuffer());
    if (typeof value === 'string') return new TextEncoder().encode(value);
    if (value instanceof ArrayBuffer) return new Uint8Array(value);
    if (ArrayBuffer.isView(value)) return new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
    if (value && typeof value === 'object' && Object.prototype.hasOwnProperty.call(value, 'data')) return toBytes(value.data);
    throw invalid('Workspace writes accept Blob, string, ArrayBuffer, or typed-array data.');
  };

  class NativeWorkspaceFileHandle {
    constructor(root, path, name, options = {}) {
      this.kind = 'file';
      this.name = name;
      this.path = path;
      this.root = root;
      this.createAllowed = options.create === true;
      this.__runMindNativeWorkspace = true;
    }
    async isSameEntry(other) {
      return !!other && other.__runMindNativeWorkspace === true && other.root?.workspaceId === this.root.workspaceId && other.path === this.path;
    }
    async getFile() {
      const contextId = this.root.contextId;
      const info = await callAgentHost('workspace.fs.stat', { contextId, workspaceId: this.root.workspaceId, path: this.path });
      if (!info?.exists || info.kind !== 'file') throw missing();
      if (info.size > MAX_FILE_BYTES) throw new Error(`Workspace file exceeds the ${Math.round(MAX_FILE_BYTES / 1024 / 1024)} MiB transfer limit.`);
      const chunks = [];
      let totalBytes = 0;
      for (let offset = 0; offset < info.size; offset += CHUNK_BYTES) {
        const expectedBytes = Math.min(CHUNK_BYTES, info.size - offset);
        const result = await callAgentHost('workspace.fs.readChunk', { contextId, workspaceId: this.root.workspaceId, path: this.path, offset, count: expectedBytes }, undefined, 30000);
        if (!result || result.offset !== offset || !Number.isInteger(result.bytesRead) || result.bytesRead !== expectedBytes)
          throw new Error('Native workspace returned a short, out-of-order, or inconsistent file chunk. The file may have changed while it was being read.');
        chunks.push(decodeBase64(result.data));
        totalBytes += result.bytesRead;
        if (result.eof !== (offset + result.bytesRead >= info.size)) throw new Error('Native workspace returned an inconsistent end-of-file marker.');
      }
      if (totalBytes !== info.size) throw new Error('Native workspace file transfer ended before all bytes were read.');
      const file = new File(chunks, this.name, { type: 'application/octet-stream', lastModified: info.lastModifiedMs || Date.now() });
      Object.defineProperty(file, '__runMindNativeWorkspacePath', { value: this.path, enumerable: false });
      return file;
    }
    async createWritable(options = {}) {
      if (!this.createAllowed) {
        const info = await callAgentHost('workspace.fs.stat', { contextId: this.root.contextId, workspaceId: this.root.workspaceId, path: this.path });
        if (!info?.exists) throw missing();
      }
      const contextId = this.root.contextId;
      const workspaceId = this.root.workspaceId;
      const session = await callAgentHost('workspace.fs.write.begin', {
        contextId, workspaceId, path: this.path, overwrite: options.overwrite === true
      }, undefined, 30000);
      let offset = 0;
      let closed = false;
      let aborted = false;
      return {
        async write(value) {
          if (closed || aborted) throw new Error('This workspace writer is already closed.');
          const bytes = await toBytes(value);
          if (offset + bytes.byteLength > MAX_FILE_BYTES) throw new Error('Workspace file writes are limited to 100 MiB.');
          for (let start = 0; start < bytes.byteLength; start += CHUNK_BYTES) {
            const chunk = bytes.subarray(start, Math.min(bytes.byteLength, start + CHUNK_BYTES));
            await callAgentHost('workspace.fs.write.chunk', {
              contextId, workspaceId,
              writeId: session.writeId, offset, data: encodeBase64(chunk)
            }, undefined, 30000);
            offset += chunk.byteLength;
          }
        },
        async close() {
          if (closed || aborted) return;
          await callAgentHost('workspace.fs.write.commit', { contextId, workspaceId, writeId: session.writeId }, undefined, 30000);
          closed = true;
        },
        async abort() {
          if (closed || aborted) return;
          aborted = true;
          try { await callAgentHost('workspace.fs.write.abort', { contextId, workspaceId, writeId: session.writeId }, undefined, 10000); } catch (_) {}
        }
      };
    }
  }

  class NativeWorkspaceDirectoryHandle {
    constructor(contextId, workspaceId, name, path = '') {
      this.kind = 'directory';
      this.name = name || 'workspace';
      this.path = path;
      this.contextId = contextId;
      this.workspaceId = workspaceId;
      this.__runMindNativeWorkspace = true;
    }
    async queryPermission() {
      const status = await callAgentHost('process.workspace.status', { contextId: this.contextId }, undefined, 10000);
      return status?.selected && status.workspaceId === this.workspaceId ? 'granted' : 'denied';
    }
    async requestPermission() { return this.queryPermission(); }
    async isSameEntry(other) {
      return !!other && other.__runMindNativeWorkspace === true && other.workspaceId === this.workspaceId && other.path === this.path;
    }
    async getDirectoryHandle(name, options = {}) {
      const path = join(this.path, name);
      const result = await callAgentHost('workspace.fs.stat', { contextId: this.contextId, workspaceId: this.workspaceId, path });
      if (!result?.exists) {
        if (options.create !== true) throw missing();
        await callAgentHost('workspace.fs.mkdir', { contextId: this.contextId, workspaceId: this.workspaceId, path });
      } else if (result.kind !== 'directory') {
        const error = new Error('A file already exists with this name.'); error.name = 'TypeMismatchError'; throw error;
      }
      return new NativeWorkspaceDirectoryHandle(this.contextId, this.workspaceId, name, path);
    }
    async getFileHandle(name, options = {}) {
      const path = join(this.path, name);
      const result = await callAgentHost('workspace.fs.stat', { contextId: this.contextId, workspaceId: this.workspaceId, path });
      if (result?.exists && result.kind !== 'file') {
        const error = new Error('A directory already exists with this name.'); error.name = 'TypeMismatchError'; throw error;
      }
      if (!result?.exists && options.create !== true) throw missing();
      return new NativeWorkspaceFileHandle(this, path, name, options);
    }
    async *entries() {
      let offset = 0;
      while (true) {
        const result = await callAgentHost('workspace.fs.list', { contextId: this.contextId, workspaceId: this.workspaceId, path: this.path, offset, limit: 200 }, undefined, 30000);
        for (const entry of Array.isArray(result?.entries) ? result.entries : []) {
          const handle = entry.kind === 'directory'
            ? new NativeWorkspaceDirectoryHandle(this.contextId, this.workspaceId, entry.name, join(this.path, entry.name))
            : new NativeWorkspaceFileHandle(this, join(this.path, entry.name), entry.name);
          yield [entry.name, handle];
        }
        offset += Array.isArray(result?.entries) ? result.entries.length : 0;
        if (!result?.hasMore || !result.entries?.length) break;
        if (offset >= 10000) throw new Error('Workspace directory listing exceeded the 10,000-entry safety limit.');
      }
    }
    async *values() { for await (const [, handle] of this.entries()) yield handle; }
    async *keys() { for await (const [name] of this.entries()) yield name; }
    async removeEntry(name, options = {}) {
      await callAgentHost('workspace.fs.remove', { contextId: this.contextId, workspaceId: this.workspaceId, path: join(this.path, name), recursive: options.recursive === true }, undefined, 30000);
    }
    async resolve(other) {
      if (!other || other.__runMindNativeWorkspace !== true || other.workspaceId !== this.workspaceId) return null;
      const base = this.path ? this.path.split('/') : [];
      const target = other.path ? other.path.split('/') : [];
      let common = 0;
      while (common < base.length && common < target.length && base[common] === target[common]) common++;
      return [...Array(base.length - common).fill('..'), ...target.slice(common)];
    }
  }

  function createNativeWorkspaceDirectoryHandle(contextId, status) {
    if (!/^[0-9a-f-]{36}$/i.test(String(contextId || ''))) throw new Error('Native workspace context ID is invalid.');
    if (!status?.selected || !/^[0-9a-f]{32}$/i.test(String(status.workspaceId || ''))) throw new Error('Native workspace status is incomplete.');
    return new NativeWorkspaceDirectoryHandle(String(contextId), String(status.workspaceId), String(status.folder || 'workspace'));
  }

  async function activateNativeWorkspace(status, { changed = false } = {}) {
    const handle = createNativeWorkspaceDirectoryHandle(workspaceContext.id, status);
    await persistAgentFolderHandle(handle);
    notifyWorkspaceHandleChanged(changed ? 'changed' : 'selected');
    return { granted: true, folder: handle.name, workspaceId: handle.workspaceId, alreadySelected: !changed, changed };
  }

  async function syncNativeWorkspaceAccess(signal) {
    const status = await callAgentHost('process.workspace.status', { contextId: workspaceContext.id }, signal, 10000);
    if (!status?.selected) return { selected: false };
    return { selected: true, ...(await activateNativeWorkspace(status)) };
  }

  global.createNativeWorkspaceDirectoryHandle = createNativeWorkspaceDirectoryHandle;
  global.activateNativeWorkspace = activateNativeWorkspace;
  global.syncNativeWorkspaceAccess = syncNativeWorkspaceAccess;
})(globalThis);
