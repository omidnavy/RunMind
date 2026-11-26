const test = require('node:test');
const assert = require('node:assert/strict');
const { readFile } = require('node:fs/promises');
const path = require('node:path');

const root = path.resolve(__dirname, '..');

test('Release pipeline cleans before the win-x64 restore and preserves RID assets through publish', async () => {
  const build = await readFile(path.join(root, 'Build-Release-x64.bat'), 'utf8');
  const cleanIndex = build.indexOf('dotnet clean "%SOLUTION%"');
  const restoreIndex = build.indexOf('dotnet restore "%SOLUTION%" --runtime win-x64');
  const buildIndex = build.indexOf('dotnet build "%SOLUTION%" --configuration Release --no-restore');
  const standalonePublishIndex = build.indexOf('dotnet publish "RunMind.Standalone\\RunMind.Standalone.csproj"');
  const runnerPublishIndex = build.indexOf('dotnet publish "RunMind.Runner\\RunMind.Runner.csproj"');

  assert.ok(cleanIndex >= 0 && restoreIndex > cleanIndex, 'Clean must run before the runtime-specific restore');
  assert.ok(buildIndex > restoreIndex, 'solution build must follow the win-x64 restore');
  assert.ok(standalonePublishIndex > buildIndex && runnerPublishIndex > standalonePublishIndex);
  assert.match(build, /dotnet publish "RunMind\.Standalone\\RunMind\.Standalone\.csproj"[^\r\n]*--runtime win-x64[^\r\n]*--no-restore/);
  assert.match(build, /dotnet publish "RunMind\.Runner\\RunMind\.Runner\.csproj"[^\r\n]*--runtime win-x64[^\r\n]*--no-restore/);
  assert.match(build, /Do not run an implicit restore here/);
});
