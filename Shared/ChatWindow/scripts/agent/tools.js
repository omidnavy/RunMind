    'use strict';

    function normalizeSystemManagerOperationResult(result, toolName, args = {}) {
      if (!result || typeof result !== 'object' || Array.isArray(result)) return result;
      const target = String(args.processId ?? args.service ?? args.container ?? args.name ?? args.applicationId ?? args.target ?? args.adapterId ?? 'system').slice(0, 256);
      const action = String(args.action || toolName.replace(/^system_manager_/, '').replaceAll('_', '-') || 'inspect').slice(0, 80);
      const stateVerified = typeof result.actualState === 'string' && typeof result.requestedState === 'string' && result.actualState.toLowerCase() === result.requestedState.toLowerCase();
      const verified = typeof result.verified === 'boolean' ? result.verified
        : typeof result.closed === 'boolean' ? result.closed
          : typeof result.saved === 'boolean' ? result.saved
            : result.installed === true || result.reset === true || result.removed === true || stateVerified || result.exitCode === 0 || result.diagnostics !== undefined || Array.isArray(result.processes) || Array.isArray(result.startupApplications) || Array.isArray(result.applications) || Array.isArray(result.services) || Array.isArray(result.adapters) || Array.isArray(result.items);
      const accepted = typeof result.accepted === 'boolean' ? result.accepted
        : typeof result.requestAccepted === 'boolean' ? result.requestAccepted
          : typeof result.activationRequested === 'boolean' ? result.activationRequested
            : typeof result.launched === 'boolean' ? result.launched
              : typeof result.activated === 'boolean' ? result.activated
                : typeof result.saved === 'boolean' ? result.saved
                  : Array.isArray(result.closeRequested) ? result.closeRequested.length > 0
                    : typeof result.closed === 'boolean' ? result.closed : verified;
      const completed = typeof result.completed === 'boolean' ? result.completed : (!result.cancelled && verified);
      const success = typeof result.success === 'boolean' ? result.success : (!result.cancelled && verified);
      return { ...result, success, accepted, completed, verified, action: result.action || action, target: result.target || result.application || result.id || target,
        message: result.message || result.note || (verified ? 'The operation completed and its result was verified.' : accepted ? 'The operation was accepted, but its final outcome could not be verified.' : 'The operation did not complete successfully.') };
    }
    async function executeAgentTool(name, args, signal, assistantMessage, taskContext = {}) {
      const previousTool = activeAgentApprovalTool;
      activeAgentApprovalTool = name;
      try {
        const result = await executeAgentToolInternal(name, args, signal, assistantMessage, taskContext);
        return name.startsWith('system_manager_') ? normalizeSystemManagerOperationResult(result, name, args) : result;
      }
      finally { activeAgentApprovalTool = previousTool; }
    }
    async function searchWebQuery(query, signal, maximum = 8) {
      const base = getConfiguredSearchEndpoint();
      if (!base) throw new Error('Web search is unavailable because no valid SearXNG instance URL is configured.');
      const url = new URL(`${base}/search`); url.searchParams.set('q', String(query || '').slice(0, 500)); url.searchParams.set('format', 'json');
      const response = await fetch(url, { signal, headers: { Accept: 'application/json' } });
      const body = await readBoundedResponseText(response, 4 * 1024 * 1024);
      if (!response.ok) throw new Error(`SearXNG returned HTTP ${response.status}${body ? `: ${body.slice(0, 400)}` : ''}.`);
      let data; try { data = JSON.parse(body); } catch (_) { throw new Error('SearXNG returned invalid JSON.'); }
      return (Array.isArray(data.results) ? data.results : []).slice(0, maximum).map(item => ({ title: item.title || '', url: item.url || '', content: String(item.content || '').slice(0, 1200), published: item.publishedDate || item.published || '' })).filter(item => /^https?:\/\//i.test(item.url));
    }
    function researchDomainRules(value) {
      return (Array.isArray(value) ? value : String(value || '').split(/[\s,;]+/)).map(item => String(item || '').trim().toLowerCase().replace(/^https?:\/\//, '').replace(/^\*\./, '.').replace(/\/$/, '')).filter(Boolean).slice(0, 100);
    }
    function researchDomainMatches(host, rule) {
      const normalized = String(rule || '').replace(/^\./, '').toLowerCase();
      return !!normalized && (host === normalized || host.endsWith(`.${normalized}`));
    }
    function isResearchTaskActive(task = deepResearchTask) { return !!(task && ['running','pausing','resuming'].includes(task.status)); }
    let researchLiveUiLastAt = 0;
    function setResearchLiveOperation(message, toolName = '', argumentPreview = '') {
      if (!isResearchTaskActive()) return;
      deepResearchTask.liveStatus = String(message || '').slice(0, 240);
      deepResearchTask.liveToolCalls = toolName ? [{ name: String(toolName).slice(0, 80), argumentsPreview: String(argumentPreview || '').slice(0, 180) }] : [];
      const now = Date.now();
      if (now - researchLiveUiLastAt >= 140 || !researchLiveOperation.textContent) {
        researchLiveUiLastAt = now;
        renderResearchTask();
      }
    }
    function isResearchDomainAllowed(value, task = isResearchTaskActive() ? deepResearchTask : null) {
      let host; try { host = new URL(value).hostname.toLowerCase(); } catch (_) { return false; }
      if (!task) return true;
      const blocked = researchDomainRules(task?.blockedDomains);
      if (blocked.some(rule => researchDomainMatches(host, rule))) return false;
      const allowed = researchDomainRules(task?.allowedDomains);
      return !allowed.length || allowed.some(rule => researchDomainMatches(host, rule));
    }
    function consumeResearchPages(count = 1) {
      if (!isResearchTaskActive()) return;
      const amount = Math.max(0, Math.min(100, Math.floor(Number(count) || 0)));
      if (deepResearchTask.pagesFetched + amount > deepResearchTask.pageBudget) {
        deepResearchTask.pageBudgetReached = true;
        throw new Error(`Research page budget reached (${deepResearchTask.pageBudget}). Continue with already collected evidence.`);
      }
      deepResearchTask.pagesFetched += amount;
      persistResearchTask();
    }
    async function executeAgentToolInternal(name, args, signal, assistantMessage, taskContext = {}) {
      if (signal.aborted) throw new DOMException('Task cancelled.', 'AbortError');
      const readOnlyTools = new Set(['current_datetime', 'calculate', 'regex_test', 'diagnose_connection', 'validate_json_schema', 'read_pdf', 'inspect_archive', 'infer_dataset_schema', 'fetch_webpage', 'web_search', 'list_files', 'read_file', 'search_files', 'file_info', 'checksum_file', 'compare_files', 'project_overview', 'project_index', 'project_search', 'project_index_overview', 'project_symbol_graph', 'javascript_refactor', 'project_validate', 'list_developer_workflows', 'start_developer_workflow', 'validate_json', 'transform_json', 'validate_dataset', 'verify_artifact', 'analyze_csv', 'compare_datasets', 'generate_statistics', 'compare_folders', 'find_duplicates', 'image_metadata', 'read_image', 'extract_links', 'extract_metadata', 'extract_tables', 'read_sitemap', 'read_rss_feed', 'page_to_markdown', 'check_links', 'compare_webpages', 'crawl_website', 'research_topic', 'system_manager_find_applications', 'system_manager_list_startup_applications', 'system_manager_list_adapters', 'system_manager_list_processes', 'system_manager_diagnostics']);
      if (name.startsWith('system_manager_')) {
        if (!systemManagerSettingsCache?.enabled) throw new Error('Windows System Manager is disabled. Enable it in the System Manager menu first.');
        if (deepResearchEnabled || taskContext?.deepResearch || taskContext?.subAgentDepth) throw new Error('System Manager operations are unavailable in Deep Research and delegated sub-agent tasks.');
        if (name === 'system_manager_find_applications') { const query = String(args.query || '').trim(); if (!query) throw new Error('A non-empty application name is required.'); return await callAgentHost('systemManager.apps.list',{query},signal); }
        if (name === 'system_manager_list_startup_applications') return await callAgentHost('systemManager.startup.list',{query:String(args.query||'')},signal);
        if (name === 'system_manager_set_startup_application') return await callAgentHost('systemManager.startup.set',{action:String(args.action||''),name:String(args.name||''),source:String(args.source||''),entryPath:String(args.entry_path||'')},signal);
        if (name === 'system_manager_list_adapters') return await callAgentHost('systemManager.adapters.list',{},signal);
        if (name === 'system_manager_container_action') {
          const action = String(args.action || '').toLowerCase();
          if (!['start','stop','restart'].includes(action) && !loadAgentSettings().autoApproveReads && !taskApprovedReadTools.has(name)) {
            const approved = await requireAgentApproval({ title: 'Allow read-only container inspection?', subtitle: `System Manager · ${action || 'container query'}`, detail: JSON.stringify({ runtime: args.runtime || 'auto', action, container: args.container || '' }), allowForTask: true, toolName: name }, signal);
            if (!approved) return { cancelled: true, message: 'User declined the container inspection.' };
          }
          return await callAgentHost('systemManager.containers.action',{runtime:String(args.runtime||'auto'),action,container:String(args.container||'')},signal);
        }
        if (name === 'system_manager_install_adapter') return await callAgentHost('systemManager.adapters.save',{adapter:args.adapter||{}},signal);
        if (name === 'system_manager_list_processes') return await callAgentHost('systemManager.process.list',{query:String(args.query||'')},signal);
        if (name === 'system_manager_diagnostics') return await callAgentHost('systemManager.diagnostics',{},signal);
        if (name === 'system_manager_service_action') {
          const action = String(args.action || '').toLowerCase();
          if (['list','inspect','config','dependents'].includes(action) && !loadAgentSettings().autoApproveReads && !taskApprovedReadTools.has(name)) {
            const approved = await requireAgentApproval({ title: 'Allow read-only Windows service inspection?', subtitle: `System Manager · ${action}`, detail: JSON.stringify({ action, service: args.service || '' }), allowForTask: true, toolName: name }, signal);
            if (!approved) return { cancelled: true, message: 'User declined the service inspection.' };
          }
          return await callAgentHost('systemManager.services.action',{action,service:String(args.service||'')},signal);
        }
        if (name === 'system_manager_launch_application') {
          return await callAgentHost('systemManager.apps.launch',{applicationId:String(args.applicationId||''),arguments:Array.isArray(args.arguments)?args.arguments:[],behavior:String(args.behavior||'')},signal);
        }
        if (name === 'system_manager_set_application_behavior') {
          return await callAgentHost('systemManager.apps.behavior.save',{applicationId:String(args.applicationId||''),behavior:String(args.behavior||'')},signal);
        }
        if (name === 'system_manager_close_application') {
          return await callAgentHost('systemManager.apps.close',{applicationId:String(args.applicationId||'')},signal);
        }
        if (name === 'system_manager_application_action') {
          return await callAgentHost('systemManager.adapters.launch',{adapterId:String(args.adapterId||''),operation:String(args.operation||''),target:String(args.target||''),line:args.line},signal);
        }
        if (name === 'system_manager_open_target') {
          return await callAgentHost('systemManager.open.target',{target:String(args.target||'')},signal);
        }
        if (name === 'system_manager_close_process') {
          return await callAgentHost('systemManager.process.close',{processId:args.processId,expectedName:String(args.expectedName||''),expectedPath:String(args.expectedPath||''),expectedStartedAtUtc:String(args.expectedStartedAtUtc||'')},signal);
        }
        if (name === 'system_manager_terminate_process') {
          return await callAgentHost('systemManager.process.terminate',{processId:args.processId,expectedName:String(args.expectedName||''),expectedPath:String(args.expectedPath||''),expectedStartedAtUtc:String(args.expectedStartedAtUtc||'')},signal);
        }
      }
      if (name.startsWith('mcp_')) return await executeMcpTool(name, args, signal);
      const gitRoutes = {
        git_status: ['status','gitRead'], git_diff: ['diff','gitRead'], git_log: ['log','gitRead'], git_show: ['show','gitRead'], git_branches: ['branches','gitRead'], git_remotes: ['remotes','gitRead'],
        git_stage: ['stage','gitWrite'], git_unstage: ['unstage','gitWrite'], git_commit: ['commit','gitWrite'], git_create_branch: ['create_branch','gitWrite'], git_switch_branch: ['switch_branch','gitWrite'], git_merge: ['merge','gitWrite'], git_revert: ['revert','gitWrite'], git_cherry_pick: ['cherry_pick','gitWrite'], git_stash: ['stash','gitWrite'],
        git_fetch: ['fetch','gitRemote'], git_pull: ['pull','gitRemote'], git_push: ['push','gitRemote'], git_reset_hard: ['reset_hard','gitDestructive'], git_clean: ['clean','gitDestructive'], git_restore: ['restore','gitDestructive'], git_delete_branch: ['delete_branch','gitDestructive']
      };
      if (['select_native_workspace','powershell_execute','run_native_command'].includes(name) && (deepResearchEnabled || taskContext?.deepResearch))
        throw new Error('Native process execution is disabled in Deep Research and delegated sub-agent tasks. Use Agent Mode for explicitly approved local execution.');
      if (name === 'select_native_workspace') {
        const approved = await requireAgentApproval({ title: 'Select a native execution workspace?', subtitle: 'Native process access · this chat window only', detail: 'Choose the local project directory in which approved PowerShell scripts and native executables may run. The OS folder picker is separate from browser workspace access. This does not sandbox processes.', risky: true, alwaysAsk: true }, signal);
        if (!approved) return { cancelled: true };
        const result = await callAgentHost('process.selectWorkspace', { contextId: workspaceContext.id }, signal);
        if (result?.selected) return { ...result, message: `Native execution workspace selected: ${result.folder}. Every process invocation will still require fresh approval.` };
        return result;
      }
      if (name === 'powershell_execute') {
        const script = String(args.script || '');
        if (!script.trim()) throw new Error('Provide a non-empty PowerShell script.');
        const workingDirectory = String(args.working_directory || '').trim();
        const approved = await requireAgentApproval({ title: 'Run this PowerShell script?', subtitle: 'Native process · current Windows user permissions', detail: `Working directory: ${workingDirectory || '(workspace root)'}
Timeout: ${Math.max(1, Math.min(900, Number(args.timeout_seconds) || 300))} seconds

Script:
${script}

PowerShell is not sandboxed and may access files and resources available to your Windows account.`, risky: true, alwaysAsk: true }, signal);
        if (!approved) return { cancelled: true, message: 'PowerShell execution was declined.' };
        return await callAgentHost('process.powershell', { script, working_directory: workingDirectory, timeout_seconds: args.timeout_seconds, contextId: workspaceContext.id }, signal, Math.max(60000, Math.min(930000, (Number(args.timeout_seconds) || 300) * 1000 + 15000)));
      }
      if (name === 'run_native_command') {
        const executable = String(args.executable || '').trim();
        const commandArguments = Array.isArray(args.arguments) ? args.arguments.map(value => String(value)) : [];
        if (!executable) throw new Error('Provide an executable name ending in .exe.');
        const workingDirectory = String(args.working_directory || '').trim();
        const approved = await requireAgentApproval({ title: `Run ${executable}?`, subtitle: 'Native executable · current Windows user permissions', detail: `Executable: ${executable}
Arguments:
${commandArguments.map((value, index) => `${index + 1}. ${value}`).join('\n') || '(none)'}
Working directory: ${workingDirectory || '(workspace root)'}
Timeout: ${Math.max(1, Math.min(900, Number(args.timeout_seconds) || 300))} seconds

The process is not sandboxed and runs with your Windows account permissions.`, risky: true, alwaysAsk: true }, signal);
        if (!approved) return { cancelled: true, message: 'Native command execution was declined.' };
        return await callAgentHost('process.run', { executable, arguments: commandArguments, working_directory: workingDirectory, timeout_seconds: args.timeout_seconds, contextId: workspaceContext.id }, signal, Math.max(60000, Math.min(930000, (Number(args.timeout_seconds) || 300) * 1000 + 15000)));
      }
      if (name === 'git_select_repository') {
        const approved = await requireAgentApproval({ title: 'Select a Git repository?', subtitle: 'Native folder access · this chat window only', detail: 'Choose the repository directory RunMind may access. This native Git selection is separate from the browser workspace folder grant. Repository access always requires a fresh confirmation.', risky: true, alwaysAsk: true, permissionCategory: 'gitWrite' }, signal);
        if (!approved) return { cancelled: true };
        return await callAgentHost('git.select', { contextId: workspaceContext.id }, signal);
      }
      if (name === 'git_clone') {
        const rawUrl = String(args.url || '').trim();
        let cloneUrl; try { cloneUrl = new URL(rawUrl); } catch (_) { throw new Error('Provide a valid absolute HTTP or HTTPS Git URL.'); }
        if (!['http:','https:'].includes(cloneUrl.protocol) || cloneUrl.username || cloneUrl.password || cloneUrl.hash) throw new Error('Git clone requires an HTTP or HTTPS URL without embedded credentials or fragments.');
        for (const key of cloneUrl.searchParams.keys()) if (/(?:token|secret|password|api.?key|credential|authorization|signature)/i.test(key)) throw new Error('Credential-like URL query parameters are not allowed. Configure credentials with Windows Git Credential Manager.');
        const url = cloneUrl.href;
        const approved = await requireAgentApproval({ title: 'Clone this Git repository?', subtitle: 'Remote operation · writes into a new local directory', detail: `Repository: ${url}
Destination folder: ${String(args.directory || '(derived from repository URL)')}
Git credentials, if required, must already be configured in Windows.`, risky: true, permissionCategory: 'gitRemote' }, signal);
        if (!approved) return { cancelled: true };
        const clonedContextId = crypto.randomUUID();
        const result = await callAgentHost('git.clone', { url, directory: args.directory || '', contextId: clonedContextId }, signal, 20 * 60 * 1000, progress => { const message = String(progress.message || '').trim().slice(-240); if (message) agentSkillsStatus.textContent = `Git clone: ${message}`; });
        if (result?.cloned) {
          try { await workspaceDbAction('readwrite', 'delete'); } catch (error) { result.workspaceGrantCleanupWarning = String(error?.message || error); }
          persistedDirectoryHandle = null; agentDirectoryHandle = null;
          workspaceContext.id = clonedContextId;
          bootWorkspaceContextId = null;
          workspaceContext.folderName = String(result.folder || '').slice(0, 256);
          updateAgentFolderButton(); rebuildActiveToolDefinitions(deepResearchEnabled);
          result.browserWorkspace = 'The cloned folder is selected for native Git in this conversation. To enable browser-based file tools, call request_folder_access and select the cloned folder; browser grants cannot be transferred from a native folder picker.';
        }
        return result;
      }
      if (gitRoutes[name]) {
        const [action, category] = gitRoutes[name];
        const detail = ['fetch','pull','push'].includes(action)
          ? `Action: ${action}
Remote: ${String(args.remote || '')}
Branch: ${String(args.branch || '')}`
          : action === 'commit' ? `Commit message:
${String(args.message || '')}
Git hooks may execute during commit.`
            : action === 'reset_hard' ? `Reset target: ${String(args.target || '')}
This discards local working-tree and index changes.`
              : action === 'clean' ? 'git clean -fd will permanently remove untracked files and directories in the selected repository.'
                : action === 'restore' ? `Discard working-tree changes in:
${(args.paths || []).join('\n')}`
                  : JSON.stringify(args, null, 2);
        const approved = await requireAgentApproval({ title: `Allow Git ${action.replace('_',' ')}?`, subtitle: `Git · ${category === 'gitDestructive' ? 'destructive' : category === 'gitRemote' ? 'remote' : category === 'gitWrite' ? 'repository write' : 'read-only'}`, detail: `${detail}
Repository: selected native Git folder${category !== 'gitRead' ? '\nGit hooks or configured helpers may execute as part of this operation.' : ''}`, risky: category !== 'gitRead', alwaysAsk: category === 'gitDestructive' || action === 'commit', permissionCategory: category }, signal);
        if (!approved) return { cancelled: true, message: 'Git operation was not run because approval was declined.' };
        return await callAgentHost('git.run', { action, ...args, contextId: workspaceContext.id }, signal);
      }
      if (name === 'download_file') {
        const rawUrl = String(args.url || '').trim();
        let target; try { target = new URL(rawUrl); } catch (_) { throw new Error('Provide a valid absolute HTTP or HTTPS URL.'); }
        if (!['http:','https:'].includes(target.protocol) || target.username || target.password) throw new Error('Downloads require a credential-free HTTP or HTTPS URL.');
        const approved = await requireAgentApproval({ title: 'Download this file?', subtitle: 'Network access · writes a new local file', detail: `URL: ${target.href}
Filename: ${String(args.filename || '(from URL)')}
Expected SHA-256: ${String(args.sha256 || '(not supplied)')}`, risky: true, permissionCategory: 'downloads' }, signal);
        if (!approved) return { cancelled: true };
        if (agentDirectoryHandle) {
          let download;
          try {
            download = await makeWebpageDownload(target, signal, args.filename, false);
          } catch (browserError) {
            if (signal?.aborted || browserError?.name === 'AbortError') throw browserError;
            const reason = String(browserError?.message || browserError);
            const fallbackEligible = /failed to fetch|networkerror|network request failed|cors|10 mb size limit|load failed|failed to load/i.test(reason);
            if (!fallbackEligible || !window.chrome?.webview) throw browserError;
            const native = await callAgentHost('download.file', { url: target.href, filename: args.filename || '', sha256: args.sha256 || '' }, signal, 2 * 60 * 60 * 1000, progress => { agentSkillsStatus.textContent = `Native download: ${formatBytes(progress.bytes)}${progress.total ? ` / ${formatBytes(progress.total)} (${Math.min(100, Math.round(progress.bytes / progress.total * 100))}%)` : ''}`; });
            return { ...native, transport: 'native-resumable', browserFallbackReason: reason.slice(0, 400) };
          }
          const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', await download.blob.arrayBuffer()));
          const hash = [...digest].map(value => value.toString(16).padStart(2, '0')).join('');
          if (args.sha256 && hash.toLowerCase() !== String(args.sha256).trim().toLowerCase()) throw new Error(`SHA-256 checksum mismatch: expected ${args.sha256}, received ${hash}.`);
          const written = await writeWorkspaceFile(download.filename, download.blob, false);
          return { saved: true, path: written.path, folder: agentDirectoryHandle.name || 'selected folder', bytes: written.bytes, source: target.href, sha256: hash, transport: 'browser' };
        }
        if (!window.chrome?.webview) throw new Error('Choose a workspace folder to save through the browser, or run RunMind in a desktop host for the resumable native downloader.');
        return await callAgentHost('download.file', { url: target.href, filename: args.filename || '', sha256: args.sha256 || '' }, signal, 2 * 60 * 60 * 1000, progress => { agentSkillsStatus.textContent = `Native download: ${formatBytes(progress.bytes)}${progress.total ? ` / ${formatBytes(progress.total)} (${Math.min(100, Math.round(progress.bytes / progress.total * 100))}%)` : ''}`; });
      }
      if (name === 'list_available_tools') {
        const definitions = Array.isArray(taskContext?.tools) ? taskContext.tools : (Array.isArray(activeToolDefinitions) ? activeToolDefinitions : []);
        const allTools = definitions.map(item => {
          const definition = item?.function || {};
          const name = String(definition.name || '');
          const category = name.startsWith('mcp_') ? 'MCP'
            : name.startsWith('system_manager_') ? 'System Manager'
              : name.startsWith('git_') ? 'Git'
                : name.startsWith('project_') ? 'Project'
                  : ['list_agent_skills','load_agent_skill'].includes(name) ? 'Skills'
                    : ['powershell_execute','run_native_command','select_native_workspace','download_file'].includes(name) ? 'Native'
                      : ['web_search','fetch_webpage','research_topic','extract_links','extract_metadata','extract_tables','read_sitemap','read_rss_feed','page_to_markdown','check_links','compare_webpages','crawl_website'].includes(name) ? 'Web'
                        : ['list_files','read_file','create_file','edit_file','delete_file','copy_file','move_file','rename_file','search_files','file_info','checksum_file','compare_files'].includes(name) ? 'Workspace'
                          : name.startsWith('image_') || name.startsWith('create_chart') || name.startsWith('create_diagram') ? 'Artifacts' : 'Agent';
          return { name, category, availability: 'exposed-now', description: String(definition.description || '').slice(0, 1200), required: Array.isArray(definition.parameters?.required) ? definition.parameters.required : [], parameters: Object.keys(definition.parameters?.properties || {}) };
        }).filter(item => item.name).sort((a,b) => a.category.localeCompare(b.category) || a.name.localeCompare(b.name));
        const offset = Math.max(0, Math.min(10000, Math.floor(Number(args.offset) || 0)));
        const limit = Math.max(1, Math.min(200, Math.floor(Number(args.limit) || 100)));
        const tools = allTools.slice(offset, offset + limit);
        const nextOffset = offset + tools.length;
        return { scope: 'current-agent-session', host: window.chrome?.webview ? 'native-desktop-host' : 'browser-preview', total: allTools.length, offset, limit, count: tools.length, hasMore: nextOffset < allTools.length, nextOffset: nextOffset < allTools.length ? nextOffset : null, tools, note: 'This is the exact tool-definition set currently exposed to this Agent session. Use offset=nextOffset to retrieve another page when hasMore is true. Tools omitted from the complete inventory are not currently available to the model. MCP entries reflect currently connected/enabled servers.' };
      }
      if (name === 'list_agent_skills') {
        const skills = await discoverAgentSkills(signal);
        return { skills: skills.map(skill => ({ ...skill, loaded: (workspaceContext.enabledSkills || []).includes(skill.name) })), count: skills.length, sources: [{ scope: 'builtin', supported: !!window.chrome?.webview, authorized: !!window.chrome?.webview, location: 'application-bundled Skills' }, { scope: 'global', supported: !!window.chrome?.webview, authorized: !!window.chrome?.webview, location: '%LOCALAPPDATA%\\RunMind\\Skills' }, { scope: 'project', supported: true, authorized: !!agentDirectoryHandle, location: '.agents/skills' }], note: 'Lists bundled built-in skills, installed global skills, and authorized project skills. Metadata discovery does not load instructions. Load only relevant skills explicitly; scripts are never executed automatically.' };
      }
      if (name === 'load_agent_skill') return await loadAgentSkill(String(args.name || ''), String(args.scope || ''), Array.isArray(args.references) ? args.references : [], signal);
      if (readOnlyTools.has(name) && !(name === 'javascript_refactor' && args.apply === true) && !loadAgentSettings().autoApproveReads && !taskApprovedReadTools.has(name)) {
        const detail = name === 'web_search' ? String(args.query || '') : name === 'fetch_webpage' ? String(args.url || '') : name === 'read_file' || name === 'list_files' ? String(args.path || '(selected folder root)') : JSON.stringify(args);
        const approved = await requireAgentApproval({ title: 'Allow read-only access?', subtitle: `Agent tool · ${name}`, detail, allowForTask: true, toolName: name }, signal);
        if (!approved) return { cancelled: true, message: 'User declined this tool call.' };
      }
      switch (name) {
        case 'request_folder_access': {
          const access = await requestFolderAccess(signal);
          if (access.granted) return { granted: true, folder: access.folder, message: 'Folder access granted for this WebView chat session. File tools are now available.' };
          folderAccessDeclinedForRun = true;
          rebuildActiveToolDefinitions(deepResearchEnabled);
          return { granted: false, cancelled: !!access.cancelled, message: access.message || access.error || 'Folder access was not granted. Do not ask for it again in this response.' };
        }
        case 'update_plan': {
          const title = String(args.title || 'Task plan').slice(0, 160);
          const steps = (Array.isArray(args.steps) ? args.steps : []).slice(0, 12).map(item => ({ step: String(item?.step || '').slice(0, 240), status: ['pending','in_progress','completed'].includes(item?.status) ? item.status : 'pending' })).filter(item => item.step);
          if (!steps.length) throw new Error('A plan must contain at least one step.');
          const marker = '\n\n### Task plan\n';
          const existingAt = assistantMessage?.content?.indexOf(marker) ?? -1;
          const base = existingAt >= 0 ? assistantMessage.content.slice(0, existingAt) : (assistantMessage?.content || '');
          const rendered = `${marker}**${title}**\n\n${steps.map(item => `${item.status === 'completed' ? '[x]' : item.status === 'in_progress' ? '[-]' : '[ ]'} ${item.step}`).join('\n')}`;
          if (assistantMessage) { assistantMessage.content = base + rendered; scheduleActiveMessageRender(); }
          return { updated: true, title, steps };
        }
        case 'current_datetime': return { local: new Date().toLocaleString(), iso: new Date().toISOString(), timezone: Intl.DateTimeFormat().resolvedOptions().timeZone };
        case 'delegate_subagent': return await executeSubAgent(args, signal, assistantMessage, taskContext);
        case 'calculate': {
          const expression = String(args.expression || '').trim();
          if (!expression || expression.length > 160 || !/^[\d\s()+\-*/%.^]+$/.test(expression)) throw new Error('Only basic arithmetic characters are supported.');
          const normalized = expression.replace(/\^/g, '**');
          const value = evaluateSafeArithmetic(normalized);
          return { expression, result: value };
        }
        case 'regex_test': return await regexTestInWorker(args, signal);
        case 'diagnose_connection': return await diagnoseModelConnection(signal);
        case 'research_topic': {
          const queries = [...new Set((Array.isArray(args.queries) ? args.queries : []).map(value => String(value || '').trim()).filter(Boolean))].slice(0, 6);
          if (!queries.length) throw new Error('Provide at least one research query.');
          const maxResults = Math.max(1, Math.min(8, Number(args.max_results_per_query) || 5));
          const seen = new Set(); const results = [];
          let searchesExecuted = 0;
          for (const query of queries) {
            if (signal?.aborted) throw new DOMException('Research search cancelled.', 'AbortError');
            if (researchDeadlineReached()) break;
            setResearchLiveOperation(`Searching query ${searchesExecuted + 1}/${queries.length}…`, 'research_topic', query);
            if (isResearchTaskActive() && searchesExecuted > 0) {
              if (deepResearchTask.toolCalls >= deepResearchTask.callBudget) break;
              deepResearchTask.toolCalls += 1;
              await persistResearchTask();
            }
            const items = await searchWebQuery(query, signal, maxResults);
            searchesExecuted += 1;
            for (const item of items) {
              if (!isResearchDomainAllowed(item.url)) continue;
              const normalized = normalizeResearchUrl(item.url);
              if (seen.has(normalized)) continue;
              seen.add(normalized); results.push({ ...item, url: normalized, query });
            }
          }
          const preferred = isResearchTaskActive() ? researchDomainRules(deepResearchTask?.preferredDomains) : [];
          results.sort((a,b) => {
            const score = item => { let host = ''; try { host = new URL(item.url).hostname.toLowerCase(); } catch (_) {} return preferred.some(rule => researchDomainMatches(host, rule)) ? 1 : 0; };
            return score(b) - score(a);
          });
          return { queryCount: searchesExecuted, requestedQueryCount: queries.length, resultCount: results.length, queries: queries.slice(0, searchesExecuted), results: results.slice(0, 48), preferredDomainsApplied: preferred, note: 'Search snippets are discovery leads. Fetch each source before using it as evidence.' };
        }
        case 'page_to_markdown': {
          if (!isResearchDomainAllowed(args.url)) throw new Error('This URL is blocked by the current Deep Research domain policy.');
          setResearchLiveOperation('Fetching and converting webpage to Markdown…', 'page_to_markdown', args.url);
          const page = await fetchReadableWebPage(args.url, signal);
          return { url: page.url, title: page.title, author: page.author || '', contentType: page.contentType, retrieval: page.retrieval, ...(page.directFetchFailure ? { direct_fetch_failure: page.directFetchFailure } : {}), publishedAt: page.publishedAt || '', modifiedAt: page.modifiedAt || '', retrievedAt: new Date().toISOString(), markdown: String(page.content || '').slice(0, 90000), contentCharacters: String(page.content || '').length };
        }
        case 'check_links': {
          setResearchLiveOperation('Reading page and discovering links…', 'check_links', args.url);
          if (isResearchTaskActive()) consumeResearchPages(1);
          const page = await fetchWebDocument(args.url, signal);
          const remainingResearchPages = isResearchTaskActive() ? Math.max(0, deepResearchTask.pageBudget - deepResearchTask.pagesFetched) : 30;
          const requestedMaximum = Math.max(1, Math.min(30, Number(args.max_links) || 20));
          const candidates = [...new Set([...page.document.querySelectorAll('a[href]')].map(anchor => { try { const url = new URL(anchor.getAttribute('href'), page.url); return ['http:','https:'].includes(url.protocol) && !isPrivateOrLocalHost(url.hostname) && isResearchDomainAllowed(url.href) ? url.href : null; } catch (_) { return null; } }).filter(Boolean))].slice(0, requestedMaximum);
          const maximum = Math.min(candidates.length, remainingResearchPages);
          const links = candidates.slice(0, maximum);
          const notCheckedPageBudget = Math.max(0, candidates.length - links.length);
          const results = new Array(links.length); let cursor = 0;
          const worker = async () => { while (cursor < links.length) { const index = cursor++; const url = links[index];
            if (signal?.aborted) throw new DOMException('Link check cancelled.', 'AbortError');
            setResearchLiveOperation(`Checking link ${index + 1}/${links.length}…`, 'check_links', url);
            if (isResearchTaskActive() && researchDeadlineReached()) { results[index] = { url, state: 'not-checked-time-budget' }; continue; }
            if (isResearchTaskActive()) { try { consumeResearchPages(1); } catch (error) { results[index] = { url, state: 'not-checked-page-budget', reason: error?.message || 'Page budget reached' }; continue; } }
            const controller = new AbortController(); const abort = () => controller.abort(); signal?.addEventListener('abort', abort, { once: true }); const timer = setTimeout(() => controller.abort(), 6500);
            try { const response = await fetch(url, { method: 'HEAD', redirect: 'follow', signal: controller.signal }); results[index] = { url, status: response.status, finalUrl: response.url || url, state: response.ok ? 'ok' : 'http-error' }; }
            catch (error) { if (signal?.aborted) throw new DOMException('Link check cancelled.', 'AbortError'); results[index] = { url, state: 'unverified', reason: error?.message || 'CORS, network, or HEAD request unsupported' }; }
            finally { clearTimeout(timer); signal?.removeEventListener('abort', abort); }
          } };
          await Promise.all(Array.from({ length: Math.min(4, links.length) }, () => worker()));
          return { url: page.url, ...(page.directFetchFailure ? { direct_fetch_failure: page.directFetchFailure } : {}), discovered: candidates.length, checked: results.filter(Boolean).length, ok: results.filter(item => item?.state === 'ok').length, httpErrors: results.filter(item => item?.state === 'http-error').length, unverified: results.filter(item => item?.state === 'unverified').length, notChecked: results.filter(item => item?.state?.startsWith('not-checked')).length + notCheckedPageBudget, notCheckedPageBudget, results, note: 'Private/local destinations are skipped. Links beyond the remaining research page budget are reported as not checked. A CORS or HEAD restriction is unverified, not proof that a link is broken.' };
        }
        case 'compare_webpages': {
          setResearchLiveOperation('Fetching both pages for comparison…', 'compare_webpages', `${args.url_a} ↔ ${args.url_b}`);
          const firstUrl = new URL(args.url_a); const secondUrl = new URL(args.url_b);
          if (!['http:','https:'].includes(firstUrl.protocol) || !['http:','https:'].includes(secondUrl.protocol)) throw new Error('Only HTTP and HTTPS URLs are supported.');
          if (!isResearchDomainAllowed(firstUrl.href) || !isResearchDomainAllowed(secondUrl.href)) throw new Error('One or both URLs are blocked by the current Deep Research domain policy.');
          if (isResearchTaskActive()) consumeResearchPages(2);
          const [a,b] = await Promise.all([fetchReadableWebPage(firstUrl.href, signal), fetchReadableWebPage(secondUrl.href, signal)]);
          const linesA = [...new Set(String(a.content || '').split(/\r?\n/).map(line => line.trim()).filter(line => line.length > 25))];
          const linesB = [...new Set(String(b.content || '').split(/\r?\n/).map(line => line.trim()).filter(line => line.length > 25))];
          const setA = new Set(linesA); const setB = new Set(linesB); const max = Math.max(1, Math.min(40, Number(args.max_lines) || 12));
          return { a: { url: a.url, title: a.title, author: a.author || '', retrieval: a.retrieval, ...(a.directFetchFailure ? { direct_fetch_failure: a.directFetchFailure } : {}), publishedAt: a.publishedAt || '', modifiedAt: a.modifiedAt || '', retrievedAt: new Date().toISOString(), contentText: String(a.content || '').slice(0,30000) }, b: { url: b.url, title: b.title, author: b.author || '', retrieval: b.retrieval, ...(b.directFetchFailure ? { direct_fetch_failure: b.directFetchFailure } : {}), publishedAt: b.publishedAt || '', modifiedAt: b.modifiedAt || '', retrievedAt: new Date().toISOString(), contentText: String(b.content || '').slice(0,30000) }, commonLines: linesA.filter(line => setB.has(line)).slice(0,max), onlyInA: linesA.filter(line => !setA.has(line)).slice(0,max), onlyInB: linesB.filter(line => !setA.has(line)).slice(0,max), note: 'Exact normalized line comparison; paraphrases and semantic equivalence are not detected.' };
        }
        case 'crawl_website': {
          setResearchLiveOperation('Preparing bounded same-origin crawl…', 'crawl_website', args.url);
          const start = new URL(args.url); if (!['http:','https:'].includes(start.protocol) || isPrivateOrLocalHost(start.hostname)) throw new Error('Only public HTTP or HTTPS websites can be crawled.'); if (start.protocol === 'http:') start.protocol = 'https:';
          if (!isResearchDomainAllowed(start.href)) throw new Error('The starting URL is blocked by the current Deep Research domain policy.');
          const maxPages = Math.max(1, Math.min(20, Number(args.max_pages) || 8)); const requestedDepth = Number(args.max_depth); const maxDepth = Math.max(0, Math.min(3, Number.isFinite(requestedDepth) ? requestedDepth : 1));
          const queue = [{ url: start.href, depth: 0 }]; const visited = new Set(); const pages = []; const failures = [];
          while (queue.length && pages.length < maxPages) {
            if (signal?.aborted) throw new DOMException('Website crawl cancelled.', 'AbortError');
            const item = queue.shift(); const canonical = normalizeResearchUrl(item.url); if (visited.has(canonical)) continue; visited.add(canonical);
            if (isResearchTaskActive()) { if (researchDeadlineReached()) break; try { consumeResearchPages(1); } catch (_) { break; } }
            setResearchLiveOperation(`Crawling page ${pages.length + 1}/${maxPages} · depth ${item.depth}…`, 'crawl_website', item.url);
            try {
              const page = await fetchWebDocument(item.url, signal); const title = page.title || page.document.title || new URL(page.url).hostname; const body = (page.document.body?.innerText || page.text || '').replace(/\s+/g,' ').trim();
              const meta = (...names) => { for (const name of names) { const node = page.document.querySelector(`meta[property=\"${name}\" i]`) || page.document.querySelector(`meta[name=\"${name}\" i]`); if (node?.content) return node.content.trim(); } return ''; };
              pages.push({ url: page.url, title, author: page.author || meta('author','article:author'), depth: item.depth, retrieval: page.retrieval || 'direct fetch', ...(page.directFetchFailure ? { direct_fetch_failure: page.directFetchFailure } : {}), publishedAt: page.publishedAt || meta('article:published_time','datePublished','date'), modifiedAt: page.modifiedAt || meta('article:modified_time','dateModified','last-modified'), retrievedAt: new Date().toISOString(), excerpt: body.slice(0,1600), characters: body.length });
              if (item.depth < maxDepth) {
                const anchors = [...page.document.querySelectorAll('a[href]')];
                for (const anchor of anchors) { if (queue.length + visited.size >= maxPages * 8) break; try { const next = new URL(anchor.getAttribute('href'), page.url); if (next.origin !== start.origin || !isResearchDomainAllowed(next.href) || !['http:','https:'].includes(next.protocol)) continue; const normalized = normalizeResearchUrl(next.href); if (!visited.has(normalized)) queue.push({ url: normalized, depth: item.depth + 1 }); } catch (_) {} }
              }
            } catch (error) { if (error?.name === 'AbortError') throw error; failures.push({ url: item.url, error: String(error?.message || error).slice(0,300) }); }
          }
          return { startUrl: start.href, pagesAttempted: pages.length + failures.length, pagesVisited: pages.length, failedPages: failures.length, maxDepth, pages, failures, truncated: queue.length > 0 };
        }
        case 'extract_links': {
          const page = await fetchWebDocument(args.url, signal);
          const maximum = Math.max(1, Math.min(200, Number(args.max_links) || 50));
          const links = [...page.document.querySelectorAll('a[href]')].map(anchor => {
            try { const target = new URL(anchor.getAttribute('href'), page.url); if (!['http:','https:'].includes(target.protocol)) return null; return { title: (anchor.innerText || anchor.getAttribute('aria-label') || '').trim().slice(0,240), url: target.href, rel: anchor.rel || '' }; } catch (_) { return null; }
          }).filter(Boolean);
          return { url: page.url, ...(page.directFetchFailure ? { direct_fetch_failure: page.directFetchFailure } : {}), count: links.length, links: links.slice(0, maximum) };
        }
        case 'extract_metadata': {
          const page = await fetchWebDocument(args.url, signal);
          const meta = name => page.document.querySelector(`meta[name="${name}" i]`)?.content || page.document.querySelector(`meta[property="${name}" i]`)?.content || '';
          return { url: page.url, ...(page.directFetchFailure ? { direct_fetch_failure: page.directFetchFailure } : {}), title: page.title || page.document.title || '', description: meta('description'), author: meta('author') || page.author || '', published: meta('article:published_time') || meta('datePublished') || meta('date') || page.publishedAt || '', modified: meta('article:modified_time') || meta('dateModified') || meta('last-modified') || page.modifiedAt || '', canonical: page.document.querySelector('link[rel="canonical"]')?.href || '', openGraph: { title: meta('og:title'), description: meta('og:description'), type: meta('og:type'), image: meta('og:image'), siteName: meta('og:site_name') }, language: page.document.documentElement.lang || '', contentType: page.type };
        }
        case 'extract_tables': {
          const page = await fetchWebDocument(args.url, signal);
          const maximumTables = Math.max(1, Math.min(20, Number(args.max_tables) || 5));
          const maximumRows = Math.max(1, Math.min(200, Number(args.max_rows) || 40));
          const tables = [...page.document.querySelectorAll('table')].slice(0, maximumTables).map((table, index) => ({ index, caption: table.querySelector('caption')?.innerText?.trim() || '', rows: [...table.querySelectorAll('tr')].slice(0, maximumRows).map(row => [...row.querySelectorAll('th,td')].slice(0,30).map(cell => (cell.innerText || '').trim().slice(0,1000))) }));
          return { url: page.url, ...(page.directFetchFailure ? { direct_fetch_failure: page.directFetchFailure } : {}), tableCount: page.document.querySelectorAll('table').length, tables };
        }
        case 'read_sitemap': {
          let rootUrl;
          try { rootUrl = new URL(String(args.url || '').trim()); } catch (_) { throw new Error('Provide a valid absolute sitemap URL.'); }
          if (!['http:', 'https:'].includes(rootUrl.protocol)) throw new Error('Sitemap URLs must use HTTP or HTTPS.');
          rootUrl.hash = '';
          const host = rootUrl.hostname.toLowerCase();
          const maximumUrls = Math.max(1, Math.min(2000, Math.floor(Number(args.max_urls) || 250)));
          const maximumSitemaps = Math.max(1, Math.min(20, Math.floor(Number(args.max_sitemaps) || 10)));
          const maximumScanned = Math.max(maximumUrls, Math.min(50000, maximumUrls * 25));
          const query = String(args.query || '').trim().toLowerCase();
          const queue = [rootUrl.href]; const queued = new Set(queue); const visitedSitemaps = new Set(); const seenUrls = new Set();
          const urls = []; const errors = []; let scannedUrls = 0, crossHostSitemapsSkipped = 0, urlsetCount = 0, indexCount = 0;
          let directFetchFailure = '';
          while (queue.length && visitedSitemaps.size < maximumSitemaps && scannedUrls < maximumScanned && urls.length < maximumUrls) {
            if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
            const sitemapUrl = queue.shift();
            if (visitedSitemaps.has(sitemapUrl)) continue;
            visitedSitemaps.add(sitemapUrl);
            try {
              const page = await fetchWebDocument(sitemapUrl, signal, 'application/xml,text/xml,*/*');
              directFetchFailure ||= page.directFetchFailure || '';
              const finalHostname = (() => { try { return new URL(page.url || sitemapUrl).hostname.toLowerCase(); } catch (_) { return ''; } })();
              if (finalHostname !== host) throw new Error(`Sitemap redirected outside the starting hostname (${finalHostname || 'unknown'}).`);
              const document = page.document;
              const rootName = String(document.documentElement?.localName || '').toLowerCase();
              if (rootName === 'sitemapindex') {
                indexCount++;
                const children = xmlElementsByName(document, 'sitemap').filter(node => String(node.parentNode?.localName || '').toLowerCase() === 'sitemapindex');
                for (const node of children) {
                  const location = xmlChildText(node, ['loc']);
                  if (!location) continue;
                  let child;
                  try { child = new URL(location, page.url || sitemapUrl); } catch (_) { continue; }
                  if (!['http:', 'https:'].includes(child.protocol)) continue;
                  if (child.hostname.toLowerCase() !== host) { crossHostSitemapsSkipped++; continue; }
                  child.hash = '';
                  if (!queued.has(child.href) && !visitedSitemaps.has(child.href) && queue.length + visitedSitemaps.size < maximumSitemaps) { queued.add(child.href); queue.push(child.href); }
                }
              } else if (rootName === 'urlset') {
                urlsetCount++;
                for (const node of xmlElementsByName(document, 'url').filter(item => String(item.parentNode?.localName || '').toLowerCase() === 'urlset')) {
                  if (scannedUrls >= maximumScanned || urls.length >= maximumUrls) break;
                  scannedUrls++;
                  const item = { url: xmlChildText(node, ['loc']), lastModified: xmlChildText(node, ['lastmod']), changeFrequency: xmlChildText(node, ['changefreq']), priority: xmlChildText(node, ['priority']) };
                  let target;
                  try { target = new URL(item.url, page.url || sitemapUrl); } catch (_) { continue; }
                  if (!['http:', 'https:'].includes(target.protocol) || target.hostname.toLowerCase() !== host) continue;
                  target.hash = ''; item.url = target.href;
                  if (seenUrls.has(item.url)) continue;
                  seenUrls.add(item.url);
                  if (!query || `${item.url} ${item.lastModified} ${item.changeFrequency} ${item.priority}`.toLowerCase().includes(query)) urls.push(item);
                }
              } else {
                throw new Error(`Unsupported sitemap root element '${rootName || '(missing)'}'.`);
              }
            } catch (error) {
              if (error?.name === 'AbortError') throw error;
              if (visitedSitemaps.size === 1) throw error;
              errors.push({ sitemap: sitemapUrl, error: String(error?.message || error).slice(0, 500) });
            }
          }
          return { url: rootUrl.href, type: indexCount ? 'sitemap-index' : 'urlset', hostname: host, query: query || null, sitemapsRead: visitedSitemaps.size, sitemapsQueued: queued.size, urlsetsRead: urlsetCount, scannedUrls, matchingUrls: urls.length, urls, crossHostSitemapsSkipped, errors, truncated: queue.length > 0 || scannedUrls >= maximumScanned || urls.length >= maximumUrls, ...(directFetchFailure ? { direct_fetch_failure: directFetchFailure } : {}), limits: { maxSitemaps: maximumSitemaps, maxMatchingUrls: maximumUrls, maxScannedUrls: maximumScanned } };
        }
        case 'read_rss_feed': {
          const page = await fetchWebDocument(args.url, signal, 'application/rss+xml,application/atom+xml,application/xml,text/xml,*/*');
          const maximum = Math.max(1, Math.min(100, Number(args.max_items) || 30));
          const rssItems = xmlElementsByName(page.document, 'item').slice(0, maximum).map(node => ({ title: xmlChildText(node, ['title']), link: xmlChildText(node, ['link']), published: xmlChildText(node, ['pubDate']), summary: xmlChildText(node, ['description']).slice(0,2000) }));
          const atomItems = xmlElementsByName(page.document, 'entry').slice(0, maximum).map(node => {
            const link = xmlElementsByName(node, 'link').find(item => (item.getAttribute('rel') || 'alternate') === 'alternate') || xmlElementsByName(node, 'link')[0];
            return { title: xmlChildText(node, ['title']), link: link?.getAttribute('href') || '', published: xmlChildText(node, ['published','updated']), summary: xmlChildText(node, ['summary','content']).slice(0,2000) };
          });
          const items = rssItems.length ? rssItems : atomItems;
          return { url: page.url, ...(page.directFetchFailure ? { direct_fetch_failure: page.directFetchFailure } : {}), feedTitle: xmlChildText(page.document, ['title']), count: items.length, items };
        }
        case 'fetch_webpage': {
          const page = await fetchReadableWebPage(args.url, signal);
          return {
            url: page.url,
            title: page.title,
            content_type: page.contentType,
            retrieval: page.retrieval,
            ...(page.directFetchFailure ? { direct_fetch_failure: page.directFetchFailure } : {}),
            publishedAt: page.publishedAt || '',
            modifiedAt: page.modifiedAt || '',
            author: page.author || '',
            retrievedAt: new Date().toISOString(),
            content: String(page.content || '').slice(0, 90000),
            content_characters: String(page.content || '').length,
            ...(page.description ? { description: page.description } : {})
          };
        }
        case 'web_search': {
          const results = await searchWebQuery(args.query, signal, 8);
          return results.filter(item => !deepResearchEnabled || isResearchDomainAllowed(item.url));
        }
        case 'list_files': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected. Ask the user to choose a folder first.');
          const dir = await getDirectoryAtPath(agentDirectoryHandle, args.path || ''); const items = [];
          let directoryEntriesScanned = 0, truncated = false;
          for await (const [name, handle] of dir.entries()) {
            if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
            if (directoryEntriesScanned >= 10000) { truncated = true; break; }
            directoryEntriesScanned++;
            if (items.length < 300) items.push({ name, type: handle.kind });
            else truncated = true;
          }
          return { items, directoryEntriesScanned, truncated, note: truncated ? 'Listing is capped at 300 items and scanning stops at 10,000 directory entries.' : undefined };
        }
        case 'read_file': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          const parts = safeRelativePath(args.path); const fileName = parts.pop();
          const dir = await getDirectoryAtPath(agentDirectoryHandle, parts.join('/'));
          const handle = await dir.getFileHandle(fileName); const file = await handle.getFile();
          if (file.size > 2 * 1024 * 1024) throw new Error('Files larger than 2 MB are not read by this tool.');
          return { path: args.path, content: await file.text(), size: file.size };
        }
        case 'create_file': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          const content = String(args.content || '');
          const approved = await requireAgentApproval({ title: 'Create a file?', subtitle: 'File write · existing files will not be overwritten', detail: `${args.path}\n\n${content.slice(0, 3000)}`, risky: true }, signal);
          if (!approved) return { cancelled: true, message: 'User declined file creation.' };
          const written = await writeWorkspaceFile(args.path, new Blob([content]), false);
          return { created: written.path, bytes: written.bytes };
        }
        case 'search_files': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          return await walkWorkspace(agentDirectoryHandle, { query: args.query || '', searchContent: !!args.search_content, maxDepth: args.max_depth ?? 3, maxResults: args.max_results ?? 200 }, signal);
        }
        case 'file_info': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          const { handle } = await getFileAtPath(agentDirectoryHandle, args.path);
          const file = await handle.getFile();
          return { path: args.path, name: file.name, size: file.size, type: file.type || 'unknown', lastModified: new Date(file.lastModified).toISOString() };
        }
        case 'edit_file': {
          return await withWorkspaceFileMutationLock(args.path, async () => {
            const content = String(args.content ?? ''); if (content.length > 2 * 1024 * 1024) throw new Error('Edited text is limited to 2 MB.');
            const prior = await readWorkspaceText(args.path, 2 * 1024 * 1024);
            if (prior.source !== 'workspace' || !prior.handle) throw new Error('Only files in the selected workspace can be edited; conversation attachments are read-only.');
            const approved = await requireAgentApproval({ title: 'Replace file contents?', subtitle: 'File edit · an automatic backup will be created first', detail: `${args.path}\n\nOld size: ${prior.file.size} bytes\nNew size: ${new Blob([content]).size} bytes\n\n${content.slice(0, 2200)}`, risky: true }, signal);
            if (!approved) return { cancelled: true };
            const currentFile = await prior.handle.getFile();
            if (!(await workspaceFilesMatchSnapshot(prior.file, currentFile))) throw new Error(`'${args.path}' changed while approval was pending. Nothing was modified; review the current file and retry.`);
            const backup = await createWorkspaceBackup(args.path, currentFile, 'edit');
            const immediatelyBeforeWrite = await prior.handle.getFile();
            if (!(await workspaceFilesMatchSnapshot(currentFile, immediatelyBeforeWrite))) throw new Error(`'${args.path}' changed while its backup was being created. The original was not overwritten; the recovery backup was preserved.`);
            const writable = await prior.handle.createWritable({ keepExistingData: false });
            try { await writable.write(content); await writable.close(); } catch (error) { try { await writable.abort(); } catch (_) {} throw error; }
            return { edited: args.path, previousBytes: currentFile.size, bytes: new Blob([content]).size, backup };
          });
        }
        case 'edit_files': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          const changes = Array.isArray(args.files) ? args.files : [];
          if (changes.length < 2 || changes.length > 20) throw new Error('A multi-file edit must contain between 2 and 20 files.');
          const paths = changes.map(item => String(item?.path || ''));
          const normalized = paths.map(path => safeRelativePath(path).join('/').toLowerCase());
          if (new Set(normalized).size !== normalized.length) throw new Error('The multi-file edit contains duplicate paths.');
          const totalBytes = changes.reduce((total, item) => total + new Blob([String(item?.content ?? '')]).size, 0);
          if (totalBytes > 8 * 1024 * 1024) throw new Error('The combined multi-file edit is limited to 8 MB.');
          return await withWorkspaceFileMutationLocks(paths, async () => {
            const snapshots = [];
            for (const item of changes) {
              const path = String(item.path);
              const content = String(item.content ?? '');
              if (new Blob([content]).size > 2 * 1024 * 1024) throw new Error(`'${path}' exceeds the 2 MB per-file edit limit.`);
              const prior = await readWorkspaceText(path, 2 * 1024 * 1024);
              if (prior.source !== 'workspace' || !prior.handle) throw new Error(`'${path}' is not an editable file in the selected workspace.`);
              snapshots.push({ path, content, prior, backup: null });
            }
            const summary = snapshots.map(item => `${item.path}: ${item.prior.file.size.toLocaleString()} → ${new Blob([item.content]).size.toLocaleString()} bytes`).join('\n');
            const approved = await requireAgentApproval({ title: `Apply ${snapshots.length} file changes?`, subtitle: 'Multi-file edit · all originals are backed up before writing', detail: `${summary}\n\nCombined new content: ${totalBytes.toLocaleString()} bytes`, risky: true }, signal);
            if (!approved) return { cancelled: true, changed: [], backups: [] };

            for (const item of snapshots) {
              const current = await item.prior.handle.getFile();
              if (!(await workspaceFilesMatchSnapshot(item.prior.file, current))) throw new Error(`'${item.path}' changed while approval was pending. No files were modified.`);
            }
            for (const item of snapshots) item.backup = await createWorkspaceBackup(item.path, item.prior.file, 'multi-edit');
            for (const item of snapshots) {
              const current = await item.prior.handle.getFile();
              if (!(await workspaceFilesMatchSnapshot(item.prior.file, current))) throw new Error(`'${item.path}' changed while recovery backups were being created. No files were modified; backups were preserved.`);
            }

            const changed = [];
            try {
              for (const item of snapshots) {
                const writable = await item.prior.handle.createWritable({ keepExistingData: false });
                changed.push(item.path);
                try { await writable.write(item.content); await writable.close(); }
                catch (error) { try { await writable.abort(); } catch (_) {} throw error; }
              }
            } catch (error) {
              const rollback = [];
              for (const item of snapshots.filter(entry => changed.includes(entry.path)).reverse()) {
                try {
                  const writable = await item.prior.handle.createWritable({ keepExistingData: false });
                  try { await writable.write(item.prior.file); await writable.close(); }
                  catch (rollbackError) { try { await writable.abort(); } catch (_) {} throw rollbackError; }
                } catch (rollbackError) { rollback.push({ path: item.path, error: String(rollbackError?.message || rollbackError) }); }
              }
              throw new Error(`Multi-file write failed: ${error?.message || error}. ${rollback.length ? `Rollback was incomplete for: ${rollback.map(item => item.path).join(', ')}.` : 'Previously written files were restored from their in-memory snapshots.'} Recovery backups were preserved.`);
            }
            return { edited: changed, count: changed.length, bytes: totalBytes, backups: snapshots.map(item => ({ path: item.path, ...item.backup })), rollback: 'not-needed' };
          });
        }
        case 'restore_file_backup': {
          const destination = String(args.path || '');
          return await withWorkspaceFileMutationLock(destination, async () => {
          const backupPath = String(args.backup_path || '').replace(/\\/g, '/');
          if (!backupPath.startsWith('.runmind-backups/')) throw new Error('Only files inside .runmind-backups can be restored.');
          if (!destination || destination.startsWith('.runmind-backups/')) throw new Error('Choose a destination outside the backup folder.');
          const { handle: backupHandle } = await getFileAtPath(agentDirectoryHandle, backupPath);
          const backupFile = await backupHandle.getFile();
          if (backupFile.size > 100 * 1024 * 1024) throw new Error('Backup files larger than 100 MB cannot be restored.');
          const parts = safeRelativePath(destination); const name = parts.pop();
          const parent = await getDirectoryAtPath(agentDirectoryHandle, parts.join('/'));
          let existingHandle = null, existingFile = null;
          try { existingHandle = await parent.getFileHandle(name); existingFile = await existingHandle.getFile(); }
          catch (error) { if (error?.name !== 'NotFoundError') throw error; }
          const approved = await requireAgentApproval({ title: 'Restore file backup?', subtitle: existingFile ? 'The current destination will also be backed up before replacement' : 'Restore backup to a new file', detail: `${backupPath}\n→ ${destination}\n${backupFile.size.toLocaleString()} bytes`, risky: true }, signal);
          if (!approved) return { cancelled: true };
          let currentDestination = null;
          try {
            const currentHandle = await parent.getFileHandle(name);
            currentDestination = await currentHandle.getFile();
            if (!existingFile || !(await workspaceFilesMatchSnapshot(existingFile, currentDestination))) throw new Error(`'${destination}' changed while approval was pending. Nothing was restored; review the current destination and retry.`);
          } catch (error) {
            if (error?.name !== 'NotFoundError') throw error;
            if (existingFile) throw new Error(`'${destination}' was removed while approval was pending. Nothing was restored; retry the operation.`);
          }
          const recoveryPoint = currentDestination ? await createWorkspaceBackup(destination, currentDestination, 'before-restore') : null;
          let immediatelyBeforeRestore = null;
          try { immediatelyBeforeRestore = await (await parent.getFileHandle(name, { create: false })).getFile(); }
          catch (error) { if (error?.name !== 'NotFoundError') throw error; }
          if (currentDestination ? !immediatelyBeforeRestore || !(await workspaceFilesMatchSnapshot(currentDestination, immediatelyBeforeRestore)) : !!immediatelyBeforeRestore) {
            throw new Error(`'${destination}' changed while the recovery backup was being created. The restore was cancelled and the current destination was preserved.`);
          }
          const written = await writeWorkspaceFileCore(destination, backupFile, !!currentDestination);
          return { restored: destination, from: backupPath, bytes: written.bytes, recoveryPoint };
          });
        }
        case 'delete_file': {
          return await withWorkspaceFileMutationLock(args.path, async () => {
            const { dir, name, handle } = await getFileAtPath(agentDirectoryHandle, args.path);
            const file = await handle.getFile();
            const approved = await requireAgentApproval({ title: 'Delete this file?', subtitle: 'An automatic backup will be created first', detail: `${args.path}\n${file.size.toLocaleString()} bytes`, risky: true }, signal);
            if (!approved) return { cancelled: true };
            const currentFile = await handle.getFile();
            if (!(await workspaceFilesMatchSnapshot(file, currentFile))) throw new Error(`'${args.path}' changed while approval was pending. Nothing was deleted; review the current file and retry.`);
            const backup = await createWorkspaceBackup(args.path, currentFile, 'delete');
            const immediatelyBeforeRemove = await handle.getFile();
            if (!(await workspaceFilesMatchSnapshot(currentFile, immediatelyBeforeRemove))) throw new Error(`'${args.path}' changed while its backup was being created. The source was not deleted; the recovery backup was preserved.`);
            await dir.removeEntry(name); return { deleted: args.path, bytes: currentFile.size, backup };
          });
        }
        case 'create_folder': {
          return await withWorkspaceFileMutationLock(args.path, async () => {
            const parts = safeRelativePath(args.path); const name = parts.pop(); const parent = await getDirectoryAtPath(agentDirectoryHandle, parts.join('/'));
            const approved = await requireAgentApproval({ title: 'Create folder?', subtitle: 'Workspace change', detail: args.path, risky: true }, signal);
            if (!approved) return { cancelled: true };
            try { await parent.getDirectoryHandle(name); throw new Error(`Folder '${args.path}' already exists.`); } catch (error) { if (error?.name !== 'NotFoundError') throw error; }
            await parent.getDirectoryHandle(name, { create: true }); return { createdFolder: args.path };
          });
        }
        case 'copy_file': {
          const source = await getFileAtPath(agentDirectoryHandle, args.source); const file = await source.handle.getFile();
          if (file.size > 100 * 1024 * 1024) throw new Error('Copy/move is limited to 100 MB per file.');
          const approved = await requireAgentApproval({ title: 'Copy this file?', subtitle: 'Workspace file operation', detail: `${args.source}\n→ ${args.destination}\n${file.size.toLocaleString()} bytes`, risky: true }, signal);
          if (!approved) return { cancelled: true };
          const written = await writeWorkspaceFile(args.destination, file, false);
          return { copied: args.source, to: written.path, bytes: written.bytes };
        }
        case 'move_file': {
          if (safeRelativePath(args.source).join('/').toLowerCase() === safeRelativePath(args.destination).join('/').toLowerCase()) throw new Error('Source and destination must be different paths.');
          return await withWorkspaceFileMutationLocks([args.source, args.destination], async () => {
            const source = await getFileAtPath(agentDirectoryHandle, args.source); const file = await source.handle.getFile();
            if (file.size > 100 * 1024 * 1024) throw new Error('Copy/move is limited to 100 MB per file.');
            const approved = await requireAgentApproval({ title: 'Move this file?', subtitle: 'Workspace file operation', detail: `${args.source}\n→ ${args.destination}\n${file.size.toLocaleString()} bytes`, risky: true }, signal);
            if (!approved) return { cancelled: true };
            const afterApproval = await source.handle.getFile();
            if (!(await workspaceFilesMatchSnapshot(file, afterApproval))) throw new Error(`'${args.source}' changed while approval was pending. The move was cancelled; neither file was changed.`);
            const written = await writeWorkspaceFileCore(args.destination, afterApproval, false);
            const immediatelyBeforeRemove = await source.handle.getFile();
            if (!(await workspaceFilesMatchSnapshot(afterApproval, immediatelyBeforeRemove))) throw new Error(`'${args.source}' changed while the move was being completed. The destination copy was preserved and the source was not deleted.`);
            await source.dir.removeEntry(source.name);
            return { moved: args.source, to: written.path, bytes: written.bytes };
          });
        }
        case 'rename_file': {
          const newName = String(args.new_name || '').trim();
          if (!newName || /[\\/:]/.test(newName) || newName === '.' || newName === '..') throw new Error('Provide a simple filename without path separators.');
          const destPath = args.path.split('/').slice(0,-1).concat(newName).join('/');
          if (safeRelativePath(args.path).join('/').toLowerCase() === safeRelativePath(destPath).join('/').toLowerCase()) throw new Error('The new filename must be different from the current filename.');
          return await withWorkspaceFileMutationLocks([args.path, destPath], async () => {
            const source = await getFileAtPath(agentDirectoryHandle, args.path); const file = await source.handle.getFile();
            const approved = await requireAgentApproval({ title: 'Rename this file?', subtitle: 'Workspace file operation', detail: `${args.path}\n→ ${newName}`, risky: true }, signal);
            if (!approved) return { cancelled: true };
            const afterApproval = await source.handle.getFile();
            if (!(await workspaceFilesMatchSnapshot(file, afterApproval))) throw new Error(`'${args.path}' changed while approval was pending. The rename was cancelled; neither file was changed.`);
            const written = await writeWorkspaceFileCore(destPath, afterApproval, false);
            const immediatelyBeforeRemove = await source.handle.getFile();
            if (!(await workspaceFilesMatchSnapshot(afterApproval, immediatelyBeforeRemove))) throw new Error(`'${args.path}' changed while the rename was being completed. The destination copy was preserved and the source was not deleted.`);
            await source.dir.removeEntry(source.name);
            return { renamed: args.path, to: destPath, bytes: written.bytes };
          });
        }
        case 'checksum_file': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          const { handle } = await getFileAtPath(agentDirectoryHandle, args.path);
          const file = await handle.getFile();
          if (file.size > 100 * 1024 * 1024) throw new Error('SHA-256 is limited to 100 MB per file.');
          const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
          return { path: args.path, algorithm: 'SHA-256', checksum: [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2,'0')).join('') };
        }
        case 'compare_files': {
          const a = await readWorkspaceText(args.path_a), b = await readWorkspaceText(args.path_b);
          if (a.text === b.text) return { identical: true, lineCount: a.text.split(/\r?\n/).length };
          const aa = a.text.split(/\r?\n/), bb = b.text.split(/\r?\n/); let line = 0;
          while (line < Math.max(aa.length, bb.length) && aa[line] === bb[line]) line++;
          return { identical: false, firstDifferentLine: line + 1, left: aa[line] ?? null, right: bb[line] ?? null, leftLines: aa.length, rightLines: bb.length };
        }
        case 'project_overview': {
          const scan = await walkWorkspace(agentDirectoryHandle, { maxDepth: args.max_depth ?? 3, maxResults: args.max_results ?? 300 }, signal);
          const items = scan.results;
          return { root: agentDirectoryHandle.name || 'workspace', itemCount: items.length, files: items.filter(item => item.type === 'file').length, folders: items.filter(item => item.type === 'directory').length, filesScanned: scan.filesScanned, directoryEntriesScanned: scan.directoryEntriesScanned, truncated: scan.truncated, stopReason: scan.stopReason || undefined, items };
        }
        case 'project_index': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          return await buildProjectIndex({ force: !!args.force, signal });
        }
        case 'project_search': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          return await searchProjectIndex(args.query, { kind: args.kind || 'all', limit: args.limit || 30 });
        }
        case 'project_index_overview': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          return await getProjectIndexOverview();
        }
        case 'project_symbol_graph': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          return await getProjectSymbolGraph({ path: args.path || '', name: args.name || '', limit: args.limit || 200 });
        }
        case 'javascript_refactor': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          const path = String(args.path || '');
          const action = String(args.action || 'rename-symbol');
          if (action === 'rename-public-api') {
            if (!/\.(?:js|mjs|cjs|jsx|ts|tsx|mts|cts)$/i.test(path)) throw new Error('Public API refactoring supports JavaScript, JSX, TypeScript and TSX source files.');
            const index = await loadProjectIndex();
            if (!index) throw new Error('Build or refresh the project index before renaming a public API.');
            const targetRecord = index.files.find(item => item.path === path);
            if (!targetRecord) throw new Error(`'${path}' is not present in the current project index.`);
            const current = await readWorkspaceText(path, 2 * 1024 * 1024);
            if (current.source !== 'workspace' || !current.handle) throw new Error('Refactoring is restricted to existing workspace files.');
            const oldName = String(args.old_name || ''), newName = String(args.new_name || '');
            const bindings = (index.importGraph?.resolvedBindings || []).filter(item => item.to === path && item.imported === oldName);
            const byImporter = new Map();
            for (const binding of bindings) {
              const record = index.files.find(item => item.path === binding.from);
              const source = (record?.importBindings || []).find(item => item.local === binding.local && item.imported === oldName)?.source;
              if (!source) continue;
              const sources = byImporter.get(binding.from) || new Set(); sources.add(source); byImporter.set(binding.from, sources);
            }
            const consumers = [];
            for (const [consumerPath, sources] of byImporter) {
              const file = await readWorkspaceText(consumerPath, 2 * 1024 * 1024);
              if (file.source !== 'workspace' || !file.handle) throw new Error(`'${consumerPath}' is not an editable workspace file.`);
              consumers.push({ path: consumerPath, text: file.text, sources: [...sources] });
            }
            const result = projectIndexRefactorPublicApi(path, current.text, consumers, oldName, newName);
            if (args.apply !== true) return { ...result, applied: false, nextStep: 'Review every returned file preview. Apply only after explicit user approval.' };
            const files = result.changes.map(item => ({ path: item.path, content: item.content }));
            const applied = files.length === 1
              ? await executeAgentToolInternal('edit_file', files[0], signal, assistantMessage, taskContext)
              : await executeAgentToolInternal('edit_files', { files }, signal, assistantMessage, taskContext);
            return { ...result, applied: !applied?.cancelled, editResult: applied };
          }
          if (!/\.(?:js|mjs|cjs)$/i.test(path)) throw new Error('Single-file Acorn refactoring supports .js, .mjs and .cjs. Use rename-public-api for statically resolved exported symbols.');
          const current = await readWorkspaceText(path, 2 * 1024 * 1024);
          if (current.source !== 'workspace' || !current.handle) throw new Error('Refactoring is restricted to an existing file in the selected workspace.');
          const result = projectIndexRefactorJavaScript(current.text, { action, oldName: String(args.old_name || ''), newName: String(args.new_name || args.function_name || ''), declarationStart: args.declaration_start ?? args.selection_start, selectionEnd: args.selection_end });
          if (!result.changed) return { ...result, path };
          if (args.apply !== true) return { ...result, path, applied: false, nextStep: 'Review the returned content and diff. Call javascript_refactor again with apply=true only after the user approves the change.' };
          const applied = await executeAgentToolInternal('edit_file', { path, content: result.content }, signal, assistantMessage, taskContext);
          return { ...result, path, applied: !applied?.cancelled, editResult: applied };
        }
        case 'node_resolve_modules': {
          if (!agentDirectoryHandle) throw new Error('Select a project workspace first.');
          const path = String(args.path || '').replace(/\\/g, '/');
          const specifiers = Array.isArray(args.specifiers) ? args.specifiers.map(value => String(value)).filter(Boolean).slice(0, 100) : [];
          if (!path || path.startsWith('/') || path.split('/').some(part => !part || part === '.' || part === '..')) throw new Error('Provide a safe workspace-relative importer path.');
          if (!specifiers.length) throw new Error('Provide one or more module specifiers to resolve.');
          const script = `import { createRequire } from 'node:module'; import { pathToFileURL } from 'node:url'; import path from 'node:path'; import fs from 'node:fs'; const importer=path.resolve(process.cwd(),process.argv[1]); const root=process.cwd(); if(!importer.startsWith(root+path.sep)||!fs.statSync(importer).isFile()) throw new Error('Importer must be an existing file inside the selected workspace.'); const specs=JSON.parse(process.argv[2]); const requireFrom=createRequire(importer); const results=specs.map(specifier=>{let requireResult=null,requireError=null,importResult=null,importError=null;try{requireResult=requireFrom.resolve(specifier)}catch(error){requireError=String(error.message||error).slice(0,240)}try{importResult=import.meta.resolve(specifier,pathToFileURL(importer).href)}catch(error){importError=String(error.message||error).slice(0,240)}return {specifier,require:{resolved:requireResult,error:requireError},import:{resolved:importResult,error:importError}}}); console.log(JSON.stringify({node:process.version,importer,results}));`;
          const approved = await requireAgentApproval({ title: 'Resolve modules with the installed Node.js runtime?', subtitle: 'Native Node.js · read-only resolution', detail: `Importer: ${path}
Specifiers:
${specifiers.join('\n')}

RunMind will execute a fixed resolver script using Node's createRequire().resolve() and import.meta.resolve(). It will not import or execute the target modules.`, risky: true, alwaysAsk: true }, signal);
          if (!approved) return { cancelled: true, message: 'Node.js module resolution was declined.' };
          const result = await callAgentHost('process.run', { executable: 'node.exe', arguments: ['--input-type=module', '-e', script, path, JSON.stringify(specifiers)], working_directory: '', timeout_seconds: 30, contextId: workspaceContext.id }, signal, 45000);
          if (result?.exitCode !== 0) throw new Error(`Node.js resolver failed: ${result?.stderr || result?.stdout || 'unknown error'}`);
          if (result?.outputTruncated) throw new Error('Node.js resolver output was truncated; reduce the number of specifiers and retry.');
          try { return { ...JSON.parse(result.stdout), verifiedBy: 'Node.js runtime', executionNote: result.note || 'Executed as the current Windows user; approval is not a sandbox.', note: 'Resolution was performed by the installed Node.js runtime. Target modules were not imported or executed.' }; }
          catch (_) { return { ...result, verifiedBy: 'Node.js runtime', parseWarning: 'Node returned output that was not valid JSON.' }; }
        }
        case 'project_validate': {
          if (!agentDirectoryHandle) throw new Error('No task folder selected.');
          const index = await loadProjectIndex();
          if (!index) throw new Error('Build the project index before running project validation.');
          const maximum = Math.max(1, Math.min(50, Number(args.max_manifests) || 30));
          const checks = [], packageScripts = {}, frameworks = index.frameworks || [];
          for (const item of index.manifests.slice(0, maximum)) {
            if (signal?.aborted) throw new DOMException('Project validation cancelled.', 'AbortError');
            try {
              const source = await readWorkspaceText(item.path, 256 * 1024);
              const path = item.path.toLowerCase(), text = source.text;
              if (path.endsWith('.json')) {
                const data = JSON.parse(text);
                if (path === 'package.json' && data.scripts && typeof data.scripts === 'object') {
                  for (const key of Object.keys(data.scripts).slice(0, 30)) packageScripts[key] = String(data.scripts[key]).slice(0, 240);
                }
                checks.push({ path: item.path, status: 'valid', format: 'json' });
              } else if (path.endsWith('.xml') || path.endsWith('.csproj')) {
                const document = new DOMParser().parseFromString(text, 'application/xml');
                const parseError = document.getElementsByTagName('parsererror')[0];
                if (parseError) throw new Error(String(parseError.textContent || 'Malformed XML').replace(/\s+/g, ' ').slice(0, 300));
                const targetFramework = document.getElementsByTagName('TargetFramework')[0]?.textContent?.trim() || document.getElementsByTagName('TargetFrameworks')[0]?.textContent?.trim() || '';
                checks.push({ path: item.path, status: 'valid', format: 'xml', ...(targetFramework ? { targetFramework } : {}) });
              } else if (path.endsWith('.sln')) {
                const valid = /^Microsoft Visual Studio Solution File/m.test(text) && /Project\(/.test(text);
                checks.push({ path: item.path, status: valid ? 'valid' : 'warning', format: 'solution', message: valid ? 'Solution header and project entries found.' : 'Expected Visual Studio solution header or project entries were not found.' });
              } else if (path === 'go.mod') {
                const module = text.match(/^module\s+([^\s]+)/m)?.[1] || '';
                checks.push({ path: item.path, status: module ? 'valid' : 'warning', format: 'go.mod', ...(module ? { module } : { message: 'No module directive found.' }) });
              } else if (path === 'cargo.toml') {
                const hasPackage = /^\s*\[package\]/m.test(text), hasDependencies = /^\s*\[(?:dev-|build-)?dependencies\]/m.test(text);
                checks.push({ path: item.path, status: hasPackage ? 'valid' : 'warning', format: 'toml', hasPackageSection: hasPackage, hasDependenciesSection: hasDependencies });
              } else if (path === 'pyproject.toml') {
                const hasProject = /^\s*\[project\]/m.test(text), hasTool = /^\s*\[tool\./m.test(text);
                checks.push({ path: item.path, status: hasProject || hasTool ? 'valid' : 'warning', format: 'toml', hasProjectSection: hasProject, hasToolSection: hasTool });
              } else {
                checks.push({ path: item.path, status: 'inspected', format: path.endsWith('.txt') ? 'text' : 'manifest', note: 'No full parser is available for this manifest format.' });
              }
            } catch (error) { checks.push({ path: item.path, status: 'invalid', error: String(error?.message || error).slice(0, 400) }); }
          }
          const testFiles = index.files.filter(file => /(?:^|\/)(?:test|tests|spec|specs)(?:\/|$)|\.(?:test|spec)\.[^/]+$|(?:^|\/)test_[^/]+\.py$/i.test(file.path)).map(file => file.path).slice(0, 100);
          const toolchains = index.toolchains || { packageManager: null, packageManagerSource: 'not detected', lockfiles: [], ecosystems: [] };
          const manager = toolchains.packageManager;
          const scriptCommands = manager && ['npm','pnpm','yarn','bun'].includes(manager)
            ? Object.fromEntries(Object.keys(packageScripts).filter(key => ['build','test','lint','check','typecheck','audit'].includes(key)).map(key => [key, manager === 'npm' ? `npm run ${key}` : manager === 'yarn' ? `yarn ${key}` : `${manager} run ${key}`]))
            : {};
          const importGraph = index.importGraph || { localImportEdges: [], filesWithIncomingImports: [], note: 'No local import graph is available; refresh the project index.' };
          return { root: index.rootName, indexedAt: index.updatedAt, manifestsChecked: checks.length, valid: checks.filter(item => item.status === 'valid').length, invalid: checks.filter(item => item.status === 'invalid').length, warnings: checks.filter(item => item.status === 'warning').length, checks, frameworks, toolchains, packageScripts, scriptCommands, importGraph: { localImportEdges: importGraph.localImportEdges.length, mostImportedFiles: importGraph.filesWithIncomingImports, note: importGraph.note }, testFilesFound: testFiles.length, testFiles, executionPerformed: false, note: 'Static manifest checks, lockfile/toolchain detection, heuristic local import links, and test-file discovery only. Script commands are derived from the detected package manager and declared package.json scripts; they are suggestions, not executed. For actual build, lint, test, Playwright, or audit execution, use the native PowerShell tool after selecting a native workspace and obtaining fresh approval.' };
        }
        case 'list_developer_workflows': return { workflows: (await loadDeveloperWorkflows()).map(({id,name,description,builtIn})=>({id,name,description,builtIn})) };
        case 'start_developer_workflow': return await startDeveloperWorkflow(args.id, args.context || '');
        case 'validate_json': {
          const input = await readWorkspaceText(args.path); const value = JSON.parse(input.text);
          const type = Array.isArray(value) ? 'array' : value === null ? 'null' : typeof value;
          return { valid: true, path: args.path, source: input.source, topLevelType: type, keys: type === 'object' ? Object.keys(value).slice(0,100) : undefined, length: Array.isArray(value) || typeof value === 'string' ? value.length : undefined };
        }
        case 'validate_json_schema': {
          const dataSource = await readWorkspaceText(args.data_path, 2 * 1024 * 1024);
          let data; try { data = JSON.parse(dataSource.text); } catch (error) { throw new Error(`Data file '${args.data_path}' is not valid JSON: ${error.message}`); }
          if (!!args.schema_path === (args.schema !== undefined)) throw new Error('Provide exactly one of schema_path or schema.');
          let schema = args.schema;
          if (args.schema_path) { const schemaSource = await readWorkspaceText(args.schema_path, 1024 * 1024); try { schema = JSON.parse(schemaSource.text); } catch (error) { throw new Error(`Schema file '${args.schema_path}' is not valid JSON: ${error.message}`); } }
          if (typeof schema === 'string') { try { schema = JSON.parse(schema); } catch (error) { throw new Error(`Inline schema is not valid JSON: ${error.message}`); } }
          return { dataPath: args.data_path, dataSource: dataSource.source, schemaPath: args.schema_path || null, ...(await validateJsonSchemaData(data, schema, signal)) };
        }
        case 'read_pdf': return await readWorkspacePdf(args, signal);
        case 'inspect_archive': {
          const archive = await parseWorkspaceZip(args, signal);
          return { ...archive, entries: archive.entries.slice(0, 200).map(entry => ({ path: entry.path, directory: entry.directory, size: entry.size, compressedSize: entry.compressedSize, compression: entry.method === 0 ? 'stored' : entry.method === 8 ? 'deflate' : `unsupported-${entry.method}`, encrypted: entry.encrypted, crc32: entry.crc32 })), entriesTruncated: archive.entries.length > 200, note: 'ZIP64, multi-disk ZIPs, and symbolic links are not supported. Extraction verifies each entry CRC-32 before writing.' };
        }
        case 'extract_archive': {
          return await withWorkspaceFileMutationLock(args.destination, async () => {
          if (!agentDirectoryHandle) throw new Error('Select a task folder before extracting an attached ZIP; extraction writes into a new folder inside that workspace.');
          const archive = await parseWorkspaceZip(args, signal);
          if (!archive.entries.length) return { extracted: false, message: 'The archive contains no entries.' };
          for (const entry of archive.entries) { if (entry.encrypted) throw new Error(`Cannot extract encrypted entry '${entry.path}'.`); if (![0,8].includes(entry.method)) throw new Error(`Cannot extract '${entry.path}': compression method ${entry.method} is unsupported.`); }
          const destinationParts = safeRelativePath(args.destination); const destinationName = destinationParts.pop();
          const destinationParent = await getDirectoryAtPath(agentDirectoryHandle, destinationParts.join('/'));
          try { await destinationParent.getDirectoryHandle(destinationName); throw new Error(`Destination folder '${args.destination}' already exists; extraction never overwrites.`); } catch (error) { if (error?.name !== 'NotFoundError') throw error; }
          try { await destinationParent.getFileHandle(destinationName); throw new Error(`A file named '${args.destination}' already exists.`); } catch (error) { if (error?.name !== 'NotFoundError') throw error; }
          const approval = await requireAgentApproval({ title: 'Extract ZIP archive?', subtitle: 'Creates a new folder · no overwrite', detail: `${args.path}
→ ${args.destination}
${archive.entryCount} entries · ${(archive.totalUncompressedBytes / 1024 / 1024).toFixed(2)} MB expanded
Path traversal, symlinks, encrypted files and unsupported compression are rejected.`, risky: true }, signal);
          if (!approval) return { cancelled: true };
          try { await destinationParent.getDirectoryHandle(destinationName); throw new Error(`Destination folder '${args.destination}' was created by another operation; extraction was stopped.`); } catch (error) { if (error?.name !== 'NotFoundError') throw error; }
          try { await destinationParent.getFileHandle(destinationName); throw new Error(`Destination path '${args.destination}' is now occupied by a file.`); } catch (error) { if (error?.name !== 'NotFoundError') throw error; }
          const root = await destinationParent.getDirectoryHandle(destinationName, { create: true }); let filesWritten = 0; let directoriesCreated = 0;
          try {
            const archiveFile = await readTaskFile(args.path, 50 * 1024 * 1024); const archiveBytes = new Uint8Array(await archiveFile.file.arrayBuffer());
            for (const entry of archive.entries) {
              if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
              let current = root; const parts = entry.path.split('/'); const leaf = entry.directory ? null : parts.pop();
              for (const part of parts) {
                try { current = await current.getDirectoryHandle(part); }
                catch (error) { if (error?.name !== 'NotFoundError') throw error; current = await current.getDirectoryHandle(part, { create: true }); directoriesCreated++; }
              }
              if (entry.directory) continue;
              const content = await decompressZipEntry(archiveBytes, entry, signal); const fileHandle = await current.getFileHandle(leaf, { create: true });
              const writable = await fileHandle.createWritable({ keepExistingData: false });
              try { await writable.write(new Blob([content])); await writable.close(); } catch (error) { try { await writable.abort(); } catch (_) {} throw error; }
              filesWritten++;
            }
            return { extracted: true, archive: args.path, destination: args.destination, filesWritten, directoriesCreated, expandedBytes: archive.totalUncompressedBytes, crc32Verified: true };
          } catch (error) {
            try { await destinationParent.removeEntry(destinationName, { recursive: true }); } catch (_) {}
            throw new Error(`ZIP extraction failed and the partial destination was removed: ${error?.message || error}`);
          }
          });
        }
        case 'infer_dataset_schema': return await inferCsvSchema(args);
        case 'join_datasets': return await joinCsvDatasets(args, signal);
        case 'transform_json': {
          const { text } = await readWorkspaceText(args.path, 10 * 1024 * 1024);
          const value = JSON.parse(text);
          const transformedValue = transformJsonValue(value, args);
          const transformed = JSON.stringify(transformedValue, null, args.mode === 'pretty' || args.mode === 'sort_keys' ? 2 : args.mode === 'minify' ? 0 : 2);
          if (transformed.length > 10 * 1024 * 1024) throw new Error('Transformed JSON exceeds the 10 MB output limit.');
          if (args.destination) {
            const path = String(args.destination).trim();
            if (!path) throw new Error('Destination path cannot be empty.');
            return await saveGeneratedArtifact(path, new Blob([transformed], { type: 'application/json;charset=utf-8' }), 'json', 'Save transformed JSON?', `${args.path} → ${path}\n${transformed.length.toLocaleString()} characters`, signal);
          }
          return { path: args.path, mode: args.mode, content: transformed.slice(0, 100000), characters: transformed.length, truncated: transformed.length > 100000 };
        }
        case 'validate_dataset': return await validateDataset(args);
        case 'verify_artifact': return await verifyWorkspaceArtifact(args.path, args.format || 'auto', signal, args.delimiter || '');
        case 'analyze_csv': {
          const { text } = await readWorkspaceText(args.path, 10 * 1024 * 1024);
          const delimiter = resolveCsvDelimiter(args.path, args.delimiter, text);
          return summarizeCsv(args.path, text, delimiter);
        }
        case 'generate_statistics': {
          const { text } = await readWorkspaceText(args.path, 10 * 1024 * 1024);
          return summarizeCsv(args.path, text, resolveCsvDelimiter(args.path, args.delimiter, text));
        }
        case 'filter_dataset': {
          const { text } = await readWorkspaceText(args.path, 10 * 1024 * 1024);
          const delimiter = resolveCsvDelimiter(args.path, args.delimiter, text);
          const dataset = csvDataset(args.path, text, delimiter);
          const index = datasetColumnIndex(dataset.headers, args.column);
          const wanted = String(args.value ?? '').toLowerCase();
          const rows = dataset.rows.filter(row => {
            const actual = String(row[index] ?? ''); const lower = actual.toLowerCase();
            switch (args.operator) {
              case 'equals': return lower === wanted;
              case 'not_equals': return lower !== wanted;
              case 'contains': return lower.includes(wanted);
              case 'gt': return Number(actual) > Number(args.value);
              case 'gte': return Number(actual) >= Number(args.value);
              case 'lt': return Number(actual) < Number(args.value);
              case 'lte': return Number(actual) <= Number(args.value);
              case 'empty': return actual.trim() === '';
              case 'not_empty': return actual.trim() !== '';
              default: throw new Error('Unsupported filter operator.');
            }
          });
          const output = csvRowsToText([dataset.headers, ...rows], delimiter);
          const approved = await requireAgentApproval({ title: 'Save filtered dataset?', subtitle: 'Create new CSV · source remains unchanged', detail: `${args.path}
→ ${args.destination}
${rows.length} of ${dataset.rows.length} rows retained`, risky: true }, signal);
          if (!approved) return { cancelled: true };
          const written = await writeWorkspaceFile(args.destination, new Blob([output], { type: 'text/csv;charset=utf-8' }), false);
          return { source: args.path, saved: written.path, inputRows: dataset.rows.length, outputRows: rows.length };
        }
        case 'deduplicate_dataset': {
          const { text } = await readWorkspaceText(args.path, 10 * 1024 * 1024);
          const delimiter = resolveCsvDelimiter(args.path, args.delimiter, text);
          const dataset = csvDataset(args.path, text, delimiter);
          const indexes = Array.isArray(args.columns) && args.columns.length
            ? args.columns.map(column => datasetColumnIndex(dataset.headers, column))
            : dataset.headers.map((_, index) => index);
          const seen = new Set(); const rows = dataset.rows.filter(row => { const key = JSON.stringify(indexes.map(index => row[index] ?? '')); if (seen.has(key)) return false; seen.add(key); return true; });
          const approved = await requireAgentApproval({ title: 'Save deduplicated dataset?', subtitle: 'Create new CSV · source remains unchanged', detail: `${args.path}
→ ${args.destination}
${dataset.rows.length - rows.length} duplicate row(s) removed`, risky: true }, signal);
          if (!approved) return { cancelled: true };
          const written = await writeWorkspaceFile(args.destination, new Blob([csvRowsToText([dataset.headers, ...rows], delimiter)], { type: 'text/csv;charset=utf-8' }), false);
          return { source: args.path, saved: written.path, inputRows: dataset.rows.length, outputRows: rows.length, removed: dataset.rows.length - rows.length };
        }
        case 'merge_csv': {
          if (!Array.isArray(args.paths) || args.paths.length < 2 || args.paths.length > 20) throw new Error('Provide between 2 and 20 CSV paths.');
          let outputDelimiter = args.delimiter ? normalizeCsvDelimiter(args.delimiter) : ''; let headers = null; const allRows = []; let aggregateInputBytes = 0;
          for (const path of args.paths) {
            if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
            const loaded = await readWorkspaceText(path, 10 * 1024 * 1024);
            aggregateInputBytes += loaded.file.size;
            if (aggregateInputBytes > CSV_AGGREGATE_INPUT_BYTES_LIMIT) throw new Error('Merged CSV inputs exceed the 50 MB aggregate input-size limit.');
            const delimiter = args.delimiter ? outputDelimiter : resolveCsvDelimiter(path, '', loaded.text);
            if (!outputDelimiter) outputDelimiter = delimiter;
            const data = csvDataset(path, loaded.text, delimiter);
            if (!headers) headers = data.headers; else if (JSON.stringify(headers) !== JSON.stringify(data.headers)) throw new Error(`CSV headers do not match in '${path}'.`);
            allRows.push(...data.rows);
            if (allRows.length > 200000) throw new Error('Merged dataset exceeds the 200,000-row safety limit.');
          }
          const content = csvRowsToText([headers, ...allRows], outputDelimiter, { maxBytes: CSV_OUTPUT_BYTES_LIMIT, signal });
          const approved = await requireAgentApproval({ title: 'Merge CSV datasets?', subtitle: 'Create new CSV · source files remain unchanged', detail: `${args.paths.join('\n')}\n→ ${args.destination}\n${allRows.length} data rows · ${new Blob([content]).size.toLocaleString()} output bytes`, risky: true }, signal);
          if (!approved) return { cancelled: true };
          const written = await writeWorkspaceFile(args.destination, new Blob([content], { type: 'text/csv;charset=utf-8' }), false);
          return { saved: written.path, sourceFiles: args.paths.length, inputBytes: aggregateInputBytes, rows: allRows.length, columns: headers.length, outputBytes: written.bytes };
        }
        case 'compare_datasets': {
          const leftText = await readWorkspaceText(args.path_a, 10 * 1024 * 1024); const rightText = await readWorkspaceText(args.path_b, 10 * 1024 * 1024);
          const delimiter = args.delimiter ? normalizeCsvDelimiter(args.delimiter) : resolveCsvDelimiter(args.path_a, '', leftText.text);
          const rightDelimiter = args.delimiter ? delimiter : resolveCsvDelimiter(args.path_b, '', rightText.text);
          const left = csvDataset(args.path_a, leftText.text, delimiter);
          const right = csvDataset(args.path_b, rightText.text, rightDelimiter);
          const leftSet = new Set(left.rows.map(row => JSON.stringify(row))); const rightSet = new Set(right.rows.map(row => JSON.stringify(row)));
          return { headersMatch: JSON.stringify(left.headers) === JSON.stringify(right.headers), leftRows: left.rows.length, rightRows: right.rows.length, onlyInA: [...leftSet].filter(row => !rightSet.has(row)).length, onlyInB: [...rightSet].filter(row => !leftSet.has(row)).length, headerA: left.headers, headerB: right.headers };
        }
        case 'find_duplicates': {
          const scan = await walkWorkspace(agentDirectoryHandle, { maxDepth: args.max_depth ?? 5, maxResults: Math.min(300, args.max_files ?? 300), maxFilesScanned: 5000 }, signal);
          const files = scan.results.filter(item => item.type === 'file').slice(0, 300);
          const bySize = new Map();
          let skippedByReadError = 0;
          for (const item of files) {
            if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
            try {
              const { handle } = await getFileAtPath(agentDirectoryHandle, item.path); const file = await handle.getFile();
              if (!bySize.has(file.size)) bySize.set(file.size, []);
              bySize.get(file.size).push({ path: item.path, file });
            } catch (error) { if (error?.name === 'AbortError') throw error; skippedByReadError++; }
          }
          const candidateGroups = [...bySize.values()].filter(group => group.length > 1);
          const HASHING_BUDGET_BYTES = 500 * 1024 * 1024;
          let hashingBytes = 0, filesHashed = 0, skippedByBudget = 0, skippedByFileLimit = 0;
          const byHash = new Map();
          for (const group of candidateGroups) {
            for (const candidate of group) {
              if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
              if (candidate.file.size > 100 * 1024 * 1024) { skippedByFileLimit++; continue; }
              if (hashingBytes + candidate.file.size > HASHING_BUDGET_BYTES) { skippedByBudget++; continue; }
              hashingBytes += candidate.file.size;
              try {
                const hash = await sha256File(candidate.file); filesHashed++;
                if (!byHash.has(hash)) byHash.set(hash, []);
                byHash.get(hash).push({ path: candidate.path, bytes: candidate.file.size });
              } catch (error) { if (error?.name === 'AbortError') throw error; skippedByReadError++; }
            }
          }
          return {
            filesScanned: files.length, workspaceFilesScanned: scan.filesScanned, directoryEntriesScanned: scan.directoryEntriesScanned,
            scanTruncated: scan.truncated, scanStopReason: scan.stopReason || undefined,
            filesHashed, candidateFiles: candidateGroups.reduce((total, group) => total + group.length, 0), hashingBytes,
            hashingBudgetBytes: HASHING_BUDGET_BYTES, skippedByBudget, skippedByFileLimit, skippedByReadError,
            resultsMayBeIncomplete: skippedByBudget > 0 || skippedByFileLimit > 0 || skippedByReadError > 0 || scan.truncated,
            duplicateGroups: [...byHash.entries()].filter(([, group]) => group.length > 1).map(([sha256, matches]) => ({ sha256, files: matches }))
          };
        }
        case 'compare_folders': {
          const a = await walkWorkspace(await getDirectoryAtPath(agentDirectoryHandle, args.folder_a), { maxDepth: args.max_depth ?? 5, maxResults: 500, maxFilesScanned: 5000 }, signal);
          const b = await walkWorkspace(await getDirectoryAtPath(agentDirectoryHandle, args.folder_b), { maxDepth: args.max_depth ?? 5, maxResults: 500, maxFilesScanned: 5000 }, signal);
          const left = new Map(a.results.filter(item => item.type === 'file').map(item => [item.path, item.size])); const right = new Map(b.results.filter(item => item.type === 'file').map(item => [item.path, item.size]));
          const onlyA = [...left.keys()].filter(path => !right.has(path)); const onlyB = [...right.keys()].filter(path => !left.has(path)); const sizeChanged = [...left.keys()].filter(path => right.has(path) && left.get(path) !== right.get(path)).map(path => ({ path, sizeA: left.get(path), sizeB: right.get(path) }));
          return { folderA: args.folder_a, folderB: args.folder_b, onlyInA: onlyA, onlyInB: onlyB, sizeChanged, scanTruncated: a.truncated || b.truncated, scan: { folderA: { filesScanned: a.filesScanned, directoryEntriesScanned: a.directoryEntriesScanned, stopReason: a.stopReason || null }, folderB: { filesScanned: b.filesScanned, directoryEntriesScanned: b.directoryEntriesScanned, stopReason: b.stopReason || null } }, note: 'Files with equal sizes are not guaranteed to have equal contents.' };
        }
        case 'create_markdown_report': {
          const content = reportMarkdown(args);
          if (content.length > 2 * 1024 * 1024) throw new Error('Report content exceeds 2 MB.');
          return await saveGeneratedArtifact(args.path, new Blob([content], { type: 'text/markdown;charset=utf-8' }), 'markdown', 'Create Markdown report?', `${content.length.toLocaleString()} characters`, signal);
        }
        case 'create_html_report': {
          const content = String(args.content || ''); if (content.length > 2 * 1024 * 1024) throw new Error('Report content exceeds 2 MB.');
          const title = String(args.title || 'Research report').slice(0, 200);
          let html = content;
          if (!/<!doctype html|<html[\s>]/i.test(content)) {
            const enriched = `${args.summary ? `> **Summary:** ${String(args.summary).slice(0, 1000)}\n\n` : ''}${content}`;
            const rendered = markdownReportHtml(enriched);
            html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${escapeHtml(title)}</title><style>body{font:16px/1.65 system-ui,sans-serif;max-width:1040px;margin:40px auto;padding:0 26px;color:#202833}h1,h2,h3{line-height:1.25;margin-top:1.6em}pre{overflow:auto;background:#f3f5f8;padding:14px;border-radius:8px}code{font-family:ui-monospace,monospace;background:#f1f2f4;padding:2px 4px;border-radius:4px}pre code{background:none;padding:0}table{border-collapse:collapse;width:100%;display:block;overflow:auto}th,td{border:1px solid #d8dee7;padding:8px;text-align:left}th{background:#f1f4f8}.report-toc{background:#f5f7fa;padding:16px 22px;border-radius:10px}.report-toc ol{padding-left:22px}.report-toc .level-3{margin-left:14px}.report-toc .level-4{margin-left:28px}blockquote{border-left:3px solid #8295ad;margin-left:0;padding-left:16px;color:#536174}img{max-width:100%}a{color:#4056b8}footer{margin-top:48px;padding-top:16px;border-top:1px solid #d8dee7;color:#718096;font-size:12px}@media(prefers-color-scheme:dark){body{background:#202326;color:#e8ebef}pre,code,.report-toc{background:#2b3036}th{background:#303740}td,th{border-color:#4b5562}blockquote{color:#c0cad6}}</style></head><body><h1>${escapeHtml(title)}</h1>${args.include_toc ? rendered.toc : ''}${rendered.html}<footer>Generated by RunMind · ${new Date().toISOString()}</footer></body></html>`;
          }
          return await saveGeneratedArtifact(args.path, new Blob([html], { type: 'text/html;charset=utf-8' }), 'html', 'Create HTML report?', `${html.length.toLocaleString()} characters`, signal);
        }
        case 'create_docx': {
          let path = String(args.path || '').trim(); if (!path) throw new Error('A DOCX destination path is required.'); if (!/\.docx$/i.test(path)) path += '.docx';
          const bytes = window.RunMindArtifactTools.createDocx(args);
          return await saveGeneratedArtifact(path, new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' }), 'docx', 'Create Word document?', `${bytes.length.toLocaleString()} bytes · Markdown content`, signal);
        }
        case 'create_xlsx': {
          let path = String(args.path || '').trim(); if (!path) throw new Error('An XLSX destination path is required.'); if (!/\.xlsx$/i.test(path)) path += '.xlsx';
          const bytes = window.RunMindArtifactTools.createXlsx(args);
          return await saveGeneratedArtifact(path, new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }), 'xlsx', 'Create Excel workbook?', `${args.sheets.length} worksheet(s) · ${bytes.length.toLocaleString()} bytes`, signal);
        }
        case 'create_pdf': {
          let path = String(args.path || '').trim(); if (!path) throw new Error('A PDF destination path is required.'); if (!/\.pdf$/i.test(path)) path += '.pdf';
          const result = window.RunMindArtifactTools.createPdf(args);
          const saved = await saveGeneratedArtifact(path, new Blob([result.bytes], { type: 'application/pdf' }), 'pdf', 'Create PDF document?', `${result.pages} page(s) · ${result.bytes.length.toLocaleString()} bytes`, signal);
          return { ...saved, pages: result.pages, unsupportedCharactersReplaced: result.replacements, ...(result.replacements ? { warning: `${result.replacements} unsupported character(s) were replaced because the lightweight PDF writer uses built-in fonts.` } : {}) };
        }
        case 'generate_qr_code': {
          let path = String(args.path || '').trim(); if (!path) throw new Error('A QR destination path is required.'); if (!/\.svg$/i.test(path)) path += '.svg';
          const result = window.RunMindArtifactTools.createQrSvg(args);
          const saved = await saveGeneratedArtifact(path, new Blob([result.svg], { type: 'image/svg+xml;charset=utf-8' }), 'svg', 'Create QR code?', `${result.bytes} payload bytes · QR version ${result.version}`, signal);
          return { ...saved, qrVersion: result.version, modules: result.modules, payloadBytes: result.bytes, errorCorrection: result.errorCorrection };
        }
        case 'create_image_gallery': {
          let path = String(args.path || '').trim(); if (!path) throw new Error('A gallery destination path is required.'); if (!/\.html?$/i.test(path)) path += '.html';
          const html = await buildImageGalleryHtml(args, path);
          return await saveGeneratedArtifact(path, new Blob([html], { type: 'text/html;charset=utf-8' }), 'html', 'Create image gallery?', `${args.images.length} image(s) · ${html.length.toLocaleString()} characters`, signal);
        }
        case 'create_chart': {
          let path = String(args.path || '').trim(); if (!path) throw new Error('An SVG destination path is required.'); if (!/\.svg$/i.test(path)) path += '.svg';
          const svg = buildChartSvg(args);
          return await saveGeneratedArtifact(path, new Blob([svg], { type: 'image/svg+xml;charset=utf-8' }), 'svg', 'Create chart?', `${svg.length.toLocaleString()} SVG characters`, signal);
        }
        case 'create_diagram': {
          let base = String(args.path || '').trim(); if (!base) throw new Error('A diagram destination path is required.');
          base = base.replace(/\.(?:svg|mmd)$/i, '');
          const format = ['svg','mermaid','both'].includes(String(args.format || 'both').toLowerCase()) ? String(args.format || 'both').toLowerCase() : 'both';
          const paths = format === 'svg' ? [`${base}.svg`] : format === 'mermaid' ? [`${base}.mmd`] : [`${base}.svg`, `${base}.mmd`];
          const svg = format === 'mermaid' ? '' : buildDiagramSvg(args);
          const mermaid = format === 'svg' ? '' : window.RunMindArtifactTools.buildMermaid(args);
          for (const path of paths) await assertNewWorkspacePath(path);
          const approved = await requireAgentApproval({ title: 'Create editable diagram?', subtitle: `New ${format === 'both' ? 'SVG and Mermaid' : format.toUpperCase()} file(s) · existing files will not be overwritten`, detail: paths.join('\n') + `\n${args.nodes.length} nodes · ${args.edges.length} connections`, risky: true }, signal);
          if (!approved) return { cancelled: true };
          const created = [];
          try {
            if (svg) { const written = await writeWorkspaceFile(paths[0], new Blob([svg], { type: 'image/svg+xml;charset=utf-8' }), false); created.push({ path: written.path, format: 'svg', verified: (await verifyWorkspaceArtifact(written.path, 'svg', signal)).valid }); }
            if (mermaid) { const path = paths[paths.length - 1]; const written = await writeWorkspaceFile(path, new Blob([mermaid], { type: 'text/plain;charset=utf-8' }), false); const verification = await verifyWorkspaceArtifact(written.path, 'mermaid', signal); created.push({ path: written.path, format: 'mermaid', editable: true, verified: verification.valid }); }
          } catch (error) {
            throw new Error(`Diagram creation partially completed (${created.map(item => item.path).join(', ')}). ${error?.message || error}`);
          }
          return { created, format, message: 'Mermaid source is editable; SVG is the rendered preview.' };
        }
        case 'image_metadata': return await transformWorkspaceImage(args, 'metadata', signal);
        case 'read_image': return await readWorkspaceImageForModel(args);
        case 'resize_image': return await transformWorkspaceImage(args, 'resize', signal);
        case 'convert_image': return await transformWorkspaceImage(args, 'convert', signal);
        case 'crop_image': return await transformWorkspaceImage(args, 'crop', signal);
        case 'generate_thumbnail': return await transformWorkspaceImage({ ...args, output_type: 'image/webp', max_width: 256, max_height: 256 }, 'thumbnail', signal);
        case 'download_webpage': {
          if (!agentDirectoryHandle) throw new Error('No folder is selected. Call request_folder_access first and wait for the user to choose one.');
          let target;
          try { target = new URL(String(args.url || '').trim()); } catch (_) { throw new Error('Provide a valid absolute URL.'); }
          if (!['http:', 'https:'].includes(target.protocol)) throw new Error('Only HTTP and HTTPS URLs can be downloaded.');
          const download = await makeWebpageDownload(target, signal, args.filename, name === 'download_webpage');
          const approved = await requireAgentApproval({ title: 'Save downloaded page?', subtitle: `File write · ${agentDirectoryHandle.name || 'selected folder'} · existing files will not be overwritten`, detail: `File: ${download.filename}\nSource: ${target.href}\nType: ${download.source}\nSize: ${download.bytes.toLocaleString()} bytes`, risky: true }, signal);
          if (!approved) return { cancelled: true, message: 'The page was fetched but was not saved because permission was declined.' };
          const written = await writeWorkspaceFile(download.filename, download.blob, false);
          return { saved: true, path: written.path, folder: agentDirectoryHandle.name || 'selected folder', bytes: written.bytes, source: target.href, format: download.source, message: `Downloaded page saved successfully as ${download.filename}.` };
        }
        default: throw new Error(`Tool '${name}' is not permitted.`);
      }
    }
    function jsonCanonical(value) {
      if (Array.isArray(value)) return `[${value.map(jsonCanonical).join(',')}]`;
      if (value && typeof value === 'object') return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${jsonCanonical(value[key])}`).join(',')}}`;
      return JSON.stringify(value);
    }
    function jsonPointerTarget(root, reference) {
      if (typeof reference !== 'string' || !reference.startsWith('#/')) throw new Error(`Only local JSON Schema references are supported ('${reference}').`);
      let current = root;
      for (const part of reference.slice(2).split('/').map(value => value.replace(/~1/g, '/').replace(/~0/g, '~'))) {
        if (!current || typeof current !== 'object' || !Object.prototype.hasOwnProperty.call(current, part)) throw new Error(`JSON Schema reference '${reference}' could not be resolved.`);
        current = current[part];
      }
      return current;
    }
    async function validateJsonSchemaData(data, schema, signal) {
      if (typeof schema === 'boolean') return { valid: schema, errorCount: schema ? 0 : 1, errors: schema ? [] : [{ path: '$', keyword: 'falseSchema', message: 'The root false schema rejects every value.' }], truncated: false, schemaDialect: 'boolean schema', checks: 1, patternChecks: 0, warnings: [] };
      if (!schema || typeof schema !== 'object' || Array.isArray(schema)) throw new Error('The JSON Schema must be an object or boolean schema.');
      const errors = [];
      const warnings = new Set();
      const patternCache = new Map();
      let evaluations = 0;
      let patternChecks = 0;
      const maxErrors = 100;
      const maxEvaluations = 12000;
      const actualType = value => value === null ? 'null' : Array.isArray(value) ? 'array' : Number.isInteger(value) ? 'integer' : typeof value;
      const add = (bucket, path, keyword, message, details = {}) => {
        if (bucket.length < maxErrors) bucket.push({ path, keyword, message, ...details });
      };
      const schemaPatternMatches = async (pattern, value) => {
        const key = `${pattern}\u0000${value}`;
        if (patternCache.has(key)) return patternCache.get(key);
        if (++patternChecks > 200) throw new Error('Schema contains too many regular-expression checks (limit: 200).');
        const result = await regexTestInWorker({ pattern: String(pattern), text: String(value), flags: '' }, signal);
        const matches = result.matchCount > 0;
        patternCache.set(key, matches);
        return matches;
      };
      const checkFormat = (format, value) => {
        const text = String(value);
        try {
          switch (format) {
            case 'date-time': return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})$/i.test(text) && Number.isFinite(Date.parse(text));
            case 'date': return /^\d{4}-\d{2}-\d{2}$/.test(text) && Number.isFinite(Date.parse(`${text}T00:00:00Z`));
            case 'time': return /^\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?$/.test(text);
            case 'email': return text.length <= 254 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(text);
            case 'uri': case 'url': { const parsed = new URL(text); return !!parsed.protocol; }
            case 'uuid': return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(text);
            case 'ipv4': { const parts = text.split('.'); return parts.length === 4 && parts.every(part => /^\d{1,3}$/.test(part) && Number(part) <= 255); }
            case 'hostname': return text.length <= 253 && text.split('.').every(label => label.length > 0 && label.length <= 63 && /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/i.test(label));
            default: return null;
          }
        } catch (_) { return false; }
      };
      const validate = async (value, currentSchema, path, depth, bucket) => {
        if (signal?.aborted) throw new DOMException('Task cancelled.', 'AbortError');
        if (depth > 48) throw new Error(`JSON Schema evaluation exceeded the maximum depth at ${path}.`);
        if (++evaluations > maxEvaluations) throw new Error('JSON Schema evaluation limit reached (12,000 checks).');
        if (currentSchema === true) return;
        if (currentSchema === false) { add(bucket, path, 'falseSchema', 'This value is rejected by a false schema.'); return; }
        if (!currentSchema || typeof currentSchema !== 'object' || Array.isArray(currentSchema)) throw new Error(`Invalid JSON Schema at ${path}: expected an object or boolean schema.`);
        if (typeof currentSchema.$ref === 'string') await validate(value, jsonPointerTarget(schema, currentSchema.$ref), path, depth + 1, bucket);
        if (currentSchema.type !== undefined) {
          const types = Array.isArray(currentSchema.type) ? currentSchema.type : [currentSchema.type];
          const type = actualType(value);
          const matches = types.some(expected => expected === type || (expected === 'number' && type === 'integer'));
          if (!matches) { add(bucket, path, 'type', `Expected ${types.join(' or ')} but received ${type}.`, { expected: types, actual: type }); return; }
        }
        if (Object.prototype.hasOwnProperty.call(currentSchema, 'const') && jsonCanonical(value) !== jsonCanonical(currentSchema.const)) add(bucket, path, 'const', 'Value does not equal the required constant.', { expected: currentSchema.const });
        if (Array.isArray(currentSchema.enum) && !currentSchema.enum.some(item => jsonCanonical(value) === jsonCanonical(item))) add(bucket, path, 'enum', 'Value is not one of the allowed enum values.', { allowed: currentSchema.enum.slice(0, 30) });
        if (typeof value === 'number' && Number.isFinite(value)) {
          if (Number.isFinite(currentSchema.minimum) && value < currentSchema.minimum) add(bucket, path, 'minimum', `Value must be at least ${currentSchema.minimum}.`, { limit: currentSchema.minimum, actual: value });
          if (Number.isFinite(currentSchema.maximum) && value > currentSchema.maximum) add(bucket, path, 'maximum', `Value must be at most ${currentSchema.maximum}.`, { limit: currentSchema.maximum, actual: value });
          if (Number.isFinite(currentSchema.exclusiveMinimum) && value <= currentSchema.exclusiveMinimum) add(bucket, path, 'exclusiveMinimum', `Value must be greater than ${currentSchema.exclusiveMinimum}.`, { limit: currentSchema.exclusiveMinimum, actual: value });
          if (Number.isFinite(currentSchema.exclusiveMaximum) && value >= currentSchema.exclusiveMaximum) add(bucket, path, 'exclusiveMaximum', `Value must be less than ${currentSchema.exclusiveMaximum}.`, { limit: currentSchema.exclusiveMaximum, actual: value });
          if (Number.isFinite(currentSchema.multipleOf) && currentSchema.multipleOf > 0 && Math.abs(value / currentSchema.multipleOf - Math.round(value / currentSchema.multipleOf)) > 1e-10) add(bucket, path, 'multipleOf', `Value must be a multiple of ${currentSchema.multipleOf}.`);
        }
        if (typeof value === 'string') {
          const length = Array.from(value).length;
          if (Number.isInteger(currentSchema.minLength) && length < currentSchema.minLength) add(bucket, path, 'minLength', `String must contain at least ${currentSchema.minLength} characters.`, { actual: length });
          if (Number.isInteger(currentSchema.maxLength) && length > currentSchema.maxLength) add(bucket, path, 'maxLength', `String must contain at most ${currentSchema.maxLength} characters.`, { actual: length });
          if (typeof currentSchema.pattern === 'string' && !(await schemaPatternMatches(currentSchema.pattern, value))) add(bucket, path, 'pattern', `String does not match pattern '${currentSchema.pattern}'.`);
          if (typeof currentSchema.format === 'string') {
            const valid = checkFormat(currentSchema.format, value);
            if (valid === false) add(bucket, path, 'format', `String is not a valid ${currentSchema.format}.`);
            else if (valid === null) warnings.add(`Format '${currentSchema.format}' is treated as an annotation and is not asserted.`);
          }
        }
        if (Array.isArray(value)) {
          if (Number.isInteger(currentSchema.minItems) && value.length < currentSchema.minItems) add(bucket, path, 'minItems', `Array must contain at least ${currentSchema.minItems} items.`, { actual: value.length });
          if (Number.isInteger(currentSchema.maxItems) && value.length > currentSchema.maxItems) add(bucket, path, 'maxItems', `Array must contain at most ${currentSchema.maxItems} items.`, { actual: value.length });
          if (currentSchema.uniqueItems === true) {
            const seen = new Set();
            for (let index = 0; index < value.length; index++) { const key = jsonCanonical(value[index]); if (seen.has(key)) { add(bucket, `${path}[${index}]`, 'uniqueItems', 'Array item duplicates an earlier item.'); break; } seen.add(key); }
          }
          if (Array.isArray(currentSchema.prefixItems)) for (let index = 0; index < Math.min(value.length, currentSchema.prefixItems.length); index++) await validate(value[index], currentSchema.prefixItems[index], `${path}[${index}]`, depth + 1, bucket);
          if (currentSchema.items !== undefined) {
            const start = Array.isArray(currentSchema.prefixItems) ? currentSchema.prefixItems.length : 0;
            for (let index = start; index < value.length; index++) await validate(value[index], currentSchema.items, `${path}[${index}]`, depth + 1, bucket);
          }
          if (currentSchema.contains !== undefined) {
            let matches = 0;
            for (let index = 0; index < value.length; index++) { const local = []; await validate(value[index], currentSchema.contains, `${path}[${index}]`, depth + 1, local); if (!local.length) matches++; }
            const min = Number.isInteger(currentSchema.minContains) ? currentSchema.minContains : 1;
            if (matches < min) add(bucket, path, 'contains', `Array has ${matches} matching items; at least ${min} required.`, { matches, minimum: min });
            if (Number.isInteger(currentSchema.maxContains) && matches > currentSchema.maxContains) add(bucket, path, 'maxContains', `Array has ${matches} matching items; at most ${currentSchema.maxContains} allowed.`, { matches, maximum: currentSchema.maxContains });
          }
        }
        if (value && typeof value === 'object' && !Array.isArray(value)) {
          const keys = Object.keys(value);
          if (Number.isInteger(currentSchema.minProperties) && keys.length < currentSchema.minProperties) add(bucket, path, 'minProperties', `Object must contain at least ${currentSchema.minProperties} properties.`);
          if (Number.isInteger(currentSchema.maxProperties) && keys.length > currentSchema.maxProperties) add(bucket, path, 'maxProperties', `Object must contain at most ${currentSchema.maxProperties} properties.`);
          for (const required of Array.isArray(currentSchema.required) ? currentSchema.required : []) if (!Object.prototype.hasOwnProperty.call(value, required)) add(bucket, path, 'required', `Required property '${required}' is missing.`, { property: required });
          const properties = currentSchema.properties && typeof currentSchema.properties === 'object' ? currentSchema.properties : {};
          const patterns = currentSchema.patternProperties && typeof currentSchema.patternProperties === 'object' ? Object.entries(currentSchema.patternProperties) : [];
          for (const [key, item] of Object.entries(value)) {
            let matched = false;
            if (Object.prototype.hasOwnProperty.call(properties, key)) { matched = true; await validate(item, properties[key], `${path}.${key}`, depth + 1, bucket); }
            for (const [pattern, childSchema] of patterns) if (await schemaPatternMatches(pattern, key)) { matched = true; await validate(item, childSchema, `${path}.${key}`, depth + 1, bucket); }
            if (!matched && currentSchema.additionalProperties !== undefined) await validate(item, currentSchema.additionalProperties, `${path}.${key}`, depth + 1, bucket);
          }
          if (currentSchema.propertyNames !== undefined) for (const key of keys) await validate(key, currentSchema.propertyNames, `${path}.[property:${key}]`, depth + 1, bucket);
          if (currentSchema.dependentRequired && typeof currentSchema.dependentRequired === 'object') for (const [key, required] of Object.entries(currentSchema.dependentRequired)) if (Object.prototype.hasOwnProperty.call(value, key) && Array.isArray(required)) for (const dependency of required) if (!Object.prototype.hasOwnProperty.call(value, dependency)) add(bucket, path, 'dependentRequired', `Property '${key}' requires property '${dependency}'.`);
          if (currentSchema.dependentSchemas && typeof currentSchema.dependentSchemas === 'object') for (const [key, childSchema] of Object.entries(currentSchema.dependentSchemas)) if (Object.prototype.hasOwnProperty.call(value, key)) await validate(value, childSchema, path, depth + 1, bucket);
          if (currentSchema.dependencies && typeof currentSchema.dependencies === 'object') for (const [key, dependency] of Object.entries(currentSchema.dependencies)) if (Object.prototype.hasOwnProperty.call(value, key)) { if (Array.isArray(dependency)) { for (const required of dependency) if (!Object.prototype.hasOwnProperty.call(value, required)) add(bucket, path, 'dependencies', `Property '${key}' requires property '${required}'.`); } else if (dependency && typeof dependency === 'object') await validate(value, dependency, path, depth + 1, bucket); }
        }
        if (Array.isArray(currentSchema.allOf)) for (const child of currentSchema.allOf) await validate(value, child, path, depth + 1, bucket);
        if (Array.isArray(currentSchema.anyOf)) {
          let passed = 0; for (const child of currentSchema.anyOf) { const local = []; await validate(value, child, path, depth + 1, local); if (!local.length) passed++; }
          if (!passed) add(bucket, path, 'anyOf', 'Value does not satisfy any anyOf branch.', { branches: currentSchema.anyOf.length });
        }
        if (Array.isArray(currentSchema.oneOf)) {
          let passed = 0; for (const child of currentSchema.oneOf) { const local = []; await validate(value, child, path, depth + 1, local); if (!local.length) passed++; }
          if (passed !== 1) add(bucket, path, 'oneOf', `Value must satisfy exactly one oneOf branch; matched ${passed}.`, { matched: passed });
        }
        if (currentSchema.not !== undefined) { const local = []; await validate(value, currentSchema.not, path, depth + 1, local); if (!local.length) add(bucket, path, 'not', 'Value matches a schema that must not match.'); }
        if (currentSchema.if !== undefined) { const local = []; await validate(value, currentSchema.if, path, depth + 1, local); const branch = local.length ? currentSchema.else : currentSchema.then; if (branch !== undefined) await validate(value, branch, path, depth + 1, bucket); }
        for (const keyword of ['unevaluatedItems','unevaluatedProperties','$dynamicRef','$recursiveRef','contentSchema','contentEncoding','contentMediaType']) if (Object.prototype.hasOwnProperty.call(currentSchema, keyword)) warnings.add(`Keyword '${keyword}' is not enforced by RunMind's bounded validator.`);
      };
      await validate(data, schema, '$', 0, errors);
      return { valid: errors.length === 0, errorCount: errors.length, errors, truncated: errors.length >= maxErrors, schemaDialect: schema.$schema || 'unspecified', checks: evaluations, patternChecks, warnings: [...warnings] };
    }
