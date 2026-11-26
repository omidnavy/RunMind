    'use strict';

    async function sendMessage() {
      if (activeController || isConfirmingAttachments) return false;
      const typedContent = promptInput.value.trim();
      if (!typedContent && !pendingAttachments.length) return false;
      if (typedContent && !pendingAttachments.length) { try { if (await tryInstallSkillUrlMessage(typedContent) || await tryInstallPastedSkill(typedContent)) { promptInput.value=''; autoResizePrompt(); return false; } } catch(error) { showToast(`Skill installation failed: ${error?.message || error}`); return false; } }
      if (window.chrome?.webview && typeof loadSystemManagerSettings === 'function') await loadSystemManagerSettings();
      const selection = snapshotModelSelection();
      if (!selection) { showToast('No model is selected. Refresh model lists and choose a model.'); return false; }
      applyConnectionForSelection(selection);
      const shouldSaveAgentTask = agentModeEnabled || mcpModeEnabled || deepResearchEnabled;
      const attachments = pendingAttachments.slice();
      const totalAttachmentBytes = attachments.reduce((total, file) => total + Number(file.size || 0), 0);
      if (attachments.length && totalAttachmentBytes >= ATTACHMENT_LIMITS.confirmBeforeSendBytes) {
        isConfirmingAttachments = true;
        sendButton.disabled = true;
        let confirmed = false;
        try { confirmed = await requestAttachmentConfirmation(totalAttachmentBytes, attachments); }
        finally { isConfirmingAttachments = false; sendButton.disabled = false; }
        if (!confirmed) return false;
      }
      // Sending a new message exits comparison mode first so the new turn and
      // its streamed response are visible in the normal conversation transcript.
      if (!conversationBranchCompareView.hidden) hideBranchComparisonWorkspace();
      const content = typedContent || 'Please analyze the attached file(s).';
      const generateTitleAfterResponse = BOOT.generateTitleAfterFirstResponse === true
        && !BOOT.initialTitleGenerated
        && !messages.some(message => message.role === 'user');
      if (generateTitleAfterResponse) setConversationTitle(createFallbackConversationTitle(content));
      const settingsForRequest = { ...generationSettings(), keepAliveSeconds: Number(BOOT.keepAliveSeconds ?? 300) };
      const thinkForRequest = !!thinkToggle.checked;
      const isContinuationRequest = nextMessageIsContinuation;
      if (editingMessageIndex !== null) {
        messages.splice(editingMessageIndex);
        contextSummary = ''; contextSummaryThrough = 0; contextSummaryDirty = false;
        lmStudioNativeResponseId = null; lmStudioNativeModelKey = null;
        editingMessageIndex = null;
        draftBeforeEdit = null;
        editModeNotice.hidden = true;
      }
      promptInput.value = '';
      autoResizePrompt();
      pendingAttachments = [];
      renderPendingAttachments();
      const startedAt = Date.now();
      generationStallWarningActive = false;
      const previousAssistant = [...messages].reverse().find(item => item.role === 'assistant');
      const contextPlanForRequest = getManagedContextPlan(messages.filter(message => !message.pending));
      const managedHistoryIsComplete = contextPlanForRequest.recentStart === 0 && contextPlanForRequest.omittedCount === 0 && !contextSummary;
      const previousResponseId = selection.provider === 'lmstudio' && managedHistoryIsComplete && previousAssistant?.provider === 'lmstudio' && previousAssistant?.model === selection.model
        ? previousAssistant.providerResponseId || null : null;
      const userMessage = { role: 'user', content, attachments, timestamp: startedAt, provider: selection.provider, connectionName: selection.serverName || selection.connection?.name || '', model: selection.model, thinkEnabled: thinkForRequest, isContinuation: isContinuationRequest };
      nextMessageIsContinuation = false;
      messages.push(userMessage);
      const assistantMessage = { role: 'assistant', content: '', thinking: '', thinkingOpen: defaultThinkingOpen, thinkRequested: thinkForRequest && !agentModeEnabled && !mcpModeEnabled && !deepResearchEnabled, generationSettings: settingsForRequest, timestamp: startedAt, provider: selection.provider, connectionName: selection.serverName || selection.connection?.name || '', model: selection.model, pending: true, metrics: { startedAt, lastActivityAt: startedAt, elapsedMs: 0, timeToFirstTokenMs: null, tokenCount: null, tokensPerSecond: null, approximateRate: false } };
      messages.push(assistantMessage);
      renderMessages();
      activeAssistantMessage = assistantMessage;
      const controller = new AbortController();
      beginActiveController(controller);
      activeMetricsHandle = setInterval(() => {
        if (activeAssistantMessage) {
          updateGenerationMetrics(activeAssistantMessage);
          updateActiveMetricsOnly();
          const lastActivityAt = Number(activeAssistantMessage.metrics?.lastActivityAt || activeAssistantMessage.metrics?.startedAt || Date.now());
          const idleSeconds = Math.floor((Date.now() - lastActivityAt) / 1000);
          if (activeAssistantMessage.pending && idleSeconds >= 30) {
            setStatus(`No output for ${idleSeconds}s · request is still running. You can stop it.`, 'warn');
            generationStallWarningActive = true;
          }
        }
      }, 1000);
      sendButton.disabled = true; promptInput.disabled = true; attachButton.disabled = true; thinkButton.disabled = true; refreshButton.disabled = true;
      stopButton.hidden = false;
      setStatus(`Waiting for ${selection.provider === 'lmstudio' ? 'LM Studio' : 'Ollama'} (${selection.model})…`);
      const requestMessages = buildManagedContext(selection.provider);
      const onMetrics = stats => {
        assistantMessage.metrics.lastActivityAt = Date.now();
        if (stats.diagnostic) {
          assistantMessage.diagnostics ||= [];
          if (!assistantMessage.diagnostics.includes(String(stats.diagnostic))) assistantMessage.diagnostics.push(String(stats.diagnostic));
        }
        if (Number.isFinite(stats.timeToFirstTokenMs)) assistantMessage.metrics.timeToFirstTokenMs = stats.timeToFirstTokenMs;
        if (Number.isFinite(stats.modelLoadTimeMs)) assistantMessage.metrics.modelLoadTimeMs = stats.modelLoadTimeMs;
        if (Number.isFinite(stats.tokenCount)) assistantMessage.metrics.tokenCount = stats.tokenCount;
        if (Number.isFinite(stats.tokensPerSecond) && stats.tokensPerSecond > 0) {
          assistantMessage.metrics.tokensPerSecond = stats.tokensPerSecond;
          assistantMessage.metrics.approximateRate = !!stats.approximateRate;
        } else if (Number.isFinite(stats.tokenCount)) {
          assistantMessage.metrics.approximateRate = true;
          const elapsedSeconds = Math.max(.1, (Date.now() - assistantMessage.metrics.startedAt) / 1000);
          assistantMessage.metrics.tokensPerSecond = stats.tokenCount / elapsedSeconds;
        }
        updateGenerationMetrics(assistantMessage);
        scheduleActiveMessageRender();
      };
      try {
        const onToken = text => {
          assistantMessage.metrics.lastActivityAt = Date.now();
          if (assistantMessage.metrics.timeToFirstTokenMs === null) assistantMessage.metrics.timeToFirstTokenMs = Date.now() - startedAt;
          assistantMessage.content += text; updateGenerationMetrics(assistantMessage); scheduleActiveMessageRender();
        };
        const onThinking = text => { assistantMessage.metrics.lastActivityAt = Date.now(); if (assistantMessage.metrics.timeToFirstTokenMs === null) assistantMessage.metrics.timeToFirstTokenMs = Date.now() - startedAt; assistantMessage.thinking = (assistantMessage.thinking || '') + text; if (assistantMessage.thinkingOpen === undefined) assistantMessage.thinkingOpen = defaultThinkingOpen; updateGenerationMetrics(assistantMessage); scheduleActiveMessageRender(); };
        let output;
        const connectedMcpEnabled = [...mcpRuntime.entries()].some(([id, runtime]) => mcpServers.get(id)?.enabled && (runtime.tools.length || runtime.resources.length || runtime.prompts.length));
        if (deepResearchEnabled) {
          await runDeepResearch(selection, requestMessages, assistantMessage, controller.signal);
          output = { content: assistantMessage.content };
        } else if (agentModeEnabled || (mcpModeEnabled && connectedMcpEnabled)) {
          await runAgent(selection, requestMessages, assistantMessage, controller.signal);
          output = { content: assistantMessage.content };
        } else {
          if (mcpModeEnabled && !connectedMcpEnabled) showToast('MCP access is enabled, but no enabled MCP server is connected. Sending as normal chat.');
          const providerAdapter = PROVIDERS[selection.provider];
          if (!providerAdapter) throw new Error(`Unsupported provider: ${selection.provider}`);
          output = await providerAdapter.generate({ model: selection.model, messages: requestMessages, controller, onToken, onThinking, onMetrics, settings: settingsForRequest, thinkingEnabled: thinkForRequest, previousResponseId, connection: selection.connection, lmStudioApiMode: selection.lmStudioApiMode });
        }
        if (output && typeof output === 'object') {
          assistantMessage.providerResponseId = output.responseId || null;
          if (Number.isFinite(output.timeToFirstTokenMs)) assistantMessage.metrics.timeToFirstTokenMs = output.timeToFirstTokenMs;
        }
        assistantMessage.pending = false;
        const outputText = typeof output === 'string' ? output : output?.content;
        if (!assistantMessage.content && !outputText) assistantMessage.content = '(The model returned an empty response.)';
        setStatus(`Connected · ${selection.provider === 'lmstudio' ? 'LM Studio' : 'Ollama'} · ${selection.model}`, 'good');
      } catch (error) {
        assistantMessage.pending = false;
        if (error?.name === 'AbortError') {
          assistantMessage.cancelled = true;
          if (!assistantMessage.content && !assistantMessage.thinking) assistantMessage.content = '[Generation stopped.]';
          setStatus('Generation stopped.');
        } else {
          assistantMessage.error = true;
          assistantMessage.content = (assistantMessage.content ? assistantMessage.content + '\n\n' : '') + `Request failed: ${error?.message || String(error)}\n\nIf this is a browser network/CORS error, verify the provider's allowed origins and that the local endpoint is reachable from WebView2.`;
          setStatus(`Request failed · ${selection.provider === 'lmstudio' ? 'LM Studio' : 'Ollama'}`, 'bad');
        }
      } finally {
        clearInterval(activeMetricsHandle);
        activeMetricsHandle = null;
        updateGenerationMetrics(assistantMessage);
        const elapsedSeconds = Math.max(.1, assistantMessage.metrics.elapsedMs / 1000);
        if (Number.isFinite(assistantMessage.metrics.tokenCount) && !Number.isFinite(assistantMessage.metrics.tokensPerSecond)) {
          assistantMessage.metrics.tokensPerSecond = assistantMessage.metrics.tokenCount / elapsedSeconds;
          assistantMessage.metrics.approximateRate = true;
        }
        finishActiveController(controller); activeAssistantMessage = null; assistantMessage.pending = false;
        generationStallWarningActive = false;
        if (shouldSaveAgentTask) saveAgentTaskHistory();
        // Keep the user-granted folder handle for this WebView session until explicitly cleared.
        sendButton.disabled = false; promptInput.disabled = false; attachButton.disabled = false; thinkButton.disabled = false; refreshButton.disabled = false;
        stopButton.hidden = true;
        renderMessages(); saveActiveConversationBranch(); promptInput.focus(); autoResizePrompt();
        // Ollama reports the effective context length for a loaded model through /api/ps.
        // Refresh after generation so subsequent turns can use that active value.
        void updateSelectedModelInfo();
      }
      if (generateTitleAfterResponse && !assistantMessage.error && assistantMessage.content
        && !assistantMessage.content.startsWith('[Generation stopped.]')) {
        BOOT.initialTitleGenerated = true;
        void generateInitialConversationTitle(selection, content, assistantMessage.content);
      }
      return true;
    }
    function updateGenerationMetrics(message) {
      if (!message?.metrics) return;
      message.metrics.elapsedMs = Math.max(0, Date.now() - Number(message.metrics.startedAt || Date.now()));
      if (message.metrics.approximateRate && Number.isFinite(message.metrics.tokenCount)) {
        const seconds = Math.max(.1, message.metrics.elapsedMs / 1000);
        message.metrics.tokensPerSecond = message.metrics.tokenCount / seconds;
      }
    }
    function filenameSafe(value) { return String(value || 'conversation').replace(/[<>:"/\\|?*\x00-\x1f]/g, '-').replace(/\s+/g, ' ').trim().slice(0, 80) || 'conversation'; }
    function downloadFile(filename, content, mimeType) {
      const blob = new Blob([content], { type: mimeType }); const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = filename; anchor.style.display = 'none'; document.body.appendChild(anchor); anchor.click(); anchor.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1500);
    }
    function allMessages() { return messages.filter(m => !m.pending); }
    let conversationDisplayTitle = '';
    let titleGenerationController = null;
    function extractOpenAITitleText(response) {
      const choice = response?.choices?.[0];
      const content = choice?.message?.content;
      if (typeof content === 'string') return content;
      if (Array.isArray(content)) {
        return content.map(part => typeof part === 'string' ? part : (typeof part?.text === 'string' ? part.text : (typeof part?.content === 'string' ? part.content : ''))).join('');
      }
      return typeof choice?.text === 'string' ? choice.text : '';
    }
    async function generateInitialConversationTitle(selection, prompt, responseText) {
      const userPrompt = String(prompt || '').slice(0, 1800);
      const answer = String(responseText || '').slice(0, 1800);
      const titlePrompt = `Create a concise chat title of 3 to 7 words. Use the user's request and the assistant's response to identify the topic. Return only the title, without quotes, Markdown, or explanation.\n\nUSER REQUEST:\n${userPrompt}\n\nASSISTANT RESPONSE:\n${answer}`;
      const controller = new AbortController();
      titleGenerationController?.abort();
      titleGenerationController = controller;
      const timeout = setTimeout(() => controller.abort(new DOMException('Title generation timed out.', 'TimeoutError')), 20000);
      try {
        let url, payload;
        if (selection.provider === 'ollama') {
          url = `${normalizeBase(selection.connection?.baseUrl || BOOT.ollamaBaseUrl)}/api/generate`;
          payload = { model: selection.model, prompt: titlePrompt, stream: false, think: false, keep_alive: Number(BOOT.keepAliveSeconds ?? 300), options: { temperature: 0.2, num_predict: 32 } };
        } else {
          url = `${normalizeBase(selection.connection?.baseUrl || BOOT.lmStudioBaseUrl || 'http://localhost:1234/v1')}/chat/completions`;
          payload = {
            model: selection.model,
            messages: [{ role: 'system', content: 'You generate short, accurate conversation titles. Return only a title.' }, { role: 'user', content: titlePrompt }],
            stream: false, temperature: 0.2, max_tokens: 32,
            reasoning_effort: 'none',
            chat_template_kwargs: {
              enable_thinking: false,
              ...(isNemotron35BoltE4BModel(selection.model) ? { conversational_mode: generationSettings().conversational_mode === true } : {})
            }
          };
        }
        let response, raw;
        if (selection.provider === 'lmstudio') {
          const optionalTitleParameters = [
            { key: 'reasoning_effort', pattern: /(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)[^\n]{0,120}reasoning_effort|reasoning_effort[^\n]{0,120}(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)/i },
            { key: 'chat_template_kwargs', pattern: /(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)[^\n]{0,120}(?:chat_template_kwargs|enable_thinking|conversational_mode)|(?:chat_template_kwargs|enable_thinking|conversational_mode)[^\n]{0,120}(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)/i },
            { key: 'max_tokens', pattern: /(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)[^\n]{0,120}max_tokens|max_tokens[^\n]{0,120}(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)/i }
          ];
          for (let attempt = 0; attempt <= optionalTitleParameters.length; attempt++) {
            response = await fetch(url, { method: 'POST', signal: controller.signal, headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', ...modelAuthorizationHeaders(selection.connection) }, body: JSON.stringify(payload) });
            raw = await readBoundedResponseText(response, 128 * 1024);
            if (response.ok) break;
            const rejected = [400, 422].includes(response.status)
              ? optionalTitleParameters.find(item => Object.prototype.hasOwnProperty.call(payload, item.key) && item.pattern.test(raw))
              : null;
            if (!rejected) throw new Error(`Title generation HTTP ${response.status}: ${raw.slice(0, 300)}`);
            delete payload[rejected.key];
          }
        } else {
          response = await fetch(url, { method: 'POST', signal: controller.signal, headers: { 'Content-Type': 'application/json', 'Accept': 'application/json', ...modelAuthorizationHeaders(selection.connection) }, body: JSON.stringify(payload) });
          raw = await readBoundedResponseText(response, 128 * 1024);
          if (!response.ok) throw new Error(`Title generation HTTP ${response.status}: ${raw.slice(0, 300)}`);
        }
        if (!response?.ok) throw new Error('Title generation failed after LM Studio compatibility retries.');
        const parsed = JSON.parse(raw);
        let title = selection.provider === 'ollama'
          ? (parsed.response || '')
          : extractOpenAITitleText(parsed);
        if (selection.provider === 'lmstudio' && !String(title || '').trim()) {
          const choice = parsed?.choices?.[0];
          const messageFields = choice?.message && typeof choice.message === 'object' ? Object.keys(choice.message).join(',') : 'missing';
          throw new Error(`LM Studio title response contained no assistant text (finish_reason: ${choice?.finish_reason || 'missing'}; message fields: ${messageFields}).`);
        }
        title = String(title).replace(/[\r\n]+/g, ' ').trim().replace(/^["'` ]+|["'` ]+$/g, '');
        title = title.replace(/^(?:title|conversation title)\s*:\s*/i, '').replace(/\s+/g, ' ').trim();
        if (title.length > 64) title = `${title.slice(0, 61).trimEnd()}…`;
        if (title) setConversationTitle(title);
      } catch (error) {
        if (error?.name !== 'AbortError') console.warn('RunMind conversation title generation failed; keeping the prompt-derived title.', error);
      } finally {
        clearTimeout(timeout);
        if (titleGenerationController === controller) titleGenerationController = null;
      }
    }
    window.addEventListener('pagehide', () => {
      titleGenerationController?.abort(); activeController?.abort();
      branchCompareController?.abort(); researchDiagnosticsController?.abort();
    });
    function createFallbackConversationTitle(prompt) {
      const value = String(prompt || '').split(/\s+/).filter(Boolean).join(' ').trim().replace(/[.!?]+$/, '');
      if (!value) return 'RunMind Chat';
      const words = value.split(' ').slice(0, 7).join(' ');
      const title = words.length > 58 ? `${words.slice(0, 55).trimEnd()}…` : words;
      return title.charAt(0).toUpperCase() + title.slice(1);
    }
    function setConversationTitle(value) {
      const title = String(value || '').replace(/\s+/g, ' ').trim().slice(0, 72);
      if (!title) return;
      conversationDisplayTitle = title;
      const heading = $('#conversationTitle');
      if (heading) heading.textContent = title;
      document.title = `RunMind — ${title}`;
    }
    window.setConversationTitle = setConversationTitle;
    function conversationTitle() {
      if (conversationDisplayTitle) return conversationDisplayTitle;
      const first = messages.find(m => m.role === 'user' && m.content);
      const base = (first?.content || BOOT.title || 'RunMind Conversation').replace(/\s+/g, ' ').trim();
      return base.length > 72 ? base.slice(0, 69) + '…' : base;
    }
    function portableConversation(options = {}) {
      const includeBranches = options.includeBranches !== false;
      const activeBranch = conversationBranches.find(branch => branch.id === activeConversationBranchId);
      const activeMessages = serializeConversationMessages(allMessages());
      const payload = {
        format: 'runmind', version: 2, title: conversationTitle(),
        createdAt: new Date(conversationStartedAt).toISOString(), updatedAt: new Date().toISOString(),
        session: { provider: selectedModel()?.provider || null, model: selectedModel()?.model || null, connectionId: selectedModel()?.connectionId || null, serverName: selectedModel()?.serverName || null, thinkEnabled: !!thinkToggle.checked, defaultThinkingOpen, generationSettings: generationSettings(), lmStudioApiMode: settingLmStudioApiMode.value, contextSummary, contextSummaryThrough, historyTurns: readBoundedNumber(settingHistoryTurns, 2, 200, true), historyTokenBudget: readBoundedNumber(settingHistoryTokenBudget, 256, 1000000, true) },
        workspaceContext: captureWorkspaceContext(),
        messages: activeMessages
      };
      if (includeBranches && conversationBranches.length > 1) {
        if (!activeBranch) throw new Error('The active conversation branch could not be found.');
        payload.branchWorkspace = {
          version: 1, activeId: activeConversationBranchId,
          branches: conversationBranches.slice(0, CONVERSATION_BRANCH_LIMIT).map(branch => ({
            id: String(branch.id), name: String(branch.name || (branch.id === 'main' ? 'Original' : 'Branch')).slice(0, 48),
            createdAt: String(branch.createdAt || ''), updatedAt: String(branch.updatedAt || ''),
            ...(typeof branch.parentId === 'string' ? { parentId: branch.parentId } : {}),
            ...(Number.isInteger(branch.forkedFromMessageIndex) ? { forkedFromMessageIndex: branch.forkedFromMessageIndex } : {}),
            ...(branch.id === activeConversationBranchId ? {} : { messages: serializeConversationMessages(branch.messages || []) })
          }))
        };
      }
      return payload;
    }
    function handleNativeSaveResult(event) {
      const result = event?.data;
      if (!result || result.type !== 'runmind.saveConversationResult'
        || typeof result.requestId !== 'string') return;
      const resolve = pendingNativeSaveRequests.get(result.requestId);
      if (!resolve) return;
      pendingNativeSaveRequests.delete(result.requestId);
      if (result.cancelled) { resolve(false); return; }
      if (!result.success) {
        showToast(`Could not save conversation: ${result.error || 'The save operation failed.'}`);
        resolve(false);
        return;
      }
      const fileName = String(result.fileName || 'Conversation.runmind');
      setConversationTitle(conversationFileTitle(fileName));
      showToast(`Conversation saved as ${fileName}.`);
      resolve(true);
    }
    window.chrome?.webview?.addEventListener('message', handleNativeSaveResult);
    async function saveSessionFile() {
      if (messages.some(message => message.pending)) { showToast('Stop or wait for the current response before saving.'); return false; }
      if (!allMessages().length) { showToast('Send a message before saving a conversation.'); return false; }
      const scope = conversationBranches.length > 1 ? await requestSaveConversationScope() : 'active';
      if (!scope) return false;
      if (scope === 'all' && !conversationBranchHistoryComplete) {
        showToast('All branches was not saved because RunMind could not verify the complete branch history. Restore the full archive or save the active branch only.');
        return false;
      }
      if (scope === 'active' && !conversationBranchHistoryComplete)
        showToast('Only the locally available active-branch history will be saved; some older legacy messages or attachment bytes may already be missing.');
      saveActiveConversationBranch();
      const suggestedTitle = filenameSafe(conversationTitle());
      let payload;
      try { payload = portableConversation({ includeBranches: scope === 'all' }); }
      catch (error) { showToast(`Could not prepare the conversation file: ${error?.message || error}`); return false; }
      const serializedPayload = JSON.stringify(payload);
      const payloadBytes = new TextEncoder().encode(serializedPayload).byteLength;
      if (payloadBytes > 50 * 1024 * 1024) { showToast(`This conversation is ${formatBytes(payloadBytes)}. RunMind conversation files are limited to 50 MB.`); return false; }
      const bridge = window.chrome?.webview;
      if (bridge && typeof bridge.postMessage === 'function') {
        const requestId = `save-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
        const saved = await new Promise(resolve => {
          pendingNativeSaveRequests.set(requestId, resolve);
          try {
            bridge.postMessage({
              type: 'runmind.saveConversation',
              requestId,
              fileName: `${suggestedTitle}.runmind`,
              conversation: payload,
            });
            $('#conversationMenu').open = false;
          } catch (error) {
            pendingNativeSaveRequests.delete(requestId);
            showToast(`Could not open the save dialog: ${error?.message || error}`);
            resolve(false);
          }
        });
        return saved;
      }

      // Browser-only fallback for previewing the shared page outside a desktop host.
      payload.title = suggestedTitle;
      setConversationTitle(suggestedTitle);
      downloadFile(`${suggestedTitle}.runmind`, serializedPayload, 'application/json;charset=utf-8');
      $('#conversationMenu').open = false;
      showToast(`Conversation downloaded as ${suggestedTitle}.runmind${scope === 'all' ? ' with all branches' : ' with the active branch only'}.`);
      return true;
    }
    function conversationFileTitle(fileName) {
      const value = String(fileName || '').replace(/\.runmind$/i, '').replace(/\s+/g, ' ').trim();
      return value || 'RunMind Chat';
    }
    function applyImportedGenerationSettings(settings) {
      const source = settings && typeof settings === 'object' ? settings : {};
      const pairs = [[settingTemperature, source.temperature], [settingTopP, source.top_p ?? source.topP], [settingMaxTokens, source.max_tokens ?? source.maxTokens], [settingContextLength, source.context_length ?? source.contextLength], [settingSeed, source.seed]];
      for (const [input, value] of pairs) {
        const number = value === null || value === undefined || value === '' ? null : Number(value);
        input.value = number !== null && Number.isFinite(number) ? String(number) : '';
      }
      settingNemotronConversationalMode.checked = source.conversational_mode === true;
    }
    function validateConversationMessages(rawMessages, sessionData, label = 'Conversation', allowEmpty = false) {
      if (!Array.isArray(rawMessages) || rawMessages.length > 10000 || (!allowEmpty && rawMessages.length < 1))
        throw new Error(`${label} must contain ${allowEmpty ? 'no more than' : 'between 1 and'} 10,000 messages.`);
      const clean = rawMessages.map((message, index) => {
        if (!message || !['user', 'assistant'].includes(message.role) || typeof message.content !== 'string') throw new Error(`Message ${index + 1} is invalid.`);
        if (Array.isArray(message.attachments) && message.attachments.length > ATTACHMENT_LIMITS.maxFilesPerMessage) throw new Error(`Message ${index + 1} exceeds the ${ATTACHMENT_LIMITS.maxFilesPerMessage}-attachment limit.`);
        const attachments = Array.isArray(message.attachments) ? message.attachments.map(file => {
          if (!file || typeof file !== 'object' || Array.isArray(file)) throw new Error(`An attachment in message ${index + 1} is invalid.`);
          const declaredType = String(file.type || '').slice(0, 200).toLowerCase();
          const allowedImageTypes = new Set(['image/png', 'image/jpeg', 'image/gif', 'image/webp']);
          const allowedBinaryTypes = new Set(['application/pdf', 'application/zip']);
          let normalizedType = declaredType;
          let decodedSize = 0;
          let dataUrl = '';
          if (typeof file.dataUrl === 'string' && file.dataUrl.length > 36 * 1024 * 1024) throw new Error(`Attachment in message ${index + 1} has an oversized data URL.`);
          if (typeof file.dataUrl === 'string') {
            const match = file.dataUrl.match(/^data:(image\/(?:png|jpeg|gif|webp)|application\/(?:pdf|zip));base64,([a-z0-9+/]+=*)$/i);
            if (!match) throw new Error(`Attachment in message ${index + 1} has an unsupported or malformed data URL.`);
            normalizedType = match[1].toLowerCase();
            if (declaredType && declaredType !== normalizedType) throw new Error(`Attachment in message ${index + 1} has a MIME type that does not match its data URL.`);
            try {
              const decoded = atob(match[2]);
              decodedSize = decoded.length;
              const isImage = allowedImageTypes.has(normalizedType);
              const limit = isImage ? ATTACHMENT_LIMITS.maxImageBytes : normalizedType === 'application/pdf' ? ATTACHMENT_LIMITS.maxPdfBytes : ATTACHMENT_LIMITS.maxArchiveBytes;
              if (decodedSize > limit) throw new Error(`Attachment in message ${index + 1} exceeds its ${formatBytes(limit)} decoded size limit.`);
              if (normalizedType === 'application/pdf' && (decoded.length < 5 || decoded.slice(0, 5) !== '%PDF-')) throw new Error(`Attachment in message ${index + 1} does not have a valid PDF signature.`);
              if (normalizedType === 'application/zip' && (decoded.length < 4 || decoded.charCodeAt(0) !== 0x50 || decoded.charCodeAt(1) !== 0x4B || ![[0x03,0x04],[0x05,0x06],[0x07,0x08]].some(pair => decoded.charCodeAt(2) === pair[0] && decoded.charCodeAt(3) === pair[1]))) throw new Error(`Attachment in message ${index + 1} does not have a valid ZIP signature.`);
            } catch (error) {
              if (/exceeds its .* decoded size limit|valid (?:PDF|ZIP) signature/.test(error?.message || '')) throw error;
              throw new Error(`Attachment in message ${index + 1} contains invalid base64 data.`);
            }
            dataUrl = file.dataUrl;
          }
          if (declaredType.startsWith('image/') && !allowedImageTypes.has(declaredType)) throw new Error(`Attachment in message ${index + 1} uses an unsupported image type.`);
          if (declaredType.startsWith('application/') && !allowedBinaryTypes.has(declaredType) && !['application/json','application/xml','application/javascript','application/x-yaml','application/yaml','application/x-ndjson'].includes(declaredType)) throw new Error(`Attachment in message ${index + 1} uses an unsupported binary type.`);
          const size = Math.max(0, Number(file.size) || 0, decodedSize);
          const isImageData = dataUrl && allowedImageTypes.has(normalizedType);
          const isBinaryData = dataUrl && allowedBinaryTypes.has(normalizedType);
          const limit = isImageData ? ATTACHMENT_LIMITS.maxImageBytes : isBinaryData ? (normalizedType === 'application/pdf' ? ATTACHMENT_LIMITS.maxPdfBytes : ATTACHMENT_LIMITS.maxArchiveBytes) : ATTACHMENT_LIMITS.maxTextBytes;
          if (size > limit) throw new Error(`Attachment in message ${index + 1} exceeds its ${formatBytes(limit)} per-file limit.`);
          return { name: String(file.name || 'Attachment').slice(0, 512), type: normalizedType, size, ...(typeof file.text === 'string' ? { text: file.text.slice(0, 2 * 1024 * 1024) } : {}), ...(dataUrl ? { dataUrl } : {}) };
        }) : [];
        if (attachments.reduce((total, file) => total + file.size, 0) > ATTACHMENT_LIMITS.maxTotalBytes) throw new Error(`Attachments in message ${index + 1} exceed the combined 25 MB limit.`);
        const parsedTime = Date.parse(message.timestamp || '');
        return { role: message.role, content: message.content.slice(0, 20 * 1024 * 1024), thinking: typeof message.thinking === 'string' ? message.thinking.slice(0, 20 * 1024 * 1024) : '', thinkingOpen: typeof message.thinkingOpen === 'boolean' ? message.thinkingOpen : (sessionData.session?.defaultThinkingOpen ?? true), thinkRequested: !!message.thinkRequested, ...(typeof message.thinkEnabled === 'boolean' ? { thinkEnabled: message.thinkEnabled } : {}), generationSettings: message.generationSettings && typeof message.generationSettings === 'object' ? message.generationSettings : null, isContinuation: !!message.isContinuation, attachments, timestamp: Number.isFinite(parsedTime) ? parsedTime : Date.now(), provider: ['ollama', 'lmstudio'].includes(message.provider) ? message.provider : null, connectionName: typeof message.connectionName === 'string' ? message.connectionName.slice(0, 120) : null, model: typeof message.model === 'string' ? message.model.slice(0, 512) : null, error: !!message.error };
      });
      return clean;
    }
    function validateSessionFile(data) {
      if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('The selected file is not a conversation object.');
      if (data.format !== 'runmind') throw new Error('This is not a RunMind `.runmind` conversation file.');
      const formatVersion = Number(data.version);
      if (![1, 2].includes(formatVersion)) throw new Error(`Unsupported conversation format version: ${String(data.version)}.`);
      const workspaceContextData = formatVersion >= 2 ? createWorkspaceContext(data.workspaceContext) : null;
      const clean = validateConversationMessages(data.messages, data, 'Active conversation', false);
      let branchWorkspace = null;
      const rawWorkspace = data.branchWorkspace;
      if (rawWorkspace !== undefined && rawWorkspace !== null) {
        if (!rawWorkspace || typeof rawWorkspace !== 'object' || rawWorkspace.version !== 1 || !Array.isArray(rawWorkspace.branches)
          || rawWorkspace.branches.length < 2 || rawWorkspace.branches.length > CONVERSATION_BRANCH_LIMIT)
          throw new Error('The saved branch workspace is malformed or exceeds the supported branch limit.');
        if (typeof rawWorkspace.activeId !== 'string' || rawWorkspace.activeId.length > 100)
          throw new Error('The saved branch workspace has an invalid active branch identifier.');
        const seenIds = new Set();
        const seenNames = new Set();
        let totalMessages = clean.length;
        const branches = rawWorkspace.branches.map((branch, index) => {
          if (!branch || typeof branch !== 'object' || typeof branch.id !== 'string' || !branch.id || branch.id.length > 100)
            throw new Error(`Branch ${index + 1} has an invalid identifier.`);
          if (seenIds.has(branch.id)) throw new Error(`Branch ${index + 1} duplicates another branch identifier.`);
          seenIds.add(branch.id);
          const name = branch.id === 'main' ? 'Original' : String(branch.name || '').trim().slice(0, 48);
          if (!name) throw new Error(`Branch ${index + 1} has no name.`);
          const nameKey = name.toLocaleLowerCase();
          if (seenNames.has(nameKey)) throw new Error(`The saved workspace contains duplicate branch names: ${name}.`);
          seenNames.add(nameKey);
          let messagesForBranch;
          if (branch.id === rawWorkspace.activeId) {
            messagesForBranch = clean;
          } else {
            messagesForBranch = validateConversationMessages(branch.messages, data, `Branch “${name}”`, true);
            totalMessages += messagesForBranch.length;
          }
          const parentId = typeof branch.parentId === 'string' ? branch.parentId : null;
          const forkedFromMessageIndex = Number.isInteger(branch.forkedFromMessageIndex) ? branch.forkedFromMessageIndex : null;
          if ((parentId === null) !== (forkedFromMessageIndex === null))
            throw new Error(`Branch “${name}” has incomplete fork-origin metadata.`);
          if (branch.id === 'main' && parentId !== null)
            throw new Error('The Original branch cannot have a parent branch.');
          return {
            id: branch.id, name,
            createdAt: typeof branch.createdAt === 'string' ? branch.createdAt.slice(0, 80) : '',
            updatedAt: typeof branch.updatedAt === 'string' ? branch.updatedAt.slice(0, 80) : '',
            ...(parentId !== null ? { parentId, forkedFromMessageIndex } : {}),
            messages: messagesForBranch
          };
        });
        if (!seenIds.has('main')) throw new Error('The saved branch workspace is missing the Original branch.');
        if (!seenIds.has(rawWorkspace.activeId)) throw new Error('The saved branch workspace points to a missing active branch.');
        const branchById = new Map(branches.map(branch => [branch.id, branch]));
        for (const branch of branches) {
          if (!branch.parentId) continue; // Legacy branch files have no fork-origin metadata.
          if (branch.parentId === branch.id || !seenIds.has(branch.parentId))
            throw new Error(`Branch “${branch.name}” refers to an invalid parent branch.`);
          const parent = branchById.get(branch.parentId);
          if (!parent || branch.forkedFromMessageIndex < 0 || branch.forkedFromMessageIndex >= parent.messages.length)
            throw new Error(`Branch “${branch.name}” has an invalid fork point.`);
          const visited = new Set([branch.id]);
          let ancestor = parent;
          while (ancestor) {
            if (visited.has(ancestor.id)) throw new Error(`The saved branch workspace contains a parent cycle at “${ancestor.name}”.`);
            visited.add(ancestor.id);
            ancestor = ancestor.parentId ? branchById.get(ancestor.parentId) : null;
          }
        }
        if (totalMessages > 20000) throw new Error('The saved branch workspace exceeds the 20,000-message total limit.');
        branchWorkspace = { version: 1, activeId: rawWorkspace.activeId, branches };
      }
      return { data, messages: clean, branchWorkspace, workspaceContext: workspaceContextData };
    }
    async function activateWorkspaceContext(value, { allowLegacyMigration = false } = {}) {
      workspaceContext = value ? createWorkspaceContext(value) : createWorkspaceContext();
      bootWorkspaceContextId = null;
      agentDirectoryHandle = null;
      persistedDirectoryHandle = null;
      if (typeof projectIndexController !== 'undefined') projectIndexController?.abort();
      if (typeof scheduleProjectIndexAutoRefresh === 'function') scheduleProjectIndexAutoRefresh('workspace context changed');
      applyAgentSettings();
      if (workspaceContext.instructions !== null) {
        settingSystemPrompt.value = workspaceContext.instructions;
        updateSystemPromptPreview();
      }
      workspaceRestorePromise = restoreAgentFolderHandle({ allowLegacyMigration });
      // Opening a saved conversation restores its trusted MCP descriptors, but
      // never starts external MCP processes/network sessions automatically.
      workspaceMcpRestorePromise = restoreMcpConfigs(workspaceContext.mcpServers);
      renderPermissionPolicy();
      await Promise.all([workspaceRestorePromise, workspaceMcpRestorePromise]);
      updateMcpModeButton();
      await refreshAgentSkillManager();
      await refreshProjectIndexStatus();
      await refreshDeveloperWorkflowList();
    }
    async function applySessionData(data, restored, fileName, branchWorkspace = null, savedWorkspaceContext = null) {
      const contextToApply = savedWorkspaceContext || (Number(data.version) === 1 ? workspaceContext : null);
      if (bootWorkspaceContextId && contextToApply?.id === bootWorkspaceContextId) {
        bootWorkspaceContextId = null;
        await Promise.all([workspaceRestorePromise, workspaceMcpRestorePromise]);
      } else await activateWorkspaceContext(contextToApply);
      conversationBranchHistoryComplete = true;
      conversationBranches = branchWorkspace?.branches?.length
        ? branchWorkspace.branches.map(branch => ({
            id: branch.id, name: branch.name, createdAt: branch.createdAt || new Date().toISOString(), updatedAt: branch.updatedAt || '',
            ...(typeof branch.parentId === 'string' ? { parentId: branch.parentId } : {}),
            ...(Number.isInteger(branch.forkedFromMessageIndex) ? { forkedFromMessageIndex: branch.forkedFromMessageIndex } : {}),
            messages: (branch.messages || []).map(message => cloneBranchMessage(message, false))
          }))
        : [{ id: 'main', name: 'Original', createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(), messages: restored.map(message => cloneBranchMessage(message, false)) }];
      activeConversationBranchId = branchWorkspace?.activeId && conversationBranches.some(branch => branch.id === branchWorkspace.activeId)
        ? branchWorkspace.activeId : 'main';
      const activeBranch = conversationBranches.find(branch => branch.id === activeConversationBranchId);
      messages.splice(0, messages.length, ...(activeBranch?.messages || restored).map(message => cloneBranchMessage(message, false)));
      saveConversationBranches(); renderConversationBranchBar();
      // The selected file name is authoritative. Renames performed in Explorer are
      // reflected in both the native window caption and in-app conversation heading.
      const selectedTitle = fileName ? conversationFileTitle(fileName) : (String(data.title || '').trim() || 'RunMind Chat');
      setConversationTitle(selectedTitle);
      deepResearchTask = null; researchLastAssistantMessage = null;
      void deleteResearchTaskCheckpoint();
      deepResearchEnabled = false;
      try { sessionStorage.removeItem(DEEP_RESEARCH_STORAGE_KEY); } catch (_) {}
      deepResearchButton.classList.remove('active'); deepResearchButton.setAttribute('aria-pressed', 'false');
      deepResearchButton.title = 'Deep Research mode is off'; deepResearchButton.setAttribute('aria-label', deepResearchButton.title);
      setResearchPanelOpen(false);
      setAgentMode(false);
      lmStudioNativeResponseId = null; lmStudioNativeModelKey = null;
      const created = Date.parse(data.createdAt || '');
      conversationStartedAt = Number.isFinite(created) ? created : (restored[0]?.timestamp || Date.now());
      defaultThinkingOpen = typeof data.session?.defaultThinkingOpen === 'boolean' ? data.session.defaultThinkingOpen : (restored.at(-1)?.thinkingOpen ?? true);
      const session = data.session && typeof data.session === 'object' ? data.session : {};
      contextSummary = typeof session.contextSummary === 'string' ? session.contextSummary.slice(0, 200000) : '';
      contextSummaryThrough = Math.max(0, Number(session.contextSummaryThrough) || 0);
      contextSummaryDirty = false;
      settingLmStudioApiMode.value = session.lmStudioApiMode === 'native' ? 'native' : 'openai';
      settingHistoryTurns.value = Number.isFinite(Number(session.historyTurns)) && session.historyTurns !== null && Number(session.historyTurns) !== 12
        ? String(Math.min(200, Math.max(2, Number(session.historyTurns)))) : '';
      settingHistoryTokenBudget.value = session.historyTokenBudget === null || session.historyTokenBudget === undefined || !Number.isFinite(Number(session.historyTokenBudget)) ? '' : String(Math.min(1000000, Math.max(256, Number(session.historyTokenBudget))));
      if (typeof session.thinkEnabled === 'boolean') thinkToggle.checked = session.thinkEnabled;
      applyImportedGenerationSettings(session.generationSettings || restored.slice().reverse().find(m => m.generationSettings)?.generationSettings || {});
      syncThinkButton();
      const savedProvider = ['ollama', 'lmstudio'].includes(session.provider) ? session.provider : restored.slice().reverse().find(m => m.provider)?.provider;
      const savedModel = typeof session.model === 'string' ? session.model : restored.slice().reverse().find(m => m.model)?.model;
      let modelUnavailable = false;
      if (savedProvider && savedModel) {
        const option = [...modelSelect.options].find(item => item.dataset.provider === savedProvider && item.dataset.model === savedModel && session.connectionId && item.dataset.serverId === session.connectionId)
          || [...modelSelect.options].find(item => item.dataset.provider === savedProvider && item.dataset.model === savedModel);
        const savedKey = option?.value || `${savedProvider}::${savedModel}`;
        preferredModelKey = savedKey;
        if (option) modelSelect.value = option.value;
        else modelUnavailable = true;
      }
      promptInput.value = '';
      pendingAttachments = [];
      renderPendingAttachments();
      renderMessages(false);
      updateContextManagerUI();
      autoResizePrompt();
      savePreferences();
      void updateSelectedModelInfo();
      $('#conversationMenu').open = false;
      showToast(modelUnavailable ? `Conversation opened; model “${savedModel}” is unavailable. Choose a model to continue.` : `Opened “${filenameSafe(selectedTitle)}”.`);
    }
    function openSessionInNewWindow(data, fileName) {
      const bridge = window.chrome?.webview;
      if (!bridge || typeof bridge.postMessage !== 'function') {
        throw new Error('Opening a conversation in a new window is only available in the RunMind desktop hosts.');
      }
      bridge.postMessage({
        type: 'runmind.openConversationInNewWindow',
        fileName: String(fileName || 'Conversation.runmind').slice(0, 255),
        conversation: data,
      });
    }
    async function postNewConversationHostMessage(destination) {
      const bridge = window.chrome?.webview;
      if (!bridge || typeof bridge.postMessage !== 'function')
        throw new Error('Starting a new conversation in a desktop window is only available in the RunMind desktop hosts.');
      const selection = selectedModel();
      if (destination === 'current') {
        const previousSessionId = CHAT_WINDOW_INSTANCE_ID;
        try { await retireConversationSession(previousSessionId); } catch (_) {}
        createNewChatSessionId();
        configureWindowScopedStorage();
        loadWorkflowState(); renderWorkflowView();
        researchSessionClosing = false;
        await touchConversationSession();
      }
      bridge.postMessage({
        type: destination === 'new' ? 'runmind.newConversationInNewWindow' : 'runmind.newConversationInCurrentWindow',
        provider: selection?.provider || null,
        model: selection?.model || null,
        connectionId: selection?.connectionId || null,
        thinkEnabled: !!thinkToggle.checked
      });
    }
    function hasCurrentConversationWork() {
      return allMessages().length > 0 || !!promptInput.value.trim() || pendingAttachments.length > 0
        || contextSummaryDirty || (Array.isArray(conversationBranches) && conversationBranches.length > 1)
        || !!activeBranchCompareSession;
    }
    async function stopActiveWorkForNewConversation() {
      const controller = activeController;
      if (!controller) return;
      const completion = activeControllerCompletion;
      showToast('Stopping current work before starting a new conversation…');
      controller.abort();
      if (pendingAgentApproval) pendingAgentApproval.finish(false);
      await completion;
    }
    async function startNewConversationInCurrentWindow() {
      let choice = 'discard';
      if (hasCurrentConversationWork()) {
        const canSave = allMessages().length > 0;
        const hasUnsentContent = !!promptInput.value.trim() || pendingAttachments.length > 0 || editingMessageIndex !== null || !!draftBeforeEdit;
        const choiceResult = await requestNewConversationUnsavedChoice(canSave, {
          hasUnsentContent,
          hasBranchComparison: !!activeBranchCompareSession
        });
        if (!choiceResult) return;
        choice = choiceResult;
      }
      // Do not interrupt a generation, research run, or tool operation until the user
      // confirms replacing the current window. Cancel leaves the current work untouched.
      await stopActiveWorkForNewConversation();
      if (choice === 'save') {
        const saved = await saveSessionFile();
        if (!saved) return;
      }
      try {
        await postNewConversationHostMessage('current');
        $('#conversationMenu').open = false;
      } catch (error) {
        showToast(error?.message || 'Could not start a new conversation.');
      }
    }
    async function startNewConversation() {
      const destination = await requestNewConversationDestination();
      if (!destination) return;
      if (destination === 'new') {
        try {
          await postNewConversationHostMessage('new');
          $('#conversationMenu').open = false;
          showToast('Opening a new conversation in a separate window.');
        } catch (error) {
          showToast(error?.message || 'Could not open a new conversation window.');
        }
        return;
      }
      await startNewConversationInCurrentWindow();
    }
    async function openSessionFile(file, options = {}) {
      if (!file) return;
      const fileName = String(file.name || '');
      if (!/\.runmind$/i.test(fileName)) { showToast('Choose a RunMind conversation file with the .runmind extension.'); return; }
      if (file.size > 50 * 1024 * 1024) { showToast('Conversation files must be 50 MB or smaller.'); return; }
      if (activeController || messages.some(message => message.pending)) { showToast('Stop the current response before opening another conversation.'); return; }
      try {
        const parsed = JSON.parse(await file.text());
        const { data, messages: restored, branchWorkspace, workspaceContext: workspaceContextData } = validateSessionFile(parsed);
        if (!restored.length) throw new Error('This conversation contains no messages.');
        const destination = options.destination || await requestOpenSessionDestination(fileName);
        if (!destination) return;
        if (destination === 'new') {
          openSessionInNewWindow(data, fileName);
          showToast(`Opening “${filenameSafe(conversationFileTitle(fileName))}” in a new window.`);
          return;
        }
        if (messages.length && options.confirmReplace !== false
          && !window.confirm('Open this conversation in the current window and replace the current chat? Any unsaved changes will be lost.')) return;
        await applySessionData(data, restored, fileName, branchWorkspace, workspaceContextData);
      } catch (error) {
        showToast(`Could not open conversation: ${error?.message || String(error)}`);
      } finally { sessionFileInput.value = ''; }
    }
    function openWebUiExport() {
      const items = allMessages(); const ids = items.map((_, i) => `local-llm-${i + 1}-${Math.random().toString(36).slice(2, 9)}`); const tree = {};
      items.forEach((m, i) => {
        const timestamp = Math.floor(Number(m.timestamp || Date.now()) / 1000);
        tree[ids[i]] = { id: ids[i], parentId: i === 0 ? null : ids[i - 1], childrenIds: i + 1 < ids.length ? [ids[i + 1]] : [], role: m.role, content: messageContentWithTextAttachments(m), timestamp, ...(m.thinking ? { reasoning_content: m.thinking } : {}) };
        if (m.role === 'assistant') { tree[ids[i]].model = m.model || selectedModel()?.model || BOOT.initialModel || 'local-model'; tree[ids[i]].done = !m.pending; }
      });
      const models = [...new Set(items.filter(m => m.role === 'assistant').map(m => m.model).filter(Boolean))];
      const created = Math.floor(conversationStartedAt / 1000); const updated = Math.floor(Date.now() / 1000);
      return [{ chat: { title: conversationTitle(), models, history: { currentId: ids.length ? ids[ids.length - 1] : null, messages: tree } }, meta: { tags: ['local-llm'] }, pinned: false, folder_id: null, created_at: created, updated_at: updated }];
    }
    function openAiRequestExport() {
      const choice = selectedModel();
      const thinkingEnabled = !!thinkToggle.checked;
      const settings = generationSettings();
      return {
        model: choice?.model || BOOT.initialModel || '',
        messages: providerMessages('lmstudio', allMessages()),
        stream: true,
        reasoning_effort: thinkingEnabled ? 'high' : 'none',
        chat_template_kwargs: {
          enable_thinking: thinkingEnabled,
          ...(isNemotron35BoltE4BModel(choice?.model) ? { conversational_mode: settings.conversational_mode === true } : {})
        },
        ...(settings.temperature !== null ? { temperature: settings.temperature } : {}),
        ...(settings.top_p !== null ? { top_p: settings.top_p } : {}),
        ...(settings.max_tokens !== null ? { max_tokens: settings.max_tokens } : {}),
        ...(settings.seed !== null ? { seed: settings.seed } : {})
      };
    }
    function markdownExport() {
      const rows = [`# ${conversationTitle()}`, '', `Exported: ${new Date().toLocaleString()}`, ''];
      for (const m of allMessages()) {
        const role = m.role === 'user' ? 'User' : 'Assistant';
        rows.push(`## ${role}${m.model ? ` · ${m.model}` : ''}${m.error ? ' · Request error' : ''}`, '');
        if (m.attachments?.length) rows.push(`Attachments: ${m.attachments.map(file => `${file.name} (${formatBytes(file.size || 0)})`).join(', ')}`, '');
        if (m.thinking) rows.push('<details><summary>Thinking</summary>', '', m.thinking, '', '</details>', '');
        rows.push(m.content, '');
      }
      return rows.join('\n');
    }
    function csvCell(value) {
      let text = String(value ?? '');
      if (/^[\t\r\n]/.test(text) || /^[\s]*[=+\-@]/.test(text)) text = "'" + text;
      return `"${text.replace(/"/g, '""')}"`;
    }
    function csvExport() {
      const rows = [['timestamp', 'role', 'provider', 'model', 'error', 'thinking', 'attachments', 'content']];
      for (const m of allMessages()) rows.push([dateLabel(m.timestamp), m.role, m.provider || '', m.model || '', m.error ? 'true' : 'false', m.thinking || '', (m.attachments || []).map(file => file.name).join('; '), m.content]);
      return '\uFEFF' + rows.map(row => row.map(csvCell).join(',')).join('\r\n');
    }
