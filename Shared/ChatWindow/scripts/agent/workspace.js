    'use strict';

    function suggestedPageFilename(target, contentType, fallback = 'download') {
      const mime = String(contentType || '').toLowerCase();
      let extension = '.bin';
      if (mime.includes('html') || mime.includes('xhtml')) extension = '.html';
      else if (mime.includes('json')) extension = '.json';
      else if (mime.includes('markdown')) extension = '.md';
      else if (mime.startsWith('text/plain')) extension = '.txt';
      else if (mime.includes('pdf')) extension = '.pdf';
      else if (mime.includes('xml')) extension = '.xml';
      else if (mime.includes('zip')) extension = '.zip';
      else if (mime.includes('gzip')) extension = '.gz';
      else if (mime.includes('png')) extension = '.png';
      else if (mime.includes('jpeg')) extension = '.jpg';
      else if (mime.includes('webp')) extension = '.webp';
      else if (mime.includes('svg')) extension = '.svg';
      let base = '';
      try {
        const last = decodeURIComponent(target.pathname.split('/').filter(Boolean).pop() || '');
        base = last && last.includes('.') ? last : (last || target.hostname || fallback);
      } catch (_) { base = target.hostname || fallback; }
      base = filenameSafe(base || fallback);
      if (!/\.[a-z0-9]{1,10}$/i.test(base)) base += extension;
      return base;
    }
    async function makeWebpageDownload(target, signal, requestedFilename, allowReaderFallback = true) {
      let blob = null;
      let defaultName = '';
      let sourceDescription = '';
      let directError = null;
      const originalTarget = new URL(target.href);
      const secureTarget = new URL(target.href);
      if (secureTarget.protocol === 'http:') secureTarget.protocol = 'https:';
      const candidates = secureTarget.href === originalTarget.href ? [secureTarget] : [secureTarget, originalTarget];

      for (const candidate of candidates) {
        try {
          if (signal?.aborted) throw new DOMException('Download cancelled.', 'AbortError');
          const response = await fetch(candidate.href, { signal, headers: { Accept: '*/*' } });
          if (!response.ok) {
            try { await response.body?.cancel(); } catch (_) {}
            throw new Error(`Resource returned HTTP ${response.status}.`);
          }
          const contentType = response.headers.get('content-type') || 'application/octet-stream';
          blob = await readBoundedResponseBlob(response, 10 * 1024 * 1024, contentType);
          let finalTarget = candidate;
          try { if (response.url) finalTarget = new URL(response.url); } catch (_) {}
          defaultName = suggestedPageFilename(finalTarget, contentType);
          sourceDescription = `${contentType} · direct browser fetch`;
          break;
        } catch (error) {
          if (signal?.aborted || error?.name === 'AbortError') throw new DOMException('Download cancelled.', 'AbortError');
          if (/download exceeds the 10 MB size limit/i.test(String(error?.message || error))) throw error;
          directError = error;
        }
      }

      if (!blob) {
        // Reader services return extracted text/Markdown, not original binary bytes.
        if (!allowReaderFallback) {
          throw new Error(`Direct file download failed (${directError?.message || directError || 'the server did not return a readable response'}). Reader fallback was not used because it cannot preserve the original file bytes. Check the URL, browser CORS policy, and 10 MB size limit.`);
        }
        const page = await fetchReadableWebPage(target.href, signal, { skipDirect: true, directError: directError || new Error('Direct browser fetch did not return the page.') });
        if (!page.content) throw new Error('The Reader returned no page content; nothing was saved.');
        const markdown = `# ${page.title || target.hostname}\n\nSource: ${page.url || target.href}\n\n${page.content}\n`;
        blob = new Blob([markdown], { type: 'text/markdown;charset=utf-8' });
        defaultName = `${filenameSafe(page.title || target.hostname)}.md`;
        sourceDescription = `${page.retrieval || 'Reader fallback'} · Markdown page snapshot`;
      }

      const requested = String(requestedFilename || '').trim();
      let name = requested ? filenameSafe(requested) : defaultName;
      if (!/\.[a-z0-9]{1,10}$/i.test(name)) name += /\.[a-z0-9]{1,10}$/i.exec(defaultName)?.[0] || '.bin';
      return { blob, filename: name, source: sourceDescription, bytes: blob.size };
    }

    function safeRelativePath(value) {
      const raw = String(value || '').replace(/\\/g, '/').trim();
      if (!raw || raw.startsWith('/') || /^[a-z]:/i.test(raw) || raw.split('/').some(part => !part || part === '.' || part === '..')) throw new Error('A safe relative path is required. Absolute paths and . / .. segments are not allowed.');
      return raw.split('/');
    }
    async function getDirectoryAtPath(root, relativePath) {
      let handle = root;
      if (relativePath) for (const part of safeRelativePath(relativePath)) handle = await handle.getDirectoryHandle(part);
      return handle;
    }
    async function getFileAtPath(root, relativePath) {
      const parts = safeRelativePath(relativePath); const name = parts.pop();
      const dir = await getDirectoryAtPath(root, parts.join('/'));
      return { dir, name, handle: await dir.getFileHandle(name) };
    }
    function findConversationAttachment(path) {
      const requested = String(path || '').trim();
      if (!requested || requested.includes('/') || requested.includes('\\')) return null;
      const matches = [];
      for (let index = messages.length - 1; index >= 0; index--) {
        const message = messages[index];
        if (message?.role !== 'user' || !Array.isArray(message.attachments)) continue;
        matches.push(...message.attachments.filter(file =>
          (typeof file?.text === 'string' || typeof file?.dataUrl === 'string')
          && String(file?.name || '').toLowerCase() === requested.toLowerCase()
        ));
      }
      if (matches.length > 1) throw new Error(`More than one conversation attachment is named '${requested}'. Use a unique filename so the correct file can be selected.`);
      return matches[0] || null;
    }
    function fileFromConversationAttachment(attachment) {
      if (typeof attachment.text === 'string') return new File([attachment.text], attachment.name, { type: attachment.type || 'text/plain' });
      const match = String(attachment.dataUrl || '').match(/^data:(application\/pdf|application\/zip);base64,([a-z0-9+/]+=*)$/i);
      if (!match) throw new Error(`Attachment '${attachment.name}' does not contain readable text or supported PDF/ZIP bytes.`);
      const binary = atob(match[2]); const bytes = new Uint8Array(binary.length);
      for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
      return new File([bytes], attachment.name, { type: match[1].toLowerCase() });
    }
    async function readTaskFile(path, maxBytes = 2 * 1024 * 1024) {
      const attachment = findConversationAttachment(path);
      if (attachment) {
        const file = fileFromConversationAttachment(attachment);
        if (file.size > maxBytes) throw new Error(`Attached file exceeds the ${maxBytes.toLocaleString()} byte read limit.`);
        return { file, handle: null, source: 'attachment' };
      }
      if (!agentDirectoryHandle) throw new Error(`'${path}' is not an attached filename, and no task folder is selected. Attach the file or select a workspace folder.`);
      const { handle } = await getFileAtPath(agentDirectoryHandle, path);
      const file = await handle.getFile();
      if (file.size > maxBytes) throw new Error(`File exceeds the ${maxBytes.toLocaleString()} byte read limit.`);
      return { file, handle, source: 'workspace' };
    }
    async function readWorkspaceText(path, maxBytes = 2 * 1024 * 1024) {
      const result = await readTaskFile(path, maxBytes);
      if (['application/pdf', 'application/zip'].includes(String(result.file.type || '').toLowerCase())) throw new Error(`'${path}' is a binary attachment. Use the PDF or ZIP tool for this file.`);
      return { ...result, text: await result.file.text() };
    }
    async function workspaceFilesMatchSnapshot(expected, current) {
      if (!expected || !current || expected.size !== current.size || expected.lastModified !== current.lastModified || String(expected.type || '') !== String(current.type || '')) return false;
      const chunkSize = 1024 * 1024;
      for (let offset = 0; offset < expected.size; offset += chunkSize) {
        const [expectedBytes, currentBytes] = await Promise.all([
          expected.slice(offset, Math.min(offset + chunkSize, expected.size)).arrayBuffer(),
          current.slice(offset, Math.min(offset + chunkSize, current.size)).arrayBuffer()
        ]);
        const a = new Uint8Array(expectedBytes), b = new Uint8Array(currentBytes);
        if (a.length !== b.length) return false;
        for (let index = 0; index < a.length; index++) if (a[index] !== b[index]) return false;
      }
      return true;
    }
    const workspaceFileMutationLocks = new Map();
    async function withWorkspaceFileMutationLock(path, operation) {
      const key = safeRelativePath(path).join('/').toLowerCase();
      const previous = workspaceFileMutationLocks.get(key) || Promise.resolve();
      let release;
      const current = new Promise(resolve => { release = resolve; });
      workspaceFileMutationLocks.set(key, current);
      await previous;
      try { return await operation(); }
      finally {
        if (workspaceFileMutationLocks.get(key) === current) workspaceFileMutationLocks.delete(key);
        release();
      }
    }
    async function writeWorkspaceFileCore(path, blob, overwrite = false) {
      const parts = safeRelativePath(path); const name = parts.pop();
      const dir = await getDirectoryAtPath(agentDirectoryHandle, parts.join('/'));
      if (!overwrite) {
        // App-level path locks serialize RunMind mutations. This final existence
        // check narrows (but cannot atomically eliminate) races with external writers.
        try { await dir.getFileHandle(name, { create: false }); throw new Error(`File '${path}' already exists; overwrite is disabled.`); }
        catch (error) { if (error?.name !== 'NotFoundError') throw error; }
      }
      const handle = await dir.getFileHandle(name, { create: true });
      const writable = await handle.createWritable({ keepExistingData: false });
      try { await writable.write(blob); await writable.close(); }
      catch (error) { try { await writable.abort(); } catch (_) {} throw error; }
      return { path, bytes: blob.size };
    }
    async function withWorkspaceFileMutationLocks(paths, operation) {
      const keys = [...new Set(paths.map(path => safeRelativePath(path).join('/').toLowerCase()))].sort();
      const held = [];
      try {
        for (const key of keys) {
          const previous = workspaceFileMutationLocks.get(key) || Promise.resolve();
          let release;
          const current = new Promise(resolve => { release = resolve; });
          workspaceFileMutationLocks.set(key, current);
          held.push({ key, current, release });
          await previous;
        }
        return await operation();
      } finally {
        for (const entry of held.reverse()) {
          if (workspaceFileMutationLocks.get(entry.key) === entry.current) workspaceFileMutationLocks.delete(entry.key);
          entry.release();
        }
      }
    }
    async function writeWorkspaceFile(path, blob, overwrite = false) {
      return await withWorkspaceFileMutationLock(path, () => writeWorkspaceFileCore(path, blob, overwrite));
    }
    const WORKSPACE_BACKUP_MAX_FILES = 20;
    const WORKSPACE_BACKUP_MAX_BYTES = 500 * 1024 * 1024;
    let lastWorkspaceBackupTimestamp = 0;
    async function pruneWorkspaceBackups(directory) {
      const backups = [];
      let directoryEntriesScanned = 0;
      for await (const [name, handle] of directory.entries()) {
        if (directoryEntriesScanned >= 10000) return { warning: 'Backup retention inventory exceeded the 10,000-entry safety budget; no backups were deleted.', inventoryTruncated: true };
        directoryEntriesScanned++;
        if (handle.kind !== 'file' || !name.toLowerCase().endsWith('.bak')) continue;
        try {
          const file = await handle.getFile();
          const createdAt = Number(name.match(/^(\d+)-/)?.[1]) || 0;
          backups.push({ name, size: file.size, modifiedAt: file.lastModified || 0, createdAt });
        } catch (error) {
          return { warning: `Backup retention could not inspect '${name}': ${error?.message || String(error)}; no backups were deleted.`, inventoryIncomplete: true };
        }
      }
      backups.sort((a, b) => b.createdAt - a.createdAt || b.modifiedAt - a.modifiedAt || b.name.localeCompare(a.name));
      let totalBytes = backups.reduce((total, item) => total + item.size, 0);
      let remainingFiles = backups.length, deletedFiles = 0, deletedBytes = 0, warning = '';
      for (const backup of backups.slice().reverse()) {
        if (remainingFiles <= WORKSPACE_BACKUP_MAX_FILES && totalBytes <= WORKSPACE_BACKUP_MAX_BYTES) break;
        try {
          await directory.removeEntry(backup.name);
          remainingFiles--; totalBytes -= backup.size; deletedFiles++; deletedBytes += backup.size;
        } catch (error) {
          warning = `Could not remove older backup '${backup.name}': ${error?.message || String(error)}`;
          break;
        }
      }
      return { retainedFiles: remainingFiles, retainedBytes: totalBytes, deletedFiles, deletedBytes, limitFiles: WORKSPACE_BACKUP_MAX_FILES, limitBytes: WORKSPACE_BACKUP_MAX_BYTES, ...(warning ? { warning } : {}) };
    }
    async function createWorkspaceBackup(path, file, operation) {
      if (!agentDirectoryHandle) throw new Error('No task folder selected.');
      if (file.size > 100 * 1024 * 1024) throw new Error('Automatic backups are limited to 100 MB per file; the original file was not changed.');
      const directory = await agentDirectoryHandle.getDirectoryHandle('.runmind-backups', { create: true });
      const id = typeof crypto.randomUUID === 'function' ? crypto.randomUUID() : Math.random().toString(36).slice(2, 10);
      const backupTimestamp = Math.max(Date.now(), lastWorkspaceBackupTimestamp + 1);
      lastWorkspaceBackupTimestamp = backupTimestamp;
      const name = `${backupTimestamp}-${id}.bak`;
      const handle = await directory.getFileHandle(name, { create: true });
      const writable = await handle.createWritable({ keepExistingData: false });
      try { await writable.write(file); await writable.close(); }
      catch (error) { try { await writable.abort(); } catch (_) {} throw error; }
      let retention = null;
      try { retention = await pruneWorkspaceBackups(directory); }
      catch (error) { retention = { warning: `Backup retention cleanup could not finish: ${error?.message || String(error)}` }; }
      return { backupPath: `.runmind-backups/${name}`, originalPath: path, bytes: file.size, mimeType: file.type || 'application/octet-stream', operation, createdAt: new Date(backupTimestamp).toISOString(), retention };
    }
    function normalizeCsvDelimiter(delimiter = ',') {
      const value = String(delimiter ?? ',');
      if (value.length !== 1 || value === '"' || value === '\r' || value === '\n') throw new Error('CSV delimiter must be one character other than a quote or newline.');
      return value;
    }
    function parseCsvRows(source, delimiter = ',') {
      delimiter = normalizeCsvDelimiter(delimiter);
      const text = String(source || '').replace(/^\uFEFF/, '');
      const rows = []; let row = [], cell = '', quoted = false, quoteClosed = false;
      const pushCell = () => { row.push(cell); cell = ''; quoteClosed = false; };
      const pushRow = () => { pushCell(); rows.push(row); row = []; };
      for (let i = 0; i < text.length; i++) {
        const ch = text[i];
        if (quoted) {
          if (ch === '"' && text[i + 1] === '"') { cell += '"'; i++; }
          else if (ch === '"') { quoted = false; quoteClosed = true; }
          else cell += ch;
          continue;
        }
        if (quoteClosed) {
          if (ch === delimiter) { pushCell(); continue; }
          if (ch === '\n') { pushRow(); continue; }
          if (ch === '\r' && text[i + 1] === '\n') { i++; pushRow(); continue; }
          throw new Error('CSV contains characters after a closing quote before the next delimiter or row ending.');
        }
        if (ch === '"') {
          if (cell.length) throw new Error('CSV contains a quote inside an unquoted field.');
          quoted = true;
        } else if (ch === delimiter) pushCell();
        else if (ch === '\n') pushRow();
        else if (ch === '\r' && text[i + 1] === '\n') { i++; pushRow(); }
        else cell += ch;
      }
      if (quoted) throw new Error('CSV contains an unterminated quoted field.');
      if (quoteClosed || cell.length || row.length) { row.push(cell.replace(/\r$/, '')); rows.push(row); }
      return rows.filter(item => item.some(value => value !== ''));
    }
    function csvEscape(value, delimiter = ',') { const text = value == null ? '' : String(value); return text.includes(delimiter) || /["\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text; }
    function summarizeCsv(path, source, delimiter = ',') {
      const rows = parseCsvRows(source, delimiter);
      if (!rows.length) return { path, columns: [], rowCount: 0, numericSummary: {} };
      const headers = rows[0].map((value, index) => String(value || `column_${index + 1}`).trim());
      const data = rows.slice(1);
      const numericSummary = Object.create(null);
      headers.forEach((header, column) => {
        let count = 0, min = Infinity, max = -Infinity, mean = 0;
        for (const row of data) {
          const raw = String(row[column] ?? '').trim();
          if (!raw) continue;
          const value = Number(raw);
          if (!Number.isFinite(value)) continue;
          count++;
          if (value < min) min = value;
          if (value > max) max = value;
          mean = mean * ((count - 1) / count) + value / count;
        }
        if (count >= Math.max(2, Math.ceil(data.length * .5))) {
          const baseKey = header || `column_${column + 1}`;
          let summaryKey = baseKey;
          if (Object.prototype.hasOwnProperty.call(numericSummary, summaryKey)) summaryKey = `${baseKey} [column ${column + 1}]`;
          let suffix = 2;
          while (Object.prototype.hasOwnProperty.call(numericSummary, summaryKey)) summaryKey = `${baseKey} [column ${column + 1}, ${suffix++}]`;
          numericSummary[summaryKey] = { columnIndex: column, column: header, count, min, max, mean };
        }
      });
      return { path, columns: headers, rowCount: data.length, sample: data.slice(0, 5), numericSummary };
    }
    const CSV_AGGREGATE_INPUT_BYTES_LIMIT = 50 * 1024 * 1024;
    const CSV_JOIN_INPUT_BYTES_LIMIT = 20 * 1024 * 1024;
    const CSV_OUTPUT_BYTES_LIMIT = 50 * 1024 * 1024;
    function csvRowsToText(rows, delimiter = ',', { maxBytes = Infinity, signal = null } = {}) {
      delimiter = normalizeCsvDelimiter(delimiter);
      const lines = []; let outputBytes = 0;
      for (let index = 0; index < rows.length; index++) {
        if ((index & 255) === 0 && signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
        const line = rows[index].map(value => csvEscape(value, delimiter)).join(delimiter);
        outputBytes += new Blob([line]).size + (index ? 2 : 0);
        if (outputBytes > maxBytes) throw new Error(`CSV output exceeds the ${Math.floor(maxBytes / (1024 * 1024))} MB output-size limit.`);
        lines.push(line);
      }
      return lines.join('\r\n');
    }
    function csvDataset(path, source, delimiter = ',') {
      delimiter = normalizeCsvDelimiter(delimiter);
      const rows = parseCsvRows(source, delimiter);
      if (!rows.length) throw new Error(`CSV file '${path}' is empty.`);
      const headers = rows[0].map((value, index) => String(value || `column_${index + 1}`).trim());
      return { headers, rows: rows.slice(1), delimiter };
    }
    function datasetColumnIndex(headers, requested) {
      const wanted = String(requested || '').toLowerCase();
      const matches = [];
      headers.forEach((item, index) => { if (item.toLowerCase() === wanted) matches.push(index); });
      if (!matches.length) throw new Error(`CSV column '${requested}' was not found. Available columns: ${headers.join(', ')}`);
      if (matches.length > 1) throw new Error(`CSV column '${requested}' appears more than once (columns ${matches.map(index => index + 1).join(', ')}). Select a unique column name.`);
      return matches[0];
    }
    async function sha256File(file) {
      if (file.size > 100 * 1024 * 1024) throw new Error(`'${file.name}' exceeds the 100 MB checksum limit.`);
      const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
      return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, '0')).join('');
    }
    async function walkWorkspace(root, options = {}, signal) {
      const rawDepth = Number(options.maxDepth ?? 3), rawResults = Number(options.maxResults ?? 200);
      const rawFiles = Number(options.maxFilesScanned ?? 2500), rawBytes = Number(options.maxBytesRead ?? 10 * 1024 * 1024);
      const rawEntries = Number(options.maxDirectoryEntries ?? 10000);
      const maxDepth = Number.isFinite(rawDepth) ? Math.max(0, Math.min(8, Math.floor(rawDepth))) : 3;
      const maxResults = Number.isFinite(rawResults) ? Math.max(1, Math.min(1000, Math.floor(rawResults))) : 200;
      const maxFilesScanned = Number.isFinite(rawFiles) ? Math.max(1, Math.min(10000, Math.floor(rawFiles))) : 2500;
      const maxDirectoryEntries = Number.isFinite(rawEntries) ? Math.max(1, Math.min(50000, Math.floor(rawEntries))) : 10000;
      const maxBytesRead = Number.isFinite(rawBytes) ? Math.max(0, Math.min(100 * 1024 * 1024, Math.floor(rawBytes))) : 10 * 1024 * 1024;
      const maxFileBytes = Math.max(1, Math.min(2 * 1024 * 1024, Number(options.maxFileBytes) || 512 * 1024));
      const query = String(options.query || '').trim().toLowerCase();
      const includeContent = !!options.searchContent;
      const results = [];
      const skippedDirectories = new Set(['.git', '.hg', '.svn', '.runmind-backups', '.cache', 'node_modules', 'vendor', 'bin', 'obj', 'dist', 'build', 'out', 'target', '.next', '.nuxt', '.venv', 'venv', '__pycache__', 'coverage']);
      let filesScanned = 0, directoryEntriesScanned = 0, contentBytesRead = 0, oversizedFilesSkipped = 0, truncated = false, stopReason = '';
      const stop = reason => { truncated = true; if (!stopReason) stopReason = reason; };
      async function visit(dir, prefix, depth) {
        if (signal?.aborted) { stop('cancelled'); return; }
        if (results.length >= maxResults) { stop('result limit reached'); return; }
        if (filesScanned >= maxFilesScanned) { stop('file-scan budget reached'); return; }
        if (depth > maxDepth) return;
        for await (const [name, handle] of dir.entries()) {
          if (signal?.aborted) { stop('cancelled'); return; }
          if (results.length >= maxResults) { stop('result limit reached'); return; }
          if (filesScanned >= maxFilesScanned) { stop('file-scan budget reached'); return; }
          if (directoryEntriesScanned >= maxDirectoryEntries) { stop('directory-entry budget reached'); return; }
          directoryEntriesScanned++;
          const path = prefix ? `${prefix}/${name}` : name;
          if (handle.kind === 'directory') {
            if (skippedDirectories.has(name.toLowerCase())) continue;
            if (!query) results.push({ path, type: 'directory' });
            if (results.length < maxResults) await visit(handle, path, depth + 1);
            continue;
          }
          filesScanned++;
          let file = null;
          let match = !query || name.toLowerCase().includes(query);
          if (includeContent && query && !match && /\.(txt|md|csv|json|xml|html|js|ts|css|py|cs|log|ya?ml)$/i.test(name)) {
            try {
              file = await handle.getFile();
              if (file.size > maxFileBytes) {
                oversizedFilesSkipped++;
                stop('per-file content limit reached');
              } else if (contentBytesRead + file.size <= maxBytesRead) {
                contentBytesRead += file.size;
                match = (await file.text()).toLowerCase().includes(query);
              } else {
                stop('content-byte budget reached');
              }
            } catch (_) {}
          }
          if (match) {
            try {
              file ||= await handle.getFile();
              results.push({ path, type: 'file', size: file.size, lastModified: file.lastModified });
            } catch (_) { results.push({ path, type: 'file' }); }
          }
        }
      }
      await visit(root, '', 0);
      return { results: results.slice(0, maxResults), filesScanned, directoryEntriesScanned, contentBytesRead, oversizedFilesSkipped, truncated, stopReason };
    }
