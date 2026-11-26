const test = require('node:test');
const assert = require('node:assert/strict');
const { readFile, access } = require('node:fs/promises');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = (relative) => readFile(path.join(root, relative), 'utf8');

test('Runner has no duplicate WebView2 chat host and relies on Standalone for full chat', async () => {
  const project = await read('RunMind.Runner/RunMind.Runner.csproj');
  const main = await read('RunMind.Runner/MainWindow.xaml.cs');
  await assert.rejects(access(path.join(root, 'RunMind.Runner/RunnerChatWindow.xaml')));
  await assert.rejects(access(path.join(root, 'RunMind.Runner/RunnerChatWindow.xaml.cs')));
  assert.doesNotMatch(project, /Microsoft\.Web\.WebView2|ChatWindow\.html|WebView2RuntimeBootstrapper/);
  assert.match(main, /OpenStandaloneChatAsync/);
  assert.doesNotMatch(main, /RunnerChatWindow/);
});

test('Runner project excludes retired chat XAML and code-behind left in an in-place extraction folder', async () => {
  const project = await read('RunMind.Runner/RunMind.Runner.csproj');
  assert.match(project, /DefaultItemExcludesInProjectFolder[^\n]*RunnerChatWindow\.xaml/);
  assert.match(project, /DefaultItemExcludesInProjectFolder[^\n]*RunnerChatWindow\.xaml\.cs/);
  assert.match(project, /<Page Remove="RunnerChatWindow\.xaml"\s*\/>/);
  assert.match(project, /<Compile Remove="RunnerChatWindow\.xaml\.cs"\s*\/>/);
});

test('Release build removes retired Runner chat files from reused source directories', async () => {
  const build = await read('Build-Release-x64.bat');
  assert.match(build, /if exist "RunMind\.Runner\\RunnerChatWindow\.xaml"[\s\S]*?del \/f \/q "RunMind\.Runner\\RunnerChatWindow\.xaml"/);
  assert.match(build, /if exist "RunMind\.Runner\\RunnerChatWindow\.xaml\.cs"[\s\S]*?del \/f \/q "RunMind\.Runner\\RunnerChatWindow\.xaml\.cs"/);
});

test('Standalone receives handoff context without Quick Host restrictions or automatic resubmission', async () => {
  const app = await read('RunMind.Standalone/App.xaml.cs');
  const main = await read('RunMind.Standalone/MainWindow.xaml.cs');
  const application = await read('Shared/ChatWindow/scripts/application.js');
  assert.match(app, /new MainWindow\(runnerHandoff\.Prompt, runnerHandoff\.Response,\s*runnerHandoff\.IsError, "Standalone"\)/);
  assert.match(main, /standalone = true/);
  assert.match(main, /prompt = freshConversation \? null : _initialPrompt/);
  assert.match(main, /response = freshConversation \? null : _initialResponse/);
  assert.match(application, /if \(BOOT\.prompt\) messages\.push\(\{ role: 'user'/);
  assert.match(application, /if \(BOOT\.response !== undefined && BOOT\.response !== null\)/);
  assert.match(application, /const pendingPrompt = String\(BOOT\.autoSendPromptText \|\| ''\)\.trim\(\)/);
});

test('Standalone handoff pipe is user-scoped and validates bounded prompt/response context', async () => {
  const shared = await read('Shared/RunnerHandoff.cs');
  const runner = await read('RunMind.Runner/MainWindow.xaml.cs');
  const locator = await read('RunMind.Runner/StandaloneLocator.cs');
  const app = await read('RunMind.Standalone/App.xaml.cs');
  assert.match(shared, /PipeOptions\.Asynchronous \| PipeOptions\.CurrentUserOnly/);
  assert.match(shared, /MaximumPayloadBytes = 256 \* 1024/);
  assert.match(shared, /MaximumPromptCharacters = 12_000/);
  assert.match(shared, /MaximumResponseCharacters = 180_000/);
  assert.match(shared, /FileOptions\.WriteThrough/);
  assert.match(shared, /FileAttributes\.ReparsePoint/);
  assert.match(runner, /await RunnerHandoffPipe\.TrySendAsync\(payload, 900\)/);
  assert.match(runner, /StandaloneLocator\.FindExecutable/);
  assert.match(shared, /TrySendWithRetryAsync/);
  assert.match(shared, /CurrentUserOnly/);
  assert.match(shared, /StandalonePathStore/);
  assert.match(locator, /StandalonePathStore\.ReadCachedExecutable/);
  assert.ok(locator.indexOf('FindDevelopmentPaths()') < locator.indexOf('FindRunningExecutable()'), 'prefer current built Standalone over a possibly old running process');
  assert.match(locator, /using RunMind\.Shared;/);
  assert.match(app, /StandalonePathStore\.RememberCurrentExecutable/);
  assert.match(app, /HasOtherStandaloneProcess\(\)/);
  assert.match(app, /new MainWindow\(runnerHandoff\.Prompt, runnerHandoff\.Response/);
});

test('StandaloneLocator explicitly imports System.IO for filesystem path discovery', async () => {
  const locator = await read('RunMind.Runner/StandaloneLocator.cs');
  assert.match(locator, /^using System\.IO;$/m);
  assert.match(locator, /Path\.Combine\(/);
  assert.match(locator, /new DirectoryInfo\(/);
  assert.match(locator, /File\.Exists\(/);
});
