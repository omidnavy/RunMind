    'use strict';
    let darkThemes = [];
    let lightThemes = [];
    let BOOT = {};
    const $ = (selector) => document.querySelector(selector);
    const modelSelect = $('#modelSelect');
    const modelConnectionName = $('#modelConnectionName');
    const modelConnectionType = $('#modelConnectionType');
    const modelConnectionLmStudioMetadata = $('#modelConnectionLmStudioMetadata');
    const modelConnectionLmStudioMetadataField = $('#modelConnectionLmStudioMetadataField');
    const modelConnectionUrl = $('#modelConnectionUrl');
    const modelConnectionApiKey = $('#modelConnectionApiKey');
    const modelConnectionAuthHeader = $('#modelConnectionAuthHeader');
    const cancelEditModelConnection = $('#cancelEditModelConnection');
    const changeAgentFolderButton = $('#changeAgentFolder');
    const agentFolderAccessChange = $('#agentFolderAccessChange');
    const agentFolderAccessDetail = $('#agentFolderAccessDetail');
    const modelConnectionList = $('#modelConnectionList');
    const transcript = $('#transcript');
    const transcriptInner = $('#transcriptInner');
    const promptInput = $('#promptInput');
    const sendButton = $('#sendButton');
    const stopButton = $('#stopButton');
    const refreshButton = $('#refreshModels');
    const exportFormat = $('#exportFormat');
    const sessionFileInput = $('#sessionFileInput');
    const openSessionDestinationModal = $('#openSessionDestinationModal');
    const openSessionDestinationTitle = $('#openSessionDestinationTitle');
    const openSessionDestinationMessage = $('#openSessionDestinationMessage');
    const openSessionDestinationFileName = $('#openSessionDestinationFileName');
    const newConversationUnsavedModal = $('#newConversationUnsavedModal');
    const newConversationUnsavedMessage = $('#newConversationUnsavedMessage');
    const newConversationUnsavedSave = $('#newConversationUnsavedSave');
    const newConversationUnsavedDiscard = $('#newConversationUnsavedDiscard');
    const newConversationUnsavedCancel = $('#newConversationUnsavedCancel');
    const openSessionDestinationCancel = $('#openSessionDestinationCancel');
    const openSessionDestinationNew = $('#openSessionDestinationNew');
    const openSessionDestinationCurrent = $('#openSessionDestinationCurrent');
    const thinkToggle = $('#thinkToggle');
    const statusText = $('#statusText');
    const statusDot = $('#statusDot');
    const toast = $('#toast');
    const thinkButton = $('#thinkButton');
    const agentModeButton = $('#agentModeButton');
    const deepResearchButton = $('#deepResearchButton');
    const researchPanel = $('#researchPanel');
    const researchWorkspaceMenu = $('#researchWorkspaceMenu');
    const researchWorkspaceButton = $('#researchWorkspaceButton');
    const researchStateLabel = $('#researchState');
    const researchLiveOperation = $('#researchLiveOperation');
    const researchPauseButton = $('#researchPauseButton');
    const researchResumeButton = $('#researchResumeButton');
    const researchCancelButton = $('#researchCancelButton');
    const researchExportButton = $('#researchExportButton');
    const researchCopyPlanButton = $('#researchCopyPlanButton');
    const researchCopyResultsButton = $('#researchCopyResultsButton');
    const researchCloseButton = $('#researchCloseButton');
    const researchDepth = $('#researchDepth');
    const researchPageBudget = $('#researchPageBudget');
    const researchCallBudget = $('#researchCallBudget');
    const researchPlanEditor = $('#researchPlanEditor');
    const researchPlanMeta = $('#researchPlanMeta');
    const researchSources = $('#researchSources');
    const researchTimeBudget = $('#researchTimeBudget');
    const researchPreferredDomains = $('#researchPreferredDomains');
    const researchAllowDomains = $('#researchAllowDomains');
    const researchBlockDomains = $('#researchBlockDomains');
    const researchSourceCount = $('#researchSourceCount');
    const researchEvidence = $('#researchEvidence');
    const researchEvidenceCount = $('#researchEvidenceCount');
    const researchActivity = $('#researchActivity');
    const researchActivityCount = $('#researchActivityCount');
    const researchTabButtons = [...document.querySelectorAll('[data-research-view]')];
    const researchViews = { research: $('#researchResearchView'), workflow: $('#researchWorkflowView'), review: $('#researchReviewView'), recovery: $('#researchRecoveryView') };
    const researchReviewTabCount = $('#researchReviewTabCount');
    const researchRecoveryTabCount = $('#researchRecoveryTabCount');
    const researchClaimTotal = $('#researchClaimTotal');
    const researchClaimSupported = $('#researchClaimSupported');
    const researchClaimConflict = $('#researchClaimConflict');
    const researchClaimPending = $('#researchClaimPending');
    const researchReviewMeta = $('#researchReviewMeta');
    const researchClaimFilter = $('#researchClaimFilter');
    const researchClaims = $('#researchClaims');
    const researchSourceAssessmentCount = $('#researchSourceAssessmentCount');
    const researchSourceAssessments = $('#researchSourceAssessments');
    const researchContextValue = $('#researchContextValue');
    const researchContextTrack = $('#researchContextTrack');
    const researchContextFill = $('#researchContextFill');
    const researchContextMeta = $('#researchContextMeta');
    const researchRunDiagnosticsButton = $('#researchRunDiagnosticsButton');
    const researchDiagnosticsResult = $('#researchDiagnosticsResult');
    const researchCheckpointMeta = $('#researchCheckpointMeta');
    const researchCheckpointList = $('#researchCheckpointList');
    const researchOperationRecovery = $('#researchOperationRecovery');
    const researchTemplateSelect = $('#researchTemplateSelect');
    const researchApplyTemplateButton = $('#researchApplyTemplateButton');
    const researchTemplateNote = $('#researchTemplateNote');
    const workflowParallelism = $('#workflowParallelism');
    const workflowGraph = $('#workflowGraph');
    const workflowGraphMeta = $('#workflowGraphMeta');
    const workflowSubagents = $('#workflowSubagents');
    const workflowAnalytics = $('#workflowAnalytics');
    const workflowCheckpoints = $('#workflowCheckpoints');
    const workflowTimeline = $('#workflowTimeline');
    const conversationBranchToggle = $('#conversationBranchToggle');
    const conversationBranchCount = $('#conversationBranchCount');
    const conversationBranchClose = $('#conversationBranchClose');
    const conversationBranchBar = $('#conversationBranchBar');
    const conversationBranchSelect = $('#conversationBranchSelect');
    const conversationBranchRename = $('#conversationBranchRename');
    const conversationBranchDelete = $('#conversationBranchDelete');
    const conversationBranchNote = $('#conversationBranchNote');
    const conversationBranchCompareSelect = $('#conversationBranchCompareSelect');
    const conversationBranchCompareButton = $('#conversationBranchCompare');
    const conversationBranchCompareView = $('#conversationBranchCompareView');
    const conversationBranchCompareMode = $('#conversationBranchCompareMode');
    const conversationBranchCompareTurnControls = $('#conversationBranchCompareTurnControls');
    const conversationBranchCompareLeftTurn = $('#conversationBranchCompareLeftTurn');
    const conversationBranchCompareRightTurn = $('#conversationBranchCompareRightTurn');
    const conversationBranchCompareSelectionHint = $('#conversationBranchCompareSelectionHint');
    let activeBranchCompareSourceIds = null;
    const conversationBranchCompareTitle = $('#conversationBranchCompareTitle');
    const conversationBranchCompareLeftTitle = $('#conversationBranchCompareLeftTitle');
    const conversationBranchCompareRightTitle = $('#conversationBranchCompareRightTitle');
    const conversationBranchCompareLeft = $('#conversationBranchCompareLeft');
    const conversationBranchCompareRight = $('#conversationBranchCompareRight');
    const conversationBranchCompareModel = $('#conversationBranchCompareModel');
    const conversationBranchCompareAiButton = $('#conversationBranchCompareAi');
    const conversationBranchCompareAiStop = $('#conversationBranchCompareAiStop');
    const conversationBranchCompareAiResult = $('#conversationBranchCompareAiResult');
    const conversationBranchCompareAiDialog = $('#conversationBranchCompareAiDialog');
    const conversationBranchCompareAiClose = $('#conversationBranchCompareAiClose');
    const conversationBranchCompareAiDescription = $('#conversationBranchCompareAiDescription');
    const conversationBranchCompareAiCopy = $('#conversationBranchCompareAiCopy');
    const conversationBranchCompareAiDownload = $('#conversationBranchCompareAiDownload');
    const conversationBranchCompareAiStatus = $('#conversationBranchCompareAiStatus');
    const conversationBranchCompareAiStatusRow = $('#conversationBranchCompareAiStatusRow');
    const conversationBranchCompareDiscussion = $('#conversationBranchCompareDiscussion');
    const conversationBranchCompareForm = $('#conversationBranchCompareForm');
    const conversationBranchCompareInput = $('#conversationBranchCompareInput');
    const conversationBranchCompareSend = $('#conversationBranchCompareSend');
    const conversationBranchCompareReexamine = $('#conversationBranchCompareReexamine');
    const conversationBranchCompareContextNote = $('#conversationBranchCompareContextNote');
    const conversationBranchCompareAiButtonLabel = $('#conversationBranchCompareAiButtonLabel');
    const branchCompareSessions = new Map();
    const BRANCH_COMPARE_SESSION_LIMIT = 3;
    const BRANCH_COMPARE_SESSION_MESSAGE_LIMIT = 40;
    const BRANCH_COMPARE_SESSION_HISTORY_CHAR_LIMIT = 60000;
    const BRANCH_COMPARE_MODEL_DOSSIER_LIMIT = 100000;
    const BRANCH_COMPARE_MODEL_BRANCH_LIMIT = 47000;
    const BRANCH_COMPARE_MODEL_HISTORY_ITEMS = 24;
    const BRANCH_COMPARE_DISCUSSION_LIMIT = 16000;
    const BRANCH_COMPARE_DISCUSSION_MESSAGE_LIMIT = 12;
    const BRANCH_COMPARE_STREAM_RENDER_INTERVAL_MS = 90;
    let branchCompareStreamRenderTimer = null;
    let branchCompareStreamRenderQueued = false;
    let branchCompareLastStreamRenderAt = 0;
    let branchCompareAiModalOpener = null;
    let activeBranchCompareSession = null;
    let branchCompareController = null;
    let branchCompareRunId = 0;
    let branchCompareCurrentAssistantMessage = null;
    let activeResearchView = 'research';
    let lastResearchHistorySignature = '';
    let selectedResearchTemplateId = 'general';
    let researchDiagnosticsController = null;
    const agentFolderButton = $('#agentFolderButton');
    const agentSearchUrl = $('#agentSearchUrl');
    const agentReaderFallback = $('#agentReaderFallback');
    const agentReaderBaseUrl = $('#agentReaderBaseUrl');
    const agentJinaApiKey = $('#agentJinaApiKey');
    const agentCustomReaderUrl = $('#agentCustomReaderUrl');
    const agentCustomReaderToken = $('#agentCustomReaderToken');
    const readerCredentialStatus = $('#readerCredentialStatus');
    const agentMaxSteps = $('#agentMaxSteps');
    const agentAutoApproveReads = $('#agentAutoApproveReads');
    const alwaysApprovedToolsList = $('#alwaysApprovedToolsList');
    const clearAlwaysApprovedToolsButton = $('#clearAlwaysApprovedTools');
    const projectProfileName = $('#projectProfileName');
    const projectProfileSelect = $('#projectProfileSelect');
    const saveProjectProfileButton = $('#saveProjectProfile');
    const agentSkillsStatus = $('#agentSkillsStatus');
    const agentSkillScope = $('#agentSkillScope');
    const agentSkillSelect = $('#agentSkillSelect');
    const agentSkillDescription = $('#agentSkillDescription');
    const agentSkillRemoveButton = $('#removeAgentSkill');
    const managedAgentSkills = new Map();
    const permissionGitRead = $('#permissionGitRead');
    const permissionGitWrite = $('#permissionGitWrite');
    const permissionGitRemote = $('#permissionGitRemote');
    const permissionDownloads = $('#permissionDownloads');
    const agentSettingsMenu = $('#agentSettingsMenu');
    const mcpSettingsMenu = $('#mcpSettingsMenu');
    const ocrLanguageMenu = $('#ocrLanguageMenu');
    const ocrLanguageList = $('#ocrLanguageList');
    const ocrLanguageStatus = $('#ocrLanguageStatus');
    const mcpModeButton = $('#mcpModeButton');
    const mcpServerName = $('#mcpServerName');
    const mcpServerUrl = $('#mcpServerUrl');
    const mcpServerToken = $('#mcpServerToken');
    const mcpServerList = $('#mcpServerList');
    const agentApproval = $('#agentApproval');
    const agentApprovalTitle = $('#agentApprovalTitle');
    const agentApprovalSubtitle = $('#agentApprovalSubtitle');
    const agentApprovalDetail = $('#agentApprovalDetail');
    const agentApprovalDeny = $('#agentApprovalDeny');
    const agentApprovalTask = $('#agentApprovalTask');
    const agentApprovalOnce = $('#agentApprovalOnce');
    const agentApprovalAlways = $('#agentApprovalAlways');
    const agentApprovalHint = $('#agentApprovalHint');
    const agentFolderAccessRequest = $('#agentFolderAccessRequest');
    const agentFolderAccessChoose = $('#agentFolderAccessChoose');
    const agentFolderAccessCancel = $('#agentFolderAccessCancel');
    let pendingAgentApproval = null;
    let pendingFolderAccessRequest = null;
    const taskApprovedReadTools = new Set();
    const AGENT_ALWAYS_APPROVED_KEY = 'local-llm.agent-always-approved-tools.v1';
    const alwaysApprovedAgentTools = new Set((() => { try { const rows = JSON.parse(localStorage.getItem(AGENT_ALWAYS_APPROVED_KEY) || '[]'); return Array.isArray(rows) ? rows.filter(item => typeof item === 'string') : []; } catch (_) { return []; } })());
    let activeAgentApprovalTool = '';
    let researchPanelOpen = false;
    const attachButton = $('#attachButton');
    const fileInput = $('#fileInput');
    const pendingAttachmentsContainer = $('#pendingAttachments');
    const attachmentNotice = $('#attachmentNotice');
    const attachmentNoticeText = $('#attachmentNoticeText');
    const attachmentWarningModal = $('#attachmentWarningModal');
    const attachmentWarningMessage = $('#attachmentWarningMessage');
    const attachmentWarningFiles = $('#attachmentWarningFiles');
    const attachmentWarningCancel = $('#attachmentWarningCancel');
    const attachmentWarningConfirm = $('#attachmentWarningConfirm');
    const jumpToLatestButton = $('#jumpToLatest');
    const editModeNotice = $('#editModeNotice');
    const cancelEditButton = $('#cancelEditButton');
    const settingTemperature = $('#settingTemperature');
    const settingTopP = $('#settingTopP');
    const settingMaxTokens = $('#settingMaxTokens');
    const settingContextLength = $('#settingContextLength');
    const settingSeed = $('#settingSeed');
    const settingSystemPrompt = $('#settingSystemPrompt');
    const systemPromptCharacterCount = $('#systemPromptCharacterCount');
    const systemPromptPreview = $('#systemPromptPreview');
    const settingLmStudioApiMode = $('#settingLmStudioApiMode');
    const lmStudioApiModeField = $('#lmStudioApiModeField');
    const nemotronConversationalModeField = $('#nemotronConversationalModeField');
    const settingNemotronConversationalMode = $('#settingNemotronConversationalMode');
    const nemotronConversationalModeNote = $('#nemotronConversationalModeNote');
    const settingHistoryTurns = $('#settingHistoryTurns');
    const settingHistoryTokenBudget = $('#settingHistoryTokenBudget');
    const historyContextNote = $('#historyContextNote');
    const summarizeHistoryButton = $('#summarizeHistoryButton');
    const contextUsageValue = $('#contextUsageValue');
    const contextUsageCapacity = $('#contextUsageCapacity');
    const contextUsageMode = $('#contextUsageMode');
    const contextUsagePercent = $('#contextUsagePercent');
    const contextMeter = $('#contextMeter');
    const contextMeterFill = $('#contextMeterFill');
    const contextIncludedCount = $('#contextIncludedCount');
    const contextSummarizedCount = $('#contextSummarizedCount');
    const contextOmittedCount = $('#contextOmittedCount');
    const contextTriggerState = $('#contextTriggerState');
    const contextSummaryState = $('#contextSummaryState');
    const contextSummaryEditor = $('#contextSummaryEditor');
    const contextPreviewList = $('#contextPreviewList');
    const saveContextSummaryButton = $('#saveContextSummaryButton');
    const clearContextSummaryButton = $('#clearContextSummaryButton');
    const generationSettingsMenu = $('#generationSettingsMenu');
    const modelInfoMenu = $('#modelInfoMenu');
    const modelInfoTitle = $('#modelInfoTitle');
    const modelInfoProvider = $('#modelInfoProvider');
    const modelCapabilities = $('#modelCapabilities');
    const modelInfoNote = $('#modelInfoNote');
    const selectedModelDot = $('#selectedModelDot');
    const selectedModelHealthText = $('#selectedModelHealthText');
    const contextSettingNote = $('#contextSettingNote');
    const PREFERENCES_KEY = 'local-llm.chat-ui-preferences.v1';
    function loadPreferences() {
      try {
        const raw = localStorage.getItem(PREFERENCES_KEY);
        if (!raw) return {};
        const saved = JSON.parse(raw);
        return saved && typeof saved === 'object' && !Array.isArray(saved) ? saved : {};
      } catch (_) { return {}; }
    }
    const initialPreferences = loadPreferences();
    let preferredModelKey = String(initialPreferences.selectedModelKey || '');
    let modelInfoRequestId = 0;
    let editingMessageIndex = null;
    let draftBeforeEdit = null;
    let nextMessageIsContinuation = false;
    let providerAvailability = { ollama: null, lmstudio: null };
    const modelInfoCache = new Map();
    let selectedContextMetadata = null;
    let modelMetadata = { ollama: {}, lmstudio: {} };
    // Developer-tunable upload policy. Adjust these values to change per-file, total,
    // per-message, and pre-send warning thresholds without searching through handlers.
    const ATTACHMENT_LIMITS = Object.freeze({
      maxFilesPerMessage: 10,
      maxImageBytes: 10 * 1024 * 1024,
      maxTextBytes: 2 * 1024 * 1024,
      maxPdfBytes: 25 * 1024 * 1024,
      maxArchiveBytes: 25 * 1024 * 1024,
      maxTotalBytes: 25 * 1024 * 1024,
      confirmBeforeSendBytes: 8 * 1024 * 1024
    });
    const SUPPORTED_TEXT_EXTENSIONS = new Set(['txt','md','markdown','csv','tsv','json','jsonl','xml','html','htm','svg','css','js','mjs','cjs','ts','tsx','jsx','py','cs','java','c','h','cpp','hpp','rs','go','php','sql','yaml','yml','toml','ini','cfg','conf','log','ps1','bat','cmd','sh','diff','patch','tex']);
    let conversationStartedAt = Date.now();
    const messages = [];
    let contextSummary = '';
    let contextSummaryThrough = 0;
    let contextSummaryDirty = false;
    // Thinking disclosure is a per-conversation preference for future assistant messages.
    // First use defaults to expanded; changing one section affects only messages created later.
    let defaultThinkingOpen = true;
    let pendingAttachments = [];
    let activeController = null;
    let activeControllerCompletion = Promise.resolve();
    let activeControllerCompletionResolver = null;
    let activeMetricsHandle = null;
    let activeAssistantMessage = null;
    let toastHandle = null;
    let attachmentConfirmResolver = null;
    let openSessionDestinationResolver = null;
    let newConversationUnsavedResolver = null;
    let saveConversationScopeResolver = null;
    let branchStorageWarningShown = false;
    const pendingNativeSaveRequests = new Map();
    let isConfirmingAttachments = false;

    function beginActiveController(controller) {
      activeController = controller;
      activeControllerCompletion = new Promise(resolve => { activeControllerCompletionResolver = resolve; });
    }
    function finishActiveController(controller) {
      if (activeController !== controller) return;
      activeController = null;
      const finish = activeControllerCompletionResolver;
      activeControllerCompletionResolver = null;
      if (finish) finish();
    }
    let modelLists = { ollama: [], lmstudio: [] };
    const MODEL_CONNECTIONS_KEY = 'local-llm.model-connections.v1';
    let modelConnections = [];
    let activeModelConnection = null;
    let editingModelConnectionId = null;
    let modelConnectionResults = new Map();
    let agentModeEnabled = false;
    let deepResearchEnabled = false;
    // Each top-level WebView gets a separate sessionStorage namespace. It survives
    // reloads in that window but is not shared with separately-created chat windows.
    // window.name is only a fallback for runtimes where sessionStorage is unavailable.
    const CHAT_SESSION_STORAGE_KEY = 'runmind.active-session-id.v1';
    function getChatWindowInstanceId() {
      try {
        const existing = sessionStorage.getItem(CHAT_SESSION_STORAGE_KEY);
        if (existing && /^[0-9a-f-]{36}$/i.test(existing)) return existing;
        const id = crypto.randomUUID();
        sessionStorage.setItem(CHAT_SESSION_STORAGE_KEY, id);
        return id;
      } catch (_) {
        const marker = 'localLLMChatInstance=';
        const existing = String(window.name || '').match(/(?:^|[|;])localLLMChatInstance=([0-9a-f-]{36})(?:$|[|;])/i);
        if (existing) return existing[1];
        const id = crypto.randomUUID();
        window.name = `${window.name ? `${window.name}|` : ''}${marker}${id}`;
        return id;
      }
    }
    function createNewChatSessionId() {
      const id = crypto.randomUUID();
      CHAT_WINDOW_INSTANCE_ID = id;
      try { sessionStorage.setItem(CHAT_SESSION_STORAGE_KEY, id); }
      catch (_) {
        const marker = 'localLLMChatInstance=';
        const name = String(window.name || '');
        const pattern = /(?:^|[|;])localLLMChatInstance=[0-9a-f-]{36}(?=$|[|;])/i;
        window.name = pattern.test(name)
          ? name.replace(pattern, match => `${match.startsWith('|') || match.startsWith(';') ? match[0] : ''}${marker}${id}`)
          : `${name ? `${name}|` : ''}${marker}${id}`;
      }
      return id;
    }
    let CHAT_WINDOW_INSTANCE_ID = getChatWindowInstanceId();
    let DEEP_RESEARCH_STORAGE_KEY = `local-llm.deep-research-checkpoint.v3.${CHAT_WINDOW_INSTANCE_ID}`;
    let AGENT_TASK_HISTORY_KEY = `local-llm.agent-task-history.v3.${CHAT_WINDOW_INSTANCE_ID}`;
    function configureWindowScopedStorage() {
      let id = CHAT_WINDOW_INSTANCE_ID;
      try {
        const stored = String(sessionStorage.getItem(CHAT_SESSION_STORAGE_KEY) || '');
        if (/^[0-9a-f-]{36}$/i.test(stored)) id = stored;
        else sessionStorage.setItem(CHAT_SESSION_STORAGE_KEY, id);
      } catch (_) { /* getChatWindowInstanceId() already established a window.name fallback. */ }
      if (!/^[0-9a-f-]{36}$/i.test(id)) id = createNewChatSessionId();
      CHAT_WINDOW_INSTANCE_ID = id;
      DEEP_RESEARCH_STORAGE_KEY = `local-llm.deep-research-checkpoint.v3.${id}`;
      AGENT_TASK_HISTORY_KEY = `local-llm.agent-task-history.v3.${id}`;
      workflowStorageKey = `${WORKFLOW_STORAGE_KEY_BASE}.${id}`;
      conversationBranchStorageKey = `${CONVERSATION_BRANCH_STORAGE_KEY_BASE}.${id}`;
    }
    let deepResearchTask = null;
    const WORKFLOW_STORAGE_KEY_BASE = 'runmind.workflow-state.v1';
    const CONVERSATION_BRANCH_STORAGE_KEY_BASE = 'runmind.conversation-session.v2';
    let workflowStorageKey = `${WORKFLOW_STORAGE_KEY_BASE}.${CHAT_WINDOW_INSTANCE_ID}`;
    let conversationBranchStorageKey = `${CONVERSATION_BRANCH_STORAGE_KEY_BASE}.${CHAT_WINDOW_INSTANCE_ID}`;
    let workflowOperationHistory = [];
    let workflowSubagentHistory = [];
    let workflowAnalyticsState = { tools: {}, providers: {}, totals: { invocations: 0, succeeded: 0, failed: 0, cancelled: 0, parallelGroups: 0, providerRequests: 0, providerSucceeded: 0, providerFailed: 0, providerLatencyMs: 0, providerTokens: 0, providerTokenReports: 0 } };
    let workflowBatchSequence = 0;
    let workflowToolPriorityOrder = [];
    const workflowLastBatchByLane = new Map();
    let conversationBranches = [];
    let conversationBranchHistoryComplete = true;
    let activeConversationBranchId = 'main';
    let conversationBranchManagerOpen = false;
    let researchResumeResolver = null;
    let researchLastAssistantMessage = null;
    let mcpModeEnabled = false;
    let activeToolDefinitions = [];
    let systemManagerSettingsCache = null;
    const MCP_STORAGE_KEY = 'local-llm.mcp-servers.v1';
    // Recent Agent Tasks use the same host-assigned per-window namespace as research checkpoints.
    const mcpServers = new Map();
    const mcpRuntime = new Map();
    let mcpRequestId = 0;
    const mcpToolRoutes = new Map();
    let agentDirectoryHandle = null;
    let persistedDirectoryHandle = null;
    let workspaceRestorePromise = Promise.resolve();
    let workspaceMcpRestorePromise = Promise.resolve();
    let bootWorkspaceContextId = null;
    let workspaceDbPromise = null;
    let workspaceDb = null;
    let folderAccessDeclinedForRun = false;
    const AGENT_SETTINGS_KEY = 'local-llm.agent-settings.v1';
    const WORKSPACE_DB_NAME = 'local-llm-workspace';
    const WORKSPACE_DB_STORE = 'handles';
    const LEGACY_WORKSPACE_HANDLE_KEY = 'default-workspace';
    const WORKSPACE_HANDLE_KEY_PREFIX = 'runmind.workspace-context.';
    const WORKSPACE_MIGRATION_KEY = 'runmind.workspace-context.legacy-handle-migrated.v1';
    const WORKSPACE_CONTEXT_VERSION = 1;
    const DEFAULT_JINA_READER_URL = 'https://r.jina.ai';
    // This page is the shared agent runtime for both WPF hosts. Web tools stay here;
    // host bridges are reserved for window lifecycle and genuinely OS-specific work.
    function safeWorkspaceAgentSettings(value = {}) {
      const settings = value && typeof value === 'object' && !Array.isArray(value) ? value : {};
      const searchUrl = normalizeSearchEndpoint(settings.searchUrl);
      const readerBaseUrl = normalizeReaderEndpoint(settings.readerBaseUrl || DEFAULT_JINA_READER_URL, false) || DEFAULT_JINA_READER_URL;
      const customReaderUrl = normalizeReaderEndpoint(settings.customReaderUrl, true);
      const maxSteps = Number.isFinite(Number(settings.maxSteps)) ? Math.min(20, Math.max(1, Math.round(Number(settings.maxSteps)))) : 8;
      return {
        searchUrl,
        readerBaseUrl,
        customReaderUrl,
        maxSteps,
        // Read-approval preferences are device-local and must never be imported
        // from a portable conversation file.
        readerFallback: settings.readerFallback !== false
      };
    }
    function normalizeWorkspacePermissionPolicy(value) {
      const level = (key, fallback = 'ask', allowAlways = true) => {
        const candidate = value?.[key];
        if (candidate == null) return fallback;
        if (candidate === 'allow' || candidate === 'ask' || (allowAlways && candidate === 'always-ask')) return candidate;
        return 'ask';
      };
      return { gitRead: level('gitRead', 'allow', false), gitWrite: level('gitWrite'), gitRemote: level('gitRemote'), gitDestructive: 'always-ask', downloads: level('downloads') };
    }
    function createWorkspaceContext(value = null) {
      const source = value && typeof value === 'object' && !Array.isArray(value) ? value : {};
      const id = typeof source.id === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(source.id)
        ? source.id : crypto.randomUUID();
      const policySource = source.permissionPolicy && typeof source.permissionPolicy === 'object' && !Array.isArray(source.permissionPolicy) ? source.permissionPolicy : null;
      return {
        version: WORKSPACE_CONTEXT_VERSION,
        id,
        permissionPolicy: policySource ? normalizeWorkspacePermissionPolicy(policySource) : null,
        folderName: typeof source.folderName === 'string' ? source.folderName.slice(0, 256) : '',
        instructions: typeof source.instructions === 'string' ? source.instructions.slice(0, 16000) : null,
        provider: typeof source.provider === 'string' && /^[a-z0-9][a-z0-9._-]{0,59}$/i.test(source.provider) ? source.provider : null,
        model: typeof source.model === 'string' ? source.model.slice(0, 160) : null,
        connectionId: typeof source.connectionId === 'string' && /^[a-z0-9][a-z0-9._-]{0,99}$/i.test(source.connectionId) ? source.connectionId : null,
        enabledSkills: Array.isArray(source.enabledSkills) ? source.enabledSkills.filter(value => typeof value === 'string' && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value)).slice(0, 50) : [],
        agentSettings: source.agentSettings && typeof source.agentSettings === 'object' && !Array.isArray(source.agentSettings)
          ? safeWorkspaceAgentSettings(source.agentSettings) : null,
        mcpServers: Array.isArray(source.mcpServers) ? source.mcpServers.slice(0, 50).flatMap(row => {
          if (!row || typeof row !== 'object') return [];
          const serverId = String(row.id || '').trim().slice(0, 100);
          const name = String(row.name || '').trim().slice(0, 120);
          let url = '';
          try { url = normalizeMcpEndpoint(row.url); } catch (_) { return []; }
          if (!/^[a-zA-Z0-9_-]{1,100}$/.test(serverId) || !name) return [];
          return [{ id: serverId, name, url, enabled: row.enabled === true }];
        }) : null
      };
    }
    let workspaceContext = createWorkspaceContext();
    const projectProfiles = new Map();
    const DEFAULT_PERMISSION_POLICY = Object.freeze({ gitRead: 'allow', gitWrite: 'ask', gitRemote: 'ask', gitDestructive: 'always-ask', downloads: 'ask' });
    let sharedPermissionPolicy = { ...DEFAULT_PERMISSION_POLICY };
    function workspaceHandleStorageKey(context = workspaceContext) {
      return `${WORKSPACE_HANDLE_KEY_PREFIX}${context.id}`;
    }
    function captureWorkspaceContext() {
      const settings = safeWorkspaceAgentSettings(loadAgentSettings());
      return {
        version: WORKSPACE_CONTEXT_VERSION,
        id: workspaceContext.id,
        folderName: String(workspaceContext.folderName || agentDirectoryHandle?.name || persistedDirectoryHandle?.name || '').slice(0, 256),
        instructions: String(settingSystemPrompt?.value || '').slice(0, 16000),
        provider: selectedModel()?.provider || workspaceContext.provider || null,
        model: selectedModel()?.model || workspaceContext.model || null,
        connectionId: selectedModel()?.connectionId || workspaceContext.connectionId || null,
        enabledSkills: Array.isArray(workspaceContext.enabledSkills) ? workspaceContext.enabledSkills.slice(0, 50) : [],
        permissionPolicy: workspaceContext.permissionPolicy ? { ...workspaceContext.permissionPolicy, gitDestructive: 'always-ask' } : null,
        agentSettings: settings,
        mcpServers: [...mcpServers.values()].slice(0, 50).map(({ id, name, url, enabled }) => ({ id, name, url, enabled: enabled !== false }))
      };
    }
    function loadReaderCredentials() {
      const settings = loadAgentSettings();
      return { jinaApiKey: String(settings.jinaApiKey || ''), customGatewayToken: String(settings.customGatewayToken || '') };
    }
    function saveReaderCredentials(value) {
      try {
        const settings = loadAgentSettings();
        settings.jinaApiKey = String(value?.jinaApiKey || '');
        settings.customGatewayToken = String(value?.customGatewayToken || '');
        localStorage.setItem(AGENT_SETTINGS_KEY, JSON.stringify(settings));
        return true;
      } catch (_) { showToast('Could not save Reader credentials in this WebView profile.'); return false; }
    }
    function updateReaderCredentialStatus() {
      const credentials = loadReaderCredentials();
      if (readerCredentialStatus) readerCredentialStatus.textContent = [credentials.jinaApiKey ? 'Jina key saved' : 'Jina anonymous access', credentials.customGatewayToken ? 'Custom gateway token saved' : 'No custom gateway token'].join(' · ');
      if (agentJinaApiKey) agentJinaApiKey.placeholder = credentials.jinaApiKey ? 'Saved — enter a new key to replace' : 'Optional';
      if (agentCustomReaderToken) agentCustomReaderToken.placeholder = credentials.customGatewayToken ? 'Saved — enter a new token to replace' : 'Optional';
    }
    function openWorkspaceDatabase() {
      if (workspaceDbPromise) return workspaceDbPromise;
      workspaceDbPromise = new Promise((resolve, reject) => {
        if (!('indexedDB' in window)) { reject(new Error('IndexedDB is unavailable in this WebView profile.')); return; }
        const request = indexedDB.open(WORKSPACE_DB_NAME, 1);
        request.onupgradeneeded = () => { if (!request.result.objectStoreNames.contains(WORKSPACE_DB_STORE)) request.result.createObjectStore(WORKSPACE_DB_STORE); };
        request.onsuccess = () => {
          const db = request.result;
          workspaceDb = db;
          db.onversionchange = () => {
            try { db.close(); } catch (_) {}
            if (workspaceDb === db) { workspaceDb = null; workspaceDbPromise = null; }
          };
          resolve(db);
        };
        request.onerror = () => reject(request.error || new Error('Could not open workspace storage.'));
      }).catch(error => { workspaceDbPromise = null; throw error; });
      return workspaceDbPromise;
    }
    async function workspaceDbAction(mode, action, value, key = workspaceHandleStorageKey()) {
      const db = await openWorkspaceDatabase();
      return await new Promise((resolve, reject) => {
        const transaction = db.transaction(WORKSPACE_DB_STORE, mode);
        const store = transaction.objectStore(WORKSPACE_DB_STORE);
        const request = action === 'get' ? store.get(key) : action === 'put' ? store.put(value, key) : store.delete(key);
        let result;
        request.onsuccess = () => { result = request.result; };
        request.onerror = () => reject(request.error || new Error('Workspace storage operation failed.'));
        transaction.oncomplete = () => resolve(result);
        transaction.onerror = () => reject(transaction.error || request.error || new Error('Workspace storage transaction failed.'));
        transaction.onabort = () => reject(transaction.error || new Error('Workspace storage transaction aborted.'));
      });
    }
    async function restoreAgentFolderHandle({ allowLegacyMigration = false } = {}) {
      const contextId = workspaceContext.id;
      if (window.chrome?.webview && typeof createNativeWorkspaceDirectoryHandle === 'function') {
        try {
          const nativeStatus = await callAgentHost('process.workspace.status', { contextId }, undefined, 10000);
          if (workspaceContext.id !== contextId) return;
          if (nativeStatus?.selected) {
            const nativeHandle = createNativeWorkspaceDirectoryHandle(contextId, nativeStatus);
            persistedDirectoryHandle = null;
            agentDirectoryHandle = nativeHandle;
            workspaceContext.folderName = String(nativeStatus.folder || nativeHandle.name || '').slice(0, 256);
            updateAgentFolderButton();
            rebuildActiveToolDefinitions(deepResearchEnabled);
            if (typeof scheduleProjectIndexAutoRefresh === 'function') scheduleProjectIndexAutoRefresh('native workspace restored');
            return;
          }
        } catch (error) {
          if (workspaceContext.id !== contextId) return;
          showToast(`Native workspace could not be restored: ${error?.message || error}`);
        }
        // In native hosts the native bridge is the sole workspace authority. An older
        // IndexedDB browser handle is retained only as a folder-name migration hint;
        // it is never used for file operations because it may not match the native root.
        let legacyHandle = null;
        try { legacyHandle = await workspaceDbAction('readonly', 'get', undefined, workspaceHandleStorageKey()); } catch (_) {}
        if (workspaceContext.id !== contextId) return;
        persistedDirectoryHandle = legacyHandle || null;
        agentDirectoryHandle = null;
        if (legacyHandle?.name && !workspaceContext.folderName) workspaceContext.folderName = String(legacyHandle.name).slice(0, 256);
        updateAgentFolderButton();
        if (typeof scheduleProjectIndexAutoRefresh === 'function') scheduleProjectIndexAutoRefresh('native workspace selection required');
        return;
      }
      let restoredHandle = null;
      let activeHandle = null;
      try {
        const key = workspaceHandleStorageKey();
        restoredHandle = await workspaceDbAction('readonly', 'get', undefined, key);
        if (!restoredHandle && allowLegacyMigration) {
          let migrateLegacy = false;
          try { migrateLegacy = localStorage.getItem(WORKSPACE_MIGRATION_KEY) !== '1'; } catch (_) {}
          if (migrateLegacy) {
            const legacyHandle = await workspaceDbAction('readonly', 'get', undefined, LEGACY_WORKSPACE_HANDLE_KEY);
            if (legacyHandle && workspaceContext.id === contextId) {
              await workspaceDbAction('readwrite', 'put', legacyHandle, key);
              restoredHandle = legacyHandle;
            }
            // Mark complete only after the IndexedDB lookup/copy succeeded. A storage
            // failure must leave the legacy handle eligible for a later retry.
            try { localStorage.setItem(WORKSPACE_MIGRATION_KEY, '1'); } catch (_) {}
          }
        }
        if (restoredHandle && typeof restoredHandle.queryPermission === 'function') {
          const permission = await restoredHandle.queryPermission({ mode: 'readwrite' });
          activeHandle = permission === 'granted' ? restoredHandle : null;
        }
      } catch (error) {
        if (workspaceContext.id !== contextId) return;
        persistedDirectoryHandle = null; agentDirectoryHandle = null;
        showToast(`Workspace handle could not be restored: ${error?.message || error}`);
      }
      // A conversation may have been switched while IndexedDB or permission checks
      // were pending. Never apply an older context's handle to the newly active one.
      if (workspaceContext.id !== contextId) return;
      persistedDirectoryHandle = restoredHandle;
      agentDirectoryHandle = activeHandle;
      if (restoredHandle?.name && !workspaceContext.folderName)
        workspaceContext.folderName = String(restoredHandle.name).slice(0, 256);
      updateAgentFolderButton();
      if (agentDirectoryHandle) {
        rebuildActiveToolDefinitions(deepResearchEnabled);
        if (typeof scheduleProjectIndexAutoRefresh === 'function') scheduleProjectIndexAutoRefresh('workspace restored');
      } else if (typeof scheduleProjectIndexAutoRefresh === 'function') scheduleProjectIndexAutoRefresh('workspace access unavailable');
    }
    async function persistAgentFolderHandle(handle) {
      if (handle?.__runMindNativeWorkspace === true) {
        const previousHandle = agentDirectoryHandle || persistedDirectoryHandle;
        const same = previousHandle?.__runMindNativeWorkspace === true
          && previousHandle.workspaceId === handle.workspaceId;
        if (!same && typeof projectIndexKey === 'function') {
          try { await workspaceDbAction('readwrite', 'delete', undefined, projectIndexKey()); } catch (_) {}
          try { projectIndexStatus?.(null); } catch (_) {}
        }
        // Native handles are virtual adapters, not structured-cloneable browser handles.
        // Never put them in IndexedDB; the host persists the canonical path by context ID.
        try { await workspaceDbAction('readwrite', 'delete'); } catch (_) {}
        persistedDirectoryHandle = null;
        agentDirectoryHandle = handle;
        workspaceContext.folderName = String(handle.name || '').slice(0, 256);
        updateAgentFolderButton();
        rebuildActiveToolDefinitions(deepResearchEnabled);
        if (typeof scheduleProjectIndexAutoRefresh === 'function') scheduleProjectIndexAutoRefresh('unified native workspace selected');
        return;
      }
      const previousHandle = persistedDirectoryHandle || agentDirectoryHandle;
      if (previousHandle) {
        let sameEntry = false;
        try { sameEntry = await previousHandle.isSameEntry(handle); } catch (_) {}
        if (!sameEntry && typeof projectIndexKey === 'function') {
          try { await workspaceDbAction('readwrite', 'delete', undefined, projectIndexKey()); } catch (_) {}
          try { projectIndexStatus?.(null); } catch (_) {}
        }
      }
      persistedDirectoryHandle = handle;
      workspaceContext.folderName = String(handle?.name || '').slice(0, 256);
      await workspaceDbAction('readwrite', 'put', handle);
      agentDirectoryHandle = handle;
      updateAgentFolderButton();
      rebuildActiveToolDefinitions(deepResearchEnabled);
      if (typeof scheduleProjectIndexAutoRefresh === 'function') scheduleProjectIndexAutoRefresh('workspace selected');
    }
    async function clearPersistedAgentFolderHandle() {
      if (agentDirectoryHandle?.__runMindNativeWorkspace === true && window.chrome?.webview) {
        await callAgentHost('process.workspace.clear', { contextId: workspaceContext.id }, undefined, 10000);
      }
      await workspaceDbAction('readwrite', 'delete');
      try { if (typeof projectIndexKey === 'function') await workspaceDbAction('readwrite', 'delete', undefined, projectIndexKey()); } catch (_) {}
      try { projectIndexStatus?.(null); } catch (_) {}
      persistedDirectoryHandle = null;
      agentDirectoryHandle = null;
      workspaceContext.folderName = '';
      if (typeof projectIndexController !== 'undefined') projectIndexController?.abort();
      if (typeof scheduleProjectIndexAutoRefresh === 'function') scheduleProjectIndexAutoRefresh('workspace cleared');
      updateAgentFolderButton();
      rebuildActiveToolDefinitions(deepResearchEnabled);
    }
    function notifyWorkspaceHandleChanged(action) {
      try { localStorage.setItem('local-llm.workspace-handle-event', JSON.stringify({ action, workspaceContextId: workspaceContext.id, at: Date.now(), nonce: crypto.randomUUID() })); } catch (_) {}
      // The Project Skills scope is tied to the active workspace grant. Refresh it
      // after select, reconnect, change, or clear so it cannot show another folder.
      void refreshAgentSkillManager();
    }
    function updateAgentFolderButton() {
      const handle = agentDirectoryHandle || persistedDirectoryHandle;
      const folderName = handle?.name || workspaceContext.folderName || '';
      const granted = !!agentDirectoryHandle;
      agentFolderButton.classList.toggle('active', granted);
      agentFolderButton.classList.toggle('pending', !!folderName && !granted);
      agentFolderButton.title = !folderName ? 'Choose a workspace folder' : granted ? `Workspace: ${folderName} · click for details` : `Reconnect/select access to ${folderName}`;
      agentFolderButton.setAttribute('aria-label', agentFolderButton.title);
      const clear = $('#clearAgentFolder'); if (clear) clear.disabled = !handle;
      const change = $('#changeAgentFolder'); if (change) change.disabled = false;
    }
    function loadAgentSettings() {
      try {
        const value = JSON.parse(localStorage.getItem(AGENT_SETTINGS_KEY) || '{}');
        const globalSettings = value && typeof value === 'object' && !Array.isArray(value) ? value : {};
        return { ...globalSettings, ...(workspaceContext?.agentSettings || {}) };
      } catch (_) { return { ...(workspaceContext?.agentSettings || {}) }; }
    }
    async function refreshProjectProfiles() {
      if (!window.chrome?.webview) {
        projectProfileSelect.innerHTML = '<option value="">Desktop host required</option>';
        return;
      }
      try {
        const result = await callAgentHost('profiles.list');
        projectProfiles.clear();
        for (const profile of (Array.isArray(result?.profiles) ? result.profiles : [])) {
          if (!profile || typeof profile.id !== 'string' || typeof profile.name !== 'string') continue;
          projectProfiles.set(profile.id, profile);
        }
        const prior = projectProfileSelect.value;
        projectProfileSelect.replaceChildren();
        const placeholder = document.createElement('option'); placeholder.value = ''; placeholder.textContent = projectProfiles.size ? 'Choose a profile…' : 'No saved profiles';
        projectProfileSelect.appendChild(placeholder);
        for (const profile of [...projectProfiles.values()].sort((a, b) => a.name.localeCompare(b.name))) {
          const option = document.createElement('option'); option.value = profile.id; option.textContent = profile.name; projectProfileSelect.appendChild(option);
        }
        if (projectProfiles.has(prior)) projectProfileSelect.value = prior;
        saveProjectProfileButton.disabled = false;
        agentSkillsStatus.textContent = `${projectProfiles.size} shared profile(s) · Skills discovered by the Agent when relevant`;
      } catch (error) {
        projectProfileSelect.replaceChildren();
        const option = document.createElement('option'); option.value = ''; option.textContent = 'Profile store unavailable'; projectProfileSelect.appendChild(option);
        agentSkillsStatus.textContent = `Native profile store unavailable: ${error?.message || error}`;
      }
    }
    function profileFromCurrentConversation(name, id = crypto.randomUUID()) {
      const selection = selectedModel();
      const settings = safeWorkspaceAgentSettings(loadAgentSettings());
      return {
        id, name: String(name || '').trim().slice(0, 80), formatVersion: 1,
        instructions: String(settingSystemPrompt.value || '').slice(0, 16000),
        provider: selection?.provider || workspaceContext.provider || '',
        model: selection?.model || workspaceContext.model || '',
        connectionId: selection?.connectionId || workspaceContext.connectionId || '',
        workspaceName: String(agentDirectoryHandle?.name || workspaceContext.folderName || '').slice(0, 256),
        agentSettings: settings,
        skills: Array.isArray(workspaceContext.enabledSkills) ? workspaceContext.enabledSkills.slice(0, 50) : [],
        mcpServerIds: [...mcpServers.values()].filter(item => item.enabled !== false).map(item => item.id).slice(0, 50),
        permissionPolicy: { ...effectivePermissionPolicy() }
      };
    }
    async function saveProjectProfile() {
      const name = projectProfileName.value.trim();
      if (!name) { showToast('Enter a name for this Project Profile.'); projectProfileName.focus(); return; }
      const selected = projectProfiles.get(projectProfileSelect.value);
      const profile = profileFromCurrentConversation(name, selected?.id || crypto.randomUUID());
      const button = saveProjectProfileButton; button.disabled = true;
      try {
        const result = await callAgentHost('profiles.save', { profile });
        showToast(`Project Profile '${result.name}' saved for both desktop hosts.`);
        await refreshProjectProfiles(); projectProfileSelect.value = result.id;
      } catch (error) { showToast(`Could not save Project Profile: ${error?.message || error}`); }
      finally { button.disabled = false; }
    }
    async function applyProjectProfile() {
      const profile = projectProfiles.get(projectProfileSelect.value);
      if (!profile) { showToast('Choose a saved Project Profile first.'); return; }
      if (typeof profile.instructions === 'string') {
        settingSystemPrompt.value = profile.instructions;
        updateSystemPromptPreview();
        workspaceContext.instructions = profile.instructions;
      }
      workspaceContext.agentSettings = safeWorkspaceAgentSettings(profile.agentSettings || {});
      workspaceContext.enabledSkills = Array.isArray(profile.skills) ? profile.skills.filter(value => typeof value === 'string' && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value)).slice(0, 50) : [];
      workspaceContext.provider = typeof profile.provider === 'string' ? profile.provider.slice(0, 60) : null;
      workspaceContext.model = String(profile.model || '').slice(0, 160) || null;
      workspaceContext.connectionId = typeof profile.connectionId === 'string' ? profile.connectionId.slice(0, 100) : null;
      workspaceContext.permissionPolicy = profile.permissionPolicy && typeof profile.permissionPolicy === 'object'
        ? normalizeWorkspacePermissionPolicy(profile.permissionPolicy)
        : null;
      renderPermissionPolicy();
      const configRows = [...mcpServers.values()].filter(item => (profile.mcpServerIds || []).includes(item.id));
      workspaceContext.mcpServers = configRows.map(({ id, name, url, enabled }) => ({ id, name, url, enabled: enabled !== false }));
      await restoreMcpConfigs(workspaceContext.mcpServers);
      // A profile stores only a folder-name hint. It never transfers or reuses a browser grant.
      const wantedFolder = String(profile.workspaceName || '').trim();
      try { await workspaceDbAction('readwrite', 'delete'); } catch (error) { showToast(`The old browser folder grant could not be removed from IndexedDB: ${error?.message || error}`); }
      persistedDirectoryHandle = null; agentDirectoryHandle = null;
      workspaceContext.id = crypto.randomUUID();
      bootWorkspaceContextId = null;
      workspaceContext.folderName = wantedFolder;
      updateAgentFolderButton();
      applyAgentSettings();
      if (profile.provider && profile.model) {
        const options = [...modelSelect.options].filter(item => item.dataset.provider === profile.provider && item.dataset.model === profile.model && !item.disabled);
        const option = options.find(item => profile.connectionId && item.dataset.serverId === profile.connectionId) || options[0];
        if (option) modelSelect.value = option.value;
        else showToast(`Profile settings applied; ${profile.provider}/${profile.model} is not currently available. Select or add that model connection.`);
      }
      updateAgentFolderButton();
      rebuildActiveToolDefinitions(deepResearchEnabled);
      await refreshAgentSkillManager();
      showToast(`Project Profile '${profile.name}' applied to this conversation. Folder access must be reconnected separately.`);
    }
    async function deleteProjectProfile() {
      const id = projectProfileSelect.value;
      const profile = projectProfiles.get(id);
      if (!profile) { showToast('Choose a saved Project Profile first.'); return; }
      if (!window.confirm(`Delete Project Profile '${profile.name}'? This does not delete workspace files or conversations.`)) return;
      try { await callAgentHost('profiles.delete', { id }); projectProfiles.delete(id); projectProfileName.value = ''; await refreshProjectProfiles(); showToast(`Project Profile '${profile.name}' deleted.`); }
      catch (error) { showToast(`Could not delete Project Profile: ${error?.message || error}`); }
    }
    async function loadSystemManagerSettings() {
      try {
        const result = await callAgentHost('systemManager.settings.get');
        systemManagerSettingsCache = result?.settings && typeof result.settings === 'object' ? result.settings : null;
      } catch (_) { systemManagerSettingsCache = null; }
      if (typeof rebuildActiveToolDefinitions === 'function') rebuildActiveToolDefinitions(deepResearchEnabled);
      renderSystemManagerSettings();
    }
    function renderSystemManagerSettings() {
      const settings = systemManagerSettingsCache;
      const enabled = document.getElementById('systemManagerEnabled');
      const profile = document.getElementById('systemManagerProfile');
      if (!enabled || !profile) return;
      enabled.checked = !!settings?.enabled;
      profile.value = settings?.profile || 'restricted';
      const commandTimeout=document.getElementById('systemManagerCommandTimeout'); if(commandTimeout) commandTimeout.value=String(settings?.commandTimeoutSeconds ?? 0);
      const launchPermission=document.getElementById('systemManagerLaunchPermission'); if(launchPermission) launchPermission.value=settings?.permissions?.launchApplications || 'ask';
      const openPermission=document.getElementById('systemManagerOpenPermission'); if(openPermission) openPermission.value=settings?.permissions?.openTargets || 'ask';
      for (const [key,id] of Object.entries({applications:'systemManagerGroupApplications',processes:'systemManagerGroupProcesses',diagnostics:'systemManagerGroupDiagnostics'})) {
        const control=document.getElementById(id); if(control) control.checked = !!settings?.groups?.[key];
      }
      for (const [key,id] of Object.entries({discoverApplications:'systemManagerDiscoverApplications',launchApplications:'systemManagerLaunchApplications',openTargets:'systemManagerOpenTargets',listProcesses:'systemManagerListProcesses',inspectProcesses:'systemManagerInspectProcesses',terminateProcesses:'systemManagerTerminateProcesses',systemInformation:'systemManagerSystemInformation',environmentInformation:'systemManagerEnvironmentInformation',serviceInformation:'systemManagerServiceInformation',networkInformation:'systemManagerNetworkInformation',eventLogInformation:'systemManagerEventLogInformation',crashInvestigation:'systemManagerCrashInvestigation',resourceMonitoring:'systemManagerResourceMonitoring',scheduledTaskInformation:'systemManagerScheduledTaskInformation',scheduledTaskManagement:'systemManagerScheduledTaskManagement',storageHealth:'systemManagerStorageHealth',securityPosture:'systemManagerSecurityPosture',targetedNetworkDiagnostics:'systemManagerTargetedNetworkDiagnostics'})) {
        const control=document.getElementById(id); if(control) control.checked = !!settings?.capabilities?.[key];
      }
      const status=document.getElementById('systemManagerStatus'); if(status) status.textContent=settings?.enabled?'System Manager is enabled. Native operations still enforce these settings.':'System Manager is disabled. Its tools are not exposed to the Agent.';
    }
    async function saveSystemManagerSettingsFromUi() {
      const current=systemManagerSettingsCache || {};
      const read=(id,fallback=false)=>document.getElementById(id)?.checked ?? fallback;
      const settings={
        enabled:read('systemManagerEnabled'), profile:document.getElementById('systemManagerProfile')?.value || 'restricted',
        commandTimeoutSeconds:Math.max(0,Math.min(3600,Math.floor(Number(document.getElementById('systemManagerCommandTimeout')?.value)||0))),
        groups:{applications:read('systemManagerGroupApplications'),processes:read('systemManagerGroupProcesses'),diagnostics:read('systemManagerGroupDiagnostics')},
        capabilities:{discoverApplications:read('systemManagerDiscoverApplications'),launchApplications:read('systemManagerLaunchApplications'),openTargets:read('systemManagerOpenTargets'),listProcesses:read('systemManagerListProcesses'),inspectProcesses:read('systemManagerInspectProcesses'),terminateProcesses:read('systemManagerTerminateProcesses'),systemInformation:read('systemManagerSystemInformation'),environmentInformation:read('systemManagerEnvironmentInformation'),serviceInformation:read('systemManagerServiceInformation'),networkInformation:read('systemManagerNetworkInformation'),eventLogInformation:read('systemManagerEventLogInformation'),crashInvestigation:read('systemManagerCrashInvestigation'),resourceMonitoring:read('systemManagerResourceMonitoring'),scheduledTaskInformation:read('systemManagerScheduledTaskInformation'),scheduledTaskManagement:read('systemManagerScheduledTaskManagement'),storageHealth:read('systemManagerStorageHealth'),securityPosture:read('systemManagerSecurityPosture'),targetedNetworkDiagnostics:read('systemManagerTargetedNetworkDiagnostics')},
        permissions:{launchApplications:document.getElementById('systemManagerLaunchPermission')?.value || 'ask',openTargets:document.getElementById('systemManagerOpenPermission')?.value || 'ask'}
      };
      try {
        const result=await callAgentHost('systemManager.settings.save',{settings});
        systemManagerSettingsCache=result?.settings || settings;
        rebuildActiveToolDefinitions(deepResearchEnabled); renderSystemManagerSettings();
        await loadSystemManagerAdapters();
        showToast('System Manager settings saved.');
      } catch(error) { showToast(`Could not save System Manager settings: ${error?.message || error}`); await loadSystemManagerSettings(); }
    }
    async function loadSystemManagerAdapters() {
      const list = document.getElementById('systemManagerAdapterList');
      const status = document.getElementById('systemManagerAdapterStatus');
      if (!list) return;
      list.textContent = 'Loading adapters…';
      try {
        const result = await callAgentHost('systemManager.adapters.list', {});
        const adapters = Array.isArray(result?.adapters) ? result.adapters : [];
        list.replaceChildren();
        if (!adapters.length) {
          const empty = document.createElement('p'); empty.className = 'setting-note'; empty.textContent = 'No adapters are registered.'; list.append(empty);
        }
        for (const adapter of adapters) {
          const row = document.createElement('div'); row.className = 'capability-row system-manager-adapter-row';
          const copy = document.createElement('div'); copy.className = 'capability-copy';
          const title = document.createElement('strong'); title.textContent = adapter.displayName || adapter.id;
          const detail = document.createElement('span'); detail.textContent = `${adapter.builtIn ? 'Built-in' : 'Custom'} · matches ${adapter.match} · ${(adapter.operations || []).map(operation => typeof operation === 'string' ? operation : operation?.name).filter(Boolean).join(', ')}`;
          copy.append(title, detail); row.append(copy);
          if (adapter.builtIn) {
            const edit = document.createElement('button'); edit.type = 'button'; edit.className = 'setting-reset'; edit.textContent = 'Edit'; edit.dataset.adapterEdit = adapter.id; edit.dataset.adapterName = adapter.displayName || adapter.id; edit.dataset.adapterMatch = adapter.match || ''; edit.dataset.adapterOperations = JSON.stringify(Object.fromEntries((adapter.operations || []).map(op => [op.name, Array.isArray(op.templates) ? op.templates : []]))); row.append(edit);
            if (adapter.hasOverride) { const reset = document.createElement('button'); reset.type = 'button'; reset.className = 'setting-reset'; reset.textContent = 'Reset'; reset.dataset.adapterReset = adapter.id; row.append(reset); }
          }
          if (!adapter.builtIn) {
            const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'setting-reset'; remove.textContent = 'Remove'; remove.dataset.adapterRemove = adapter.id; row.append(remove);
          }
          list.append(row);
        }
        if (status) status.textContent = `${adapters.length} adapter(s) · custom manifests: ${result?.customAdapterDirectory || 'local System Manager directory'}`;
      } catch (error) {
        list.textContent = `Adapter management unavailable: ${error?.message || error}`;
        if (status) status.textContent = 'Enable System Manager, Application Management, and application launch permission.';
      }
    }
    async function installSystemManagerAdapterFromUi() {
      const input = document.getElementById('systemManagerAdapterManifest');
      const status = document.getElementById('systemManagerAdapterStatus');
      if (!input) return;
      let adapter;
      try { adapter = JSON.parse(input.value); } catch (error) { if (status) status.textContent = `Invalid JSON: ${error.message}`; return; }
      if (!adapter || typeof adapter !== 'object' || Array.isArray(adapter)) { if (status) status.textContent = 'The manifest must be a JSON object.'; return; }
      try {
        const result = await callAgentHost('systemManager.adapters.save', { adapter });
        if (result?.installed) { input.value = ''; input.dataset.adapterEditId = ''; const saveButton = document.getElementById('installSystemManagerAdapter'); if (saveButton) saveButton.textContent = 'Validate and install'; const cancelButton = document.getElementById('cancelSystemManagerAdapterEdit'); if (cancelButton) cancelButton.hidden = true; const details = input.closest('details'); if (details) details.open = false; if (status) status.textContent = `${result.overridden ? 'Saved override for' : 'Installed'} ${result.displayName}.`; await loadSystemManagerAdapters(); }
        else if (result?.cancelled) { if (status) status.textContent = 'Installation cancelled.'; }
        else if (status) status.textContent = 'The adapter was not installed.';
      } catch (error) { if (status) status.textContent = `Could not install adapter: ${error?.message || error}`; }
    }
    async function removeSystemManagerAdapterFromUi(adapterId) {
      const status = document.getElementById('systemManagerAdapterStatus');
      try {
        const result = await callAgentHost('systemManager.adapters.remove', { adapterId });
        if (result?.removed) { if (status) status.textContent = `Removed ${result.displayName}.`; await loadSystemManagerAdapters(); }
        else if (result?.cancelled) { if (status) status.textContent = 'Removal cancelled.'; }
        else if (status) status.textContent = `Adapter '${adapterId}' was not found.`;
      } catch (error) { if (status) status.textContent = `Could not remove adapter: ${error?.message || error}`; }
    }
    async function resetSystemManagerAdapter(adapterId) {
      const status = document.getElementById('systemManagerAdapterStatus');
      try { const result = await callAgentHost('systemManager.adapters.reset', { adapterId }); if (result?.reset) { if (status) status.textContent = `Restored shipped defaults for ${adapterId}.`; await loadSystemManagerAdapters(); } else if (result?.cancelled) { if (status) status.textContent = 'Reset cancelled.'; } else if (status) status.textContent = 'No user override exists.'; }
      catch (error) { if (status) status.textContent = `Could not reset adapter: ${error?.message || error}`; }
    }
    function setSystemManagerTab(tab) {
      document.querySelectorAll('[data-system-manager-tab]').forEach(button => button.setAttribute('aria-selected', String(button.dataset.systemManagerTab === tab)));
      document.querySelectorAll('[data-system-manager-view]').forEach(view => { view.hidden = view.dataset.systemManagerView !== tab; });
      if (tab === 'adapters') void loadSystemManagerAdapters();
    }
    async function loadSharedPermissionPolicy() {
      try {
        const result = await callAgentHost('permissions.get');
        sharedPermissionPolicy = { ...DEFAULT_PERMISSION_POLICY, ...(result?.policy || {}), gitDestructive: 'always-ask' };
      } catch (_) { sharedPermissionPolicy = { ...DEFAULT_PERMISSION_POLICY }; }
      renderPermissionPolicy();
    }
    function effectivePermissionPolicy() {
      return { ...sharedPermissionPolicy, ...(workspaceContext.permissionPolicy || {}), gitDestructive: 'always-ask' };
    }
    function renderPermissionPolicy() {
      const policy = effectivePermissionPolicy();
      for (const [key, id] of [['gitRead','permissionGitRead'],['gitWrite','permissionGitWrite'],['gitRemote','permissionGitRemote'],['downloads','permissionDownloads']]) {
        const control = document.getElementById(id); if (control) control.value = policy[key] || DEFAULT_PERMISSION_POLICY[key];
      }
    }
    async function saveSharedPermissionPolicy() {
      workspaceContext.permissionPolicy = {
        gitRead: permissionGitRead.value,
        gitWrite: permissionGitWrite.value,
        gitRemote: permissionGitRemote.value,
        gitDestructive: 'always-ask',
        downloads: permissionDownloads.value
      };
      renderPermissionPolicy();
      showToast('Permission policy saved with this conversation. Save a Project Profile to reuse it.');
    }
    function permissionRequiresApproval(category) {
      const policy = effectivePermissionPolicy();
      const level = policy[category] || DEFAULT_PERMISSION_POLICY[category] || 'ask';
      return level !== 'allow';
    }
    async function listProjectAgentSkills() {
      const skills = [];
      if (!agentDirectoryHandle) return skills;
      try {
        const agents = await agentDirectoryHandle.getDirectoryHandle('.agents');
        const skillRoot = await agents.getDirectoryHandle('skills');
        for await (const [name, handle] of skillRoot.entries()) {
          if (handle.kind !== 'directory' || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(name) || skills.length >= 200) continue;
          try {
            const file = await (await handle.getFileHandle('SKILL.md')).getFile();
            if (file.size > 128 * 1024) continue;
            const metadata = parseAgentSkillFrontmatter(await file.text());
            if (metadata.name === name && metadata.description) skills.push({ ...metadata, scope: 'project' });
          } catch (_) {}
        }
      } catch (_) {}
      return skills;
    }
    async function refreshAgentSkillManager() {
      const scope = agentSkillScope.value === 'project' ? 'project' : 'global';
      agentSkillSelect.replaceChildren();
      managedAgentSkills.clear();
      agentSkillRemoveButton.disabled = true;
      agentSkillDescription.textContent = 'Select a skill to view its description.';
      try {
        let skills = [];
        if (scope === 'global') {
          const result = await callAgentHost('skills.list', {});
          skills = Array.isArray(result?.skills) ? result.skills.filter(item => item?.scope === 'global') : [];
        } else {
          if (!agentDirectoryHandle) {
            const option = document.createElement('option'); option.value = ''; option.textContent = 'Select a workspace folder first'; agentSkillSelect.appendChild(option);
            agentSkillsStatus.textContent = 'Project Skills require an authorized workspace folder.';
            return;
          }
          skills = await listProjectAgentSkills();
        }
        for (const skill of skills) managedAgentSkills.set(skill.name, skill);
        for (const skill of skills.sort((a,b) => a.name.localeCompare(b.name))) {
          const option = document.createElement('option'); option.value = skill.name;
          option.textContent = skill.name; agentSkillSelect.appendChild(option);
        }
        if (!skills.length) {
          const option = document.createElement('option'); option.value = ''; option.textContent = 'No skills installed'; agentSkillSelect.appendChild(option);
        } else agentSkillSelect.selectedIndex = 0;
        agentSkillsStatus.textContent = `${skills.length} ${scope} skill(s) available. Skills load only when relevant; scripts are never run automatically.`;
        updateManagedSkillSelection();
      } catch (error) {
        const option = document.createElement('option'); option.value = ''; option.textContent = 'Could not load skills'; agentSkillSelect.appendChild(option);
        agentSkillsStatus.textContent = `Skill manager unavailable: ${error?.message || error}`;
      }
    }
    function updateManagedSkillSelection() {
      const skill = managedAgentSkills.get(agentSkillSelect.value);
      agentSkillRemoveButton.disabled = !skill;
      agentSkillDescription.textContent = skill?.description || 'Select a skill to view its description.';
    }
    async function copySkillFolderContents(source, destination, counter, depth = 0) {
      if (depth > 10) throw new Error('Skill folder nesting exceeds the 10-level limit.');
      for await (const [name, entry] of source.entries()) {
        if (!name || name === '.' || name === '..' || name.includes('/') || name.includes('\\') || name.includes(':')) throw new Error('A Skill contains an invalid path entry.');
        if (name === '.git' || name === 'node_modules') continue;
        if (entry.kind === 'directory') {
          const child = await destination.getDirectoryHandle(name, { create: true });
          await copySkillFolderContents(entry, child, counter, depth + 1);
          continue;
        }
        if (entry.kind !== 'file') continue;
        const file = await entry.getFile();
        counter.files++;
        counter.bytes += file.size;
        if (counter.files > 500 || file.size > 5 * 1024 * 1024 || counter.bytes > 20 * 1024 * 1024) throw new Error('Skill folder exceeds the safety limits (500 files, 5 MB per file, 20 MB total).');
        const target = await destination.getFileHandle(name, { create: true });
        const writable = await target.createWritable();
        try { await writable.write(file); } finally { await writable.close(); }
      }
    }
    async function installProjectAgentSkill() {
      if (!agentDirectoryHandle) throw new Error('Select and authorize a project workspace folder first.');
      if (typeof window.showDirectoryPicker !== 'function') throw new Error('This WebView2 runtime does not support directory selection.');
      const source = await window.showDirectoryPicker({ mode: 'read' });
      const skillFile = await (await source.getFileHandle('SKILL.md')).getFile();
      if (skillFile.size > 128 * 1024) throw new Error('SKILL.md exceeds the 128 KB limit.');
      const metadata = parseAgentSkillFrontmatter(await skillFile.text());
      if (!metadata.name || !metadata.description || metadata.name !== source.name) throw new Error('The selected folder must contain a valid SKILL.md whose name matches the folder name.');
      const agents = await agentDirectoryHandle.getDirectoryHandle('.agents', { create: true });
      const skills = await agents.getDirectoryHandle('skills', { create: true });
      try { await skills.getDirectoryHandle(metadata.name); throw new Error(`Project Skill '${metadata.name}' already exists.`); }
      catch (error) { if (error?.name !== 'NotFoundError') throw error; }
      const destination = await skills.getDirectoryHandle(metadata.name, { create: true });
      try { await copySkillFolderContents(source, destination, { files: 0, bytes: 0 }); }
      catch (error) { try { await skills.removeEntry(metadata.name, { recursive: true }); } catch (_) {} throw error; }
      return metadata;
    }
    async function writeProjectSkillContent(content, source) {
      if (!agentDirectoryHandle) throw new Error('Select and authorize a project workspace before installing a project Skill.');
      const metadata=parseAgentSkillFrontmatter(content);
      if(!metadata.name || !metadata.description) throw new Error('Skill content needs valid name and description frontmatter.');
      const agents=await agentDirectoryHandle.getDirectoryHandle('.agents',{create:true});
      const skills=await agents.getDirectoryHandle('skills',{create:true});
      try { await skills.getDirectoryHandle(metadata.name); throw new Error(`Project Skill '${metadata.name}' already exists.`); } catch(error) { if(error?.name!=='NotFoundError') throw error; }
      const folder=await skills.getDirectoryHandle(metadata.name,{create:true});
      try {
        for(const [name,value] of [['SKILL.md',content],['runmind-source.txt',source]]) {
          const file=await folder.getFileHandle(name,{create:true}), writable=await file.createWritable();
          try { await writable.write(value); } finally { await writable.close(); }
        }
      } catch(error) { try { await skills.removeEntry(metadata.name,{recursive:true}); } catch(_) {} throw error; }
      return metadata;
    }
    async function installSkillContentWithPreview(content, source='pasted conversation content') {
      const metadata=parseAgentSkillFrontmatter(content);
      if(!metadata.name || !metadata.description || String(content).length>128*1024) throw new Error('This content is not a valid Agent Skill SKILL.md or exceeds 128 KB.');
      const preview=String(content).slice(0,2600);
      if(!window.confirm(`Install this Agent Skill?\n\nName: ${metadata.name}\nDescription: ${metadata.description}\nSource: ${source}\n\nPreview:\n${preview}\n\nSkill instructions are untrusted. Scripts are not executed.`)) return {cancelled:true};
      const scope=agentSkillScope?.value==='project'?'project':'global';
      if(scope==='project') await writeProjectSkillContent(content,source);
      else await callAgentHost('skills.installContent',{content,source});
      await refreshAgentSkillManager();
      showToast(`Installed ${scope} Skill '${metadata.name}'.`);
      return {installed:true,name:metadata.name,scope};
    }
    async function installAgentSkillFromUrl(rawOverride = '') {
      const raw=String(rawOverride || document.getElementById('agentSkillSourceUrl')?.value||'').trim();
      if(!raw) { showToast('Enter a direct SKILL.md URL or supported GitHub/GitLab skill-folder URL.'); return false; }
      try {
        agentSkillsStatus.textContent='Fetching and validating Skill source…';
        const preview=await callAgentHost('skills.previewUrl',{url:raw});
        const metadata={name:preview.name,description:preview.description};
        if(!metadata.name||!metadata.description||!preview.content) throw new Error('The source did not return a valid SKILL.md.');
        const confirmed=window.confirm(`Skill preview\n\nName: ${metadata.name}\nDescription: ${metadata.description}\nSource: ${preview.source}\nSize: ${preview.bytes} bytes\n\n${preview.preview}\n\nInstall to ${agentSkillScope.value==='project'?'the current project':'Global Skills'}?\n\n${preview.note||''}\nScripts are never executed automatically.`);
        if(!confirmed) { agentSkillsStatus.textContent='Installation cancelled.'; return true; }
        const scope=agentSkillScope.value==='project'?'project':'global';
        if(scope==='project') await writeProjectSkillContent(preview.content,preview.source);
        else await callAgentHost('skills.installContent',{content:preview.content,source:preview.source});
        document.getElementById('agentSkillSourceUrl').value='';
        await refreshAgentSkillManager(); showToast(`Installed ${scope} Skill '${metadata.name}'.`); return true;
      } catch(error) { agentSkillsStatus.textContent=`URL installation failed: ${error?.message||error}`; showToast(`Could not install Skill URL: ${error?.message||error}`); return true; }
    }
    async function tryInstallSkillUrlMessage(content) {
      const value=String(content||'');
      if(!/\b(?:install|add|import)\b/i.test(value) || !/\bskill\b/i.test(value)) return false;
      const match=value.match(/https:\/\/[^\s<>"']+/i);
      if(!match) return false;
      const url=match[0].replace(/[),.;!?]+$/,'');
      await installAgentSkillFromUrl(url);
      return true;
    }
    async function tryInstallPastedSkill(content) {
      let value=String(content||'').trim();
      value=value.replace(/^```(?:markdown|md|text)?\s*\r?\n/i,'').replace(/\r?\n```\s*$/,'').trim();
      if(value.length>128*1024 || !/^---\s*\r?\n[\s\S]*?\r?\n---(?:\r?\n)/.test(value)) return false;
      const metadata=parseAgentSkillFrontmatter(value);
      if(!metadata.name||!metadata.description||!/(?:^|\n)#\s+|(?:^|\n)##\s+|\b(?:instructions|when to use|workflow|examples)\b/i.test(value.slice(0,12000))) return false;
      const result=await installSkillContentWithPreview(value,'pasted conversation content');
      return !!result?.installed;
    }
    async function installManagedAgentSkill() {
      const scope = agentSkillScope.value === 'project' ? 'project' : 'global';
      try {
        agentSkillsStatus.textContent = `Choose a local ${scope} Skill folder…`;
        if (scope === 'global') {
          const result = await callAgentHost('skills.install', {});
          if (result?.cancelled) { agentSkillsStatus.textContent = 'Installation cancelled.'; return; }
          showToast(`Installed global Skill '${result.name}'.`);
        } else {
          const metadata = await installProjectAgentSkill();
          showToast(`Installed project Skill '${metadata.name}'.`);
        }
        await refreshAgentSkillManager();
      } catch (error) {
        if (error?.name !== 'AbortError') showToast(`Could not install Skill: ${error?.message || error}`);
        agentSkillsStatus.textContent = `Skill installation did not complete: ${error?.message || error}`;
      }
    }
    async function removeManagedAgentSkill() {
      const name = agentSkillSelect.value;
      if (!managedAgentSkills.has(name) || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(name)) return;
      const scope = agentSkillScope.value === 'project' ? 'project' : 'global';
      if (!window.confirm(`Remove the ${scope} Skill '${name}'? Its folder and resources will be deleted. This does not remove another installation in the other scope.`)) return;
      try {
        if (scope === 'global') await callAgentHost('skills.remove', { name });
        else {
          if (!agentDirectoryHandle) throw new Error('The project workspace is no longer connected. Reconnect it before removing project Skills.');
          const agents = await agentDirectoryHandle.getDirectoryHandle('.agents');
          const skills = await agents.getDirectoryHandle('skills');
          await skills.removeEntry(name, { recursive: true });
        }
        const otherScope = scope === 'global' ? 'project' : 'global';
        const stillInstalledElsewhere = (await discoverAgentSkills()).some(item => item.name === name && item.scope === otherScope);
        if (!stillInstalledElsewhere) workspaceContext.enabledSkills = workspaceContext.enabledSkills.filter(item => item !== name);
        showToast(`Removed ${scope} Skill '${name}'.`);
        await refreshAgentSkillManager();
      } catch (error) { showToast(`Could not remove Skill: ${error?.message || error}`); }
    }
    async function discoverAgentSkills(signal) {
      const skills = [];
      if (window.chrome?.webview) {
        try { const result = await callAgentHost('skills.list', {}, signal); skills.push(...(result?.skills || [])); } catch (_) {}
      }
      if (agentDirectoryHandle) {
        try {
          const agents = await agentDirectoryHandle.getDirectoryHandle('.agents');
          const skillRoot = await agents.getDirectoryHandle('skills');
          for await (const [name, handle] of skillRoot.entries()) {
            if (handle.kind !== 'directory' || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(name) || skills.length >= 300) continue;
            try {
              const fileHandle = await handle.getFileHandle('SKILL.md');
              const file = await fileHandle.getFile(); if (file.size > 128 * 1024) continue;
              const content = await file.text();
              const metadata = parseAgentSkillFrontmatter(content);
              if (metadata.name === name && metadata.description) skills.push({ ...metadata, scope: 'project' });
            } catch (_) {}
          }
        } catch (_) {}
      }
      return skills;
    }
    function parseAgentSkillFrontmatter(content) {
      if (!String(content || '').startsWith('---')) return { name: '', description: '' };
      const end = content.indexOf('\n---', 3); if (end < 0 || end > 4096) return { name: '', description: '' };
      const lines = content.slice(3, end).split(/\r?\n/);
      let name = ''; let description = '';
      for (let index = 0; index < lines.length; index++) {
        const match = lines[index].match(/^\s*([a-zA-Z0-9_-]+):\s*(.*)$/);
        if (!match) continue;
        if (match[1] === 'name') name = match[2].trim().replace(/^['"]|['"]$/g, '');
        if (match[1] === 'description') {
          const value = match[2].trim();
          if (/^[>|][+-]?$/.test(value)) {
            const block = [];
            for (let next = index + 1; next < lines.length; next++) {
              const continuation = lines[next];
              if (continuation && !/^\s/.test(continuation)) break;
              block.push(continuation.trim());
            }
            description = value.startsWith('>') ? block.filter(Boolean).join(' ') : block.join('\n').trim();
          } else description = value.replace(/^['"]|['"]$/g, '');
        }
      }
      return { name: /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(name) && name.length <= 64 ? name : '', description: description.slice(0, 1000) };
    }
    async function loadAgentSkill(name, scope, references = [], signal) {
      if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(String(name || ''))) throw new Error('Invalid Agent Skill name.');
      if (scope === 'builtin' || scope === 'global') {
        const loaded = await callAgentHost('skills.load', { name, scope, references }, signal);
        workspaceContext.enabledSkills = [...new Set([...workspaceContext.enabledSkills, name])].slice(0, 50);
        agentSkillsStatus.textContent = `${workspaceContext.enabledSkills.length} skill(s) loaded in this conversation.`;
        return loaded;
      }
      if (scope !== 'project' || !agentDirectoryHandle) throw new Error('Project Skills require an authorized workspace folder.');
      const root = await agentDirectoryHandle.getDirectoryHandle('.agents');
      const skills = await root.getDirectoryHandle('skills');
      const directory = await skills.getDirectoryHandle(name);
      const readSafe = async relative => {
        const parts = String(relative || '').replace(/\\/g, '/').split('/');
        if (!parts.length || parts.some(part => !part || part === '.' || part === '..' || part.startsWith('.'))) throw new Error('Skill reference paths must be safe relative paths.');
        let current = directory;
        for (const part of parts.slice(0, -1)) current = await current.getDirectoryHandle(part);
        const handle = await current.getFileHandle(parts[parts.length - 1]);
        const file = await handle.getFile(); if (file.size > 128 * 1024) throw new Error(`Skill file '${relative}' exceeds 128 KB.`);
        return await file.text();
      };
      const instructions = await readSafe('SKILL.md');
      const parsed = parseAgentSkillFrontmatter(instructions);
      if (parsed.name !== name || !parsed.description) throw new Error('The project skill has invalid or mismatched Agent Skills frontmatter.');
      const loadedReferences = [];
      for (const reference of references.slice(0, 10)) loadedReferences.push({ path: reference, content: await readSafe(reference) });
      workspaceContext.enabledSkills = [...new Set([...workspaceContext.enabledSkills, name])].slice(0, 50);
      agentSkillsStatus.textContent = `${workspaceContext.enabledSkills.length} skill(s) loaded in this conversation.`;
      return { name, scope: 'project', instructions: instructions.slice(0, 128 * 1024), references: loadedReferences };
    }
    function normalizeSearchEndpoint(value) {
      const raw = String(value || '').trim();
      if (!raw) return '';
      try {
        const parsed = new URL(raw);
        if (!['http:', 'https:'].includes(parsed.protocol) || !parsed.hostname || parsed.username || parsed.password) return '';
        if ([...parsed.searchParams.keys()].some(key => /(?:token|secret|password|api[_-]?key|credential|authorization|signature|access[_-]?key)/i.test(key))) return '';
        if (parsed.hostname.toLowerCase() === 'localhost') parsed.hostname = '127.0.0.1';
        // SearXNG is mounted at the instance root; remove a trailing slash only.
        return parsed.href.replace(/\/$/, '');
      } catch (_) { return ''; }
    }
    function normalizeReaderEndpoint(value, allowEmpty = true) {
      const raw = String(value || '').trim();
      if (!raw) return allowEmpty ? '' : DEFAULT_JINA_READER_URL;
      try {
        const parsed = new URL(raw);
        if (!['http:', 'https:'].includes(parsed.protocol) || !parsed.hostname || parsed.username || parsed.password || parsed.search || parsed.hash) return '';
        if (parsed.hostname.toLowerCase() === 'localhost') parsed.hostname = '127.0.0.1';
        return parsed.href.replace(/\/+$/, '');
      } catch (_) { return ''; }
    }
    function normalizeMcpEndpoint(value) {
      const parsed = new URL(String(value || '').trim());
      if (!['http:', 'https:'].includes(parsed.protocol) || !parsed.hostname || parsed.username || parsed.password) throw new Error('MCP endpoints must use HTTP or HTTPS and must not embed credentials.');
      if ([...parsed.searchParams.keys()].some(key => /(?:token|api[_-]?key|secret|password|passwd|auth|session|signature|access[_-]?key|access[_-]?token|client[_-]?secret|code)/i.test(key)))
        throw new Error('MCP endpoint URLs must not contain credential-like query parameters. Enter bearer credentials in the token field instead.');
      if (parsed.hostname.toLowerCase() === 'localhost') parsed.hostname = '127.0.0.1';
      return parsed.href;
    }
    function getConfiguredSearchEndpoint() {
      return normalizeSearchEndpoint(loadAgentSettings().searchUrl);
    }
    function saveAgentSettings() {
      const rawSearchUrl = agentSearchUrl.value.trim();
      const searchUrl = normalizeSearchEndpoint(rawSearchUrl);
      if (rawSearchUrl && !searchUrl) {
        showToast('Search is not enabled: enter a valid HTTP or HTTPS SearXNG instance URL.');
        return;
      }
      const rawReaderUrl = agentReaderBaseUrl.value.trim();
      const readerBaseUrl = normalizeReaderEndpoint(rawReaderUrl, false);
      if (rawReaderUrl && !readerBaseUrl) {
        showToast('Enter a valid Reader endpoint using HTTP or HTTPS, without credentials, query strings, or fragments.');
        return;
      }
      const rawCustomReaderUrl = agentCustomReaderUrl.value.trim();
      const customReaderUrl = normalizeReaderEndpoint(rawCustomReaderUrl, true);
      if (rawCustomReaderUrl && !customReaderUrl) {
        showToast('Enter a valid custom gateway URL using HTTP or HTTPS, without credentials, query strings, or fragments.');
        return;
      }
      const settings = { ...loadAgentSettings(), searchUrl, readerBaseUrl: readerBaseUrl || DEFAULT_JINA_READER_URL, customReaderUrl, jinaApiKey: agentJinaApiKey.value.trim(), customGatewayToken: agentCustomReaderToken.value.trim(), maxSteps: readBoundedNumber(agentMaxSteps, 1, 20, true) || 8, autoApproveReads: !!agentAutoApproveReads.checked, readerFallback: !!agentReaderFallback.checked };
      try {
        workspaceContext.agentSettings = safeWorkspaceAgentSettings(settings);
        localStorage.setItem(AGENT_SETTINGS_KEY, JSON.stringify(settings));
        agentSearchUrl.value = searchUrl;
        agentReaderBaseUrl.value = settings.readerBaseUrl;
        agentCustomReaderUrl.value = customReaderUrl;
        agentJinaApiKey.value = settings.jinaApiKey;
        agentCustomReaderToken.value = settings.customGatewayToken;
        updateReaderCredentialStatus();
        showToast('Agent and web retrieval settings saved.');
      } catch (_) { showToast('Could not save Agent settings in this WebView profile.'); }
    }
    function saveAlwaysApprovedAgentTools() {
      try { localStorage.setItem(AGENT_ALWAYS_APPROVED_KEY, JSON.stringify([...alwaysApprovedAgentTools].sort())); }
      catch (_) { showToast('Could not save always-approved tools in this WebView profile.'); }
      renderAlwaysApprovedAgentTools();
    }
    function renderAlwaysApprovedAgentTools() {
      if (!alwaysApprovedToolsList) return;
      const names = [...alwaysApprovedAgentTools].sort();
      alwaysApprovedToolsList.replaceChildren();
      if (!names.length) {
        alwaysApprovedToolsList.textContent = 'None';
      } else {
        const rows = document.createElement('div');
        rows.className = 'always-approved-tools';
        for (const name of names) {
          const row = document.createElement('div');
          row.className = 'always-approved-tool-row';
          const label = document.createElement('span');
          label.className = 'always-approved-tool-name';
          label.textContent = name;
          const remove = document.createElement('button');
          remove.type = 'button';
          remove.className = 'always-approved-tool-remove';
          remove.textContent = 'Remove';
          remove.title = `Remove ${name} from always-approved tools`;
          remove.setAttribute('aria-label', `Remove ${name} from always-approved tools`);
          remove.addEventListener('click', () => {
            if (!alwaysApprovedAgentTools.delete(name)) return;
            saveAlwaysApprovedAgentTools();
            showToast(`Always approval removed for ${name}.`);
          });
          row.append(label, remove);
          rows.appendChild(row);
        }
        alwaysApprovedToolsList.appendChild(rows);
      }
      clearAlwaysApprovedToolsButton.disabled = names.length === 0;
    }
    function applyAgentSettings() {
      const settings = loadAgentSettings();
      agentSearchUrl.value = settings.searchUrl || '';
      agentReaderBaseUrl.value = normalizeReaderEndpoint(settings.readerBaseUrl || DEFAULT_JINA_READER_URL, false);
      agentCustomReaderUrl.value = settings.customReaderUrl || '';
      agentJinaApiKey.value = settings.jinaApiKey || '';
      agentCustomReaderToken.value = settings.customGatewayToken || '';
      agentMaxSteps.value = String(settings.maxSteps || 8);
      agentAutoApproveReads.checked = !!settings.autoApproveReads;
      agentReaderFallback.checked = settings.readerFallback !== false;
      updateReaderCredentialStatus();
      renderAlwaysApprovedAgentTools();
    }
    function setAgentMode(enabled) {
      if (enabled && deepResearchEnabled) setDeepResearchMode(false);
      agentModeEnabled = !!enabled;
      agentModeButton.classList.toggle('active', agentModeEnabled);
      agentModeButton.setAttribute('aria-pressed', String(agentModeEnabled));
      agentModeButton.title = agentModeEnabled ? 'Agent Mode is on — Deep Research is off; model may call tools' : 'Agent Mode is off';
      agentModeButton.setAttribute('aria-label', agentModeButton.title);
      promptInput.placeholder = agentModeEnabled ? 'Ask the agent to research, calculate, or work with a selected folder…' : 'Message the selected local model…';
      updateAgentFolderButton();
      if (typeof updateMcpModeButton === 'function') updateMcpModeButton();
    }

