    'use strict';

    const standaloneCss = `
      :root{color-scheme:light dark;--bg:#101216;--surface:#171a21;--text:#e9edf5;--muted:#a0a8b7;--border:#323a49;--accent:#9aabff;--user:#232b42;--code:#11141a;--radius:12px;--font:Segoe UI,system-ui,sans-serif;--mono:Consolas,Cascadia Code,monospace}
      @media(prefers-color-scheme:light){:root{color-scheme:light;--bg:#f4f6fa;--surface:#fff;--text:#20232b;--muted:#616a7a;--border:#d7dce6;--accent:#3d55bf;--user:#e7ebff;--code:#f6f7fa}}
      *{box-sizing:border-box}body{margin:0;padding:28px;background:var(--bg);color:var(--text);font:15px/1.7 var(--font)}main{max-width:1000px;margin:0 auto}.export-title{font-size:24px;margin:0 0 6px}.export-note{color:var(--muted);font-size:12px;margin-bottom:22px}.message{margin:0 0 18px;padding:15px 17px;border:1px solid var(--border);border-radius:var(--radius);background:var(--surface);overflow-wrap:anywhere}.message.user{background:var(--user)}.message.error{border-color:#bd5860}.message-meta{display:flex;flex-wrap:wrap;gap:9px;margin-bottom:8px;color:var(--muted);font-size:11px;font-weight:700;text-transform:uppercase}.message-meta .role{color:var(--text)}.message-meta .error-label{color:#ff858a}.message-content p{margin:0 0 12px}.message-content h1,.message-content h2,.message-content h3{line-height:1.35;margin:20px 0 9px}.message-content ul,.message-content ol{padding-left:24px}.message-content blockquote{margin:10px 0;padding:3px 13px;border-left:3px solid var(--accent);color:var(--muted)}.message-content a{color:var(--accent)}.message-content pre{position:relative;overflow:auto;padding:38px 14px 14px;background:var(--code);border:1px solid var(--border);border-radius:8px}.message-content code{font:.92em var(--mono);background:var(--code);padding:.13em .35em;border-radius:4px}.message-content pre code{display:block;background:transparent;padding:0;white-space:pre}.copy-code{position:absolute;right:7px;top:6px;padding:3px 8px;border:1px solid var(--border);border-radius:5px;background:var(--surface);color:var(--text);cursor:pointer}.message-content hr{border:0;border-top:1px solid var(--border)}
      .message-thinking{margin:0 0 12px;border:1px solid var(--border);border-radius:var(--radius);background:var(--surface);overflow:hidden}.message-thinking summary{cursor:pointer;padding:9px 12px;color:var(--muted);font-size:12px;font-weight:700}.thinking-body{padding:10px 12px;border-top:1px solid var(--border);color:var(--muted);font-size:13px}.message-attachments{display:flex;flex-wrap:wrap;gap:7px;margin-top:10px}.message-attachment{display:inline-flex;align-items:center;gap:7px;padding:6px 9px;border:1px solid var(--border);border-radius:8px;background:var(--surface);color:var(--muted);font-size:12px}.message-attachment img{width:28px;height:28px;border-radius:6px;object-fit:cover}.message-attachment-name{overflow-wrap:anywhere}.generation-metrics{display:flex;flex-wrap:wrap;align-items:center;gap:7px;margin-top:8px;color:var(--muted);font-size:11px;line-height:1.3;opacity:.78}.generation-metrics span+span:before{content:'·';margin-right:7px;opacity:.65}
      @media print{body{padding:0;background:#fff;color:#111}.message,.message.user{background:#fff;color:#111;break-inside:avoid;box-shadow:none}.message-content pre{white-space:pre-wrap;overflow:visible}.message-content pre code{white-space:pre-wrap}.message-thinking:not([open])>.thinking-body{display:block!important}.copy-code{display:none}}
    `;
    function standaloneHtmlExport() {
      const exportMessages = allMessages();
      const renderContext = getMessageRenderContext(exportMessages);
      const body = exportMessages.map((message, index) => renderMessageMarkup(message, index, false, renderContext)).join('\n');
      const title = escapeHtml(conversationTitle());
      const copyHelper = `(function(){document.querySelectorAll('pre').forEach(function(pre){var b=document.createElement('button');b.type='button';b.className='copy-code';b.textContent='Copy code';b.addEventListener('click',function(){var c=pre.querySelector('code');var t=(c||pre).textContent||'';var a=document.createElement('textarea');a.value=t;a.style.position='fixed';a.style.opacity='0';document.body.appendChild(a);a.select();var ok=false;try{ok=document.execCommand('copy')}catch(e){}a.remove();b.textContent=ok?'Copied':'Copy failed';setTimeout(function(){b.textContent='Copy code'},1400)});pre.appendChild(b)})})();`;
      return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${title}</title><style>${standaloneCss}</style></head><body><main><h1 class="export-title">${title}</h1><div class="export-note">Standalone transcript export · no provider connection or plugin bridge</div>${body}</main><script>${copyHelper}<\/script></body></html>`;
    }
    function exportConversation() {
      const key = exportFormat.value;
      if (!key) { showToast('Choose an export format first.'); return; }
      const base = filenameSafe(conversationTitle());
      switch (key) {
        case 'html': downloadFile(`${base}.html`, standaloneHtmlExport(), 'text/html;charset=utf-8'); break;
        case 'openwebui': downloadFile(`${base}-open-webui.json`, JSON.stringify(openWebUiExport(), null, 2), 'application/json;charset=utf-8'); break;
        case 'portable': downloadFile(`${base}-conversation.json`, JSON.stringify(portableConversation(), null, 2), 'application/json;charset=utf-8'); break;
        case 'openai': downloadFile(`${base}-openai-request.json`, JSON.stringify(openAiRequestExport(), null, 2), 'application/json;charset=utf-8'); break;
        case 'markdown': downloadFile(`${base}.md`, markdownExport(), 'text/markdown;charset=utf-8'); break;
        case 'csv': downloadFile(`${base}.csv`, csvExport(), 'text/csv;charset=utf-8'); break;
        case 'txt': downloadFile(`${base}.txt`, allMessages().map(m => `${m.role === 'user' ? 'USER' : 'ASSISTANT'}${m.model ? ` · ${m.model}` : ''}${m.error ? ' · REQUEST ERROR' : ''}${m.attachments?.length ? `\nATTACHMENTS: ${m.attachments.map(file => file.name).join(', ')}` : ''}${m.thinking ? `\n\nTHINKING OUTPUT:\n${m.thinking}` : ''}\n\n${m.content}`).join('\n\n' + '─'.repeat(56) + '\n\n'), 'text/plain;charset=utf-8'); break;
        case 'jsonl': downloadFile(`${base}.jsonl`, allMessages().map(m => JSON.stringify({ role: m.role, content: m.content, thinking: m.thinking || '', attachments: (m.attachments || []).map(file => ({ name: file.name, type: file.type, size: file.size, text: file.text })), timestamp: new Date(m.timestamp || Date.now()).toISOString(), provider: m.provider || null, connectionName: m.connectionName || null, model: m.model || null, error: !!m.error })).join('\n') + '\n', 'application/x-ndjson;charset=utf-8'); break;
        case 'print': window.print(); break;
      }
    }

    transcriptInner.addEventListener('click', event => { const button = event.target.closest('[data-message-action]'); if (button) void handleMessageAction(button.dataset.messageAction, Number(button.dataset.index)); });
    transcriptInner.addEventListener('toggle', event => {
      const details = event.target;
      if (!(details instanceof HTMLDetailsElement) || !details.matches('details.message-thinking[data-thinking-index]')) return;
      const message = messages[Number(details.dataset.thinkingIndex)];
      if (message) { message.thinkingOpen = details.open; defaultThinkingOpen = details.open; }
    }, true);
    transcript.addEventListener('scroll', updateJumpToLatest, { passive: true });
    jumpToLatestButton.addEventListener('click', () => { transcript.scrollTo({ top: transcript.scrollHeight, behavior: 'smooth' }); });
    modelSelect.addEventListener('change', () => { preferredModelKey = currentModelKey(); savePreferences(); updateNemotronConversationalModeControl(); void updateSelectedModelInfo(); updateContextManagerUI(); });
    settingLmStudioApiMode.addEventListener('change', () => {
      lmStudioNativeResponseId = null; lmStudioNativeModelKey = null;
      for (const message of messages) if (message.role === 'assistant') message.providerResponseId = null;
      savePreferences(); updateContextSettingForProvider(selectedModel()?.provider || 'ollama'); updateNemotronConversationalModeControl(); updateContextManagerUI();
    });
    refreshButton.addEventListener('click', () => refreshModels(true));
    sendButton.addEventListener('click', sendMessage);
    $('#stopButton').addEventListener('click', () => { if (activeController) activeController.abort(); if (pendingAgentApproval) pendingAgentApproval.finish(false); });
    thinkButton.addEventListener('click', () => { thinkToggle.checked = !thinkToggle.checked; syncThinkButton(); savePreferences(); });
    thinkToggle.addEventListener('change', () => { syncThinkButton(); savePreferences(); });
    for (const input of [settingTemperature, settingTopP, settingMaxTokens, settingSeed]) {
      input.addEventListener('change', () => { savePreferences(); updateContextManagerUI(); });
    }
    settingNemotronConversationalMode.addEventListener('change', () => savePreferences());
    settingSystemPrompt.addEventListener('input', () => {
      if (settingSystemPrompt.value.length > MAX_CUSTOM_SYSTEM_PROMPT_CHARACTERS) settingSystemPrompt.value = settingSystemPrompt.value.slice(0, MAX_CUSTOM_SYSTEM_PROMPT_CHARACTERS);
      updateSystemPromptPreview();
      savePreferences();
      // Native LM Studio response IDs represent a server-side context chain. A changed
      // system prompt must start a fresh chain so the new instructions are not skipped.
      lmStudioNativeResponseId = null; lmStudioNativeModelKey = null;
      for (const message of messages) if (message.role === 'assistant') message.providerResponseId = null;
      updateContextManagerUI();
    });
    $('#clearSystemPrompt').addEventListener('click', () => {
      settingSystemPrompt.value = '';
      updateSystemPromptPreview();
      savePreferences();
      lmStudioNativeResponseId = null; lmStudioNativeModelKey = null;
      for (const message of messages) if (message.role === 'assistant') message.providerResponseId = null;
      updateContextManagerUI();
      showToast('Custom system instructions cleared.');
    });
    for (const input of [settingContextLength, settingHistoryTurns, settingHistoryTokenBudget]) {
      input.addEventListener('change', () => {
        if (input === settingHistoryTurns && settingHistoryTurns.value.trim()) settingHistoryTurns.value = String(Math.min(200, Math.max(2, Number(settingHistoryTurns.value) || 2)));
        if (input === settingHistoryTokenBudget && settingHistoryTokenBudget.value.trim()) settingHistoryTokenBudget.value = String(Math.min(1000000, Math.max(256, Number(settingHistoryTokenBudget.value) || 256)));
        for (const message of messages) if (message.role === 'assistant') message.providerResponseId = null;
        lmStudioNativeResponseId = null; lmStudioNativeModelKey = null;
        savePreferences(); updateContextManagerUI();
      });
    }
    summarizeHistoryButton.addEventListener('click', () => void summarizeOlderConversation());
    contextSummaryEditor.addEventListener('input', () => {
      contextSummaryDirty = true;
      contextSummaryState.textContent = 'Unsaved edits';
    });
    saveContextSummaryButton.addEventListener('click', () => {
      const edited = contextSummaryEditor.value.trim();
      if (edited && !contextSummary) contextSummaryThrough = getManagedContextPlan().recentStart;
      contextSummary = edited;
      contextSummaryDirty = false;
      if (!contextSummary) contextSummaryThrough = 0;
      for (const message of messages) if (message.role === 'assistant') message.providerResponseId = null;
      lmStudioNativeResponseId = null; lmStudioNativeModelKey = null;
      savePreferences(); updateContextManagerUI(); showToast(contextSummary ? 'Context summary saved.' : 'Context summary cleared.');
    });
    clearContextSummaryButton.addEventListener('click', () => {
      if (!contextSummary) return;
      if (!window.confirm('Clear the context summary? Your original messages will remain in the transcript, but older turns may be omitted from future model requests.')) return;
      contextSummary = ''; contextSummaryThrough = 0; contextSummaryDirty = false;
      for (const message of messages) if (message.role === 'assistant') message.providerResponseId = null;
      lmStudioNativeResponseId = null; lmStudioNativeModelKey = null;
      savePreferences(); updateContextManagerUI(); showToast('Context summary cleared.');
    });
    $('#resetGenerationSettings').addEventListener('click', () => {
      settingTemperature.value = ''; settingTopP.value = ''; settingMaxTokens.value = ''; settingSeed.value = '';
      settingNemotronConversationalMode.checked = false;
      savePreferences();
    });
    cancelEditButton.addEventListener('click', () => { cancelEditing(); promptInput.focus(); });
    attachButton.addEventListener('click', () => fileInput.click());
    fileInput.addEventListener('change', handleFilesSelected);
    promptInput.addEventListener('input', autoResizePrompt);
    promptInput.addEventListener('keydown', event => {
      if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
        event.preventDefault();
        void sendMessage();
      }
      // Shift+Enter deliberately keeps the browser's native newline behavior.
    });
    attachmentNoticeDismiss.addEventListener('click', () => setAttachmentNotice(''));
    attachmentWarningCancel.addEventListener('click', () => finishAttachmentConfirmation(false));
    attachmentWarningConfirm.addEventListener('click', () => finishAttachmentConfirmation(true));
    attachmentWarningModal.addEventListener('click', event => { if (event.target === attachmentWarningModal) finishAttachmentConfirmation(false); });
    openSessionDestinationCurrent.addEventListener('click', () => finishOpenSessionDestination('current'));
    openSessionDestinationNew.addEventListener('click', () => finishOpenSessionDestination('new'));
    openSessionDestinationCancel.addEventListener('click', () => finishOpenSessionDestination(null));
    openSessionDestinationModal.addEventListener('click', event => { if (event.target === openSessionDestinationModal) finishOpenSessionDestination(null); });
    newConversationUnsavedSave.addEventListener('click', () => finishNewConversationUnsavedChoice('save'));
    newConversationUnsavedDiscard.addEventListener('click', () => finishNewConversationUnsavedChoice('discard'));
    newConversationUnsavedCancel.addEventListener('click', () => finishNewConversationUnsavedChoice(null));
    newConversationUnsavedModal.addEventListener('click', event => { if (event.target === newConversationUnsavedModal) finishNewConversationUnsavedChoice(null); });
    exportFormat.addEventListener('change', () => { if (exportFormat.value === 'print') { exportConversation(); exportFormat.value = ''; } });
    $('#exportButton').addEventListener('click', () => {
      exportConversation();
      if (exportFormat.value && exportFormat.value !== 'print') $('#conversationMenu').open = false;
    });
    $('#newConversationButton').addEventListener('click', () => { void startNewConversation(); });
    $('#saveSessionButton').addEventListener('click', () => { void saveSessionFile(); });
    $('#saveConversationScopeCancel').addEventListener('click', () => finishSaveConversationScope(null));
    $('#saveConversationScopeContinue').addEventListener('click', () => {
      const scope = document.querySelector('input[name="saveConversationScope"]:checked')?.value || 'all';
      finishSaveConversationScope(scope);
    });
    $('#saveConversationScopeModal').addEventListener('click', event => {
      if (event.target === $('#saveConversationScopeModal')) finishSaveConversationScope(null);
    });
    $('#openSessionButton').addEventListener('click', () => sessionFileInput.click());
    sessionFileInput.addEventListener('change', () => { void openSessionFile(sessionFileInput.files?.[0]); });
    document.addEventListener('click', event => {
      // Enabling Deep Research from the composer intentionally opens its header flyout.
      // Do not interpret that same bubbling click as an outside click and close it again.
      if (event.target instanceof Node && deepResearchButton.contains(event.target)) {
        syncResearchPanelState();
        return;
      }
      for (const menu of toolbarFlyoutMenus()) {
        if (menu.open && !menu.contains(event.target)) menu.open = false;
      }
      syncResearchPanelState();
    });
    document.addEventListener('keydown', event => {
      if (event.key !== 'Escape') return;
      if (!$('#saveConversationScopeModal').hidden) { finishSaveConversationScope(null); return; }
      if (!newConversationUnsavedModal.hidden) { finishNewConversationUnsavedChoice(null); return; }
      if (!openSessionDestinationModal.hidden) { finishOpenSessionDestination(null); return; }
      if (!researchPanel.hidden) { closeResearchWorkspace(); return; }
      if (!agentFolderAccessRequest.hidden) { agentFolderAccessCancel.click(); return; }
      if (!attachmentWarningModal.hidden) { finishAttachmentConfirmation(false); return; }
      if (editingMessageIndex !== null) cancelEditing();
      closeOtherToolbarFlyouts(null);
    });

    for (const menu of toolbarFlyoutMenus()) {
      menu.addEventListener('toggle', () => {
        if (menu.open) {
          closeOtherToolbarFlyouts(menu);
          menu._flyoutState = null;
          positionToolbarFlyout(menu);
        } else {
          resetToolbarFlyout(menu);
        }
        if (menu === researchWorkspaceMenu) {
          syncResearchPanelState();
          if (researchPanelOpen) updateResearchPanelPosition();
        }
      });
    }
    window.addEventListener('resize', positionOpenToolbarFlyouts);
    window.visualViewport?.addEventListener('resize', positionOpenToolbarFlyouts);
    document.addEventListener('scroll', event => {
      // Scrolls originating inside an open flyout must remain native user
      // scrolling. Reposition only when the surrounding document/viewport moves.
      const target = event.target;
      if (target instanceof Node) {
        for (const menu of toolbarFlyoutMenus()) {
          if (!menu.open) continue;
          const panel = menu.querySelector(':scope > .toolbar-panel, :scope > .conversation-panel, :scope > .appearance-panel');
          if (panel && (panel === target || panel.contains(target))) return;
        }
      }
      positionOpenToolbarFlyouts();
    }, true);
    deepResearchButton.addEventListener('click', () => setDeepResearchMode(!deepResearchEnabled));
    researchPauseButton.addEventListener('click', pauseDeepResearch);
    researchResumeButton.addEventListener('click', resumeDeepResearch);
    researchCancelButton.addEventListener('click', cancelDeepResearch);
    researchExportButton.addEventListener('click', exportResearchReport);
    researchCopyPlanButton.addEventListener('click', () => { void copyResearchPlan(); });
    researchCopyResultsButton.addEventListener('click', () => { void copyResearchResults(); });
    researchCloseButton.addEventListener('click', closeResearchWorkspace);
    for (const button of researchTabButtons) {
      button.addEventListener('click', () => setResearchView(button.dataset.researchView));
      button.addEventListener('keydown', event => {
        if (!['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) return;
        event.preventDefault();
        const currentIndex = researchTabButtons.indexOf(button);
        const nextIndex = event.key === 'Home' ? 0 : event.key === 'End' ? researchTabButtons.length - 1 : (currentIndex + (event.key === 'ArrowRight' ? 1 : researchTabButtons.length - 1)) % researchTabButtons.length;
        researchTabButtons[nextIndex].focus(); researchTabButtons[nextIndex].click();
      });
    }
    researchApplyTemplateButton.addEventListener('click', applyResearchTemplate);
    researchClaimFilter.addEventListener('change', renderResearchReview);
    researchRunDiagnosticsButton.addEventListener('click', () => { void runSelectedModelDiagnostics(); });
    researchPlanEditor.addEventListener('input', queueResearchPlanEdit);
    researchDepth.addEventListener('change', () => {
      const presetPages = { standard: 8, deep: 18, extensive: 40 };
      researchPageBudget.value = String(presetPages[researchDepth.value] || 18);
      if (deepResearchTask && ['running','paused','interrupted'].includes(deepResearchTask.status)) {
        deepResearchTask.depth = researchDepth.value;
        deepResearchTask.pageBudget = Number(researchPageBudget.value);
        persistResearchTask(); renderResearchTask();
      }
    });
    researchPageBudget.addEventListener('change', () => { if (deepResearchTask && ['running','paused','interrupted'].includes(deepResearchTask.status)) { deepResearchTask.pageBudget = readBoundedNumber(researchPageBudget, 1, 60, true) || 18; persistResearchTask(); } });
    researchCallBudget.addEventListener('change', () => { if (deepResearchTask && ['running','paused','interrupted'].includes(deepResearchTask.status)) { deepResearchTask.callBudget = readBoundedNumber(researchCallBudget, 4, 80, true) || 30; persistResearchTask(); } });
    researchTimeBudget.addEventListener('change', () => {
      const minutes = readBoundedNumber(researchTimeBudget, 0, 240, true) ?? 0;
      if (deepResearchTask && ['running','paused','interrupted'].includes(deepResearchTask.status)) {
        deepResearchTask.timeBudgetMinutes = minutes;
        deepResearchTask.deadlineAt = minutes ? new Date(Date.now() + minutes * 60000).toISOString() : '';
        deepResearchTask.timeBudgetReached = false; persistResearchTask(); renderResearchTask();
      }
    });
    const saveResearchDomainPolicy = () => {
      if (!deepResearchTask || !['running','paused','interrupted'].includes(deepResearchTask.status)) return;
      deepResearchTask.preferredDomains = parseResearchDomainInput(researchPreferredDomains);
      deepResearchTask.allowedDomains = parseResearchDomainInput(researchAllowDomains);
      deepResearchTask.blockedDomains = parseResearchDomainInput(researchBlockDomains);
      deepResearchTask.activity ||= []; deepResearchTask.activity.push({ at: new Date().toISOString(), type: 'domain_policy', summary: 'Research domain policy updated.' });
      persistResearchTask(); renderResearchTask();
    };
    researchPreferredDomains.addEventListener('change', saveResearchDomainPolicy);
    researchAllowDomains.addEventListener('change', saveResearchDomainPolicy);
    researchBlockDomains.addEventListener('change', saveResearchDomainPolicy);
    agentModeButton.addEventListener('click', () => {
      setAgentMode(!agentModeEnabled);
      if (agentModeEnabled && settingLmStudioApiMode.value === 'native') showToast('Switch LM Studio to OpenAI-compatible mode to use Agent Mode.');
    });
    agentFolderButton.addEventListener('click', async () => {
      await workspaceRestorePromise;
      if (agentDirectoryHandle) { showToast(`Workspace ready: ${agentDirectoryHandle.name || 'selected folder'}. Use Agent settings to change or clear it.`); return; }
      const access = await requestFolderAccess(activeController?.signal);
      if (access.granted) showToast(`Workspace access is ready: ${access.folder}.`);
      else if (access.error) showToast(access.error);
    });
    changeAgentFolderButton.addEventListener('click', async () => {
      const access = await requestFolderAccess(activeController?.signal, true);
      if (access.error) showToast(access.error);
    });
    const agentSettingsTabs = [
      ['agentSettingsTabAgent', 'agentSettingsPageAgent'],
      ['agentSettingsTabProject', 'agentSettingsPageProject'],
      ['agentSettingsTabSkills', 'agentSettingsPageSkills'],
      ['agentSettingsTabSecurity', 'agentSettingsPageSecurity']
    ].map(([tabId, pageId]) => ({ tab: document.getElementById(tabId), page: document.getElementById(pageId) }));
    function selectAgentSettingsTab(index, focusTab = false) {
      const bounded = Math.max(0, Math.min(agentSettingsTabs.length - 1, index));
      agentSettingsTabs.forEach((item, itemIndex) => {
        const selected = itemIndex === bounded;
        item.tab.setAttribute('aria-selected', String(selected));
        item.tab.tabIndex = selected ? 0 : -1;
        item.page.hidden = !selected;
      });
      if (agentSettingsMenu.open) {
        const panel = agentSettingsMenu.querySelector(':scope > .toolbar-panel');
        if (panel) panel.scrollTop = 0;
        positionToolbarFlyout(agentSettingsMenu);
      }
      if (focusTab) agentSettingsTabs[bounded].tab.focus();
    }
    agentSettingsTabs.forEach((item, index) => {
      item.tab.addEventListener('click', () => selectAgentSettingsTab(index));
      item.tab.addEventListener('keydown', event => {
        if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault();
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? agentSettingsTabs.length - 1
          : (index + (event.key === 'ArrowRight' ? 1 : agentSettingsTabs.length - 1)) % agentSettingsTabs.length;
        selectAgentSettingsTab(next, true);
      });
    });
    selectAgentSettingsTab(0);
    const projectIndexStatusElement = $('#projectIndexStatus');
    const projectMissionControlElement = $('#projectMissionControl');
    let projectIndexController = null;
    function missionControlNode(tag, className = '', text = null) {
      const node = document.createElement(tag);
      if (className) node.className = className;
      if (text !== null && text !== undefined) node.textContent = String(text);
      return node;
    }
    function missionControlSection(title, description = '') {
      const section = missionControlNode('section', 'mc-section');
      const heading = missionControlNode('div', 'mc-section-heading');
      heading.append(missionControlNode('h4', '', title));
      if (description) heading.append(missionControlNode('p', '', description));
      section.append(heading);
      return section;
    }
    function missionControlMetric(label, value, detail = '', state = '') {
      const card = missionControlNode('article', `mc-metric${state ? ` is-${state}` : ''}`);
      card.append(missionControlNode('span', 'mc-metric-label', label));
      card.append(missionControlNode('strong', 'mc-metric-value', value));
      if (detail) card.append(missionControlNode('span', 'mc-metric-detail', detail));
      return card;
    }
    function missionControlEvidence(container, evidence) {
      const values = Array.isArray(evidence) ? evidence.filter(Boolean).slice(0, 8) : [];
      if (!values.length) return;
      const list = missionControlNode('ul', 'mc-evidence');
      for (const value of values) list.append(missionControlNode('li', '', value));
      container.append(list);
    }
    function missionControlCheckList(container, checks, mode = 'diagnostic') {
      if (!Array.isArray(checks) || !checks.length) {
        container.append(missionControlNode('p', 'mc-empty', 'No checks were reported.'));
        return;
      }
      const list = missionControlNode('div', 'mc-check-list');
      for (const check of checks.slice(0, 50)) {
        const state = mode === 'readiness' ? (check.status || 'unknown') : (check.severity || 'info');
        const details = missionControlNode('details', 'mc-check');
        const summary = missionControlNode('summary', 'mc-check-summary');
        summary.append(missionControlNode('span', `mc-state-dot is-${state}`, state.toUpperCase()));
        summary.append(missionControlNode('span', 'mc-check-message', check.message || check.id || 'Check'));
        details.append(summary);
        if (check.id) details.append(missionControlNode('code', 'mc-check-id', check.id));
        missionControlEvidence(details, check.evidence);
        list.append(details);
      }
      container.append(list);
    }
    function renderProjectMissionControl(result) {
      if (!projectMissionControlElement) return;
      projectMissionControlElement.replaceChildren();
      projectMissionControlElement.hidden = false;
      if (!result?.available) {
        projectMissionControlElement.append(missionControlNode('p', 'mc-unavailable', result?.message || 'Project Mission Control is unavailable.'));
        return;
      }

      const status = String(result.status || 'unknown');
      const stateLabels = { blocked: 'Blockers found', attention: 'Review recommended', incomplete: 'Evidence incomplete', 'static-checks-passed': 'Static checks passed' };
      const header = missionControlNode('header', 'mc-header');
      const heading = missionControlNode('div', 'mc-title-group');
      heading.append(missionControlNode('h3', '', 'Project Mission Control'));
      heading.append(missionControlNode('p', 'mc-subtitle', `${result.root || 'Workspace'} · project intelligence overview`));
      header.append(heading);
      header.append(missionControlNode('span', `mc-status-badge is-${status}`, stateLabels[status] || status));
      projectMissionControlElement.append(header);
      if (result.statusExplanation) projectMissionControlElement.append(missionControlNode('p', 'mc-status-explanation', result.statusExplanation));

      const index = result.index || {};
      const ageText = index.ageMinutes === null || index.ageMinutes === undefined ? 'age unknown' : index.ageMinutes < 1 ? 'updated less than a minute ago' : `updated ${index.ageMinutes} min ago`;
      const freshnessText = index.freshness === 'fresh' ? 'Fresh snapshot' : index.freshness === 'stale' ? 'Snapshot is stale' : 'Freshness unknown';
      const metadata = missionControlNode('div', 'mc-metadata');
      metadata.append(missionControlNode('span', `mc-state-dot is-${index.freshness || 'unknown'}`, freshnessText));
      metadata.append(missionControlNode('span', '', `Index timestamp: ${result.indexedAt || 'not available'} (${ageText})`));
      const reportTimestamp = Date.parse(result.generatedAt || '');
      metadata.append(missionControlNode('span', '', `Report generated: ${Number.isFinite(reportTimestamp) ? new Date(reportTimestamp).toLocaleString() : 'not available'}`));
      projectMissionControlElement.append(metadata);
      projectMissionControlElement.append(missionControlNode('p', 'mc-muted', `Index scan: ${index.scanned || 0} visited · ${index.files || 0} indexed · ${index.reused || 0} reused · ${index.removed || 0} removed · ${index.skipped || 0} skipped · ${Number(index.readBytes || 0).toLocaleString()} bytes read${index.truncated ? ` · Partial: ${index.truncationReason || 'scan limit reached'}` : ''}`));
      if (index.refresh?.attempted) {
        const refreshText = index.refresh.succeeded ? 'The stale index was refreshed before this report.' : `An automatic refresh was attempted but could not be confirmed${index.refresh.error ? `: ${index.refresh.error}` : '.'}`;
        projectMissionControlElement.append(missionControlNode('p', `mc-refresh-note ${index.refresh.succeeded ? 'is-success' : 'is-warning'}`, refreshText));
      }

      const architecture = result.projectUnderstanding || {};
      const diagnostics = result.diagnostics || {};
      const diagnosticSummary = diagnostics.summary || {};
      const readiness = result.releaseReadiness || {};
      const releaseSummary = readiness.summary || {};
      const impact = result.testImpact || {};
      const impactSummary = impact.summary || {};
      const metrics = missionControlNode('div', 'mc-metrics');
      metrics.append(missionControlMetric('Indexed files', index.files ?? architecture.analysisCoverage?.indexedFiles ?? 0, index.truncated ? `Partial scan · ${index.truncationReason || 'limit reached'}` : `${Number(index.readBytes || 0).toLocaleString()} bytes read`, index.truncated ? 'warning' : ''));
      metrics.append(missionControlMetric('Architecture', architecture.modules ?? 0, `${architecture.entryPoints ?? 0} entry points · ${architecture.localImportEdges ?? 0} local import edges`));
      metrics.append(missionControlMetric('Diagnostics', diagnosticSummary.errors ?? 0, `${diagnosticSummary.warnings ?? 0} warnings · ${diagnosticSummary.information ?? 0} informational`, diagnosticSummary.errors ? 'danger' : diagnosticSummary.warnings ? 'warning' : 'success'));
      metrics.append(missionControlMetric('Release checks', releaseSummary.blockers ?? 0, `${releaseSummary.warnings ?? 0} warnings · ${releaseSummary.passed ?? 0} static checks passed`, releaseSummary.blockers ? 'danger' : releaseSummary.warnings ? 'warning' : 'success'));
      metrics.append(missionControlMetric('Test impact', impact.available ? (impactSummary.recommendedTests ?? impact.tests?.length ?? 0) : '—', impact.available ? `${impactSummary.highConfidence ?? 0} high · ${impactSummary.mediumConfidence ?? 0} medium · ${impactSummary.lowConfidence ?? 0} low confidence` : 'No changed paths supplied'));
      projectMissionControlElement.append(metrics);

      const actions = Array.isArray(result.nextActions) ? result.nextActions : [];
      const actionSection = missionControlSection('Recommended next actions', actions.length ? 'Prioritized from static findings. Review the evidence before making changes.' : 'No follow-up actions were identified by the current static checks.');
      if (actions.length) {
        const list = missionControlNode('ol', 'mc-action-list');
        for (const action of actions.slice(0, 15)) {
          const item = missionControlNode('li', 'mc-action');
          item.append(missionControlNode('span', `mc-priority is-${action.priority || 'medium'}`, String(action.priority || 'medium').toUpperCase()));
          const body = missionControlNode('div', 'mc-action-body');
          body.append(missionControlNode('p', 'mc-action-text', action.action || 'Review finding'));
          body.append(missionControlNode('span', 'mc-action-source', action.source || action.id || 'Project analysis'));
          missionControlEvidence(body, action.evidence);
          item.append(body); list.append(item);
        }
        actionSection.append(list);
      } else actionSection.append(missionControlNode('p', 'mc-empty', 'No static follow-up actions were identified.'));
      projectMissionControlElement.append(actionSection);

      const architectureSection = missionControlSection('Project Understanding', `${architecture.modules || 0} top-level module boundaries and ${architecture.entryPoints || 0} entry-point candidates were identified.`);
      const architectureGrid = missionControlNode('div', 'mc-inline-stats');
      for (const [label, value] of [['High-coupling modules', architecture.highCouplingModules || 0], ['Circular dependency groups', architecture.circularDependencyGroups || 0], ['Unresolved imports', architecture.unresolvedImports || 0], ['Resolved bindings', architecture.resolvedBindings || 0]])
        architectureGrid.append(missionControlNode('span', '', `${label}: ${value}`));
      architectureSection.append(architectureGrid);
      const moduleBreakdown = architecture.moduleBreakdown || [];
      if (moduleBreakdown.length) {
        const details = missionControlNode('details', 'mc-expandable');
        details.append(missionControlNode('summary', '', 'View top-level module boundaries'));
        const list = missionControlNode('ul', 'mc-evidence');
        for (const item of moduleBreakdown.slice(0, 20)) list.append(missionControlNode('li', '', `${item.directory} · ${item.files} files · ${item.javascript || 0} JS · ${item.typescript || 0} TS · ${item.html || 0} HTML · ${item.css || 0} CSS`));
        details.append(list); architectureSection.append(details);
      }
      const entryPoints = architecture.entryPointExamples || [];
      if (entryPoints.length) {
        const details = missionControlNode('details', 'mc-expandable');
        details.append(missionControlNode('summary', '', 'View entry-point candidates'));
        const list = missionControlNode('ul', 'mc-evidence');
        for (const item of entryPoints.slice(0, 30)) list.append(missionControlNode('li', '', `${item.path} · ${item.confidence || 'candidate'} · ${item.source || 'index evidence'}`));
        details.append(list); architectureSection.append(details);
      }
      const hotspots = architecture.highCouplingExamples || [];
      if (hotspots.length) {
        const details = missionControlNode('details', 'mc-expandable');
        details.append(missionControlNode('summary', '', 'View high-coupling modules'));
        const list = missionControlNode('ul', 'mc-evidence');
        for (const item of hotspots.slice(0, 10)) list.append(missionControlNode('li', '', `${item.path} · ${item.incomingImports} incoming · ${item.outgoingImports} outgoing imports`));
        details.append(list); architectureSection.append(details);
      }
      if ((architecture.circularDependencies || []).length) {
        const details = missionControlNode('details', 'mc-expandable');
        details.append(missionControlNode('summary', '', 'View circular dependency paths'));
        const list = missionControlNode('ul', 'mc-evidence');
        for (const cycle of architecture.circularDependencies.slice(0, 10)) list.append(missionControlNode('li', '', Array.isArray(cycle) ? cycle.join(' → ') : cycle));
        details.append(list); architectureSection.append(details);
      }
      const coverage = architecture.analysisCoverage || {};
      architectureSection.append(missionControlNode('p', 'mc-muted', `Language-aware coverage: ${coverage.javascriptFiles || 0} JavaScript · ${coverage.typescriptFiles || 0} TypeScript · ${coverage.htmlFiles || 0} HTML · ${coverage.cssFiles || 0} CSS · ${coverage.svgFiles || 0} SVG assets. ${coverage.parseErrorCount || 0} parser errors.`));
      const assetReferences = Array.isArray(architecture.assetReferences) ? architecture.assetReferences : [];
      if (assetReferences.length) {
        const details = missionControlNode('details', 'mc-expandable');
        details.append(missionControlNode('summary', '', `SVG asset references (${assetReferences.length})`));
        const list = missionControlNode('ul', 'mc-evidence');
        for (const item of assetReferences.slice(0, 25)) list.append(missionControlNode('li', '', `${item.from} → ${item.to} · ${item.kind} · ${item.targetIndexed ? 'indexed' : 'not indexed'}`));
        details.append(list); architectureSection.append(details);
      }
      projectMissionControlElement.append(architectureSection);

      const diagnosticSection = missionControlSection('Project Diagnostics', 'Manifest, package-manager, test-discovery and static import-graph checks. No environment probe or build/test command was run.');
      diagnosticSection.append(missionControlNode('p', 'mc-muted', `Package manager: ${diagnostics.packageManager || 'not confidently detected'} · Test files discovered: ${diagnostics.testFilesFound || 0} · Tooling configurations: ${(diagnostics.toolingConfigurations || []).length}`));
      missionControlCheckList(diagnosticSection, diagnostics.checks || [], 'diagnostic');
      projectMissionControlElement.append(diagnosticSection);

      const readinessSection = missionControlSection('Release Readiness', 'A static release-hygiene assessment, not an approval or verification that a release can ship.');
      readinessSection.append(missionControlNode('p', 'mc-muted', `Result: ${readiness.status || 'unknown'} · Package manager: ${readiness.packageManager || 'not detected'}`));
      missionControlCheckList(readinessSection, readiness.checks || [], 'readiness');
      const notVerified = Array.isArray(readiness.notVerified) ? readiness.notVerified : [];
      if (notVerified.length) {
        const details = missionControlNode('details', 'mc-expandable');
        details.append(missionControlNode('summary', '', 'What remains unverified'));
        const list = missionControlNode('ul', 'mc-evidence');
        for (const item of notVerified) list.append(missionControlNode('li', '', item));
        details.append(list); readinessSection.append(details);
      }
      const suggested = readiness.suggestedCommands || {};
      const commands = Object.entries(suggested);
      if (commands.length) {
        const details = missionControlNode('details', 'mc-expandable');
        details.append(missionControlNode('summary', '', 'Suggested build/test commands (not executed)'));
        const list = missionControlNode('ul', 'mc-evidence');
        for (const [name, command] of commands) list.append(missionControlNode('li', '', `${name}: ${command}`));
        details.append(list); readinessSection.append(details);
      }
      projectMissionControlElement.append(readinessSection);

      const impactSection = missionControlSection('Test Impact Analysis', impact.available ? 'Static reverse-import analysis for the changed paths supplied.' : 'Optional: supply changed paths above to identify tests that may be affected. Git changes are not inferred automatically.');
      if (impact.available) {
        impactSection.append(missionControlNode('p', 'mc-muted', `Changed: ${(impact.changedPaths || []).length} · Analyzed: ${(impact.analyzedChangedPaths || []).length} · Unsupported/unanalysed: ${(impact.unanalysedChangedPaths || []).length} · Unknown: ${(impact.unknownChangedPaths || []).length} · Impacted modules: ${impact.impactedModuleCount || 0}`));
        if ((impact.tests || []).length) {
          const list = missionControlNode('ul', 'mc-evidence');
          for (const item of impact.tests.slice(0, 100)) list.append(missionControlNode('li', '', `${String(item.confidence || 'unknown').toUpperCase()} · ${item.path} — ${item.reason}`));
          impactSection.append(list);
        } else impactSection.append(missionControlNode('p', 'mc-empty', 'No test candidates were found for these changed paths. This does not prove that no tests are affected.'));
        if ((impact.unanalysedChangedPaths || []).length || (impact.unknownChangedPaths || []).length) {
          const list = missionControlNode('ul', 'mc-evidence');
          for (const path of [...impact.unanalysedChangedPaths || [], ...impact.unknownChangedPaths || []]) list.append(missionControlNode('li', '', `Not included in dependency analysis: ${path}`));
          impactSection.append(list);
        }
      } else impactSection.append(missionControlNode('p', 'mc-empty', impact.message || 'No changed paths were supplied.'));
      projectMissionControlElement.append(impactSection);

      const limits = missionControlSection('Evidence, scope and safety', 'Mission Control never executes commands, writes files, installs packages or changes release metadata.');
      limits.append(missionControlNode('p', 'mc-muted', result.scope || 'Language-aware analysis is limited to JavaScript, TypeScript, JSX/TSX, HTML and CSS. Other files remain generic text.'));
      for (const item of result.limitations || []) limits.append(missionControlNode('p', 'mc-muted', item));
      limits.append(missionControlNode('p', 'mc-muted', 'Verification performed: static project analysis only. Builds, tests, live dependency audits, Git status, signing, packaging and publication remain separate checks.'));
      projectMissionControlElement.append(limits);
    }
    function projectIndexStatus(index) {
      if (!projectIndexStatusElement) return;
      if (!index) { projectIndexStatusElement.textContent = 'No project index loaded.'; return; }
      const stats = index.stats || {};
      projectIndexStatusElement.textContent = `${stats.files || 0} indexed files · ${index.technologies?.length || 0} languages · ${(index.frameworks || []).join(', ') || 'frameworks not detected'} · ${stats.reused || 0} reused · updated ${new Date(index.updatedAt).toLocaleString()}${stats.truncated ? ` · Partial: ${stats.reason || 'scan limit reached'}` : ''}`;
    }
    async function refreshProjectIndexStatus() {
      if (projectMissionControlElement) { projectMissionControlElement.hidden = true; projectMissionControlElement.textContent = ''; }
      if (!agentDirectoryHandle) { projectIndexStatusElement.textContent = 'Select a workspace folder to access its project index.'; return; }
      try { const index = await loadProjectIndex(); projectIndexStatus(index); }
      catch (error) { if (projectIndexStatusElement) projectIndexStatusElement.textContent = String(error?.message || error); }
    }
    async function refreshDeveloperWorkflowList() {
      const select = $('#developerWorkflowSelect');
      if (!select) return;
      const prior = select.value;
      let workflows;
      try { workflows = await loadDeveloperWorkflows(); }
      catch (error) { $('#developerWorkflowStatus').textContent = `Workflow storage unavailable: ${error?.message || error}`; return; }
      select.replaceChildren();
      for (const workflow of workflows) {
        const option = document.createElement('option'); option.value = workflow.id;
        option.textContent = `${workflow.name}${workflow.builtIn ? '' : ' · custom'}`;
        select.appendChild(option);
      }
      if (workflows.some(item => item.id === prior)) select.value = prior;
      $('#developerWorkflowStatus').textContent = `${workflows.length} workflows available · ${workflows.filter(item => item.builtIn).length} built-in`;
    }
    $('#buildProjectIndex').addEventListener('click', async () => {
      if (!agentDirectoryHandle) { showToast('Select a workspace folder before building its project index.'); return; }
      if (typeof projectIndexAutoRefreshPromise !== 'undefined' && projectIndexAutoRefreshPromise) await projectIndexAutoRefreshPromise;
      const button = $('#buildProjectIndex'); button.disabled = true; button.textContent = 'Indexing…';
      const cancelButton = $('#cancelProjectIndex'); cancelButton.disabled = false;
      projectIndexController = new AbortController();
      try {
        $('#runProjectMissionControl').disabled = true;
      const result = await buildProjectIndex({ force: false, signal: projectIndexController.signal, onProgress: progress => { projectIndexStatusElement.textContent = `Indexing ${progress.files} files · scanned ${progress.scanned}…`; } });
        projectMissionControlElement.hidden = true; projectMissionControlElement.textContent = '';
        projectIndexStatusElement.textContent = `${result.files} files indexed · ${result.technologies.length} languages · ${(result.frameworks || []).join(', ') || 'no framework hints'} · ${result.reused} reused · ${result.readBytes.toLocaleString()} bytes read${result.truncated ? ` · Partial: ${result.reason}` : ''}`;
        showToast(`Project index refreshed: ${result.files} files.`);
      } catch (error) { projectIndexStatusElement.textContent = error?.name === 'AbortError' ? 'Indexing cancelled; the previous index was retained.' : String(error?.message || error); if (error?.name !== 'AbortError') showToast('Project indexing failed.'); }
      finally { projectIndexController = null; button.disabled = false; button.textContent = 'Build / refresh index'; cancelButton.disabled = true; $('#runProjectMissionControl').disabled = false; }
    });
    $('#cancelProjectIndex').addEventListener('click', () => { projectIndexController?.abort(); });
    $('#runProjectMissionControl').addEventListener('click', async () => {
      if (!agentDirectoryHandle) { showToast('Select a workspace folder before opening Project Mission Control.'); return; }
      if (projectIndexController) { showToast('Project indexing is already running. Open Mission Control after it finishes.'); return; }
      const button = $('#runProjectMissionControl'); button.disabled = true; button.textContent = 'Analyzing…';
      try {
        const rawPaths = $('#projectMissionControlChangedPaths')?.value || '';
        const changedPaths = [...new Set(rawPaths.split(/\r?\n/).map(value => value.trim()).filter(Boolean))].slice(0, 100);
        renderProjectMissionControl(await getProjectMissionControl({ changedPaths }));
      } catch (error) { renderProjectMissionControl({ available: false, message: String(error?.message || error) }); }
      finally { button.disabled = false; button.textContent = 'Run Mission Control'; }
    });
    $('#clearProjectIndex').addEventListener('click', async () => {
      try { await workspaceDbAction('readwrite','delete',undefined,projectIndexKey()); projectIndexStatus(null); projectMissionControlElement.hidden = true; projectMissionControlElement.textContent = ''; showToast('Project index cleared. Automatic refresh remains enabled for this workspace.'); }
      catch (error) { showToast(`Could not clear project index: ${error?.message || error}`); }
    });
    $('#runDeveloperWorkflow').addEventListener('click', () => {
      if (!agentDirectoryHandle) { showToast('Select a workspace folder before starting a developer workflow.'); return; }
      const workflow = $('#developerWorkflowSelect').value;
      const context = $('#developerWorkflowContext').value.trim();
      const prompt = `Run the developer workflow '${workflow}'. ${context ? `Task context: ${context}` : 'Use the current conversation request as the task context.'} First call start_developer_workflow to load its exact instructions, then follow them using the existing Agent tools.`;
      promptInput.value = prompt; promptInput.dispatchEvent(new Event('input', { bubbles: true }));
      $('#sendButton').click();
    });
    $('#saveDeveloperWorkflow').addEventListener('click', async () => {
      if (!agentDirectoryHandle) { showToast('Select a workspace folder before saving a custom workflow.'); return; }
      try {
        const selected = (await loadDeveloperWorkflows()).find(item => item.id === $('#developerWorkflowSelect').value);
        const name = window.prompt('Name for this custom workflow:', selected && !selected.builtIn ? selected.name : '');
        if (!name) return;
        const prompt = window.prompt('Workflow instructions (the Agent will execute these through its existing tools):', selected && !selected.builtIn ? selected.prompt : '');
        if (!prompt) return;
        const saved = await saveDeveloperWorkflow({ id: selected && !selected.builtIn ? selected.id : null, name, prompt, description: 'Custom workspace workflow' });
        await refreshDeveloperWorkflowList(); $('#developerWorkflowSelect').value = saved.id; showToast(`Workflow '${name}' saved.`);
      } catch (error) { showToast(`Could not save workflow: ${error?.message || error}`); }
    });
    $('#deleteDeveloperWorkflow').addEventListener('click', async () => {
      try {
        const id = $('#developerWorkflowSelect').value;
        const workflow = (await loadDeveloperWorkflows()).find(item => item.id === id);
        if (!workflow || workflow.builtIn) { showToast('Built-in workflows cannot be deleted.'); return; }
        await deleteDeveloperWorkflow(id); await refreshDeveloperWorkflowList(); showToast(`Workflow '${workflow.name}' deleted.`);
      } catch (error) { showToast(`Could not delete workflow: ${error?.message || error}`); }
    });
    void refreshProjectIndexStatus();
    void refreshDeveloperWorkflowList();
    $('#agentSaveSettings').addEventListener('click', saveAgentSettings);
    const persistReaderCredentialInputs = () => {
      saveReaderCredentials({ jinaApiKey: agentJinaApiKey.value.trim(), customGatewayToken: agentCustomReaderToken.value.trim() });
      updateReaderCredentialStatus();
    };
    agentJinaApiKey.addEventListener('change', persistReaderCredentialInputs);
    agentCustomReaderToken.addEventListener('change', persistReaderCredentialInputs);
    $('#testJinaReader').addEventListener('click', async () => {
      const base = normalizeReaderEndpoint(agentReaderBaseUrl.value || DEFAULT_JINA_READER_URL, false);
      if (!base) { showToast('Enter a valid Jina Reader endpoint first.'); return; }
      const credentials = loadReaderCredentials();
      const key = agentJinaApiKey.value.trim() || credentials.jinaApiKey || '';
      const button = $('#testJinaReader'); button.disabled = true;
      try {
        const result = await testReaderEndpoint(base, key, `Jina Reader (${new URL(base).host})`);
        showToast(`Jina Reader connection works. Retrieved ${result.content.length.toLocaleString()} characters.`);
      } catch (error) { showToast(`Jina Reader test failed: ${error?.message || String(error)}`); }
      finally { button.disabled = false; }
    });
    $('#testCustomReader').addEventListener('click', async () => {
      const base = normalizeReaderEndpoint(agentCustomReaderUrl.value, true);
      if (!base) { showToast('Enter a custom gateway URL first.'); return; }
      const credentials = loadReaderCredentials();
      const key = agentCustomReaderToken.value.trim() || credentials.customGatewayToken || '';
      const button = $('#testCustomReader'); button.disabled = true;
      try {
        const result = await testReaderEndpoint(base, key, `Custom Reader gateway (${new URL(base).host})`);
        showToast(`Custom gateway works. Retrieved ${result.content.length.toLocaleString()} characters.`);
      } catch (error) { showToast(`Custom gateway test failed: ${error?.message || String(error)}`); }
      finally { button.disabled = false; }
    });
    $('#clearReaderCredentials').addEventListener('click', () => {
      const settings = loadAgentSettings(); delete settings.jinaApiKey; delete settings.customGatewayToken;
      try { localStorage.setItem(AGENT_SETTINGS_KEY, JSON.stringify(settings)); } catch (_) {}
      agentJinaApiKey.value = ''; agentCustomReaderToken.value = ''; updateReaderCredentialStatus();
      showToast('Reader credentials cleared.');
    });
    $('#addModelConnection').addEventListener('click', () => {
      const name = modelConnectionName.value.trim(); const baseUrl = modelConnectionUrl.value.trim(); const type = modelConnectionType.value;
      const lmStudioMetadataApi = type === 'lmstudio' && modelConnectionLmStudioMetadata.checked;
      const apiKey = modelConnectionApiKey.value.trim();
      const authHeader = modelConnectionAuthHeader.value.trim() || 'Authorization';
      if (!name || !baseUrl) { showToast('Enter a server name and base URL.'); return; }
      let parsed; try { parsed = new URL(normalizeBase(baseUrl)); } catch (_) { showToast('Enter a valid absolute HTTP or HTTPS base URL.'); return; }
      if (!['http:','https:'].includes(parsed.protocol)) { showToast('Only HTTP and HTTPS model endpoints are supported.'); return; }
      if (!/^[!#$%&'*+.^_`|~0-9A-Za-z-]+$/.test(authHeader)) { showToast('Enter a valid HTTP authentication header name.'); return; }
      if (editingModelConnectionId) {
        const config = modelConnections.find(item => item.id === editingModelConnectionId);
        if (!config) { editingModelConnectionId = null; showToast('That connection no longer exists.'); return; }
        Object.assign(config, { name: name.slice(0,60), type, baseUrl: normalizeBase(baseUrl), authHeader, apiKey: apiKey || config.apiKey || '', lmStudioMetadataApi: config.builtIn && config.id === 'default-lmstudio' ? true : lmStudioMetadataApi });
        if (config.type === 'ollama') BOOT.ollamaBaseUrl = config.baseUrl; else BOOT.lmStudioBaseUrl = config.baseUrl;
      } else {
        const id = `model-${Date.now().toString(36)}-${Math.random().toString(36).slice(2,6)}`;
        modelConnections.push({ id, name: name.slice(0,60), type, baseUrl: normalizeBase(baseUrl), apiKey, authHeader, enabled: true, lmStudioMetadataApi });
      }
      editingModelConnectionId = null; $('#addModelConnection').textContent = 'Add connection'; cancelEditModelConnection.hidden = true;
      modelConnectionName.value = ''; modelConnectionUrl.value = ''; modelConnectionApiKey.value = ''; modelConnectionApiKey.placeholder = 'Bearer API key'; modelConnectionAuthHeader.value = 'Authorization'; modelConnectionLmStudioMetadata.checked = false; modelConnectionLmStudioMetadataField.hidden = true;
      saveModelConnections(); renderModelConnections(); void refreshModels(false);
    });
    modelConnectionType.addEventListener('change', () => { modelConnectionLmStudioMetadataField.hidden = modelConnectionType.value !== 'lmstudio'; if (modelConnectionType.value !== 'lmstudio') modelConnectionLmStudioMetadata.checked = false; });
    cancelEditModelConnection.addEventListener('click', () => { editingModelConnectionId = null; modelConnectionName.value = ''; modelConnectionUrl.value = ''; modelConnectionApiKey.value = ''; modelConnectionApiKey.placeholder = 'Bearer API key'; modelConnectionAuthHeader.value = 'Authorization'; modelConnectionLmStudioMetadata.checked = false; modelConnectionLmStudioMetadataField.hidden = true; $('#addModelConnection').textContent = 'Add connection'; cancelEditModelConnection.hidden = true; });
    $('#clearAgentTaskHistory').addEventListener('click', () => { localStorage.removeItem(AGENT_TASK_HISTORY_KEY); renderAgentTaskHistory(); showToast('Agent task history cleared.'); });
    clearAlwaysApprovedToolsButton.addEventListener('click', () => { alwaysApprovedAgentTools.clear(); saveAlwaysApprovedAgentTools(); showToast('Always-approved tool permissions cleared.'); });
    $('#clearAgentFolder').addEventListener('click', async () => {
      try { await clearPersistedAgentFolderHandle(); notifyWorkspaceHandleChanged('cleared'); showToast('Default workspace access cleared.'); }
      catch (error) { showToast(`Could not clear workspace: ${error?.message || error}`); }
    });
    function updateMcpModeButton() {
      const connected = [...mcpServers.values()].filter(server => server.enabled && server.connected && mcpRuntime.has(server.id)).length;
      const availableNow = connected > 0 && (mcpModeEnabled || agentModeEnabled);
      mcpModeButton.classList.toggle('active', availableNow);
      // aria-pressed describes the separate normal-chat toggle; Agent Mode access is automatic.
      mcpModeButton.setAttribute('aria-pressed', String(mcpModeEnabled));
      mcpModeButton.dataset.connected = String(connected);
      mcpModeButton.title = agentModeEnabled
        ? (connected ? `MCP available in Agent Mode · ${connected} connected server(s)${mcpModeEnabled ? ' · also enabled in normal chat' : ''}` : 'Agent Mode is on, but no enabled MCP server is connected')
        : (mcpModeEnabled ? (connected ? `MCP enabled in normal chat · ${connected} connected server(s)` : 'MCP toggle is on, but no enabled MCP server is connected') : (connected ? `MCP off in normal chat · ${connected} server(s) connected` : 'MCP off · no connected servers'));
      mcpModeButton.setAttribute('aria-label', mcpModeButton.title);
    }
    mcpModeButton.addEventListener('click', () => {
      mcpModeEnabled = !mcpModeEnabled;
      updateMcpModeButton();
      const connected = [...mcpServers.values()].filter(server => server.enabled && server.connected).length;
      showToast(mcpModeEnabled ? (connected ? 'MCP access enabled for normal chat.' : 'MCP access enabled, but connect an MCP server to use it.') : 'MCP access disabled for normal chat.');
    });
    saveProjectProfileButton.addEventListener('click', () => { void saveProjectProfile(); });
    projectProfileSelect.addEventListener('change', () => {
      const profile = projectProfiles.get(projectProfileSelect.value);
      projectProfileName.value = profile?.name || '';
    });
    $('#applyProjectProfile').addEventListener('click', async () => { try { await applyProjectProfile(); await refreshProjectIndexStatus(); await refreshDeveloperWorkflowList(); } catch (error) { showToast(`Could not apply Project Profile: ${error?.message || error}`); } });
    $('#deleteProjectProfile').addEventListener('click', () => { void deleteProjectProfile(); });
    $('#refreshProjectProfiles').addEventListener('click', () => { void refreshProjectProfiles(); });
    for (const id of ['systemManagerEnabled','systemManagerProfile','systemManagerLaunchPermission','systemManagerOpenPermission','systemManagerGroupApplications','systemManagerGroupProcesses','systemManagerGroupDiagnostics','systemManagerDiscoverApplications','systemManagerLaunchApplications','systemManagerOpenTargets','systemManagerListProcesses','systemManagerInspectProcesses','systemManagerTerminateProcesses','systemManagerSystemInformation','systemManagerEnvironmentInformation','systemManagerServiceInformation','systemManagerNetworkInformation','systemManagerEventLogInformation','systemManagerCrashInvestigation','systemManagerResourceMonitoring','systemManagerHealthHistory','systemManagerScheduledTaskInformation','systemManagerScheduledTaskManagement','systemManagerStorageHealth','systemManagerSecurityPosture','systemManagerTargetedNetworkDiagnostics','systemManagerCommandTimeout']) {
      document.getElementById(id)?.addEventListener('change', () => { void saveSystemManagerSettingsFromUi(); });
    }
    document.querySelectorAll('[data-system-manager-tab]').forEach(button => {
      button.addEventListener('click', () => setSystemManagerTab(button.dataset.systemManagerTab || 'settings'));
    });
    setSystemManagerTab('settings');
    $('#refreshSystemManagerAdapters')?.addEventListener('click', () => { void loadSystemManagerAdapters(); });
    $('#newSystemManagerAdapter')?.addEventListener('click', () => {
      const input = $('#systemManagerAdapterManifest');
      const details = input?.closest('details');
      const saveButton = $('#installSystemManagerAdapter');
      if (!input || !details) return;
      input.value = '';
      input.dataset.adapterEditId = '';
      details.open = true;
      if (saveButton) saveButton.textContent = 'Validate and install';
      const cancelButton = $('#cancelSystemManagerAdapterEdit');
      if (cancelButton) cancelButton.hidden = true;
      const status = $('#systemManagerAdapterStatus');
      if (status) status.textContent = 'Create a new custom adapter manifest. Use a unique lowercase ID and at least one supported operation with argument templates.';
      input.focus();
      input.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    });
    $('#cancelSystemManagerAdapterEdit')?.addEventListener('click', () => {
      const input = $('#systemManagerAdapterManifest');
      const details = input?.closest('details');
      if (input) { input.value = ''; input.dataset.adapterEditId = ''; }
      if (details) details.open = false;
      const saveButton = $('#installSystemManagerAdapter');
      if (saveButton) saveButton.textContent = 'Validate and install';
      const cancelButton = $('#cancelSystemManagerAdapterEdit');
      if (cancelButton) cancelButton.hidden = true;
      const status = $('#systemManagerAdapterStatus');
      if (status) status.textContent = 'Edit cancelled. Choose New adapter to install a custom adapter.';
    });
    $('#installSystemManagerAdapter')?.addEventListener('click', () => { void installSystemManagerAdapterFromUi(); });
    $('#systemManagerAdapterList')?.addEventListener('click', event => {
      const button = event.target.closest('button[data-adapter-remove], button[data-adapter-edit], button[data-adapter-reset]');
      if (!button) return;
      if (button.dataset.adapterRemove) void removeSystemManagerAdapterFromUi(button.dataset.adapterRemove);
      else if (button.dataset.adapterReset) void resetSystemManagerAdapter(button.dataset.adapterReset);
      else if (button.dataset.adapterEdit) {
        const input = $('#systemManagerAdapterManifest');
        const details = input?.closest('details');
        const saveButton = $('#installSystemManagerAdapter');
        if (!input || !details) return;
        let operations = {};
        try { operations = JSON.parse(button.dataset.adapterOperations || '{}'); } catch (_) {}
        if (Object.values(operations).some(templates => !Array.isArray(templates) || templates.length < 1)) {
          const status = $('#systemManagerAdapterStatus');
          if (status) status.textContent = 'This adapter returned incomplete operation templates and cannot be edited safely. Refresh the adapter list and try again.';
          return;
        }
        input.value = JSON.stringify({ id: button.dataset.adapterEdit, displayName: button.dataset.adapterName || button.dataset.adapterEdit, match: button.dataset.adapterMatch || '', operations }, null, 2);
        input.dataset.adapterEditId = button.dataset.adapterEdit;
        details.open = true;
        if (saveButton) saveButton.textContent = 'Save built-in override';
        const cancelButton = $('#cancelSystemManagerAdapterEdit');
        if (cancelButton) cancelButton.hidden = false;
        const status = $('#systemManagerAdapterStatus');
        if (status) status.textContent = `Edit argument templates for ${button.dataset.adapterName || button.dataset.adapterEdit}. Keep the built-in ID, display name and match unchanged. The current argument templates are loaded for editing. Keep the built-in identity and application match unchanged.`;
        input.focus();
        input.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      }
    });
    $('#refreshAgentSkills').addEventListener('click', () => { void refreshAgentSkillManager(); });
    $('#installAgentSkill').addEventListener('click', () => { void installManagedAgentSkill(); });
    $('#installAgentSkillUrl').addEventListener('click', () => { void installAgentSkillFromUrl(); });
    agentSkillScope.addEventListener('change', () => { void refreshAgentSkillManager(); });
    agentSkillSelect.addEventListener('change', updateManagedSkillSelection);
    agentSkillRemoveButton.addEventListener('click', () => { void removeManagedAgentSkill(); });
    $('#savePermissionPolicy').addEventListener('click', () => { void saveSharedPermissionPolicy(); });
    $('#mcpConnectButton').addEventListener('click', async () => {
      const rawUrl = mcpServerUrl.value.trim();
      if (!rawUrl) { showToast('Enter an MCP endpoint URL.'); return; }
      let url; try { url = normalizeMcpEndpoint(rawUrl); } catch (error) { showToast(error?.message || 'Enter a valid absolute HTTP or HTTPS MCP URL without embedded credentials.'); return; }
      mcpServerUrl.value = url;
      const parsed = new URL(url);
      const id = `mcp${Date.now().toString(36)}${Math.random().toString(36).slice(2,6)}`;
      const config = { id, name: mcpServerName.value.trim() || parsed.host, url, token: mcpServerToken.value.trim(), enabled: true };
      mcpServers.set(id, config); saveMcpServerConfigs(); renderMcpServers();
      const button = $('#mcpConnectButton'); button.disabled = true; button.textContent = 'Connecting…';
      try { await mcpConnect(config); mcpServerToken.value = ''; showToast(`Connected to ${config.name}.`); }
      catch (error) { showToast(`MCP connection failed: ${error?.message || String(error)}. Check endpoint, authentication, and CORS.`); }
      finally { button.disabled = false; button.textContent = 'Connect server'; }
    });
    mcpServerList.addEventListener('click', async event => {
      const button = event.target.closest('button[data-mcp-action]'); if (!button) return;
      const id = button.dataset.mcpId, action = button.dataset.mcpAction, config = mcpServers.get(id); if (!config) return;
      if (action === 'toggle') { config.enabled = !config.enabled; mcpServers.set(id, config); saveMcpServerConfigs(); renderMcpServers(); }
      if (action === 'clear-token') {
        config.token = '';
        const runtime = mcpRuntime.get(id); if (runtime) runtime.token = '';
        mcpServers.set(id, config); saveMcpServerConfigs(); renderMcpServers();
        showToast(`Saved token cleared for ${config.name}.`);
      }
      if (action === 'reconnect') {
        const replacementToken = mcpServerToken.value.trim();
        if (replacementToken) { config.token = replacementToken; mcpServers.set(id, config); saveMcpServerConfigs(); }
        try { await mcpConnect(config); mcpServerToken.value = ''; showToast(`Reconnected to ${config.name}.`); }
        catch (error) { showToast(`MCP reconnect failed: ${error?.message || String(error)}. Re-enter any required bearer token above.`); }
      }
      if (action === 'disconnect' || action === 'remove') {
        const runtime = mcpRuntime.get(id);
        if (runtime?.sessionId) { try { await fetch(runtime.url, { method: 'DELETE', headers: mcpHeaders(runtime) }); } catch (_) {} }
        mcpRuntime.delete(id);
        if (action === 'remove') {
          mcpServers.delete(id);
          forgetMcpServerConfig(id);
        } else {
          config.connected = false; config.toolCount = config.resourceCount = config.promptCount = 0; mcpServers.set(id, config);
        }
        saveMcpServerConfigs(); renderMcpServers();
      }
    });

    let isInitialized = false;
    window.addEventListener('storage', event => {
      if (event.key !== 'local-llm.workspace-handle-event' || !event.newValue) return;
      try {
        const change = JSON.parse(event.newValue);
        if (change.workspaceContextId !== workspaceContext.id) return;
        if (change.action === 'cleared') {
          persistedDirectoryHandle = null; agentDirectoryHandle = null; workspaceContext.folderName = '';
          if (typeof projectIndexController !== 'undefined') projectIndexController?.abort();
          if (typeof scheduleProjectIndexAutoRefresh === 'function') scheduleProjectIndexAutoRefresh('workspace cleared in another window');
          updateAgentFolderButton(); rebuildActiveToolDefinitions(deepResearchEnabled);
          void refreshAgentSkillManager();
        } else workspaceRestorePromise = restoreAgentFolderHandle().then(() => refreshAgentSkillManager());
      } catch (_) {}
    });
    window.initializeRunMind = async function (initialState) {
      if (isInitialized) return;
      isInitialized = true;
      BOOT = initialState && typeof initialState === 'object' ? initialState : {};
      try { await initializeThemeCatalog(); }
      catch (error) { console.warn('RunMind theme discovery failed; using the built-in fallback themes.', error); }
      workspaceContext = BOOT.initialConversation?.workspaceContext
        ? createWorkspaceContext(BOOT.initialConversation.workspaceContext)
        : createWorkspaceContext();
      bootWorkspaceContextId = BOOT.initialConversation ? workspaceContext.id : null;
      configureWindowScopedStorage();
      try { await initializeConversationSessionStorage(); }
      catch (error) { console.warn('RunMind temporary session archive could not initialize.', error); }
      loadWorkflowState();
      try {
        const savedParallelism = Number(localStorage.getItem('runmind.workflow-parallelism.v1'));
        if (workflowParallelism && [1, 2, 3, 4].includes(savedParallelism)) workflowParallelism.value = String(savedParallelism);
      } catch (_) {}
      renderWorkflowView();
      modelConnections = loadModelConnections();
      renderModelConnections();
      applySavedPreferences();
      await appearanceStylesheetPromise;
      if (workspaceContext.instructions !== null) {
        settingSystemPrompt.value = workspaceContext.instructions;
        updateSystemPromptPreview();
      }
      updateNemotronConversationalModeControl();
      applyAgentSettings();
      workspaceMcpRestorePromise = (async () => {
        await restoreMcpConfigs(workspaceContext.mcpServers);
        // Fresh chats retain the existing local MCP startup behavior. A conversation
        // opened from a file restores descriptors but waits for explicit reconnect.
        if (!BOOT.initialConversation) {
          for (const config of mcpServers.values()) {
            if (config.enabled === false) continue;
            try { await mcpConnect(config); } catch (_) { /* Retain saved configuration for manual reconnect. */ }
          }
        }
      })();
      workspaceRestorePromise = restoreAgentFolderHandle({ allowLegacyMigration: !BOOT.initialConversation?.workspaceContext });
      void refreshProjectProfiles();
      void workspaceRestorePromise.then(async () => { await refreshAgentSkillManager(); await refreshProjectIndexStatus(); await refreshDeveloperWorkflowList(); });
      void loadSharedPermissionPolicy();
      void loadSystemManagerSettings();
      void loadSystemManagerAdapters();
      renderAgentTaskHistory();
      setAgentMode(false);
      try {
        const savedTemplate = localStorage.getItem('runmind.research-template.v1');
        if (savedTemplate && RESEARCH_TEMPLATES[savedTemplate]) selectedResearchTemplateId = savedTemplate;
      } catch (_) {}
      researchTemplateSelect.value = selectedResearchTemplateId;
      deepResearchTask = await loadResearchTask();
      await loadResearchCheckpointHistory();
      if (deepResearchTask && ['running','pausing','resuming','cancelling'].includes(deepResearchTask.status)) {
        deepResearchTask.status = 'interrupted';
        deepResearchTask.activity ||= [];
        deepResearchTask.activity.push({ at: new Date().toISOString(), type: 'interrupted', summary: 'The WebView closed or reloaded during research. Resume from the last saved checkpoint.' });
        persistResearchTask();
      }
      if (deepResearchTask) {
        researchDepth.value = deepResearchTask.depth || 'deep';
        researchPageBudget.value = String(deepResearchTask.pageBudget || 18);
        researchCallBudget.value = String(deepResearchTask.callBudget || 30);
        researchTimeBudget.value = String(deepResearchTask.timeBudgetMinutes || 0);
        researchPreferredDomains.value = (deepResearchTask.preferredDomains || []).join(', ');
        researchAllowDomains.value = (deepResearchTask.allowedDomains || []).join(', ');
        researchBlockDomains.value = (deepResearchTask.blockedDomains || []).join(', ');
        renderResearchTask();
      }
      updateContextSettingForProvider('ollama');
      const sourceProvider = BOOT.sourceProvider === 'lmstudio' ? 'lmstudio' : (BOOT.initialProvider === 'lmstudio' ? 'lmstudio' : 'ollama');
      const sourceModel = String(BOOT.sourceModel || BOOT.initialModel || '');
      if (BOOT.prompt) messages.push({ role: 'user', content: String(BOOT.prompt), timestamp: Date.now(), provider: sourceProvider, model: sourceModel, initial: true });
      if (BOOT.response !== undefined && BOOT.response !== null) {
        const responseParts = extractEmbeddedThinking(String(BOOT.response));
        messages.push({ role: 'assistant', content: responseParts.content, thinking: responseParts.thinking, thinkingOpen: true, thinkRequested: !!BOOT.initialThink, timestamp: Date.now(), provider: sourceProvider, model: sourceModel, error: !!BOOT.isError, initial: true });
      }
      await loadConversationBranches(!BOOT.prompt && BOOT.response == null && !BOOT.initialConversation);
      setConversationTitle(BOOT.title || 'New chat');
      if (!Object.prototype.hasOwnProperty.call(initialPreferences, 'thinkEnabled')) thinkToggle.checked = !!BOOT.initialThink;
      syncThinkButton();
      autoResizePrompt();
      renderMessages(false);
      updateContextManagerUI();
      window.addEventListener('resize', updateResearchPanelPosition);
      if (typeof ResizeObserver === 'function') {
        const researchLayoutObserver = new ResizeObserver(updateResearchPanelPosition);
        researchLayoutObserver.observe($('.topbar'));
      }
      setResearchPanelOpen(false);
      void refreshModels(false).then(async () => {
        if (BOOT.initialConversation && typeof BOOT.initialConversation === 'object') {
          try {
            const { data, messages: restored, branchWorkspace, workspaceContext: savedWorkspaceContext } = validateSessionFile(BOOT.initialConversation);
            if (!restored.length) throw new Error('This conversation contains no messages.');
            await applySessionData(data, restored, BOOT.initialConversationFileName || `${filenameSafe(data.title || 'Conversation')}.runmind`, branchWorkspace, savedWorkspaceContext);
          } catch (error) {
            showToast(`Could not open conversation: ${error?.message || String(error)}`);
          }
          return;
        }
        const pendingPrompt = String(BOOT.autoSendPromptText || '').trim();
        if (!pendingPrompt) return;
        promptInput.value = pendingPrompt;
        autoResizePrompt();
        const selected = selectedModel();
        if (BOOT.initialModel && BOOT.initialProvider
          && (!selected || selected.provider !== BOOT.initialProvider || selected.model !== BOOT.initialModel)) {
          showToast(`The requested ${BOOT.initialProvider === 'lmstudio' ? 'LM Studio' : 'Ollama'} model '${BOOT.initialModel}' is not available in the chat connections. Select or add the matching server/model, then send the prompt.`);
          return;
        }
        // Launch only after model discovery has selected the exact inline provider/model.
        void sendMessage();
      });
      // Native title generation waits for this marker: the setter itself is defined
      // earlier in the script, before initializeRunMind has applied the fallback title.
      window.__runMindInitialized = true;
    };
