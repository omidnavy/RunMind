const test = require('node:test');
const assert = require('node:assert/strict');
const { readFile } = require('node:fs/promises');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = (relativePath) => readFile(path.join(root, relativePath), 'utf8');

test('Runner is an optional WPF host with a lazy, restricted shared ChatWindow', async () => {
  const project = await read('RunMind.Runner/RunMind.Runner.csproj');
  const chat = await read('RunMind.Runner/RunnerChatWindow.xaml.cs');
  const main = await read('RunMind.Runner/MainWindow.xaml.cs');
  assert.match(project, /<OutputType>WinExe<\/OutputType>/);
  assert.match(project, /<UseWPF>true<\/UseWPF>/);
  assert.match(project, /<TargetFramework>net10\.0-windows/);
  assert.match(project, /Microsoft\.Web\.WebView2/);
  assert.match(project, /ChatWindow\.html/);
  assert.doesNotMatch(project, /AgentHostBridge|WorkspaceFileBridge|SystemManager\.cs|Mcp/i);
  assert.doesNotMatch(chat, /new\s+AgentHostBridge|new\s+WorkspaceFileBridge|new\s+NativePageReader|new\s+ConversationSaveDialog|SystemManager\./i);
  assert.match(chat, /quickHostMode\s*=\s*true/);
  assert.match(chat, /operation == "themes\.list"/);
  assert.match(chat, /Runner Quick Host does not expose native Agent or workspace capabilities/);
  assert.match(main, /new RunnerChatWindow\(/);
  assert.match(main, /new RunnerChatWindow/);
});

test('Runner catalog is bounded to Start Menu shortcuts and rejects reparse traversal', async () => {
  const catalog = await read('RunMind.Runner/ApplicationCatalog.cs');
  assert.match(catalog, /MaximumShortcuts\s*=\s*3_500/);
  assert.match(catalog, /MaximumSearchResults\s*=\s*9/);
  assert.match(catalog, /SpecialFolder\.StartMenu/);
  assert.match(catalog, /SpecialFolder\.CommonStartMenu/);
  assert.match(catalog, /"\*\.lnk"/);
  assert.match(catalog, /AttributesToSkip\s*=\s*FileAttributes\.ReparsePoint/);
  assert.match(catalog, /MaxRecursionDepth\s*=\s*8/);
});

test('direct path and URL opening has explicit target restrictions', async () => {
  const commands = await read('RunMind.Runner/RunnerCommandService.cs');
  assert.match(commands, /UriSchemeHttp/);
  assert.match(commands, /UriSchemeHttps/);
  assert.match(commands, /Path\.IsPathRooted/);
  assert.match(commands, /BlockedDirectFileExtensions/);
  for (const extension of ['.exe', '.bat', '.cmd', '.ps1', '.vbs', '.js', '.msi', '.lnk', '.url', '.appref-ms']) {
    assert.ok(commands.includes(`"${extension}"`), `missing blocked extension ${extension}`);
  }
  assert.doesNotMatch(commands, /UseShellExecute\s*=\s*false[\s\S]{0,200}Arguments\s*=/);
  assert.doesNotMatch(commands, /cmd\.exe|powershell\.exe|pwsh\.exe/i);
});

test('launcher is plugin-driven and registers all five built-in plugins including RunMind', async () => {
  const plugins = await read('RunMind.Runner/RunnerPlugins.cs');
  const settings = await read('RunMind.Runner/RunnerSettings.cs');
  const window = await read('RunMind.Runner/MainWindow.xaml.cs');
  for (const id of ['applications', 'files', 'urls', 'calculator', 'runmind']) {
    assert.ok(settings.includes(`"${id}"`), `settings must persist ${id}`);
  }
  for (const registration of ['Register(new DirectTargetPlugin())', 'Register(new UrlPlugin())', 'Register(new CalculatorPlugin())', 'Register(_applications)', 'Register(new RunMindInlinePlugin())']) {
    assert.ok(plugins.includes(registration), `missing ${registration}`);
  }
  assert.match(plugins, /internal interface IRunnerLauncherPlugin/);
  assert.match(plugins, /public void Register\(IRunnerLauncherPlugin plugin\)/);
  assert.match(plugins, /A launcher plugin with ID/);
  assert.match(plugins, /class SafeArithmetic/);
  assert.doesNotMatch(plugins, /\beval\s*\(|new\s+Function\s*\(/);
  assert.match(plugins, /class RunMindInlinePlugin/);
  assert.match(plugins, /RunnerPluginAction\.ShowInlineAnswer/);
  assert.match(window, /case RunnerPluginAction\.ShowInlineAnswer/);
  assert.doesNotMatch(window, /case RunnerPluginAction\.OpenRunMindChat/);
  assert.match(window, /ScheduleInlineGeneration\(SearchBox\.Text\.Trim\(\)\)/);
  assert.match(window, /MaximumInlineDisplayCharacters/);
  assert.match(window, /CopyAnswerButton/);
  assert.match(window, /case RunnerPluginAction\.ShowInlineAnswer/);
  assert.match(window, /private void Chat_Click[\s\S]{0,260}OpenChat\(prompt\)/);
});

test('RunMind launcher activation mirrors the PowerToys Run llm action keyword', async () => {
  const plugin = await read('RunMind.Runner/RunnerPlugins.cs');
  const main = await read('RunMind.Runner/MainWindow.xaml.cs');
  const powerToys = await read('Community.PowerToys.Run.Plugin.RunMind/plugin.json');
  assert.match(powerToys, /\"ActionKeyword\"\s*:\s*\"llm\"/);
  assert.match(plugin, /const string ActionKeyword = \"llm\"/);
  assert.match(plugin, /StartsWith\(ActionKeyword, StringComparison\.OrdinalIgnoreCase\)/);
  assert.match(plugin, /char\.IsWhiteSpace\(value\[ActionKeyword\.Length\]\)/);
  assert.match(plugin, /TryExtractPrompt\(query, out var prompt\)/);
  assert.match(main, /RunMindInlinePlugin\.TryExtractPrompt\(query, out var prompt\)/);
  assert.match(main, /GenerateAsync\(prompt, _inlineSettings/);
  assert.match(main, /ResetInlineAnswerForNonRunMindQuery/);
  assert.match(main, /GenerateInlineAfterDelayAsync\(query, prompt, cancellation\)/);
  assert.match(plugin, /Value: prompt/);
  assert.match(plugin, /runMindActivated && !string\.Equals\(plugin\.Id, "runmind"/);
  assert.match(main, /var wasGenerating = _inlineIsRunning/);
  assert.match(main, /partialInlineResponse/);
});

test('Runner inline provider configuration covers Ollama and LM Studio independently of full chat', async () => {
  const settings = await read('RunMind.Runner/RunnerInlineSettings.cs');
  const client = await read('RunMind.Runner/RunnerInlineModelClient.cs');
  const ui = await read('RunMind.Runner/RunnerSettingsWindow.xaml');
  const code = await read('RunMind.Runner/RunnerSettingsWindow.xaml.cs');
  for (const key of ['OllamaEndpoint', 'LMStudioBaseUrl', 'Thinking', 'KeepAliveSeconds', 'RequestTimeoutSeconds', 'ModelDiscoveryTimeoutSeconds', 'InlineCpuThreads', 'SystemPrompt', 'Temperature', 'TopP', 'MaxOutputTokens', 'Seed', 'Nemotron35BoltE4BConversationalMode']) {
    assert.ok(settings.includes(key), `missing inline setting ${key}`);
  }
  for (const contract of ['BuildOllamaTagsUri', 'api/generate', 'api/v1/', 'chat/completions', 'chat_template_kwargs', 'conversational_mode', 'ApplyAuthentication', 'CancellationToken']) {
    assert.ok(client.includes(contract), `missing inline provider behavior ${contract}`);
  }
  assert.match(client, /catch \(OperationCanceledException\) when \(cancellationToken\.IsCancellationRequested\)/);
  assert.match(client, /credentialFingerprint/);
  assert.match(client, /MaximumStreamLineCharacters/);
  assert.match(client, /BoundedStreamLineReader/);
  assert.match(ui, /Refresh models \/ test connection/);
  assert.match(code, /ModelDiscoveryTimeoutSeconds/);
  assert.match(code, /Nemotron35BoltE4BConversationalMode/);
  assert.match(code, /TemperatureLabel\.Text = isLmStudio/);
  assert.match(ui, /Full chat-window provider connections remain separate/);
});

test('inline credentials are protected with Windows DPAPI and not serialized into JSON settings', async () => {
  const credentials = await read('RunMind.Runner/RunnerInlineCredentialsStore.cs');
  const settings = await read('RunMind.Runner/RunnerInlineSettings.cs');
  assert.match(credentials, /CryptProtectData/);
  assert.match(credentials, /CryptUnprotectData/);
  assert.match(credentials, /CryptProtectUiForbidden/);
  assert.match(credentials, /CryptographicOperations\.ZeroMemory/);
  assert.doesNotMatch(credentials, /CRYPTPROTECT_LOCAL_MACHINE/i);
  assert.match(settings, /JsonSerializer\.Serialize\(normalized/);
  assert.match(settings, /provider == "lmstudio" \? 1 : 2/);
  assert.doesNotMatch(settings, /RunnerInlineCredentialsStore|credentials\./);
});

test('Runner reuses the real theme catalog and chat UI while retaining a fast native startup path', async () => {
  const theme = await read('RunMind.Runner/RunnerThemeManager.cs');
  const main = await read('RunMind.Runner/MainWindow.xaml.cs');
  const chat = await read('RunMind.Runner/RunnerChatWindow.xaml.cs');
  const chatXaml = await read('RunMind.Runner/RunnerChatWindow.xaml');
  const project = await read('RunMind.Runner/RunMind.Runner.csproj');
  assert.match(theme, /ThemeCatalog\.Discover/);
  assert.match(theme, /AppearancePreferenceStore/);
  assert.match(theme, /theme\?\.Palette/);
  assert.match(main, /Task\.Run\(_applicationCatalog\.Load\)/);
  assert.match(main, /new WindowThemeSynchronizer/);
  assert.match(chat, /EnsureInstalledAsync/);
  assert.match(chatXaml, /Loaded="Window_Loaded"/);
  assert.match(project, /ChatWindow\\\*\*/);
  assert.match(project, /AppearancePreferences\.cs/);
});

test('Runner settings are current-user only and startup is opt-in', async () => {
  const settings = await read('RunMind.Runner/RunnerSettings.cs');
  assert.match(settings, /StartWithWindows\s*\{\s*get;\s*set;\s*\}/);
  assert.match(settings, /"Ctrl\+Alt\+Space"/);
  assert.match(settings, /Registry\.CurrentUser/);
  assert.match(settings, /EnabledPlugins\s*\{[^}]+\}\s*=\s*new\(\)\s*\{[^}]+\};/);
  assert.match(settings, /Software\\Microsoft\\Windows\\CurrentVersion\\Run/);
  assert.match(settings, /--background/);
  assert.doesNotMatch(settings, /Registry\.LocalMachine/);
});

test('release and install workflow treats Runner as an independent optional component', async () => {
  const solution = await read('RunMind.sln');
  const build = await read('Build-Release-x64.bat');
  const installer = await read('Install-RunMind-Runner.ps1');
  const uninstaller = await read('Uninstall-RunMind-Runner.ps1');
  assert.match(solution, /RunMind\.Runner\\RunMind\.Runner\.csproj/);
  assert.match(build, /RunMind\.Runner\\RunMind\.Runner\.csproj/);
  assert.match(build, /Release\\Runner\\win-x64\\publish/);
  assert.match(installer, /LOCALAPPDATA/);
  assert.match(installer, /RunMind\.Runner\.exe/);
  assert.match(uninstaller, /RunMind\.Runner/);
  assert.match(uninstaller, /RunMind Standalone, PowerToys Run and Command Palette were not changed/);
});

test('Runner and shared host sources include the earlier Windows compile corrections', async () => {
  const serverManager = await read('Shared/DevelopmentServerManager.cs');
  const mainWindow = await read('RunMind.Runner/MainWindow.xaml.cs');
  const settingsWindow = await read('RunMind.Runner/RunnerSettingsWindow.xaml.cs');

  assert.match(serverManager, /readiness\.Host\.Equals\("localhost", StringComparison\.OrdinalIgnoreCase\)\)\)\)\)/);
  assert.match(settingsWindow, /public partial class RunnerSettingsWindow : Window/);
  assert.match(settingsWindow, /internal RunnerSettingsWindow\([\s\S]*RunnerSettings currentSettings/);
  assert.match(mainWindow, /using WpfKeyEventArgs = System\.Windows\.Input\.KeyEventArgs;/);
  assert.match(mainWindow, /SearchBox_KeyDown\(object sender, WpfKeyEventArgs e\)/);
  assert.match(mainWindow, /ResultsList_KeyDown\(object sender, WpfKeyEventArgs e\)/);
  assert.doesNotMatch(mainWindow, /\bKeyEventArgs e\)/);

  const settings = await read('RunMind.Runner/RunnerSettings.cs');
  const commands = await read('RunMind.Runner/RunnerCommandService.cs');
  const hotkey = await read('RunMind.Runner/GlobalHotkey.cs');
  const systemManager = await read('Shared/SystemManager.cs');

  assert.match(settings, /^using System\.IO;/m);
  assert.match(commands, /^using System\.IO;/m);
  assert.match(settingsWindow, /System\.Windows\.MessageBox\.Show\(/);
  assert.match(mainWindow, /System\.Windows\.MessageBox\.Show\(/);
  assert.match(hotkey, /_ => 0u,/);
  assert.match(systemManager, /private static bool WaitForProcessExit\(Process process\)/);
  assert.match(systemManager, /process\.WaitForExit\(\);\s*return true;/);
  assert.match(systemManager, /const string inventoryScript = """/);
  assert.match(systemManager, /RunPowerShellJson\(inventoryScript, new \{ query, maxResults = maximum \}, 30000\)/);
});


test('RunMind is a true inline launcher result and chat is a separate handoff', async () => {
  const pluginSource = await read('RunMind.Runner/RunnerPlugins.cs');
  const main = await read('RunMind.Runner/MainWindow.xaml.cs');
  const xaml = await read('RunMind.Runner/MainWindow.xaml');
  assert.match(pluginSource, /class RunMindInlinePlugin/);
  assert.match(pluginSource, /Id, "RunMind", \$"Inline answer/);
  assert.match(pluginSource, /RunnerPluginAction\.ShowInlineAnswer/);
  assert.match(main, /UpdateRunMindResult\("RunMind", "Generating inline answer/);
  assert.match(main, /CreateAnswerPreview\(response\)/);
  assert.match(main, /case RunnerPluginAction\.ShowInlineAnswer:[\s\S]{0,260}InlineAnswerText\.Focus\(\)/);
  assert.match(xaml, /Content="Open chat" Click="Chat_Click"/);
  assert.match(main, /new RunnerChatWindow\(contextPrompt, contextResponse/);
  assert.doesNotMatch(main, /new RunnerChatWindow\([\s\S]{0,180}Owner = this/);
});

test('Runner Quick Host blocks workspace and file attachment entry points', async () => {
  const application = await read('Shared/ChatWindow/scripts/application.js');
  const styles = await read('Shared/ChatWindow/styles/chat.css');
  assert.match(application, /attachButton\.addEventListener\('click', \(\) => \{ if \(BOOT\?\.quickHostMode === true\) return;/);
  assert.match(application, /agentFolderButton\.addEventListener\('click', async \(\) => \{\s*if \(BOOT\?\.quickHostMode === true\) return;/);
  assert.match(application, /changeAgentFolderButton\.addEventListener\('click', async \(\) => \{\s*if \(BOOT\?\.quickHostMode === true\) return;/);
  assert.match(application, /attachButton\.disabled = true/);
  assert.match(application, /agentFolderButton\.disabled = true/);
  assert.match(application, /changeAgentFolderButton\.disabled = true/);
  assert.match(styles, /body\.quick-host-mode #attachButton/);
  assert.match(styles, /body\.quick-host-mode #agentFolderButton/);
  assert.match(styles, /body\.quick-host-mode #changeAgentFolder/);
});

test('cancelled inline requests always dispose their cancellation source', async () => {
  const main = await read('RunMind.Runner/MainWindow.xaml.cs');
  assert.match(main, /finally\s*\{\s*if \(ReferenceEquals\(_inlineGeneration, owner\)\)[\s\S]*?\}\s*owner\.Dispose\(\);\s*\}/);
});

test('Calculator keeps standard unary/exponent precedence and never evaluates arbitrary code', async () => {
  const plugins = await read('RunMind.Runner/RunnerPlugins.cs');
  const term = plugins.match(/private double ParseTerm\(\)[\s\S]*?\n    }/);
  const power = plugins.match(/private double ParsePower\(\)[\s\S]*?\n    }/);
  const unary = plugins.match(/private double ParseUnary\(\)[\s\S]*?\n    }/);
  assert.ok(term && power && unary);
  assert.match(term[0], /var value = ParseUnary\(\)/);
  assert.match(power[0], /Math\.Pow\(value, ParseUnary\(\)\)/);
  assert.match(unary[0], /return ParsePower\(\)/);
  assert.doesNotMatch(plugins, /\beval\s*\(|\bnew Function\s*\(/);
});


test('Runner explicitly qualifies WPF drawing controls and imports HTTP/IO APIs', async () => {
  const theme = await read('RunMind.Runner/RunnerThemeManager.cs');
  const settingsWindow = await read('RunMind.Runner/RunnerSettingsWindow.xaml.cs');
  const client = await read('RunMind.Runner/RunnerInlineModelClient.cs');

  assert.match(theme, /^using MediaColor = System\.Windows\.Media\.Color;$/m);
  assert.match(theme, /^using MediaColorConverter = System\.Windows\.Media\.ColorConverter;$/m);
  assert.match(theme, /private static MediaColor ParseColor\(/);
  assert.doesNotMatch(theme, /private static Color ParseColor\(/);

  assert.match(settingsWindow, /^using WpfComboBox = System\.Windows\.Controls\.ComboBox;$/m);
  assert.match(settingsWindow, /SelectTag\(WpfComboBox combo/);
  assert.match(settingsWindow, /GetTag\(WpfComboBox combo/);

  assert.match(client, /^using System\.IO;$/m);
  assert.match(client, /^using System\.Net\.Http;$/m);
  for (const type of ['HttpClient', 'HttpResponseMessage', 'HttpRequestMessage', 'HttpContent', 'StreamReader']) {
    assert.ok(client.includes(type), `inline model client should use ${type}`);
  }
});

test('Runner uses explicit IO and WPF types to avoid Windows Forms global-using collisions', async () => {
  const credentials = await read('RunMind.Runner/RunnerInlineCredentialsStore.cs');
  const settings = await read('RunMind.Runner/RunnerInlineSettings.cs');
  const theme = await read('RunMind.Runner/RunnerThemeManager.cs');
  const main = await read('RunMind.Runner/MainWindow.xaml.cs');
  const plugins = await read('RunMind.Runner/RunnerPlugins.cs');
  const client = await read('RunMind.Runner/RunnerInlineModelClient.cs');

  assert.match(credentials, /^#nullable enable\s+using System\.IO;/);
  assert.match(settings, /^#nullable enable\s+using System\.IO;/);
  assert.match(theme, /using WpfApplication = System\.Windows\.Application;/);
  assert.match(theme, /WpfApplication\.Current\.Resources/);
  assert.doesNotMatch(theme, /(?<!Wpf)\bApplication\.Current\.Resources/);
  assert.match(main, /using WpfClipboard = System\.Windows\.Clipboard;/);
  assert.match(main, /WpfClipboard\.SetText\(/);
  assert.doesNotMatch(main, /(?<!Wpf)\bClipboard\.SetText\(/);
  assert.match(plugins, /var parenthesizedValue = ParseExpression\(\);/);
  assert.match(plugins, /return parenthesizedValue;/);
  assert.match(client, /HttpRequestException when|HttpRequestException \{ StatusCode: null \}/);
  assert.match(client, /StatusCode is \{ \} statusCode/);
  assert.doesNotMatch(client, /\(int\)http\.StatusCode\b/);
});
