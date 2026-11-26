'use strict';

const PROJECT_DEVELOPMENT_MEMORY_PREFIX = 'runmind.project-development-memory.v1.';
const PROJECT_DEVELOPMENT_MEMORY_LIMITS = Object.freeze({ decisions: 60, runs: 120, checkpoints: 20, ownedChanges: 100, characters: 320000 });
let projectDevelopmentIdentity = null;

async function projectDevelopmentEnsureIdentity(signal = null) {
  // Refresh the native fingerprint every time: the selected workspace can change without changing the chat context ID.
  if (!window.chrome?.webview || !workspaceContext?.id) { projectDevelopmentIdentity = null; return { available: false, reason: 'A selected native workspace is required for cross-session project memory.' }; }
  try {
    const status = await callAgentHost('process.workspace.status', { contextId: workspaceContext.id }, signal, 10000);
    if (!status?.selected || !/^[a-f0-9]{64}$/i.test(String(status.workspaceFingerprint || ''))) { projectDevelopmentIdentity = null; return { available: false, reason: 'The native workspace is not selected or did not provide a stable workspace fingerprint.' }; }
    projectDevelopmentIdentity = { available: true, contextId: String(workspaceContext.id), key: String(status.workspaceFingerprint).toLowerCase(), folder: String(status.folder || workspaceContext.folderName || 'workspace').slice(0, 120) };
    return projectDevelopmentIdentity;
  } catch (error) {
    projectDevelopmentIdentity = null;
    return { available: false, reason: String(error?.message || error).slice(0, 240) };
  }
}

function projectDevelopmentMemoryKey() {
  return projectDevelopmentIdentity?.key ? `${PROJECT_DEVELOPMENT_MEMORY_PREFIX}${projectDevelopmentIdentity.key}` : '';
}

function projectDevelopmentLoadMemory() {
  const key = projectDevelopmentMemoryKey();
  if (!key) return null;
  try {
    const value = JSON.parse(localStorage.getItem(key) || 'null');
    if (!value || value.schemaVersion !== 1 || value.workspaceFingerprint !== projectDevelopmentIdentity.key) return null;
    return {
      schemaVersion: 1,
      workspaceFingerprint: projectDevelopmentIdentity.key,
      folder: String(value.folder || projectDevelopmentIdentity.folder || 'workspace').slice(0, 120),
      updatedAt: String(value.updatedAt || ''),
      decisions: Array.isArray(value.decisions) ? value.decisions.slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.decisions) : [],
      runs: Array.isArray(value.runs) ? value.runs.slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.runs) : [],
      checkpoints: Array.isArray(value.checkpoints) ? value.checkpoints.slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.checkpoints) : [],
      ownedChanges: Array.isArray(value.ownedChanges) ? value.ownedChanges.slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.ownedChanges) : []
    };
  } catch (_) { return null; }
}

function projectDevelopmentSaveMemory(memory) {
  const key = projectDevelopmentMemoryKey();
  if (!key || !memory) return false;
  const value = {
    schemaVersion: 1,
    workspaceFingerprint: projectDevelopmentIdentity.key,
    folder: String(memory.folder || projectDevelopmentIdentity.folder || 'workspace').slice(0, 120),
    updatedAt: new Date().toISOString(),
    decisions: (Array.isArray(memory.decisions) ? memory.decisions : []).slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.decisions),
    runs: (Array.isArray(memory.runs) ? memory.runs : []).slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.runs),
    checkpoints: (Array.isArray(memory.checkpoints) ? memory.checkpoints : []).slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.checkpoints),
    ownedChanges: (Array.isArray(memory.ownedChanges) ? memory.ownedChanges : []).slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.ownedChanges)
  };
  try {
    let serialized = JSON.stringify(value);
    while (serialized.length > PROJECT_DEVELOPMENT_MEMORY_LIMITS.characters && (value.runs.length || value.checkpoints.length || value.ownedChanges.length || value.decisions.length)) {
      if (value.runs.length) value.runs.shift();
      else if (value.checkpoints.length) value.checkpoints.shift();
      else if (value.ownedChanges.length) value.ownedChanges.shift();
      else value.decisions.shift();
      serialized = JSON.stringify(value);
    }
    localStorage.setItem(key, serialized);
    return true;
  } catch (_) { return false; }
}

function projectDevelopmentRedact(value, maximum = 1200) {
  const text = String(value || '');
  const safe = typeof globalThis.RunMindP4?.redactText === 'function' ? globalThis.RunMindP4.redactText(text, maximum) : text.slice(0, maximum);
  return safe.replace(/(Bearer\s+)[^\s]+/gi, '$1[REDACTED]')
    .replace(/((?:token|secret|password|passwd|api[_-]?key|authorization|client[_-]?secret)\s*[:=]\s*)[^\s,;&}]+/gi, '$1[REDACTED]')
    .replace(/([?&](?:token|secret|password|api[_-]?key|authorization|credential|session|code)=)[^&#\s]+/gi, '$1[REDACTED]');
}

function projectDevelopmentHashText(value) {
  if (!globalThis.crypto?.subtle || typeof TextEncoder !== 'function') throw new Error('SHA-256 is unavailable in this WebView context.');
  return crypto.subtle.digest('SHA-256', new TextEncoder().encode(String(value ?? '')))
    .then(bytes => [...new Uint8Array(bytes)].map(value => value.toString(16).padStart(2, '0')).join(''));
}

async function projectDevelopmentHashBlob(file) {
  if (!globalThis.crypto?.subtle || !file || typeof file.arrayBuffer !== 'function') throw new Error('SHA-256 file hashing is unavailable.');
  const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
  return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('');
}

function projectDevelopmentParseGitStatus(snapshot) {
  const lines = String(snapshot?.status || '').split(/\r?\n/).filter(Boolean);
  const branchLine = lines.find(line => line.startsWith('## ')) || '';
  const files = [];
  for (const line of lines) {
    if (line.startsWith('## ') || line.length < 4) continue;
    const x = line[0], y = line[1];
    const rawPath = line.slice(3);
    const rename = rawPath.split(' -> ');
    const path = (rename.length > 1 ? rename.at(-1) : rawPath).replace(/^"|"$/g, '');
    files.push({ path, originalPath: rename.length > 1 ? rename[0].replace(/^"|"$/g, '') : null, indexStatus: x, worktreeStatus: y,
      staged: x !== ' ' && x !== '?', unstaged: y !== ' ' && y !== '?', untracked: x === '?' && y === '?',
      conflicted: x === 'U' || y === 'U' || (x === 'A' && y === 'A') || (x === 'D' && y === 'D') });
  }
  const unique = new Map(files.map(item => [item.path.toLowerCase(), item]));
  for (const path of Array.isArray(snapshot?.untrackedPaths) ? snapshot.untrackedPaths : []) {
    const normalized = String(path || '').replace(/\\/g, '/');
    if (normalized && !unique.has(normalized.toLowerCase())) unique.set(normalized.toLowerCase(), { path: normalized, indexStatus: '?', worktreeStatus: '?', staged: false, unstaged: false, untracked: true, conflicted: false });
  }
  const rows = [...unique.values()];
  return {
    branch: String(snapshot?.branch || branchLine.replace(/^##\s*/, '').split('...')[0] || 'unknown'),
    branchStatus: branchLine,
    files: rows,
    changedPaths: [...new Set(rows.flatMap(item => [item.path, item.originalPath].filter(Boolean)))].slice(0, 500),
    conflictedPaths: [...new Set([...rows.filter(item => item.conflicted).map(item => item.path), ...(snapshot?.conflictPaths || [])])].slice(0, 200),
    stagedCount: rows.filter(item => item.staged).length,
    unstagedCount: rows.filter(item => item.unstaged).length,
    untrackedCount: rows.filter(item => item.untracked).length,
    conflictCount: rows.filter(item => item.conflicted).length,
    aheadBehind: (branchLine.match(/\[(.*?)\]/)?.[1] || '').slice(0, 120)
  };
}

function projectDevelopmentDiffPaths(diff) {
  const output = [];
  for (const line of String(diff || '').split(/\r?\n/)) if (line.startsWith('+++ b/')) output.push(line.slice(6));
  return [...new Set(output)].slice(0, 500);
}

function projectDevelopmentAnalyzeGitSnapshot(index, snapshot) {
  const current = snapshot?.current || snapshot || {};
  const status = projectDevelopmentParseGitStatus(current);
  const staged = projectIndexParseUnifiedDiff(current?.stagedDiff || '');
  const unstaged = projectIndexParseUnifiedDiff(current?.unstagedDiff || '');
  const branch = projectIndexParseUnifiedDiff(snapshot?.branchComparison?.diff || '');
  const parsedFiles = new Map();
  const mergeDiff = parsed => {
    for (const file of parsed?.files || []) {
      const key = String(file.path || file.oldPath || '').toLowerCase();
      if (!key) continue;
      const existing = parsedFiles.get(key) || { path: file.path, oldPath: file.oldPath, status: file.status, addedLines: [], removedLines: [], changedLineRanges: [], binary: false };
      existing.path = file.path || existing.path;
      existing.oldPath = file.oldPath || existing.oldPath;
      if (file.status && file.status !== 'modified') existing.status = file.status;
      existing.addedLines.push(...(file.addedLines || []));
      existing.removedLines.push(...(file.removedLines || []));
      existing.changedLineRanges.push(...(file.changedLineRanges || []));
      existing.binary ||= file.binary === true;
      parsedFiles.set(key, existing);
    }
  };
  mergeDiff(branch);
  mergeDiff(staged);
  mergeDiff(unstaged);
  const indexedByPath = new Map((index?.files || []).map(file => [file.path.toLowerCase(), file]));
  // Untracked files are intentionally absent from Git's unified diff. The project index has their content,
  // so treat their complete declaration ranges as new code rather than silently omitting symbol impact.
  for (const row of status.files.filter(item => item.untracked)) {
    const key = row.path.toLowerCase();
    if (parsedFiles.has(key)) continue;
    const record = indexedByPath.get(key);
    const lineCount = record?.analysisSource ? String(record.analysisSource).split(/\r?\n/).length : 0;
    parsedFiles.set(key, { path: row.path, oldPath: row.path, status: 'untracked', addedLines: [], removedLines: [], changedLineRanges: lineCount ? [{ start: 1, end: lineCount, kind: 'added' }] : [], binary: false, wholeFile: true });
  }
  const combinedDiff = {
    files: [...parsedFiles.values()],
    changedPaths: [...new Set([...status.changedPaths, ...branch.changedPaths, ...staged.changedPaths, ...unstaged.changedPaths])].slice(0, 1000),
    addedLineCount: branch.addedLineCount + staged.addedLineCount + unstaged.addedLineCount,
    removedLineCount: branch.removedLineCount + staged.removedLineCount + unstaged.removedLineCount,
    binaryFiles: [...new Set([...branch.binaryFiles, ...staged.binaryFiles, ...unstaged.binaryFiles])]
  };
  const changedPaths = combinedDiff.changedPaths.slice(0, 1000);
  const inferred = index ? projectIndexInferChangedSymbols(index, combinedDiff) : { symbols: [], removedCandidates: [] };
  let impact = null;
  if (index && changedPaths.length) {
    try {
      impact = projectIndexAnalyzeChangeImpact(index, changedPaths, inferred.symbols, {
        source: snapshot?.branchComparison ? 'git-working-tree-and-branch' : 'git-working-tree',
        diffLineRanges: combinedDiff.files.map(file => ({ path: file.path, ranges: file.changedLineRanges || [], removedLines: file.removedLines || [] })),
        git: { branch: status.branch, baseRef: snapshot?.branchComparison?.baseRef || null, branchComparisonAvailable: !!snapshot?.branchComparison, branchComparisonTruncated: snapshot?.branchComparison?.truncated === true }
      });
    } catch (error) { impact = { available: false, message: String(error?.message || error).slice(0, 300) }; }
  }
  const diffPaths = [...new Set(combinedDiff.files.map(file => file.path).filter(Boolean))].slice(0, 1000);
  const testImpact = impact?.testImpact || null;
  return {
    available: true,
    branch: status.branch,
    baseRef: snapshot?.branchComparison?.baseRef || null,
    status: status.branchStatus,
    changedFiles: status.files,
    changedPaths,
    diffPaths,
    changedSymbols: impact?.changedSymbols || inferred.symbols,
    removedSymbolCandidates: inferred.removedCandidates,
    diffSummary: { files: combinedDiff.files.length, addedLines: combinedDiff.addedLineCount, removedLines: combinedDiff.removedLineCount, binaryFiles: combinedDiff.binaryFiles },
    summary: { changed: changedPaths.length, staged: status.stagedCount, unstaged: status.unstagedCount, untracked: status.untrackedCount, conflicted: status.conflictCount,
      directDependents: impact?.summary?.directDependents || 0, transitiveDependents: impact?.summary?.transitiveDependents || 0,
      affectedTests: testImpact?.summary?.recommendedTests || 0, affectedAssets: impact?.summary?.assetConsumers || 0,
      riskScore: impact?.risk?.score ?? null, confidenceScore: impact?.confidence?.score ?? null },
    conflicts: { paths: status.conflictedPaths, count: status.conflictedPaths.length, unmergedIndexEntries: Array.isArray(current?.conflictIndexEntries) ? current.conflictIndexEntries.slice(0, 100) : [] },
    whitespace: current?.diffCheck || { exitCode: null, output: '' },
    architectureImpact: impact,
    testImpact,
    diffs: { staged: String(current?.stagedDiff || '').slice(0, 220000), unstaged: String(current?.unstagedDiff || '').slice(0, 220000), branch: String(snapshot?.branchComparison?.diff || '').slice(0, 220000) },
    branchComparison: snapshot?.branchComparison ? { baseRef: snapshot.branchComparison.baseRef, mergeBase: snapshot.branchComparison.mergeBase || null, truncated: snapshot.branchComparison.truncated === true } : null,
    limitations: [
      'Git status, staged/unstaged tracked diffs and optional base-ref comparison are repository evidence. Untracked file contents are read from the already-authorized project index; Git itself does not include them in unified diffs.',
      'Architecture and test relationships are derived from the current bounded static project index, not runtime execution.',
      'A Git status snapshot cannot attribute pre-existing changes to RunMind. Agent-owned changes are tracked separately when RunMind writes them.',
      'Merge conflict status is detected from Git index/status evidence; conflict markers in arbitrary files are not exhaustively searched.',
      'Istanbul/LCOV coverage provides line execution counts, not individual test-to-line attribution. Test recommendations therefore combine import graph, asset links and explicitly labeled filename heuristics.'
    ]
  };
}

function projectDevelopmentPackageRecords(index) {
  return (Array.isArray(index?.manifests) ? index.manifests : []).filter(item => /(^|\/)package\.json$/i.test(item.path || '')).map(item => {
    try { const data = JSON.parse(item.text); return { path: item.path, data }; }
    catch (_) { return { path: item.path, data: null, invalid: true }; }
  });
}

function projectDevelopmentBuildTestAnalysis(index, memory, ciResults = []) {
  const manifests = projectDevelopmentPackageRecords(index);
  const packages = manifests.filter(item => item.data);
  const scripts = packages.flatMap(item => Object.entries(item.data.scripts || {}).map(([name, command]) => ({ packagePath: item.path, name, command: String(command).slice(0, 500), category: /test|spec|vitest|jest|mocha|playwright|tap/i.test(name + ' ' + command) ? 'test' : /build|bundle|compile|tsc|vite|webpack|rollup|esbuild/i.test(name + ' ' + command) ? 'build' : /lint|eslint|prettier|format|typecheck|check/i.test(name + ' ' + command) ? 'quality' : 'other' })));
  const files = Array.isArray(index?.files) ? index.files : [];
  const testFiles = files.filter(file => /(^|\/)(?:__tests__\/.*|.*\.(?:test|spec)\.(?:[cm]?[jt]sx?))$/i.test(file.path)).map(file => file.path).slice(0, 500);
  const ciFiles = files.filter(file => /(^|\/)(?:\.github\/workflows\/[^/]+\.(?:ya?ml)|\.gitlab-ci\.ya?ml|azure-pipelines\.ya?ml|\.circleci\/config\.ya?ml)$/i.test(file.path)).map(file => ({ path: file.path,
    provider: file.path.includes('.github/workflows/') ? 'GitHub Actions' : file.path.endsWith('.gitlab-ci.yml') || file.path.endsWith('.gitlab-ci.yaml') ? 'GitLab CI' : file.path.includes('.circleci/') ? 'CircleCI' : 'Azure Pipelines',
    jobNames: [...String(file.text || '').matchAll(/^\s{0,4}([A-Za-z0-9_.-]+):\s*$/gm)].map(match => match[1]).filter(name => !['name','on','permissions','env','defaults','concurrency'].includes(name)).slice(0, 40) }));
  const dependencies = packages.flatMap(item => Object.keys({ ...(item.data.dependencies || {}), ...(item.data.devDependencies || {}) }));
  const referenced = new Set(scripts.flatMap(script => [...script.command.matchAll(/\b(vitest|jest|mocha|playwright|eslint|prettier|tsc|vite|webpack|rollup|esbuild)\b/gi)].map(match => match[1].toLowerCase())));
  const dependencyNames = new Set(dependencies.map(value => value.toLowerCase()));
  const toolingSignals = [...referenced].map(name => ({ name, declared: dependencyNames.has(name) || (name === 'tsc' && dependencyNames.has('typescript')), note: dependencyNames.has(name) || (name === 'tsc' && dependencyNames.has('typescript')) ? 'Declared in package metadata.' : 'Referenced by a script but not declared as a direct dependency; it may be provided by a workspace, package manager, or global installation.' }));
  const runs = Array.isArray(memory?.runs) ? memory.runs : [];
  const recent = runs.slice(-80);
  const groups = new Map();
  for (const run of recent) {
    const key = `${run.kind}|${run.commandKey}`;
    const row = groups.get(key) || { kind: run.kind, command: run.command, runs: [], failures: new Map(), successCount: 0, failureCount: 0 };
    row.runs.push(run);
    if (run.success) row.successCount++; else { row.failureCount++; const item = row.failures.get(run.failureSignature) || { signature: run.failureSignature, count: 0, excerpt: run.excerpt }; item.count++; row.failures.set(run.failureSignature, item); }
    groups.set(key, row);
  }
  const runHistory = [...groups.values()].map(row => {
    const environmentGroups = new Map();
    for (const run of row.runs) {
      if (!run.environment) continue;
      const key = JSON.stringify(run.environment);
      const environment = environmentGroups.get(key) || { environment: run.environment, attempts: 0, successCount: 0, failureCount: 0 };
      environment.attempts++;
      if (run.success) environment.successCount++; else environment.failureCount++;
      environmentGroups.set(key, environment);
    }
    const environmentOutcomes = [...environmentGroups.values()].slice(0, 10);
    const outcomePatterns = new Set(environmentOutcomes.map(item => `${item.successCount > 0 ? 'pass' : ''}:${item.failureCount > 0 ? 'fail' : ''}`));
    const environmentOutcomeDiffers = environmentOutcomes.length > 1 && outcomePatterns.size > 1;
    return { kind: row.kind, command: row.command, attempts: row.runs.length, successCount: row.successCount, failureCount: row.failureCount,
      flaky: row.successCount > 0 && row.failureCount > 0,
      recurringFailures: [...row.failures.values()].filter(item => item.count >= 2).map(item => ({ count: item.count, excerpt: item.excerpt })).slice(0, 5),
      environmentVariants: environmentOutcomes, environmentVaried: environmentOutcomes.length > 1, environmentOutcomeDiffers,
      environmentSignal: environmentOutcomeDiffers ? 'Possible environment-sensitive outcome: success/failure patterns differ across recorded runtime or package-manager environments. Investigate; this does not prove causation.' : '',
      latest: row.runs.at(-1) ? { at: row.runs.at(-1).at, success: row.runs.at(-1).success, exitCode: row.runs.at(-1).exitCode, durationMs: row.runs.at(-1).durationMs, environment: row.runs.at(-1).environment || null } : null
    };
  }).slice(0, 50);
  const comparisons = [];
  for (const ci of (Array.isArray(ciResults) ? ciResults : []).slice(0, 100)) {
    const suite = String(ci?.suite || ci?.script || '').trim().slice(0, 120);
    const local = recent.filter(run => run.command === suite || run.scriptName === suite).at(-1);
    const status = String(ci?.status || '').toLowerCase();
    comparisons.push({ suite, ciStatus: status || 'unknown', ciRun: String(ci?.run_id || '').slice(0, 120), completedAt: String(ci?.completed_at || '').slice(0, 40),
      localStatus: local ? (local.success ? 'passed' : 'failed') : 'not-recorded', outcome: !local ? 'no-local-result' : !/^(success|passed|pass|succeeded|failure|failed|error)$/i.test(status) ? 'ci-status-unknown' : (local.success === /^(success|passed|pass|succeeded)$/i.test(status) ? 'match' : 'difference'),
      note: 'CI facts are user-supplied evidence; RunMind does not query a CI provider or infer remote job status.' });
  }
  return {
    available: true,
    packages: packages.map(item => ({ path: item.path, name: String(item.data.name || '').slice(0, 120), packageManager: item.data.packageManager || null, engines: item.data.engines || {}, scripts: Object.keys(item.data.scripts || {}) })),
    scripts: scripts.slice(0, 200),
    scriptSummary: { total: scripts.length, test: scripts.filter(item => item.category === 'test').length, build: scripts.filter(item => item.category === 'build').length, quality: scripts.filter(item => item.category === 'quality').length },
    testFiles,
    toolingSignals,
    ci: { configured: ciFiles, providerCount: new Set(ciFiles.map(item => item.provider)).size, comparisons },
    history: { recordedRuns: recent.length, commands: runHistory, flakyCommands: runHistory.filter(item => item.flaky), recurringFailures: runHistory.filter(item => item.recurringFailures.length), environmentVariantCommands: runHistory.filter(item => item.environmentVaried), environmentSensitiveCommands: runHistory.filter(item => item.environmentOutcomeDiffers) },
    verification: { buildsOrTestsExecutedByThisAnalysis: false, ciQueried: false },
    limitations: ['Local run history begins when RunMind 3.3.0 project execution is used; older runs cannot be reconstructed reliably.', 'Test flakiness is a heuristic: the same recorded command having both successful and failed outcomes across separate runs.', 'Environment snapshots are recorded by approved RunMind script runs only. Different success/failure patterns across captured Node.js, package-manager, OS or PowerShell contexts are an investigation signal, not proof of cause; other variables and installed package state may be missing.', 'CI files are statically identified. Remote CI outcomes are compared only when explicitly supplied as ci_results.', 'Script declarations are inspected; scripts are not executed by this read-only analysis.']
  };
}

function projectDevelopmentDependencyAnalysis(index) {
  const packages = projectDevelopmentPackageRecords(index);
  const files = new Map((Array.isArray(index?.files) ? index.files : []).map(file => [file.path.toLowerCase(), file]));
  const packageLock = files.get('package-lock.json');
  let lock = null;
  if (packageLock) { try { lock = JSON.parse(packageLock.text); } catch (_) {} }
  const findings = [], items = [];
  const managerSignals = index?.toolchains || {};
  if (managerSignals.conflictingPackageManagerSignals) findings.push({ severity: 'error', message: 'package.json packageManager declaration conflicts with detected lockfile manager(s).', evidence: managerSignals.lockfiles || [] });
  if ((managerSignals.lockfiles || []).length > 1 && new Set(managerSignals.lockfiles.map(item => item.manager)).size > 1) findings.push({ severity: 'warning', message: 'Multiple package-manager lockfiles are present; verify which package manager is authoritative.', evidence: managerSignals.lockfiles });
  for (const entry of packages) {
    if (!entry.data) { findings.push({ severity: 'error', packagePath: entry.path, message: 'package.json is not valid JSON.' }); continue; }
    const declarations = { ...(entry.data.dependencies || {}), ...(entry.data.devDependencies || {}), ...(entry.data.optionalDependencies || {}) };
    for (const [name, spec] of Object.entries(declarations)) {
      const resolved = lock?.packages?.[`node_modules/${name}`]?.version || lock?.dependencies?.[name]?.version || null;
      const exact = /^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(String(spec));
      const mismatch = exact && resolved && String(spec).replace(/^v/, '') !== String(resolved).replace(/^v/, '');
      items.push({ name, packagePath: entry.path, declared: String(spec).slice(0, 120), resolved: resolved || null, exactDeclaration: exact, mismatch: !!mismatch });
      if (mismatch) findings.push({ severity: 'warning', packagePath: entry.path, package: name, message: `Exact package.json version ${spec} differs from lockfile resolution ${resolved}.` });
      if (lock && !resolved && !['workspace:', 'file:', 'link:', 'git+', 'https:', 'http:'].some(prefix => String(spec).startsWith(prefix))) findings.push({ severity: 'warning', packagePath: entry.path, package: name, message: 'Direct dependency is not represented in the root package-lock resolution table; verify workspace and nested package lockfile layout.' });
    }
  }
  const lockfiles = [...files.keys()].filter(path => /(^|\/)(package-lock\.json|pnpm-lock\.yaml|yarn\.lock|bun\.lockb?)$/i.test(path));
  const npmAudit = { available: lockfiles.length > 0, lockfile: packageLock ? 'package-lock.json' : lockfiles[0] || null, auditRequired: !!packages.some(item => item.data && Object.keys({ ...(item.data.dependencies || {}), ...(item.data.devDependencies || {}) }).length), vulnerabilityAudit: 'not-run', outdatedCheck: 'not-run' };
  return { available: true, packages: packages.length, dependencies: items.slice(0, 1000), findings: findings.slice(0, 300), summary: { declared: items.length, exactLockMismatches: items.filter(item => item.mismatch).length, findings: findings.length, lockfiles }, audit: npmAudit,
    limitations: ['Static outdated/vulnerability status cannot be established from package metadata and lockfiles alone. Run the selected package manager audit with fresh approval to query configured advisory sources.', 'Exact version mismatches are checked for npm package-lock v2/v3. Other lockfile formats are inventoried but not fully resolved by this static check.', 'Workspace, alias, git, URL and protocol-specific dependency declarations need package-manager-specific interpretation.'] };
}

function projectDevelopmentRecordRun(record) {
  if (!projectDevelopmentIdentity?.key || !record || !['test', 'build', 'audit', 'quality'].includes(record.kind)) return false;
  const memory = projectDevelopmentLoadMemory() || { decisions: [], runs: [], checkpoints: [], ownedChanges: [] };
  const safeCommand = projectDevelopmentRedact(record.command || record.scriptName || record.kind, 140);
  const excerpt = projectDevelopmentRedact(record.excerpt || '', 320);
  const normalized = safeCommand.toLowerCase().replace(/\s+/g, ' ').trim();
  const item = { id: crypto.randomUUID(), at: new Date().toISOString(), kind: record.kind, command: safeCommand, commandKey: normalized,
    scriptName: String(record.scriptName || '').slice(0, 100), success: record.success === true, exitCode: Number.isFinite(Number(record.exitCode)) ? Number(record.exitCode) : null,
    durationMs: Math.max(0, Math.min(3600000, Number(record.durationMs) || 0)), excerpt, environment: record.environment && typeof record.environment === 'object' ? Object.fromEntries(Object.entries(record.environment).slice(0, 8).map(([key, value]) => [String(key).slice(0, 40), projectDevelopmentRedact(value, 100)])) : null,
    failureSignature: record.success ? '' : String(record.failureSignature || excerpt.toLowerCase().replace(/\d+/g, '#').slice(0, 200)).slice(0, 200) };
  memory.runs.push(item);
  return projectDevelopmentSaveMemory(memory);
}

function projectDevelopmentPersistCheckpoints(checkpoints, operations = []) {
  if (!projectDevelopmentIdentity?.key) return false;
  const memory = projectDevelopmentLoadMemory() || { decisions: [], runs: [], checkpoints: [], ownedChanges: [] };
  const current = Array.isArray(checkpoints) ? checkpoints.slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.checkpoints) : [];
  const rows = current.map(item => ({ id: String(item.id || '').slice(0, 80), task: projectDevelopmentRedact(item.task || '', 1800), status: String(item.status || 'unknown').slice(0, 40), stage: String(item.stage || '').slice(0, 80),
    summary: projectDevelopmentRedact(item.summary || '', 600), plan: (Array.isArray(item.plan) ? item.plan : []).slice(0, 40).map(step => ({ step: projectDevelopmentRedact(step.step || '', 300), status: String(step.status || 'pending').slice(0, 30) })),
    operationIds: (Array.isArray(item.operationIds) ? item.operationIds : []).slice(-60), resumedFrom: String(item.resumedFrom || '').slice(0, 80), createdAt: item.createdAt || '', updatedAt: item.updatedAt || '',
    operations: (Array.isArray(operations) ? operations : []).filter(operation => operation.checkpointId === item.id).slice(-60).map(operation => ({ id: String(operation.id || '').slice(0, 80), name: String(operation.name || '').slice(0, 100), status: String(operation.status || '').slice(0, 40), error: projectDevelopmentRedact(operation.error || '', 350), resultPreview: projectDevelopmentRedact(operation.resultPreview || '', 350), startedAt: operation.startedAt || '', finishedAt: operation.finishedAt || '' })) }));
  const map = new Map((memory.checkpoints || []).map(item => [item.id, item]));
  for (const item of rows) map.set(item.id, item);
  memory.checkpoints = [...map.values()].slice(-PROJECT_DEVELOPMENT_MEMORY_LIMITS.checkpoints);
  return projectDevelopmentSaveMemory(memory);
}

function projectDevelopmentRestoreCheckpoints(target, currentWorkspaceId) {
  if (!projectDevelopmentIdentity?.key || !Array.isArray(target)) return { restored: 0, available: false };
  const memory = projectDevelopmentLoadMemory();
  if (!memory) return { restored: 0, available: true };
  const ids = new Set(target.map(item => item.id));
  let restored = 0;
  for (const item of memory.checkpoints || []) {
    if (!['interrupted', 'needs_recovery'].includes(item.status) || !item.id || ids.has(item.id)) continue;
    const copy = { ...item, workspaceId: String(currentWorkspaceId || ''), operationIds: Array.isArray(item.operationIds) ? item.operationIds : [], recoveryOperations: Array.isArray(item.operations) ? item.operations : [],
      plan: Array.isArray(item.plan) ? item.plan : [], resumedFrom: item.resumedFrom || '', provider: '', model: '', completedAt: '' };
    target.push(copy); ids.add(copy.id); restored++;
  }
  return { restored, available: true };
}

function projectDevelopmentRecordDecision(title, detail) {
  if (!projectDevelopmentIdentity?.key) throw new Error('Cross-session project memory is unavailable until a native workspace is selected.');
  const heading = projectDevelopmentRedact(title, 160).trim();
  const note = projectDevelopmentRedact(detail, 900).trim();
  if (!heading || !note) throw new Error('A short decision title and detail are required.');
  const memory = projectDevelopmentLoadMemory() || { decisions: [], runs: [], checkpoints: [], ownedChanges: [] };
  memory.decisions.push({ id: crypto.randomUUID(), at: new Date().toISOString(), title: heading, detail: note });
  projectDevelopmentSaveMemory(memory);
  return { saved: true, title: heading, decisionCount: memory.decisions.length };
}

async function projectDevelopmentCaptureGitBaseline(signal = null) {
  try {
    if (typeof requireAgentApproval === 'function') {
      const approved = await requireAgentApproval({ title: 'Read Git status before recording an Agent file change?', subtitle: 'Git read-only · ownership baseline', detail: 'RunMind reads branch/status and changed paths only. This lets later reviews distinguish files already changed before the Agent write from new changes. No Git state is modified.', risky: false, permissionCategory: 'gitRead' }, signal);
      if (!approved) return null;
    }
    const result = await callAgentHost('git.run', { action: 'review_snapshot', contextId: workspaceContext.id }, signal, 45000);
    if (!result?.success) return null;
    return projectDevelopmentParseGitStatus(result);
  } catch (_) { return null; }
}

async function projectDevelopmentRecordOwnedChanges(rows, baseline = null) {
  if (!projectDevelopmentIdentity?.key || !Array.isArray(rows) || !rows.length) return false;
  const memory = projectDevelopmentLoadMemory() || { decisions: [], runs: [], checkpoints: [], ownedChanges: [] };
  const baselinePaths = Array.isArray(baseline?.changedPaths) ? baseline.changedPaths.slice(0, 500) : null;
  for (const row of rows.slice(0, 20)) {
    const path = String(row.path || '').replace(/\\/g, '/').slice(0, 500);
    if (!path || path.startsWith('/') || path.split('/').some(part => part === '..' || part === '.')) continue;
    memory.ownedChanges.push({ id: crypto.randomUUID(), at: new Date().toISOString(), operation: String(row.operation || 'edit').slice(0, 20), path,
      backupPath: String(row.backupPath || '').slice(0, 600), previousSha256: String(row.previousSha256 || '').slice(0, 64), currentSha256: String(row.currentSha256 || '').slice(0, 64), baselinePaths });
  }
  return projectDevelopmentSaveMemory(memory);
}


async function projectDevelopmentPrepareMutation(signal = null) {
  try {
    await projectDevelopmentEnsureIdentity(signal);
    return await projectDevelopmentCaptureGitBaseline(signal);
  } catch (_) { return null; }
}

async function projectDevelopmentTrackMutation({ path, operation, backupPath = '', previousText = '', currentText = '', previousFile = null, currentFile = null, baseline = null } = {}) {
  try {
    if (!projectDevelopmentIdentity?.key) await projectDevelopmentEnsureIdentity();
    if (!projectDevelopmentIdentity?.key) return false;
    const previousSha256 = operation === 'create' ? '' : previousFile ? await projectDevelopmentHashBlob(previousFile).catch(() => '') : await projectDevelopmentHashText(previousText).catch(() => '');
    const currentSha256 = operation === 'delete' ? '' : currentFile ? await projectDevelopmentHashBlob(currentFile).catch(() => '') : await projectDevelopmentHashText(currentText).catch(() => '');
    return await projectDevelopmentRecordOwnedChanges([{ path, operation, backupPath, previousSha256, currentSha256 }], baseline);
  } catch (_) { return false; }
}

function projectDevelopmentBuildOwnedChangeAudit(snapshot) {
  const memory = projectDevelopmentLoadMemory();
  if (!memory) return { available: false, message: 'No cross-session project memory is available for this workspace.' };
  const current = projectDevelopmentParseGitStatus(snapshot);
  const latest = new Map();
  for (const item of memory.ownedChanges || []) latest.set(item.path.toLowerCase(), item);
  const owned = [...latest.values()].map(item => {
    const observed = current.files.find(file => file.path.toLowerCase() === item.path.toLowerCase()) || null;
    return { path: item.path, operation: item.operation, at: item.at, backupPath: item.backupPath || null, previousSha256: item.previousSha256 || null, recordedCurrentSha256: item.currentSha256 || null,
      currentlyChangedInGit: !!observed, staged: !!observed?.staged, unstaged: !!observed?.unstaged, recovery: item.backupPath ? 'backup-available' : 'no-backup-recorded' };
  });
  const baselineReceipt = (memory.ownedChanges || []).find(item => Array.isArray(item.baselinePaths));
  const baselineAvailable = !!baselineReceipt;
  const baselinePaths = new Set((baselineReceipt?.baselinePaths || []).map(path => String(path).toLowerCase()));
  const ownedPaths = new Set(latest.keys());
  const unattributedPaths = current.files.filter(file => !ownedPaths.has(file.path.toLowerCase())).map(item => item.path);
  const additionalChanges = baselineAvailable ? unattributedPaths.filter(path => !baselinePaths.has(path.toLowerCase())) : [];
  return { available: true, attributionAvailable: baselineAvailable, ownedChanges: owned, additionalChangesSinceRecordedBaseline: additionalChanges,
    unattributedChangedPaths: baselineAvailable ? [] : unattributedPaths,
    summary: { trackedAgentPaths: owned.length, currentlyChangedAgentPaths: owned.filter(item => item.currentlyChangedInGit).length, otherNewlyChangedPaths: additionalChanges.length, unattributedPaths: unattributedPaths.length },
    limitations: ['Git status is compared with the earliest captured pre-write baseline. If gitRead permission was declined or no repository was selected, non-Agent changes remain unattributed and are not counted as new since baseline.', 'A recorded backup provides recovery material only. Restoring it still requires the normal explicit approval and current-file checks.', 'Changes made outside RunMind are never claimed as RunMind-owned.'] };
}

function projectDevelopmentReadMemory() {
  if (!projectDevelopmentIdentity?.key) return { available: false, reason: 'Select the native workspace to access project memory across chat sessions.' };
  const memory = projectDevelopmentLoadMemory() || { schemaVersion: 1, folder: projectDevelopmentIdentity.folder, decisions: [], runs: [], checkpoints: [], ownedChanges: [] };
  return { available: true, folder: memory.folder, updatedAt: memory.updatedAt || null,
    decisions: memory.decisions.slice(-40), recentRuns: memory.runs.slice(-40).map(item => ({ at: item.at, kind: item.kind, command: item.command, success: item.success, exitCode: item.exitCode, failureSignature: item.failureSignature || '', environment: item.environment || null, excerpt: item.excerpt })),
    checkpoints: memory.checkpoints.slice(-20).map(item => ({ id: item.id, task: item.task, status: item.status, stage: item.stage, summary: item.summary, operations: (item.operations || []).slice(-20), createdAt: item.createdAt, updatedAt: item.updatedAt })),
    ownedChanges: memory.ownedChanges.slice(-40).map(item => ({ path: item.path, operation: item.operation, at: item.at, backupPath: item.backupPath || null })),
    limits: PROJECT_DEVELOPMENT_MEMORY_LIMITS,
    note: 'Stored locally in the RunMind WebView profile and keyed by a SHA-256 fingerprint of the selected native workspace path. No source files, full test logs, credentials or raw command output are stored.' };
}
