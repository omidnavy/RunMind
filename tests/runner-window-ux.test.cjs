const test = require('node:test');
const assert = require('node:assert/strict');
const { readFile } = require('node:fs/promises');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = (file) => readFile(path.join(root, file), 'utf8');

test('Runner window uses native draggable and resizable WPF WindowChrome', async () => {
  const xaml = await read('RunMind.Runner/MainWindow.xaml');
  const code = await read('RunMind.Runner/MainWindow.xaml.cs');
  assert.match(xaml, /WindowStyle="None" ResizeMode="CanResize"/);
  assert.match(xaml, /shell:WindowChrome\.WindowChrome/);
  assert.match(xaml, /CaptionHeight="42" ResizeBorderThickness="8"/);
  assert.match(xaml, /WindowChrome\.IsHitTestVisibleInChrome="True"/);
  assert.doesNotMatch(xaml, /AllowsTransparency="True"/);
  assert.match(code, /PositionOnConfiguredMonitor\(\)/);
});

test('Runner targets the cursor monitor by default and allows primary-monitor fallback', async () => {
  const settings = await read('RunMind.Runner/RunnerSettings.cs');
  const code = await read('RunMind.Runner/MainWindow.xaml.cs');
  const ui = await read('RunMind.Runner/RunnerSettingsWindow.xaml');
  assert.match(settings, /OpenOnActiveMonitor\s*\{\s*get;\s*set;\s*\}\s*=\s*true/);
  assert.match(code, /Forms\.Cursor\.Position/);
  assert.match(code, /Forms\.Screen\.FromPoint\(cursorPoint\)/);
  assert.match(code, /Forms\.Screen\.PrimaryScreen/);
  assert.match(code, /GetDpiForMonitor/);
  assert.match(code, /SetWindowPos/);
  assert.match(ui, /OpenOnActiveMonitorCheckBox/);
});

test('Runner automatically sizes to result count but permits a fixed user-sized window', async () => {
  const settings = await read('RunMind.Runner/RunnerSettings.cs');
  const code = await read('RunMind.Runner/MainWindow.xaml.cs');
  const ui = await read('RunMind.Runner/RunnerSettingsWindow.xaml');
  assert.match(settings, /AutoResizeToResults\s*\{\s*get;\s*set;\s*\}\s*=\s*true/);
  assert.match(code, /UpdateWindowHeightForResults\(isInitialState\)/);
  assert.match(code, /Math\.Clamp\(_results\.Count, 0, 8\)/);
  assert.match(code, /if \(!_settings\.AutoResizeToResults\) return/);
  assert.match(ui, /AutoResizeToResultsCheckBox/);
});

test('Runner combines generation controls into Inline model and shows only selected provider settings', async () => {
  const ui = await read('RunMind.Runner/RunnerSettingsWindow.xaml');
  const code = await read('RunMind.Runner/RunnerSettingsWindow.xaml.cs');
  assert.equal((ui.match(/<TabItem Header=/g) || []).length, 2);
  assert.match(ui, /<TabItem Header="Inline model">/);
  assert.doesNotMatch(ui, /<TabItem Header="Generation">/);
  for (const name of ['OllamaSettingsPanel', 'LMStudioSettingsPanel', 'OllamaGenerationPanel', 'LMStudioCompatibilityPanel'])
    assert.ok(ui.includes(`x:Name="${name}"`), `missing provider group ${name}`);
  assert.match(code, /OllamaSettingsPanel\.Visibility = isLmStudio \? Visibility\.Collapsed : Visibility\.Visible/);
  assert.match(code, /LMStudioSettingsPanel\.Visibility = isLmStudio \? Visibility\.Visible : Visibility\.Collapsed/);
  assert.match(code, /OllamaGenerationPanel\.Visibility/);
  assert.match(code, /LMStudioCompatibilityPanel\.Visibility/);
  assert.match(code, /ModelBox\.ItemsSource = null/);
  assert.match(code, /ModelDiscoveryStatus\.Text = \"Select Refresh models/);
});

test('Runner global-mode plugin weights affect ordering before the result cap', async () => {
  const settings = await read('RunMind.Runner/RunnerSettings.cs');
  const plugins = await read('RunMind.Runner/RunnerPlugins.cs');
  const ui = await read('RunMind.Runner/RunnerSettingsWindow.xaml');
  const code = await read('RunMind.Runner/RunnerSettingsWindow.xaml.cs');
  assert.match(settings, /Dictionary<string, int> PluginWeights/);
  assert.match(settings, /Math\.Clamp\(value, 0, 100\)/);
  assert.match(plugins, /if \(!runMindGlobalMode\) return result/);
  assert.match(plugins, /Rank = result\.Rank \+ \(\(50 - weight\) \* 2\)/);
  assert.match(plugins, /return ordered\.Take\(14\)/);
  for (const id of ['Applications', 'Files', 'Urls', 'Calculator', 'RunMind'])
    assert.ok(ui.includes(`x:Name="${id}WeightSlider"`), `missing ${id} ranking control`);
  assert.match(code, /PluginWeights = new Dictionary<string, int>/);
});

test('Runner text inputs and ComboBox popup use consistent themed dimensions and colors', async () => {
  const app = await read('RunMind.Runner/App.xaml');
  const main = await read('RunMind.Runner/MainWindow.xaml');
  assert.match(app, /<Style TargetType="TextBox">[\s\S]*?<Setter Property="Height" Value="38" \/>/);
  assert.match(app, /<Style TargetType="PasswordBox">[\s\S]*?<Setter Property="Height" Value="38" \/>/);
  assert.match(app, /<Style TargetType="ComboBox">[\s\S]*?<Setter Property="Height" Value="38" \/>/);
  assert.match(app, /<Popup x:Name="PART_Popup"[\s\S]*?Background="\{DynamicResource RunnerSurface\}"/);
  assert.match(app, /SelectionTextBrush="\{DynamicResource RunnerAccentContrast\}"/);
  assert.match(main, /SearchBox" Height="40" MinHeight="40"/);
  assert.match(main, /Padding="0" FocusVisualStyle="\{x:Null\}"/);
});


test('Runner theme selectors display friendly theme names instead of record ToString values', async () => {
  const ui = await read('RunMind.Runner/RunnerSettingsWindow.xaml');
  assert.match(ui, /<DataTemplate x:Key="ThemeNameTemplate">[\s\S]*?Text="\{Binding Name\}"/);
  assert.match(ui, /x:Name="LightThemeBox"[\s\S]{0,300}ItemTemplate="\{StaticResource ThemeNameTemplate\}"/);
  assert.match(ui, /x:Name="DarkThemeBox"[\s\S]{0,300}ItemTemplate="\{StaticResource ThemeNameTemplate\}"/);
  assert.doesNotMatch(ui, /DisplayMemberPath=/);
});

test('Runner inline search input has a consistent centered height without extra vertical padding', async () => {
  const main = await read('RunMind.Runner/MainWindow.xaml');
  const match = main.match(/<TextBox x:Name="SearchBox"[\s\S]*?\/>/);
  assert.ok(match, 'Runner search TextBox exists');
  assert.match(match[0], /Height="40" MinHeight="40"/);
  assert.match(match[0], /VerticalContentAlignment="Center"/);
  assert.match(match[0], /Padding="0"/);
  assert.match(match[0], /TextRenderingMode="ClearType"/);
  assert.doesNotMatch(match[0], /Padding="0,4"/);
});


test('Runner removes redundant branding and renders a compact unified search field', async () => {
  const xaml = await read('RunMind.Runner/MainWindow.xaml');
  const search = xaml.match(/<TextBox x:Name="SearchBox"[\s\S]*?\/>/);
  assert.ok(search);
  assert.doesNotMatch(xaml, /QUICK AI|One search ·|RUNMIND  ↵/);
  assert.match(xaml, /Text="RUNMIND"/);
  assert.match(search[0], /FontSize="17"/);
  assert.match(search[0], /Height="40" MinHeight="40"/);
  assert.match(xaml, /Text="Search anything…"/);
});

test('Runner empty state provides actionable quick starts and hides results for no-match queries', async () => {
  const xaml = await read('RunMind.Runner/MainWindow.xaml');
  const code = await read('RunMind.Runner/MainWindow.xaml.cs');
  assert.match(xaml, /x:Name="ResultsPanel"[\s\S]*?Visibility="Collapsed"/);
  assert.match(xaml, /Text="Try something"/);
  for (const tag of ['notepad', '12 * 8', '__runmind_suggestion__', 'https://example.com'])
    assert.ok(xaml.includes(`Tag="${tag}"`), `missing quick-start suggestion ${tag}`);
  assert.match(code, /ResultsPanel\.Visibility = isInitialState \|\| hasResults \? Visibility\.Visible : Visibility\.Collapsed/);
  assert.match(code, /EmptyState\.Visibility = isInitialState \? Visibility\.Visible : Visibility\.Collapsed/);
  assert.match(code, /Suggestion_Click/);
  assert.match(code, /_settings\.RunMindGlobalMode[\s\S]*_settings\.RunMindActionKeyword/);
});

test('Runner expands upward when its current position is in the lower half of the monitor', async () => {
  const code = await read('RunMind.Runner/MainWindow.xaml.cs');
  assert.match(code, /Forms\.Screen\.FromHandle\(handle\)/);
  assert.match(code, /GetWindowRect\(handle, out previousBounds\)/);
  assert.match(code, /previousBounds\.Top[\s\S]*?workingArea\.Top \+ \(workingArea\.Height \/ 2\)/);
  assert.match(code, /previousBounds\.Bottom - newHeightPixels/);
  assert.match(code, /Math\.Clamp\(targetY, workingArea\.Top/);
  assert.match(code, /GetWindowRect\(IntPtr hwnd, out NativeRect rectangle\)/);
});
