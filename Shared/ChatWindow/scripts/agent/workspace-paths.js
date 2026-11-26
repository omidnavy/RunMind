(function (global, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else global.RunMindWorkspacePaths = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  function safeSegments(value) {
    const raw = String(value || '').replace(/\\/g, '/').trim();
    if (!raw) return [];
    if (raw.startsWith('/') || /^[a-z]:/i.test(raw) || raw.split('/').some(part => !part || part === '.' || part === '..'))
      throw new Error('A safe relative path is required. Absolute paths and . / .. segments are not allowed.');
    return raw.split('/');
  }

  async function walkDirectories(root, segments) {
    let directory = root;
    for (const segment of segments) directory = await directory.getDirectoryHandle(segment);
    return directory;
  }

  async function attempt(root, segments, kind) {
    if (kind === 'directory') {
      const directory = await walkDirectories(root, segments);
      return { kind, handle: directory, directory, name: segments.at(-1) || root.name || 'workspace', relativePath: segments.join('/') };
    }
    if (kind !== 'file' || segments.length === 0) throw new Error('A file path is required.');
    const directory = await walkDirectories(root, segments.slice(0, -1));
    const name = segments.at(-1);
    const handle = await directory.getFileHandle(name);
    return { kind, handle, directory, name, relativePath: segments.join('/') };
  }

  async function resolve(root, value, kind = 'file') {
    if (!root || typeof root.getDirectoryHandle !== 'function') throw new Error('The selected workspace folder is no longer available. Reconnect workspace access.');
    const segments = safeSegments(value);
    if (kind === 'directory' && segments.length === 0)
      return { kind, handle: root, directory: root, name: root.name || 'workspace', relativePath: '' };
    if (segments.length === 0) throw new Error('A file path is required.');

    let directError;
    try { return await attempt(root, segments, kind); }
    catch (error) {
      directError = error;
      if (error?.name !== 'NotFoundError') throw error;
    }

    // Models sometimes prefix paths with the selected workspace folder or a repository
    // parent. Prefer the direct path above; only try removing a matching workspace-name
    // segment after direct resolution reports that the path does not exist.
    const workspaceName = String(root.name || '').trim().toLocaleLowerCase();
    if (workspaceName) {
      for (let index = 0; index < segments.length; index++) {
        if (segments[index].toLocaleLowerCase() !== workspaceName) continue;
        const remainder = segments.slice(index + 1);
        if (kind === 'file' && remainder.length === 0) continue;
        try { return await attempt(root, remainder, kind); }
        catch (error) { if (error?.name !== 'NotFoundError') throw error; }
      }
    }
    throw directError;
  }

  return { resolve, safeSegments };
});
