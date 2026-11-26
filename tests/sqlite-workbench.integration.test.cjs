'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { DatabaseSync } = require('node:sqlite');
const { run, validateInput, validateSingleStatement } = require('../Shared/SqliteRunner.cjs');

function createDatabase(directory) {
  const filename = path.join(directory, 'app.sqlite');
  const db = new DatabaseSync(filename);
  db.exec(`
    PRAGMA foreign_keys = ON;
    CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE);
    CREATE TABLE notes (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL REFERENCES users(id), body TEXT NOT NULL);
    INSERT INTO users(name) VALUES ('Ada'), ('Grace');
    INSERT INTO notes(user_id, body) VALUES (1, 'design'), (1, 'review');
  `);
  db.close();
  return filename;
}

test('SQLite workbench validates and preserves database path, action and table identity', () => {
  assert.equal(validateInput({ action: 'schema', database_path: 'data/app.sqlite', table: 'users' }).table, 'users');
  for (const database_path of ['../app.sqlite', 'data/./app.sqlite', '/tmp/app.sqlite', 'app.sqlite.bak']) {
    assert.throws(() => validateInput({ action: 'inspect', database_path }), /database_path/);
  }
  assert.throws(() => validateInput({ action: 'schema', database_path: 'app.sqlite', table: 'users; DROP TABLE users' }), /simple table name/);
});

test('SQLite workbench inspects integrity, tables, columns and foreign keys', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-sqlite-inspect-'));
  try {
    createDatabase(directory);
    const inspected = run({ action: 'inspect', database_path: 'app.sqlite' }, { workingDirectory: directory });
    assert.equal(inspected.success, true);
    assert.deepEqual(inspected.quickCheck, [{ quick_check: 'ok' }]);
    assert.deepEqual(inspected.tables.map(item => item.name), ['notes', 'users']);

    const listed = run({ action: 'tables', database_path: 'app.sqlite' }, { workingDirectory: directory });
    assert.equal(listed.tables.length, 2);
    const schema = run({ action: 'schema', database_path: 'app.sqlite', table: 'notes' }, { workingDirectory: directory });
    assert.deepEqual(schema.columns.map(item => item.name), ['id', 'user_id', 'body']);
    assert.equal(schema.foreignKeys[0].table, 'users');
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('SQLite workbench uses bound parameters and returns bounded read-only query results', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-sqlite-query-'));
  try {
    createDatabase(directory);
    const result = run({ action: 'query', database_path: 'app.sqlite', sql: 'SELECT id, name FROM users WHERE name = ?', parameters: ["Ada' OR 1=1 --"] }, { workingDirectory: directory });
    assert.equal(result.success, true);
    assert.deepEqual(result.rows, []);
    const matched = run({ action: 'query', database_path: 'app.sqlite', sql: 'SELECT id, name FROM users WHERE name = ?', parameters: ['Ada'] }, { workingDirectory: directory });
    assert.deepEqual(matched.rows, [{ id: 1, name: 'Ada' }]);
    assert.throws(() => run({ action: 'query', database_path: 'app.sqlite', sql: 'DELETE FROM users' }, { workingDirectory: directory }), /read-only/);
    assert.throws(() => run({ action: 'query', database_path: 'app.sqlite', sql: 'PRAGMA journal_mode = WAL' }, { workingDirectory: directory }), /allowlist/);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('SQLite workbench performs one approved write statement transactionally', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-sqlite-write-'));
  try {
    createDatabase(directory);
    const inserted = run({ action: 'execute', database_path: 'app.sqlite', sql: 'INSERT INTO users(name) VALUES (?)', parameters: ['Katherine'] }, { workingDirectory: directory });
    assert.equal(inserted.success, true);
    assert.equal(inserted.changes, 1);
    assert.equal(run({ action: 'query', database_path: 'app.sqlite', sql: 'SELECT COUNT(*) AS count FROM users' }, { workingDirectory: directory }).rows[0].count, 3);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('SQLite workbench rejects multi-statement, attach, transaction-control and virtual-table writes', () => {
  assert.throws(() => validateSingleStatement('SELECT 1; DROP TABLE users'), /Only one SQL statement/);
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-sqlite-unsafe-'));
  try {
    createDatabase(directory);
    for (const sql of ['ATTACH DATABASE \'other.db\' AS other', 'VACUUM', 'PRAGMA user_version = 7', 'CREATE VIRTUAL TABLE x USING fts5(body)']) {
      assert.throws(() => run({ action: 'execute', database_path: 'app.sqlite', sql }, { workingDirectory: directory }));
    }
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('SQLite workbench rejects symbolic-link database paths and files outside the workspace', { skip: process.platform === 'win32' }, () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-sqlite-link-'));
  const outside = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-sqlite-outside-'));
  try {
    createDatabase(outside);
    fs.symlinkSync(path.join(outside, 'app.sqlite'), path.join(directory, 'linked.sqlite'));
    assert.throws(() => run({ action: 'inspect', database_path: 'linked.sqlite' }, { workingDirectory: directory }), /symbolic links/);
    assert.throws(() => run({ action: 'inspect', database_path: '../runmind-sqlite-outside/app.sqlite' }, { workingDirectory: directory }));
  } finally { fs.rmSync(directory, { recursive: true, force: true }); fs.rmSync(outside, { recursive: true, force: true }); }
});
