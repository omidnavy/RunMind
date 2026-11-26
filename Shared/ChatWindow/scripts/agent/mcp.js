    'use strict';

    // Shared Agent runtime for both desktop hosts. Web-capable tools stay in this page; AgentHostBridge exposes only named Windows-native capabilities.
    function loadMcpServerConfigs() {
      try { const rows = JSON.parse(localStorage.getItem(MCP_STORAGE_KEY) || '[]'); return Array.isArray(rows) ? rows : []; }
      catch (_) { return []; }
    }
    function saveMcpServerConfigs() {
      const active = [...mcpServers.values()].slice(0, 50);
      const saved = loadMcpServerConfigs();
      const activeKeys = new Set(active.map(row => `${row.id}\n${row.url}`));
      const retained = saved.filter(row => !activeKeys.has(`${row.id}\n${row.url}`));
      const registry = [...retained, ...active.map(({ id, name, url, enabled, token }) => ({ id, name, url, enabled: enabled !== false, token: token || '' }))];
      try { localStorage.setItem(MCP_STORAGE_KEY, JSON.stringify(registry)); }
      catch (_) { showToast('Could not save MCP server settings in this WebView profile.'); }
      if (workspaceContext) workspaceContext.mcpServers = active.map(({ id, name, url, enabled }) => ({ id, name, url, enabled: enabled !== false }));
    }
    function forgetMcpServerConfig(serverId) {
      try {
        const rows = loadMcpServerConfigs().filter(row => row?.id !== serverId);
        localStorage.setItem(MCP_STORAGE_KEY, JSON.stringify(rows));
      } catch (_) { showToast('Could not remove the trusted MCP server configuration from this WebView profile.'); }
    }
    function mcpHeaders(runtime, extra = {}) {
      return { 'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream', ...(runtime.token ? { Authorization: `Bearer ${runtime.token}` } : {}), ...(runtime.sessionId ? { 'Mcp-Session-Id': runtime.sessionId } : {}), ...(runtime.initialized ? { 'Mcp-Protocol-Version': runtime.protocolVersion } : {}), ...extra };
    }
    async function readBoundedResponseBlob(response, maxBytes = 10 * 1024 * 1024, fallbackType = 'application/octet-stream') {
      const declared = Number(response.headers.get('Content-Length') || 0);
      if (declared > maxBytes) {
        try { await response.body?.cancel('Declared response size exceeds download limit.'); } catch (_) {}
        throw new Error(`Download exceeds the ${Math.round(maxBytes / 1024 / 1024)} MB size limit.`);
      }
      if (!response.body) return new Blob([], { type: response.headers.get('Content-Type') || fallbackType });
      const reader = response.body.getReader();
      const chunks = [];
      let total = 0;
      try {
        while (true) {
          const { value, done } = await reader.read();
          if (done) break;
          total += value.byteLength;
          if (total > maxBytes) {
            try { await reader.cancel('Download size limit exceeded.'); } catch (_) {}
            throw new Error(`Download exceeds the ${Math.round(maxBytes / 1024 / 1024)} MB size limit.`);
          }
          chunks.push(value);
        }
        return new Blob(chunks, { type: response.headers.get('Content-Type') || fallbackType });
      } finally { try { reader.releaseLock(); } catch (_) {} }
    }

    async function readBoundedResponseText(response, maxBytes = 4 * 1024 * 1024) {
      const declared = Number(response.headers.get('Content-Length') || 0);
      if (declared > maxBytes) {
        try { await response.body?.cancel('Declared response size exceeds limit.'); } catch (_) {}
        throw new Error(`Response exceeded the ${Math.round(maxBytes / 1024 / 1024)} MB size limit.`);
      }
      if (!response.body) return '';
      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let total = 0, output = '';
      try {
        while (true) {
          const { value, done } = await reader.read();
          if (done) break;
          total += value.byteLength;
          if (total > maxBytes) {
            await reader.cancel('Response size limit exceeded.');
            throw new Error(`Response exceeded the ${Math.round(maxBytes / 1024 / 1024)} MB size limit.`);
          }
          output += decoder.decode(value, { stream: true });
        }
        return output + decoder.decode();
      } finally { try { reader.releaseLock(); } catch (_) {} }
    }
    async function mcpFetch(runtime, body, signal) {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(new DOMException('MCP request timed out after 30 seconds.', 'TimeoutError')), 30000);
      const abort = () => controller.abort(signal?.reason || new DOMException('MCP request cancelled.', 'AbortError'));
      if (signal?.aborted) abort();
      else signal?.addEventListener('abort', abort, { once: true });
      try {
        const response = await fetch(runtime.url, { method: 'POST', signal: controller.signal, headers: mcpHeaders(runtime), body: JSON.stringify(body) });
        Object.defineProperty(response, '__mcpCleanup', { value: () => { clearTimeout(timeout); signal?.removeEventListener('abort', abort); }, configurable: true });
        return response;
      } catch (error) {
        clearTimeout(timeout);
        signal?.removeEventListener('abort', abort);
        throw error;
      }
    }
    async function parseMcpHttpResponse(response, expectedId = null) {
      const sessionId = response.headers.get('Mcp-Session-Id');
      let reader = null;
      let totalBytes = 0;
      const maxBytes = 4 * 1024 * 1024;
      const decodePayload = raw => {
        let payload;
        try { payload = JSON.parse(raw); }
        catch (error) { throw new Error(`MCP server returned malformed JSON-RPC/SSE data: ${error?.message || error}`); }
        if (!payload || typeof payload !== 'object') return null;
        if (expectedId !== null && String(payload.id) !== String(expectedId)) return null;
        if (expectedId === null && payload.id === undefined) return null;
        if (payload.error) throw new Error(`MCP error ${payload.error.code ?? ''}: ${payload.error.message || 'Unknown error'}`);
        return { result: payload.result ?? null, sessionId };
      };
      try {
        if (!response.ok) {
          const text = await readBoundedResponseText(response, 256 * 1024);
          throw new Error(`HTTP ${response.status}: ${text.slice(0, 600)}`);
        }
        if (!response.body) return { result: null, sessionId };
        const contentType = response.headers.get('content-type') || '';
        reader = response.body.getReader();
        const decoder = new TextDecoder();
        if (!/text\/event-stream/i.test(contentType)) {
          let text = '';
          while (true) {
            const { value, done } = await reader.read();
            if (done) break;
            totalBytes += value.byteLength;
            if (totalBytes > maxBytes) throw new Error('MCP response exceeded the 4 MB size limit.');
            text += decoder.decode(value, { stream: true });
          }
          text += decoder.decode();
          if (!text.trim()) return { result: null, sessionId };
          const parsed = decodePayload(text);
          return parsed || { result: null, sessionId };
        }

        let lineBuffer = '';
        let dataLines = [];
        const consumeEvent = () => {
          if (!dataLines.length) return null;
          const raw = dataLines.join('\n');
          dataLines = [];
          return decodePayload(raw);
        };
        const consumeLine = line => {
          if (line === '') return consumeEvent();
          if (line.startsWith(':')) return null; // SSE comment/heartbeat
          const colon = line.indexOf(':');
          const field = colon < 0 ? line : line.slice(0, colon);
          let value = colon < 0 ? '' : line.slice(colon + 1);
          if (value.startsWith(' ')) value = value.slice(1);
          if (field === 'data') dataLines.push(value);
          return null;
        };
        while (true) {
          const { value, done } = await reader.read();
          if (done) break;
          totalBytes += value.byteLength;
          if (totalBytes > maxBytes) throw new Error('MCP SSE response exceeded the 4 MB size limit.');
          lineBuffer += decoder.decode(value, { stream: true });
          const lines = lineBuffer.split(/\r?\n/);
          lineBuffer = lines.pop() || '';
          for (const line of lines) {
            const parsed = consumeLine(line);
            if (parsed) return parsed;
          }
        }
        lineBuffer += decoder.decode();
        if (lineBuffer) {
          const parsed = consumeLine(lineBuffer);
          if (parsed) return parsed;
        }
        const last = consumeEvent();
        return last || { result: null, sessionId };
      } finally {
        if (reader) {
          try { await reader.cancel(); } catch (_) { /* A completed/aborted stream is already closed. */ }
          try { reader.releaseLock(); } catch (_) {}
        } else {
          try { await response.body?.cancel(); } catch (_) {}
        }
        response?.__mcpCleanup?.();
      }
    }
    async function mcpRpc(runtime, method, params = {}, signal, retryExpiredSession = true) {
      const id = ++mcpRequestId;
      const hadSession = !!runtime.sessionId;
      const response = await mcpFetch(runtime, { jsonrpc: '2.0', id, method, params }, signal);
      const status = response.status;
      try {
        const parsed = await parseMcpHttpResponse(response, id);
        if (parsed.sessionId) runtime.sessionId = parsed.sessionId;
        return parsed.result;
      } catch (error) {
        const safeToRetry = new Set(['tools/list', 'resources/list', 'resources/templates/list', 'resources/read', 'prompts/list', 'prompts/get']);
        if (!retryExpiredSession || !hadSession || ![404, 410].includes(status) || !safeToRetry.has(method) || signal?.aborted) throw error;
        // An expired MCP session may be reinitialized for idempotent discovery/read calls.
        // Never replay tools/call: a write or other side effect could be duplicated.
        runtime.sessionId = null;
        runtime.initialized = false;
        const init = await mcpRpc(runtime, 'initialize', { protocolVersion: runtime.protocolVersion, capabilities: {}, clientInfo: { name: 'RunMind', version: '1.10.19' } }, signal, false);
        if (!init?.protocolVersion) throw new Error('MCP session expired and reinitialization returned no protocolVersion.');
        runtime.protocolVersion = init.protocolVersion;
        runtime.initialized = true;
        await mcpNotify(runtime, 'notifications/initialized', {}, signal);
        return await mcpRpc(runtime, method, params, signal, false);
      }
    }
    async function mcpNotify(runtime, method, params = {}, signal) {
      const response = await mcpFetch(runtime, { jsonrpc: '2.0', method, params }, signal);
      try {
        const sessionId = response.headers.get('Mcp-Session-Id'); if (sessionId) runtime.sessionId = sessionId;
        if (!response.ok) throw new Error(`MCP notification HTTP ${response.status}`);
        if (response.body) await response.body.cancel().catch(() => {});
      } finally { response?.__mcpCleanup?.(); }
    }
    async function mcpConnect(config, token = '') {
      const savedToken = String(token || config.token || '').trim();
      const runtime = { url: normalizeMcpEndpoint(config.url), token: savedToken, sessionId: null, protocolVersion: '2025-03-26', tools: [], resources: [], prompts: [] };
      config.url = runtime.url;
      const init = await mcpRpc(runtime, 'initialize', { protocolVersion: runtime.protocolVersion, capabilities: {}, clientInfo: { name: 'RunMind', version: '1.10.19' } });
      if (!init?.protocolVersion) throw new Error('MCP initialize response did not include protocolVersion.');
      runtime.protocolVersion = init.protocolVersion;
      runtime.initialized = true;
      await mcpNotify(runtime, 'notifications/initialized');
      runtime.tools = await mcpListAll(runtime, 'tools/list', 'tools');
      try { runtime.resources = await mcpListAll(runtime, 'resources/list', 'resources'); } catch (_) { runtime.resources = []; }
      try { runtime.prompts = await mcpListAll(runtime, 'prompts/list', 'prompts'); } catch (_) { runtime.prompts = []; }
      mcpRuntime.set(config.id, runtime);
      const saved = { ...config, token: savedToken, connected: true, toolCount: runtime.tools.length, resourceCount: runtime.resources.length, promptCount: runtime.prompts.length };
      mcpServers.set(config.id, saved); saveMcpServerConfigs(); renderMcpServers();
      return saved;
    }
    async function mcpListAll(runtime, method, key) {
      const all = []; let cursor;
      for (let page = 0; page < 20; page++) {
        const result = await mcpRpc(runtime, method, cursor ? { cursor } : {});
        if (Array.isArray(result?.[key])) all.push(...result[key]);
        cursor = result?.nextCursor; if (!cursor) break;
      }
      return all;
    }
    function renderMcpServers() {
      if (!mcpServerList) return;
      const rows = [...mcpServers.values()];
      if (!rows.length) { mcpServerList.innerHTML = '<p class="setting-note">No MCP servers configured.</p>'; updateMcpModeButton(); return; }
      mcpServerList.innerHTML = rows.map(row => `<div class="capability-item"><strong>${escapeHtml(row.name)}</strong><span>${escapeHtml(row.url)}</span><span>${row.connected ? 'Connected' : 'Disconnected'} · ${row.toolCount || 0} tools · ${row.resourceCount || 0} resources · ${row.promptCount || 0} prompts · ${row.token ? 'Token saved' : 'No token'}</span><div class="context-actions"><button type="button" data-mcp-action="toggle" data-mcp-id="${escapeHtml(row.id)}">${row.enabled ? 'Disable' : 'Enable'}</button><button type="button" data-mcp-action="reconnect" data-mcp-id="${escapeHtml(row.id)}">Reconnect</button>${row.token ? `<button type="button" data-mcp-action="clear-token" data-mcp-id="${escapeHtml(row.id)}">Clear token</button>` : ''}<button type="button" data-mcp-action="disconnect" data-mcp-id="${escapeHtml(row.id)}">Disconnect</button><button type="button" data-mcp-action="remove" data-mcp-id="${escapeHtml(row.id)}">Remove</button></div></div>`).join('');
      updateMcpModeButton();
    }
    function stableToolNameHash(value) {
      let hash = 0xcbf29ce484222325n;
      for (const byte of new TextEncoder().encode(String(value))) {
        hash ^= BigInt(byte);
        hash = BigInt.asUintN(64, hash * 0x100000001b3n);
      }
      return hash.toString(16).padStart(16, '0');
    }
    function getMcpToolDefinitions() {
      const definitions = [];
      mcpToolRoutes.clear();
      const enabledRuntimes = [...mcpRuntime.entries()].filter(([serverId]) => mcpServers.get(serverId)?.enabled);
      for (const [serverId, runtime] of enabledRuntimes) {
        const config = mcpServers.get(serverId);
        for (const tool of runtime.tools) {
          const safeServer = String(serverId).replace(/[^a-zA-Z0-9_]/g, '_').slice(0, 6) || 'server';
          const safeTool = String(tool.name).replace(/[^a-zA-Z0-9_]/g, '_').slice(0, 8) || 'tool';
          const name = `mcp_${safeServer}_${stableToolNameHash(serverId)}_${safeTool}_${stableToolNameHash(`${serverId}::${tool.name}`)}`;
          definitions.push({ type: 'function', function: { name, description: `[MCP: ${config.name}] ${String(tool.description || tool.name).slice(0, 900)}`, parameters: tool.inputSchema || { type: 'object', properties: {} } } });
          mcpToolRoutes.set(name, { serverId, toolName: tool.name });
        }
      }
      if (enabledRuntimes.some(([, runtime]) => runtime.resources.length || runtime.prompts.length)) {
        definitions.push({ type: 'function', function: { name: 'mcp_list_knowledge', description: 'List tools, resources, and prompts exposed by connected MCP knowledge servers. Use this to discover available knowledge resources.', parameters: { type: 'object', properties: {}, additionalProperties: false } } });
        if (enabledRuntimes.some(([, runtime]) => runtime.resources.length)) definitions.push({ type: 'function', function: { name: 'mcp_read_resource', description: 'Read a resource by its exact server ID and URI, as discovered through mcp_list_knowledge.', parameters: { type: 'object', properties: { server_id: { type: 'string' }, uri: { type: 'string' } }, required: ['server_id','uri'], additionalProperties: false } } });
      }
      if (enabledRuntimes.some(([, runtime]) => runtime.prompts.length)) {
        definitions.push({ type: 'function', function: { name: 'mcp_get_prompt', description: 'Retrieve a named prompt from an MCP server. Supply arguments required by that prompt.', parameters: { type: 'object', properties: { server_id: { type: 'string' }, name: { type: 'string' }, arguments: { type: 'object' } }, required: ['server_id','name'], additionalProperties: false } } });
      }
      return definitions;
    }
    function requestAgentApproval({ title, subtitle, detail, risky = false, allowForTask = false, allowAlways = false, toolName = '' }, signal) {
      if (pendingAgentApproval) pendingAgentApproval.finish(false);
      agentApprovalTitle.textContent = title || 'Permission required';
      agentApprovalSubtitle.textContent = subtitle || 'Review this action before it runs';
      agentApprovalDetail.textContent = typeof detail === 'string' ? detail : JSON.stringify(detail ?? {}, null, 2);
      agentApproval.classList.toggle('is-risky', !!risky);
      const canRemember = allowForTask === true && risky !== true;
      agentApprovalTask.hidden = !canRemember;
      agentApprovalAlways.hidden = !canRemember || !allowAlways || !toolName;
      agentApprovalOnce.textContent = 'Allow this call';
      agentApprovalTask.textContent = 'Allow matching reads this reply';
      agentApprovalAlways.textContent = 'Always allow this tool';
      agentApprovalHint.textContent = canRemember && allowAlways && toolName
        ? `“Allow this call” approves once. “Always allow this read-only tool” remembers ${toolName} across chats in this WebView profile. You can revoke it in Agent settings.`
        : canRemember
          ? '“Allow this call” approves only the request shown above. “Allow matching reads this reply” also approves later calls to this same read-only tool until the current reply finishes.'
          : 'This approval applies only to the action shown above. Write, external-action, and destructive operations always require a fresh approval.';
      agentApproval.hidden = false;
      return new Promise(resolve => {
        let settled = false;
        const finish = approved => {
          if (settled) return;
          settled = true;
          if (pendingAgentApproval?.finish === finish) pendingAgentApproval = null;
          agentApproval.hidden = true;
          if (signal) signal.removeEventListener('abort', onAbort);
          resolve(approved);
        };
        const onAbort = () => finish(false);
        pendingAgentApproval = { finish };
        agentApprovalOnce.onclick = () => finish('once');
        agentApprovalTask.onclick = () => finish('task');
        agentApprovalAlways.onclick = () => finish('always');
        agentApprovalDeny.onclick = () => finish(false);
        if (signal) {
          if (signal.aborted) { finish(false); return; }
          signal.addEventListener('abort', onAbort, { once: true });
        }
        agentApproval.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      });
    }
    async function requireAgentApproval(options, signal) {
      const toolName = options.toolName || activeAgentApprovalTool || '';
      const canRemember = options.allowForTask === true && options.risky !== true;
      if (options.permissionCategory && !options.alwaysAsk && !permissionRequiresApproval(options.permissionCategory)) return true;
      // Ignore legacy persistent grants for anything that is not explicitly read-only.
      if (canRemember && toolName && alwaysApprovedAgentTools.has(toolName)) return true;
      const decision = await requestAgentApproval({ ...options, toolName, allowAlways: canRemember }, signal);
      if (decision === 'task' && canRemember && toolName) taskApprovedReadTools.add(toolName);
      if (decision === 'always' && canRemember && toolName) {
        alwaysApprovedAgentTools.add(toolName);
        saveAlwaysApprovedAgentTools();
        showToast(`Always approved read-only tool: ${toolName}. Revoke it in Agent settings.`);
      }
      return decision === 'once' || (decision === 'task' && canRemember) || (decision === 'always' && canRemember);
    }
    async function requestFolderAccess(signal, forceChoose = false) {
      await workspaceRestorePromise;
      const nativeHost = !!window.chrome?.webview && typeof window.chrome.webview.postMessage === 'function';
      if (nativeHost && !forceChoose) {
        try {
          const status = await callAgentHost('process.workspace.status', { contextId: workspaceContext.id }, signal, 10000);
          if (status?.selected && status.selectionId) {
            const current = agentDirectoryHandle;
            const matches = current?.isNativeWorkspace === true
              && current.contextId === workspaceContext.id
              && current.selectionId === status.selectionId;
            if (!matches) {
              // Another RunMind host may have changed the workspace for this conversation.
              // Rebind to the native host's current selection instead of asking for the same folder again.
              const handle = createNativeWorkspaceHandle(workspaceContext.id, status.folder || 'workspace', status.selectionId);
              await persistAgentFolderHandle(handle);
              notifyWorkspaceHandleChanged('rebound');
            }
            return { granted: true, folder: status.folder || agentDirectoryHandle?.name || 'workspace', alreadySelected: true, rebound: !matches, native: true };
          }
        } catch (error) {
          const message = `Could not verify the selected native workspace: ${error?.message || error}`;
          showToast(message);
          // A failed native status check is not evidence that no workspace exists.
          // Fail closed rather than prompting the user to pick the same folder again.
          return { granted: false, error: message };
        }
      }
      if (!nativeHost && agentDirectoryHandle && !forceChoose) {
        try {
          const permission = await agentDirectoryHandle.queryPermission({ mode: 'readwrite' });
          if (permission === 'granted') return { granted: true, folder: agentDirectoryHandle.name || 'selected folder', alreadySelected: true };
          agentDirectoryHandle = null;
          updateAgentFolderButton();
        } catch (_) {
          agentDirectoryHandle = null;
          updateAgentFolderButton();
        }
      }
      if (pendingFolderAccessRequest) return pendingFolderAccessRequest.promise;
      if (!nativeHost && typeof window.showDirectoryPicker !== 'function' && (forceChoose || !persistedDirectoryHandle)) return Promise.resolve({ granted: false, error: 'This WebView2 runtime does not support folder selection.' });
      const reconnecting = !nativeHost && !!persistedDirectoryHandle && !forceChoose;
      agentFolderAccessDetail.textContent = nativeHost
        ? (forceChoose ? 'Choose a new project folder. This replaces the current workspace for this conversation.' : 'Choose the project folder once. RunMind will use this same native workspace for Agent file access, project indexing, Git, Playwright, API testing, development servers, debugging and approved commands.')
        : reconnecting
          ? `Reconnect access to the saved folder '${persistedDirectoryHandle.name || 'workspace'}'. You do not need to select it again.`
          : workspaceContext.folderName
            ? `This conversation was saved with the folder name '${workspaceContext.folderName}'. Select it again to grant this device access. The actual folder handle is not exported.`
            : 'Choose a workspace folder. Access is limited to that folder and stays on this device.';
      agentFolderAccessChoose.textContent = nativeHost ? (forceChoose ? 'Choose new workspace' : 'Choose workspace') : (reconnecting ? 'Reconnect access' : 'Choose folder');
      agentFolderAccessChange.hidden = nativeHost ? !Boolean(agentDirectoryHandle?.isNativeWorkspace || workspaceContext.folderName) : !reconnecting;
      agentFolderAccessRequest.hidden = false;
      agentFolderAccessRequest.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      let resolveRequest;
      const promise = new Promise(resolve => { resolveRequest = resolve; });
      let settled = false;
      const finish = result => {
        if (settled) return;
        settled = true;
        if (pendingFolderAccessRequest?.finish === finish) pendingFolderAccessRequest = null;
        agentFolderAccessRequest.hidden = true;
        if (signal) signal.removeEventListener('abort', onAbort);
        resolveRequest(result);
      };
      const onAbort = () => finish({ granted: false, cancelled: true, message: 'Folder access request cancelled.' });
      pendingFolderAccessRequest = { promise, finish };
      agentFolderAccessCancel.onclick = () => finish({ granted: false, cancelled: true, message: 'The user chose not to grant folder access.' });
      agentFolderAccessChoose.onclick = async () => {
        agentFolderAccessChoose.disabled = true;
        try {
          if (nativeHost) {
            const selection = await callAgentHost('process.selectWorkspace', { contextId: workspaceContext.id, forceChange: forceChoose }, signal, 120000);
            if (settled) return;
            if (!selection?.selected || !selection.selectionId) {
              finish({ granted: false, cancelled: true, message: 'Workspace selection was cancelled.' });
              return;
            }
            const handle = createNativeWorkspaceHandle(workspaceContext.id, selection.folder || 'workspace', selection.selectionId);
            await persistAgentFolderHandle(handle);
            notifyWorkspaceHandleChanged(forceChoose ? 'changed' : 'selected');
            finish({ granted: true, folder: handle.name, changed: forceChoose, native: true });
            showToast(`Shared workspace ${forceChoose ? 'changed' : 'selected'}: ${handle.name}.`);
            return;
          }
          if (reconnecting && persistedDirectoryHandle) {
            const permission = await persistedDirectoryHandle.requestPermission({ mode: 'readwrite' });
            if (permission !== 'granted') throw new Error('Folder access was not granted. Use Choose another to select a different folder.');
            await persistAgentFolderHandle(persistedDirectoryHandle);
            notifyWorkspaceHandleChanged('selected');
            finish({ granted: true, folder: persistedDirectoryHandle.name || 'workspace', reconnected: true });
            showToast(`Workspace reconnected: ${agentDirectoryHandle.name || 'selected folder'}.`);
            return;
          }
          const handle = await window.showDirectoryPicker({ mode: 'readwrite' });
          if (settled) return;
          await persistAgentFolderHandle(handle);
          notifyWorkspaceHandleChanged('selected');
          finish({ granted: true, folder: handle.name || 'selected folder' });
          showToast(`Workspace selected: ${handle.name || 'selected folder'}.`);
        } catch (error) {
          if (error?.name === 'AbortError') finish({ granted: false, cancelled: true, message: 'Folder selection was cancelled.' });
          else showToast(`Could not access folder: ${error?.message || error}`);
        } finally { agentFolderAccessChoose.disabled = false; }
      };
      agentFolderAccessChange.onclick = async () => {
        agentFolderAccessChange.disabled = true;
        try {
          if (nativeHost) {
            const selection = await callAgentHost('process.selectWorkspace', { contextId: workspaceContext.id, forceChange: true }, signal, 120000);
            if (settled) return;
            if (!selection?.selected || !selection.selectionId) { finish({ granted: false, cancelled: true, message: 'Workspace change was cancelled.' }); return; }
            const handle = createNativeWorkspaceHandle(workspaceContext.id, selection.folder || 'workspace', selection.selectionId);
            await persistAgentFolderHandle(handle);
            notifyWorkspaceHandleChanged('changed');
            finish({ granted: true, folder: handle.name, changed: true, native: true });
            showToast(`Workspace changed: ${handle.name}.`);
          } else {
            const handle = await window.showDirectoryPicker({ mode: 'readwrite' });
            if (settled) return;
            await persistAgentFolderHandle(handle);
            notifyWorkspaceHandleChanged('selected');
            finish({ granted: true, folder: handle.name || 'selected folder', changed: true });
            showToast(`Workspace changed: ${handle.name || 'selected folder'}.`);
          }
        } catch (error) {
          if (error?.name === 'AbortError') finish({ granted: false, cancelled: true, message: 'Folder selection was cancelled.' });
          else showToast(`Could not change folder: ${error?.message || error}`);
        } finally { agentFolderAccessChange.disabled = false; }
      };
      if (signal) {
        if (signal.aborted) onAbort();
        else signal.addEventListener('abort', onAbort, { once: true });
      }
      return promise;
    }

    async function executeMcpTool(name, args, signal) {
      if (name === 'mcp_list_knowledge') return [...mcpRuntime.entries()].filter(([id]) => mcpServers.get(id)?.enabled).map(([id, runtime]) => ({ server_id: id, server: mcpServers.get(id)?.name, tools: runtime.tools.map(t => ({ name: t.name, description: t.description })), resources: runtime.resources.map(r => ({ name: r.name, uri: r.uri, description: r.description, mimeType: r.mimeType })), prompts: runtime.prompts.map(p => ({ name: p.name, description: p.description, arguments: p.arguments })) }));
      if (name === 'mcp_read_resource') {
        const runtime = mcpRuntime.get(String(args.server_id)); if (!runtime || !mcpServers.get(String(args.server_id))?.enabled) throw new Error('MCP server is not connected/enabled.');
        if (!runtime.resources.some(r => r.uri === args.uri)) throw new Error('That resource URI was not advertised by this server.');
        if (!await requireAgentApproval({ title: 'Read MCP resource?', subtitle: mcpServers.get(String(args.server_id)).name, detail: args.uri, toolName: name, allowForTask: false }, signal)) throw new Error('User declined MCP resource access.');
        return await mcpRpc(runtime, 'resources/read', { uri: args.uri }, signal);
      }
      if (name === 'mcp_get_prompt') {
        const runtime = mcpRuntime.get(String(args.server_id)); if (!runtime || !mcpServers.get(String(args.server_id))?.enabled) throw new Error('MCP server is not connected/enabled.');
        if (!runtime.prompts.some(p => p.name === args.name)) throw new Error('That prompt was not advertised by this server.');
        if (!await requireAgentApproval({ title: 'Retrieve MCP prompt?', subtitle: mcpServers.get(String(args.server_id)).name, detail: args.name, toolName: name, allowForTask: false }, signal)) throw new Error('User declined MCP prompt access.');
        return await mcpRpc(runtime, 'prompts/get', { name: args.name, arguments: args.arguments || {} }, signal);
      }
      const route = mcpToolRoutes.get(name);
      if (!route) throw new Error(`Unknown MCP tool '${name}'.`);
      const runtime = mcpRuntime.get(route.serverId); const config = mcpServers.get(route.serverId);
      if (!runtime || !config?.enabled) throw new Error('MCP server is disconnected or disabled.');
      if (!await requireAgentApproval({ title: 'Run MCP tool?', subtitle: `${config.name} · ${route.toolName}`, detail: args || {}, risky: true, toolName: name, allowForTask: false }, signal)) throw new Error('User declined MCP tool execution.');
      return await mcpRpc(runtime, 'tools/call', { name: route.toolName, arguments: args || {} }, signal);
    }
    async function restoreMcpConfigs(contextRows = null) {
      const trustedRows = loadMcpServerConfigs();
      const rows = Array.isArray(contextRows) ? contextRows : trustedRows;
      // End prior context sessions before replacing their runtime entries.
      for (const runtime of mcpRuntime.values()) {
        if (!runtime.sessionId) continue;
        try { await fetch(runtime.url, { method: 'DELETE', headers: mcpHeaders(runtime) }); } catch (_) {}
      }
      mcpServers.clear();
      mcpRuntime.clear();
      mcpToolRoutes.clear();
      for (const row of rows) {
        if (!row?.id || !row?.url) continue;
        let normalizedUrl;
        try { normalizedUrl = normalizeMcpEndpoint(row.url); } catch (_) { continue; }
        const trusted = trustedRows.find(item => item.id === row.id && item.url === normalizedUrl);
        const isTrusted = !Array.isArray(contextRows) || !!trusted;
        const config = {
          id: String(row.id), name: String(row.name || row.id), url: normalizedUrl,
          enabled: isTrusted && row.enabled !== false,
          token: isTrusted ? String(trusted?.token || '') : '',
          connected: false, toolCount: 0, resourceCount: 0, promptCount: 0
        };
        mcpServers.set(config.id, config);
      }
      workspaceContext.mcpServers = [...mcpServers.values()].map(({ id, name, url, enabled }) => ({ id, name, url, enabled }));
      renderMcpServers();
      rebuildActiveToolDefinitions(deepResearchEnabled);
    }
