const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

function git(cwd, ...args) {
  const result = spawnSync('git', args, { cwd, encoding: 'utf8', env: { ...process.env, GIT_TERMINAL_PROMPT: '0' } });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, `git ${args.join(' ')} failed: ${result.stderr}`);
  return result.stdout.trim();
}

test('real Git repository supports the Phase 1 local developer workflow', t => {
  const version = spawnSync('git', ['--version'], { encoding: 'utf8' });
  if (version.status !== 0) return t.skip('Git is not installed in this environment.');
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-phase1-git-'));
  try {
    git(directory, 'init', '-q');
    git(directory, 'config', 'user.name', 'RunMind Integration Test');
    git(directory, 'config', 'user.email', 'runmind-test@example.invalid');
    fs.writeFileSync(path.join(directory, 'index.js'), 'export const value = 1;\n');
    assert.match(git(directory, 'status', '--short'), /index\.js/);
    git(directory, 'add', 'index.js');
    git(directory, 'commit', '-m', 'initial');
    assert.match(git(directory, 'log', '-1', '--oneline'), /initial/);
    assert.match(git(directory, 'show', '--format=', '--name-only', 'HEAD'), /index\.js/);
    git(directory, 'switch', '-c', 'feature/test');
    fs.appendFileSync(path.join(directory, 'index.js'), 'export const next = 2;\n');
    assert.match(git(directory, 'diff', '--', 'index.js'), /next/);
    git(directory, 'stash', 'push', '-m', 'integration-check');
    assert.equal(fs.readFileSync(path.join(directory, 'index.js'), 'utf8'), 'export const value = 1;\n');
    git(directory, 'stash', 'pop');
    assert.match(fs.readFileSync(path.join(directory, 'index.js'), 'utf8'), /next/);
    git(directory, 'add', 'index.js');
    git(directory, 'commit', '-m', 'feature');
    assert.equal(git(directory, 'status', '--porcelain'), '');
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});
