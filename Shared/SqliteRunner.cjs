'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { DatabaseSync } = require('node:sqlite');

const MAX_INPUT_BYTES = 128 * 1024;
const MAX_SQL_LENGTH = 32 * 1024;
const MAX_ROWS = 200;
const MAX_OUTPUT_BYTES = 256 * 1024;
const SAFE_READ_PRAGMAS = new Set(['table_info', 'table_xinfo', 'index_list', 'index_info', 'foreign_key_list', 'database_list', 'integrity_check', 'quick_check', 'compile_options']);

function parseInputJson(input) { return JSON.parse(String(input || '').replace(/^\uFEFF/, '') || '{}'); }

function validateSingleStatement(sql) {
  const value = String(sql || '').trim();
  if (!value || value.length > MAX_SQL_LENGTH) throw new Error(`SQL must contain 1–${MAX_SQL_LENGTH} characters.`);
  let quote = '';
  let lineComment = false;
  let blockComment = false;
  let semicolon = -1;
  for (let i = 0; i < value.length; i++) {
    const ch = value[i], next = value[i + 1];
    if (lineComment) { if (ch === '\n' || ch === '\r') lineComment = false; continue; }
    if (blockComment) { if (ch === '*' && next === '/') { blockComment = false; i++; } continue; }
    if (quote) {
      if (ch === quote) {
        if (next === quote && quote !== ']') i++;
        else quote = '';
      } else if (ch === '\\' && quote === "'" && next) i++;
      continue;
    }
    if (ch === '-' && next === '-') { lineComment = true; i++; continue; }
    if (ch === '/' && next === '*') { blockComment = true; i++; continue; }
    if (ch === "'" || ch === '"' || ch === '`') { quote = ch; continue; }
    if (ch === '[') { quote = ']'; continue; }
    if (ch === ';') {
      if (semicolon >= 0 || value.slice(i + 1).replace(/(?:\s|--[^\r\n]*|\/\*[\s\S]*?\*\/)/g, '').length) throw new Error('Only one SQL statement is allowed per operation.');
      semicolon = i;
    }
  }
  if (quote || blockComment) throw new Error('SQL contains an unterminated quote or block comment.');
  return semicolon >= 0 ? value.slice(0, semicolon).trim() : value;
}

function normalizeRows(rows) {
  return rows.map(row => Object.fromEntries(Object.entries(row).map(([key, value]) => [key, typeof value === 'bigint' ? value.toString() : Buffer.isBuffer(value) ? `[BLOB ${value.length} bytes]` : value])));
}

function validateInput(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new Error('A SQLite operation object is required.');
  const action = String(input.action || '').toLowerCase();
  if (!['inspect', 'tables', 'schema', 'query', 'execute'].includes(action)) throw new Error('SQLite action must be inspect, tables, schema, query, or execute.');
  const databasePath = String(input.database_path || '').trim();
  if (!databasePath || databasePath.length > 1024 || !/\.(?:db|sqlite|sqlite3)$/i.test(databasePath) || path.isAbsolute(databasePath) || databasePath.split(/[\\/]/).some(part => !part || part === '.' || part === '..'))
    throw new Error('database_path must be a normalized, workspace-relative .db, .sqlite or .sqlite3 path to an existing SQLite database.');
  const sql = String(input.sql || '');
  if (['query', 'execute'].includes(action)) validateSingleStatement(sql);
  if (action === 'query' && !/^\s*(?:SELECT|WITH|EXPLAIN|PRAGMA)\b/i.test(sql)) throw new Error('Read-only query mode accepts SELECT, WITH, EXPLAIN or read-only PRAGMA statements only.');
  if (action === 'query' && /^\s*PRAGMA\s+([a-z_]+)/i.test(sql)) {
    const pragma = sql.match(/^\s*PRAGMA\s+([a-z_]+)/i)[1].toLowerCase();
    if (!SAFE_READ_PRAGMAS.has(pragma)) throw new Error(`PRAGMA '${pragma}' is not in the read-only allowlist.`);
  }
  const parameters = input.parameters === undefined ? [] : input.parameters;
  if (!Array.isArray(parameters) || parameters.length > 100) throw new Error('parameters must be an array with at most 100 values.');
  for (const item of parameters) if (!(item === null || ['string', 'number', 'boolean'].includes(typeof item)) || (typeof item === 'string' && item.length > 8000) || (typeof item === 'number' && !Number.isFinite(item))) throw new Error('SQLite parameters must be bounded strings, finite numbers, booleans or null.');
  if (action === 'schema' && (!/^[A-Za-z_][A-Za-z0-9_]{0,127}$/.test(String(input.table || '')))) throw new Error('schema requires a simple table name.');
  return { action, databasePath, sql, parameters, table: String(input.table || '') };
}

function run(input, dependencies = {}) {
  const request = validateInput(input);
  const cwd = path.resolve(dependencies.workingDirectory || process.cwd());
  const fullPath = path.resolve(cwd, request.databasePath);
  const relative = path.relative(cwd, fullPath);
  if (!relative || relative.startsWith('..' + path.sep) || path.isAbsolute(relative)) throw new Error('Database path must remain below the selected workspace directory.');
  let current = cwd;
  for (const segment of relative.split(path.sep)) {
    current = path.join(current, segment);
    const stat = fs.lstatSync(current);
    if (stat.isSymbolicLink() || stat.isBlockDevice() || stat.isCharacterDevice()) throw new Error('Database path cannot contain symbolic links or device files.');
  }
  const stat = fs.statSync(fullPath);
  if (!stat.isFile()) throw new Error('The selected SQLite database path is not a regular file.');
  if (stat.size > 512 * 1024 * 1024) throw new Error('SQLite database exceeds the 512 MB inspection limit.');

  const started = Date.now();
  const db = new DatabaseSync(fullPath, { readOnly: request.action !== 'execute' });
  try {
    db.exec('PRAGMA busy_timeout = 2000');
    let result;
    if (request.action === 'inspect') {
      const quickCheck = normalizeRows(db.prepare('PRAGMA quick_check(1)').all());
      const tables = normalizeRows(db.prepare("SELECT name, type FROM sqlite_schema WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 200").all());
      result = { database: request.databasePath, bytes: stat.size, quickCheck, tables, tableCount: tables.length };
    } else if (request.action === 'tables') {
      result = { tables: normalizeRows(db.prepare("SELECT name, type, sql FROM sqlite_schema WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 200").all()) };
    } else if (request.action === 'schema') {
      const found = db.prepare("SELECT name, type, sql FROM sqlite_schema WHERE name = ? AND type IN ('table','view')").all(request.table)[0];
      if (!found) throw new Error(`Table or view '${request.table}' was not found.`);
      const quotedTable = `"${request.table.replace(/"/g, '""')}"`;
      const columns = normalizeRows(db.prepare(`PRAGMA table_xinfo(${quotedTable})`).all());
      const foreignKeys = normalizeRows(db.prepare(`PRAGMA foreign_key_list(${quotedTable})`).all());
      result = { table: found, columns, foreignKeys };
    } else if (request.action === 'query') {
      const statement = db.prepare(validateSingleStatement(request.sql));
      if (!statement.columns().length && !/^\s*PRAGMA\b/i.test(request.sql)) throw new Error('The supplied SQL does not return rows. Use execute for a write or non-row statement.');
      const rows = normalizeRows(statement.all(...request.parameters.map(value => typeof value === 'boolean' ? (value ? 1 : 0) : value)).slice(0, MAX_ROWS + 1));
      result = { rows: rows.slice(0, MAX_ROWS), rowCount: Math.min(rows.length, MAX_ROWS), truncated: rows.length > MAX_ROWS };
    } else {
      const sql = validateSingleStatement(request.sql);
      if (/^\s*(?:SELECT|WITH|EXPLAIN|PRAGMA|ATTACH|DETACH|VACUUM|BEGIN|COMMIT|ROLLBACK)\b/i.test(sql)) throw new Error('execute accepts a single DML/DDL statement only; transaction controls, PRAGMA, ATTACH, DETACH and VACUUM are blocked.');
      if (!/^\s*(?:INSERT|UPDATE|DELETE|REPLACE|CREATE|ALTER|DROP)\b/i.test(sql) || /^\s*CREATE\s+VIRTUAL\s+TABLE\b/i.test(sql)) throw new Error('execute accepts INSERT, UPDATE, DELETE, REPLACE and ordinary CREATE/ALTER/DROP statements only.');
      db.exec('BEGIN IMMEDIATE');
      try {
        const outcome = db.prepare(sql).run(...request.parameters.map(value => typeof value === 'boolean' ? (value ? 1 : 0) : value));
        db.exec('COMMIT');
        result = { changes: Number(outcome.changes), lastInsertRowid: String(outcome.lastInsertRowid) };
      } catch (error) { try { db.exec('ROLLBACK'); } catch { } throw error; }
    }
    const output = { success: true, action: request.action, database: request.databasePath, readOnly: request.action !== 'execute', durationMs: Date.now() - started, ...result };
    if (Buffer.byteLength(JSON.stringify(output), 'utf8') > MAX_OUTPUT_BYTES) throw new Error('SQLite result exceeded the 256 KB output limit. Reduce the result size.');
    return output;
  } finally { db.close(); }
}

async function main() {
  try {
    let input = '';
    for await (const chunk of process.stdin) { input += chunk; if (Buffer.byteLength(input, 'utf8') > MAX_INPUT_BYTES) throw new Error('SQLite request exceeds the 128 KB input limit.'); }
    const result = run(parseInputJson(input));
    process.stdout.write(`${JSON.stringify(result)}\n`);
  } catch (error) { process.stdout.write(`${JSON.stringify({ success: false, error: String(error.message || error).slice(0, 2500) })}\n`); process.exitCode = 1; }
}

if (require.main === module) main();
module.exports = { parseInputJson, validateInput, validateSingleStatement, run, MAX_ROWS, MAX_INPUT_BYTES, MAX_OUTPUT_BYTES };
