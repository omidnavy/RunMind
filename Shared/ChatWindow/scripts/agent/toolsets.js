(function () {
  'use strict';

  const AGENT_TOOLSET_STORAGE_KEY = 'runmind.agent-toolsets.v1';
  const AGENT_TOOLSET_GROUPS = Object.freeze({
    core: { label: 'Core agent', description: 'Planning, calculations, connection diagnostics and agent coordination.' },
    workspace: { label: 'Workspace', description: 'Workspace-scoped files, project intelligence, source control, development and artifact tools.' },
    development: { label: 'Development', description: 'Reusable developer workflow discovery and startup.' },
    web: { label: 'Web and retrieval', description: 'Search, fetch and inspect public web content.' },
    skills: { label: 'Agent Skills', description: 'Discover and load reusable skill instructions.' },
    integrations: { label: 'External integrations', description: 'Tools supplied by connected MCP servers.' },
    research: { label: 'Deep Research', description: 'Evidence capture, claim assessment and research-specific operations.' },
    'system-manager': { label: 'System Manager', description: 'Native operating-system operations, governed by System Manager permissions.' }
  });

  const agentToolsetGroupByName = new Map();

  const BUILTIN_AGENT_TOOLSET_PROFILES = Object.freeze({
    general: { name: 'General Assistant', groups: {} },
    developer: { name: 'Developer', groups: { research: false, 'system-manager': false } },
    researcher: { name: 'Researcher', groups: { workspace: false, development: false, 'system-manager': false } },
    administrator: { name: 'System Administrator', groups: { web: false, research: false } },
    minimal: { name: 'Minimal', groups: { workspace: false, development: false, web: false, skills: false, integrations: false, research: false, 'system-manager': false } }
  });

  function agentToolsetGroupForName(name) {
    const value = String(name || '');
    if (agentToolsetGroupByName.has(value)) return agentToolsetGroupByName.get(value);
    if (value.startsWith('system_manager_')) return 'system-manager';
    if (value.startsWith('mcp_')) return 'integrations';
    if (value.startsWith('research_')) return 'research';
    if (['list_agent_skills', 'load_agent_skill'].includes(value)) return 'skills';
    if (['list_developer_workflows', 'start_developer_workflow'].includes(value)) return 'development';
    if (/^(web_|fetch_webpage$|extract_links$|extract_metadata$|extract_tables$|read_sitemap$|read_rss_feed$|page_to_markdown$|check_links$|compare_webpages$|crawl_website$|research_topic$)/.test(value)) return 'web';
    return 'core';
  }

  function agentToolsetGroupForDefinition(definition) {
    const name = String(definition?.function?.name || '');
    const declared = definition?.__runMindToolsetGroup;
    const group = AGENT_TOOLSET_GROUPS[declared] ? declared : agentToolsetGroupForName(name);
    if (name) agentToolsetGroupByName.set(name, group);
    return group;
  }

  function agentToolsetDefaultSettings() {
    return { version: 1, activeProfile: 'general', groupOverrides: {}, toolOverrides: {}, customProfiles: [] };
  }

  function normalizeAgentToolsetOverrides(value, maximum = 1000) {
    const source = value && typeof value === 'object' && !Array.isArray(value) ? value : {};
    const result = {};
    for (const [key, enabled] of Object.entries(source).slice(0, maximum)) {
      if (typeof key === 'string' && key.length <= 180 && typeof enabled === 'boolean') result[key] = enabled;
    }
    return result;
  }

  function loadAgentToolsetSettings() {
    const fallback = agentToolsetDefaultSettings();
    try {
      const stored = JSON.parse(localStorage.getItem(AGENT_TOOLSET_STORAGE_KEY) || 'null');
      if (!stored || typeof stored !== 'object' || Array.isArray(stored)) return fallback;
      const customProfiles = (Array.isArray(stored.customProfiles) ? stored.customProfiles : []).slice(0, 20).map(profile => ({
        id: String(profile?.id || '').slice(0, 80),
        name: String(profile?.name || '').trim().slice(0, 50),
        groupOverrides: normalizeAgentToolsetOverrides(profile?.groupOverrides, 32),
        toolOverrides: normalizeAgentToolsetOverrides(profile?.toolOverrides, 1000)
      })).filter(profile => profile.id.startsWith('custom-') && profile.name);
      const activeProfile = String(stored.activeProfile || 'general').slice(0, 80);
      let groupOverrides = normalizeAgentToolsetOverrides(stored.groupOverrides, 32);
      let toolOverrides = normalizeAgentToolsetOverrides(stored.toolOverrides, 1000);
      const selectedCustom = customProfiles.find(profile => profile.id === activeProfile);
      if (selectedCustom) {
        groupOverrides = { ...selectedCustom.groupOverrides };
        toolOverrides = { ...selectedCustom.toolOverrides };
      }
      return { version: 1, activeProfile: BUILTIN_AGENT_TOOLSET_PROFILES[activeProfile] || selectedCustom ? activeProfile : 'general', groupOverrides, toolOverrides, customProfiles };
    } catch (_) { return fallback; }
  }

  function saveAgentToolsetSettings(settings = loadAgentToolsetSettings()) {
    const normalized = {
      version: 1,
      activeProfile: String(settings.activeProfile || 'general').slice(0, 80),
      groupOverrides: normalizeAgentToolsetOverrides(settings.groupOverrides, 32),
      toolOverrides: normalizeAgentToolsetOverrides(settings.toolOverrides, 1000),
      customProfiles: (Array.isArray(settings.customProfiles) ? settings.customProfiles : []).slice(0, 20)
    };
    const custom = normalized.customProfiles.find(profile => profile.id === normalized.activeProfile);
    if (custom) {
      custom.groupOverrides = { ...normalized.groupOverrides };
      custom.toolOverrides = { ...normalized.toolOverrides };
    }
    localStorage.setItem(AGENT_TOOLSET_STORAGE_KEY, JSON.stringify(normalized));
    return normalized;
  }

  function persistAgentToolsetSettings(settings) {
    try { saveAgentToolsetSettings(settings); return true; }
    catch (error) {
      if (typeof showToast === 'function') showToast(`Could not save Agent Toolsets in this WebView profile: ${error?.message || String(error)}`);
      return false;
    }
  }

  function agentToolsetProfileName(settings = loadAgentToolsetSettings()) {
    return BUILTIN_AGENT_TOOLSET_PROFILES[settings.activeProfile]?.name
      || settings.customProfiles.find(profile => profile.id === settings.activeProfile)?.name
      || BUILTIN_AGENT_TOOLSET_PROFILES.general.name;
  }

  function agentToolsetGroupEnabled(group, settings = loadAgentToolsetSettings()) {
    if (!AGENT_TOOLSET_GROUPS[group]) return true;
    const profile = BUILTIN_AGENT_TOOLSET_PROFILES[settings.activeProfile];
    const profileDefault = profile?.groups?.[group] !== false;
    const preference = Object.prototype.hasOwnProperty.call(settings.groupOverrides, group)
      ? settings.groupOverrides[group] : profileDefault;
    // System Manager's existing native master switch is a hard availability gate.
    // A profile may hide its tools, but can never turn native capabilities on.
    if (group === 'system-manager') return systemManagerSettingsCache?.enabled === true && preference;
    return preference;
  }

  function agentToolsetIndividualEnabled(name, settings = loadAgentToolsetSettings()) {
    return settings.toolOverrides[String(name || '')] !== false;
  }

  function isAgentToolEnabled(name, settings = loadAgentToolsetSettings()) {
    const toolName = String(name || '');
    const group = agentToolsetGroupByName.get(toolName) || agentToolsetGroupForName(toolName);
    if (!agentToolsetGroupEnabled(group, settings) || !agentToolsetIndividualEnabled(toolName, settings)) return false;
    if (group === 'workspace') {
      const workspaceSelected = typeof agentDirectoryHandle !== 'undefined' && !!agentDirectoryHandle;
      if (toolName === 'request_folder_access') return !workspaceSelected;
      return workspaceSelected;
    }
    if (group === 'research' && typeof deepResearchEnabled !== 'undefined' && !deepResearchEnabled) return false;
    if (group === 'integrations' && typeof mcpToolRoutes !== 'undefined' && mcpToolRoutes instanceof Map && !mcpToolRoutes.has(toolName)) return false;
    return true;
  }

  function filterAgentToolDefinitions(definitions) {
    return (Array.isArray(definitions) ? definitions : []).filter(item => {
      const name = String(item?.function?.name || '');
      agentToolsetGroupForDefinition(item);
      return !!name && isAgentToolEnabled(name);
    });
  }

  function getAgentToolsetCatalog() {
    const definitions = [];
    if (typeof getAgentToolDefinitions === 'function') definitions.push(...getAgentToolDefinitions());
    if (typeof getMcpToolDefinitions === 'function') definitions.push(...getMcpToolDefinitions({ updateRoutes: false }));
    if (typeof researchToolDefinitions === 'function') definitions.push(...researchToolDefinitions());
    const seen = new Set();
    return definitions.filter(item => {
      const name = String(item?.function?.name || '');
      agentToolsetGroupForDefinition(item);
      if (!name || seen.has(name)) return false;
      seen.add(name);
      return true;
    });
  }

  function getAgentToolExecutionSummary(name, ledger = []) {
    const records = (Array.isArray(ledger) ? ledger : []).filter(item => item && item.name === name);
    const last = records[records.length - 1];
    let lastResult = last?.state || last?.status || 'not-executed';
    if (lastResult === 'uncertain') lastResult = 'unverified';
    const lastResultMessage = String(last?.message || last?.resultError || last?.resultMessage || '').replace(/[\r\n]+/g, ' ').slice(0, 360);
    return { executed: records.length > 0, executionCount: records.length, lastResult, lastResultMessage };
  }

  function getAgentToolsetReport(catalog = getAgentToolsetCatalog(), exposed = activeToolDefinitions, executionLedger = []) {
    const exposedNames = new Set((Array.isArray(exposed) ? exposed : []).map(item => String(item?.function?.name || '')).filter(Boolean));
    const settings = loadAgentToolsetSettings();
    const tools = catalog.map(item => {
      const definition = item?.function || {};
      const name = String(definition.name || '');
      const group = agentToolsetGroupForDefinition(item);
      const groupEnabled = agentToolsetGroupEnabled(group, settings);
      const groupAvailable = group === 'system-manager' ? systemManagerSettingsCache?.enabled === true
        : group === 'research' ? deepResearchEnabled === true
          : group === 'integrations' ? (typeof mcpToolRoutes !== 'undefined' && mcpToolRoutes instanceof Map && mcpToolRoutes.has(name)) : true;
      const individualEnabled = agentToolsetIndividualEnabled(name, settings);
      const exposedNow = exposedNames.has(name);
      const execution = getAgentToolExecutionSummary(name, executionLedger);
      const reason = !groupAvailable ? 'System Manager is disabled in its existing settings.'
        : !groupEnabled ? 'Tool group hidden by the active profile or user preference.'
          : !individualEnabled ? 'Tool disabled individually.'
            : !exposedNow ? 'Currently unavailable in this mode or runtime context.' : '';
      return { name, group, groupLabel: AGENT_TOOLSET_GROUPS[group]?.label || group, description: String(definition.description || '').slice(0, 1200), configured: groupEnabled && individualEnabled, available: groupAvailable, exposedNow, ...execution, reason };
    }).filter(item => item.name).sort((a, b) => a.groupLabel.localeCompare(b.groupLabel) || a.name.localeCompare(b.name));
    const groups = Object.entries(AGENT_TOOLSET_GROUPS).map(([id, meta]) => {
      const rows = tools.filter(item => item.group === id);
      return { id, name: meta.label, description: meta.description, configured: agentToolsetGroupEnabled(id, settings), available: id === 'system-manager' ? systemManagerSettingsCache?.enabled === true : id === 'research' ? deepResearchEnabled === true : id === 'integrations' ? rows.some(item => item.available) : true, total: rows.length, exposed: rows.filter(item => item.exposedNow).length, disabled: rows.filter(item => !item.configured).length };
    });
    return { profile: settings.activeProfile, profileName: agentToolsetProfileName(settings), groups, tools, total: tools.length, exposed: tools.filter(item => item.exposedNow).length, disabled: tools.filter(item => !item.configured).length, executed: tools.filter(item => item.executed).length, failed: tools.filter(item => item.lastResult === 'failed').length, unverified: tools.filter(item => item.lastResult === 'unverified').length, cancelled: tools.filter(item => item.lastResult === 'cancelled').length };
  }

  function applyAgentToolsetProfile(profileId) {
    const settings = loadAgentToolsetSettings();
    const builtin = BUILTIN_AGENT_TOOLSET_PROFILES[profileId];
    const custom = settings.customProfiles.find(profile => profile.id === profileId);
    if (!builtin && !custom) return false;
    settings.activeProfile = profileId;
    settings.groupOverrides = builtin ? {} : { ...custom.groupOverrides };
    settings.toolOverrides = builtin ? {} : { ...custom.toolOverrides };
    if (!persistAgentToolsetSettings(settings)) return false;
    if (typeof rebuildActiveToolDefinitions === 'function') rebuildActiveToolDefinitions(deepResearchEnabled);
    renderAgentToolsets();
    return true;
  }

  function setAgentToolsetGroupPreference(group, enabled) {
    if (!AGENT_TOOLSET_GROUPS[group] || group === 'system-manager') return false;
    const settings = loadAgentToolsetSettings();
    settings.groupOverrides[group] = !!enabled;
    if (!persistAgentToolsetSettings(settings)) return false;
    if (typeof rebuildActiveToolDefinitions === 'function') rebuildActiveToolDefinitions(deepResearchEnabled);
    renderAgentToolsets();
    return true;
  }

  function setAgentToolPreference(name, enabled) {
    const toolName = String(name || '');
    if (!toolName || toolName.length > 180) return false;
    const settings = loadAgentToolsetSettings();
    settings.toolOverrides[toolName] = !!enabled;
    if (!persistAgentToolsetSettings(settings)) return false;
    if (typeof rebuildActiveToolDefinitions === 'function') rebuildActiveToolDefinitions(deepResearchEnabled);
    renderAgentToolsets();
    return true;
  }

  function saveAgentToolsetProfile(name) {
    const cleanName = String(name || '').trim().slice(0, 50);
    if (!cleanName) throw new Error('Enter a name for the toolset profile.');
    const settings = loadAgentToolsetSettings();
    let profile = settings.customProfiles.find(item => item.id === settings.activeProfile);
    if (!profile && settings.customProfiles.length >= 20) throw new Error('You can save up to 20 custom toolset profiles.');
    const wasCustom = !!profile;
    if (profile) {
      profile.name = cleanName;
    } else {
      profile = { id: `custom-${crypto.randomUUID()}`, name: cleanName, groupOverrides: {}, toolOverrides: {} };
      settings.customProfiles.push(profile);
    }
    if (!wasCustom) {
      settings.groupOverrides = Object.fromEntries(Object.keys(AGENT_TOOLSET_GROUPS).map(group => [group, agentToolsetGroupEnabled(group, settings)]));
    }
    settings.activeProfile = profile.id;
    profile.groupOverrides = { ...settings.groupOverrides };
    profile.toolOverrides = { ...settings.toolOverrides };
    if (!persistAgentToolsetSettings(settings)) throw new Error('The custom toolset profile could not be saved.');
    renderAgentToolsets();
    return profile;
  }

  function deleteAgentToolsetProfile() {
    const settings = loadAgentToolsetSettings();
    const profile = settings.customProfiles.find(item => item.id === settings.activeProfile);
    if (!profile) return false;
    settings.customProfiles = settings.customProfiles.filter(item => item.id !== profile.id);
    settings.activeProfile = 'general';
    settings.groupOverrides = {};
    settings.toolOverrides = {};
    if (!persistAgentToolsetSettings(settings)) return false;
    if (typeof rebuildActiveToolDefinitions === 'function') rebuildActiveToolDefinitions(deepResearchEnabled);
    renderAgentToolsets();
    return true;
  }

  function renderAgentToolsets() {
    const list = document.getElementById('agentToolsetList');
    const profileSelect = document.getElementById('agentToolsetProfile');
    const deleteButton = document.getElementById('agentToolsetDeleteProfile');
    const saveButton = document.getElementById('agentToolsetSaveProfile');
    const reportSummary = document.getElementById('agentToolsetReportSummary');
    const searchInput = document.getElementById('agentToolsetSearch');
    if (!list || !profileSelect) return;

    const settings = loadAgentToolsetSettings();
    const catalog = getAgentToolsetCatalog();
    const report = getAgentToolsetReport(catalog, activeToolDefinitions);
    const profiles = [
      ...Object.entries(BUILTIN_AGENT_TOOLSET_PROFILES).map(([id, profile]) => ({ id, name: profile.name })),
      ...settings.customProfiles.map(profile => ({ id: profile.id, name: `${profile.name} · Custom` }))
    ];
    const prior = settings.activeProfile;
    profileSelect.replaceChildren();
    for (const profile of profiles) {
      const option = document.createElement('option'); option.value = profile.id; option.textContent = profile.name; profileSelect.appendChild(option);
    }
    profileSelect.value = profiles.some(item => item.id === prior) ? prior : 'general';
    const isCustomProfile = settings.activeProfile.startsWith('custom-');
    if (deleteButton) deleteButton.disabled = !isCustomProfile;
    if (saveButton) saveButton.textContent = isCustomProfile ? 'Update custom profile' : 'Save as custom profile';
    if (reportSummary) {
      const activeMode = agentModeEnabled || deepResearchEnabled;
      reportSummary.textContent = `${report.exposed} exposed to the current agent · ${report.disabled} disabled by tool preferences · ${report.total} currently discoverable definitions${activeMode ? '' : ' · enable Agent or Deep Research mode to expose tools'}`;
    }

    const query = String(searchInput?.value || '').trim().toLowerCase();
    const groups = Object.keys(AGENT_TOOLSET_GROUPS).map(id => ({ id, ...AGENT_TOOLSET_GROUPS[id], tools: report.tools.filter(item => item.group === id && (!query || `${item.name} ${item.description} ${AGENT_TOOLSET_GROUPS[id].label}`.toLowerCase().includes(query))) }));
    list.replaceChildren();
    let renderedGroups = 0;
    for (const group of groups) {
      if ((!group.tools.length && group.id !== 'system-manager') || (query && !group.tools.length)) continue;
      const section = document.createElement('details'); section.className = 'agent-toolset-group'; section.open = !!query;
      const header = document.createElement('summary'); header.className = 'agent-toolset-group-summary';
      const titleWrap = document.createElement('span'); titleWrap.className = 'agent-toolset-group-title';
      const title = document.createElement('strong'); title.textContent = group.label;
      const description = document.createElement('small');
      description.textContent = group.id === 'system-manager'
        ? (!systemManagerSettingsCache?.enabled ? 'Disabled by the existing System Manager master switch.' : !agentToolsetGroupEnabled('system-manager', settings) ? 'Hidden by this Toolset profile; native System Manager permissions remain unchanged.' : 'Master availability and capabilities are controlled by the existing System Manager settings.')
        : group.id === 'research' && !deepResearchEnabled ? 'Only available while Deep Research mode is active.'
          : group.description;
      titleWrap.append(title, description);
      const control = document.createElement('input'); control.type = 'checkbox'; control.className = 'agent-toolset-group-toggle'; control.dataset.toolsetGroup = group.id;
      control.checked = agentToolsetGroupEnabled(group.id, settings);
      control.disabled = group.id === 'system-manager';
      control.setAttribute('aria-label', `Enable ${group.label} tool group`);
      control.addEventListener('click', event => event.stopPropagation());
      const count = document.createElement('span'); count.className = 'agent-toolset-count'; count.textContent = `${group.tools.length} tools`;
      header.append(titleWrap, count, control);
      section.appendChild(header);
      if (!group.tools.length) {
        const empty = document.createElement('p'); empty.className = 'agent-toolset-empty'; empty.textContent = systemManagerSettingsCache?.enabled ? 'No System Manager tools are currently available under the selected capabilities.' : 'System Manager is disabled. Enable it from the System Manager menu to make its tools available.';
        section.appendChild(empty);
      }
      for (const tool of group.tools) {
        const row = document.createElement('label'); row.className = 'agent-toolset-tool';
        const checkbox = document.createElement('input'); checkbox.type = 'checkbox'; checkbox.dataset.toolsetTool = tool.name;
        checkbox.checked = agentToolsetIndividualEnabled(tool.name, settings);
        checkbox.disabled = !agentToolsetGroupEnabled(group.id, settings);
        checkbox.setAttribute('aria-label', `Enable tool ${tool.name}`);
        const content = document.createElement('span'); content.className = 'agent-toolset-tool-content';
        const name = document.createElement('strong'); name.textContent = tool.name;
        const detail = document.createElement('small'); detail.textContent = tool.description || 'No description supplied.';
        const status = document.createElement('small'); status.className = 'agent-toolset-tool-status';
        status.textContent = tool.exposedNow ? 'Exposed to current agent' : tool.reason || 'Not currently exposed';
        content.append(name, detail, status); row.append(checkbox, content); section.appendChild(row);
      }
      list.appendChild(section);
      renderedGroups++;
    }
    if (!renderedGroups) {
      const empty = document.createElement('p'); empty.className = 'agent-toolset-empty'; empty.textContent = 'No tools match this search.'; list.appendChild(empty);
    }
  }

  function initializeAgentToolsetsUi() {
    const profileSelect = document.getElementById('agentToolsetProfile');
    const saveButton = document.getElementById('agentToolsetSaveProfile');
    const deleteButton = document.getElementById('agentToolsetDeleteProfile');
    const resetButton = document.getElementById('agentToolsetResetProfile');
    const list = document.getElementById('agentToolsetList');
    const searchInput = document.getElementById('agentToolsetSearch');
    if (!profileSelect || !list || list.dataset.initialized === 'true') return;
    list.dataset.initialized = 'true';
    profileSelect.addEventListener('change', () => applyAgentToolsetProfile(profileSelect.value));
    searchInput?.addEventListener('input', () => renderAgentToolsets());
    saveButton?.addEventListener('click', () => {
      const name = document.getElementById('agentToolsetProfileName')?.value || '';
      try {
        const profile = saveAgentToolsetProfile(name);
        const nameInput = document.getElementById('agentToolsetProfileName'); if (nameInput) nameInput.value = '';
        showToast(`Toolset profile '${profile.name}' saved.`);
      } catch (error) { showToast(error?.message || String(error)); }
    });
    deleteButton?.addEventListener('click', () => {
      const settings = loadAgentToolsetSettings();
      const profile = settings.customProfiles.find(item => item.id === settings.activeProfile);
      if (!profile || !window.confirm(`Delete toolset profile '${profile.name}'?`)) return;
      deleteAgentToolsetProfile(); showToast(`Toolset profile '${profile.name}' deleted.`);
    });
    resetButton?.addEventListener('click', () => applyAgentToolsetProfile('general'));
    list.addEventListener('change', event => {
      const group = event.target?.dataset?.toolsetGroup;
      const tool = event.target?.dataset?.toolsetTool;
      if (group) setAgentToolsetGroupPreference(group, event.target.checked);
      else if (tool) setAgentToolPreference(tool, event.target.checked);
    });
    renderAgentToolsets();
  }

  // Keep the API deliberately small: one eligibility function is shared by the
  // provider-facing definition filter and the execution guard.
  Object.assign(globalThis, {
    agentToolsetGroupForName,
    agentToolsetGroupForDefinition,
    agentToolsetGroupEnabled,
    agentToolsetIndividualEnabled,
    filterAgentToolDefinitions,
    getAgentToolsetCatalog,
    getAgentToolsetReport,
    getAgentToolExecutionSummary,
    initializeAgentToolsetsUi,
    isAgentToolEnabled,
    loadAgentToolsetSettings,
    saveAgentToolsetSettings,
    saveAgentToolsetProfile,
    deleteAgentToolsetProfile,
    applyAgentToolsetProfile,
    setAgentToolPreference,
    setAgentToolsetGroupPreference,
    renderAgentToolsets
  });
})();
