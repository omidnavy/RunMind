(function (root) {
  'use strict';
  const SECRET_KEY = /(?:password|passwd|secret|token|authorization|api[_-]?key|credential|client[_-]?secret|access[_-]?key|private[_-]?key|cookie|set-cookie)/i;
  const SECRET_TEXT = [
    /(\bBearer\s+)[A-Za-z0-9._~+\/-]+=*/gi,
    /((?:api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|password|passwd|token)\s*[:=]\s*)(["']?)[^\s,;"']+\2/gi,
    /\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,})\b/g,
    /([?&](?:token|key|secret|signature|password|credential)=)[^&#\s]+/gi
  ];
  function redactText(value, limit = 3000) {
    let text = String(value ?? '').slice(0, Math.max(0, limit));
    for (const pattern of SECRET_TEXT) text = text.replace(pattern, (match, prefix) => `${prefix || ''}[REDACTED]`);
    return text;
  }
  function compactValue(value, depth = 0, budget = { count: 0 }) {
    budget.count += 1;
    if (budget.count > 1600) return '[additional values omitted]';
    if (value == null || typeof value === 'boolean' || typeof value === 'number') return value;
    if (typeof value === 'string') return redactText(value, 1600);
    if (typeof value !== 'object') return redactText(String(value), 300);
    if (depth >= 6) return '[maximum summary depth reached]';
    if (Array.isArray(value)) {
      const selected = value.slice(0, 18).map(item => compactValue(item, depth + 1, budget));
      if (value.length > selected.length) selected.push(`[${value.length - selected.length} additional item(s) omitted]`);
      return selected;
    }
    const result = {};
    for (const [key, entry] of Object.entries(value).slice(0, 48)) {
      if (SECRET_KEY.test(key)) { result[key] = '[REDACTED]'; continue; }
      if (/^(?:dataUrl|imageData|base64|binary|rawBody|requestBody|responseBody|html|documentHtml)$/i.test(key)) { result[key] = '[content omitted from tool summary]'; continue; }
      if (/^stack$/i.test(key)) { result[key] = redactText(entry, 4000); continue; }
      result[String(key).slice(0, 100)] = compactValue(entry, depth + 1, budget);
    }
    const remaining = Object.keys(value).length - Object.keys(result).length;
    if (remaining > 0) result._omittedProperties = remaining;
    return result;
  }
  function summarizeToolResultForContext(toolName, value, maxChars = 9000) {
    const safe = compactValue(value);
    let serialized;
    try { serialized = JSON.stringify({ tool: String(toolName || 'tool').slice(0, 100), result: safe }); }
    catch (_) { serialized = JSON.stringify({ tool: String(toolName || 'tool').slice(0, 100), result: '[unserializable result]' }); }
    if (serialized.length <= maxChars) return serialized;
    const previewLength = Math.max(256, maxChars - 220);
    return JSON.stringify({ tool: String(toolName || 'tool').slice(0, 100), resultPreview: redactText(serialized, previewLength), truncated: true, originalCharacters: serialized.length, omittedCharacters: Math.max(0, serialized.length - previewLength) });
  }
  function planToolBatchExecution(calls, canRunParallel, concurrency = 3) {
    const rows = Array.isArray(calls) ? calls : [];
    const safe = Array.isArray(canRunParallel) ? canRunParallel : [];
    const limit = Math.max(1, Math.min(4, Math.floor(Number(concurrency) || 1)));
    const signatures = rows.map(call => `${call?.name || ''}:${(() => { try { return JSON.stringify(call?.arguments || {}); } catch (_) { return ''; } })()}`);
    const duplicate = new Set(signatures.filter((item, index) => signatures.indexOf(item) !== index));
    const groups = [];
    let cursor = 0;
    while (cursor < rows.length) {
      if (safe[cursor] && !duplicate.has(signatures[cursor]) && limit > 1) {
        const indexes = [];
        while (cursor < rows.length && safe[cursor] && !duplicate.has(signatures[cursor])) indexes.push(cursor++);
        groups.push({ id: `p${groups.length + 1}`, indexes, mode: indexes.length > 1 ? 'parallel' : 'serialized', concurrency: Math.min(limit, indexes.length), barrier: false });
      } else {
        groups.push({ id: `b${groups.length + 1}`, indexes: [cursor++], mode: 'serialized', concurrency: 1, barrier: true });
      }
    }
    return { groups, duplicates: [...duplicate].length, maxConcurrency: limit, layers: groups.length };
  }
  function normalizeCheckpoint(value) {
    if (!value || typeof value !== 'object') return null;
    const id = String(value.id || '');
    if (!/^[a-f0-9-]{36}$/i.test(id)) return null;
    const allowed = new Set(['running', 'interrupted', 'needs_recovery', 'completed', 'cancelled', 'discarded']);
    return {
      id,
      task: redactText(value.task, 1800),
      workspaceId: String(value.workspaceId || '').slice(0, 100),
      status: allowed.has(value.status) ? value.status : 'interrupted',
      stage: redactText(value.stage || 'created', 100),
      summary: redactText(value.summary || '', 1400),
      plan: Array.isArray(value.plan) ? value.plan.slice(0, 12).map(item => ({ step: redactText(item?.step, 180), status: ['pending', 'in_progress', 'completed'].includes(item?.status) ? item.status : 'pending' })) : [],
      operationIds: Array.isArray(value.operationIds) ? [...new Set(value.operationIds.filter(item => /^[a-f0-9-]{36}$/i.test(String(item))))].slice(0, 60) : [],
      resumedFrom: /^[a-f0-9-]{36}$/i.test(String(value.resumedFrom || '')) ? String(value.resumedFrom) : '',
      createdAt: String(value.createdAt || '').slice(0, 40),
      updatedAt: String(value.updatedAt || '').slice(0, 40),
      completedAt: String(value.completedAt || '').slice(0, 40)
    };
  }
  root.RunMindP4 = Object.freeze({ redactText, compactValue, summarizeToolResultForContext, planToolBatchExecution, normalizeCheckpoint });
})(globalThis);
