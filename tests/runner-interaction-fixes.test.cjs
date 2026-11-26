const test = require('node:test');
const assert = require('node:assert/strict');
const { readFile } = require('node:fs/promises');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = (relative) => readFile(path.join(root, relative), 'utf8');

test('Every non-editable ComboBox surface toggles the popup, not only the arrow region', async () => {
  const appXaml = await read('RunMind.Runner/App.xaml');
  const settingsCode = await read('RunMind.Runner/RunnerSettingsWindow.xaml.cs');
  const template = appXaml.match(/<Style TargetType="ComboBox">[\s\S]*?<Style TargetType="ComboBoxItem">/);
  assert.ok(template, 'global ComboBox style exists');
  assert.match(template[0], /<ToggleButton Grid\.Column="0" Grid\.ColumnSpan="2" HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/);
  assert.match(template[0], /IsChecked="\{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource=\{RelativeSource TemplatedParent\}\}"/);
  assert.match(template[0], /ContentPresenter x:Name="ContentSite"[^>]*IsHitTestVisible="False"/);
  assert.match(template[0], /PART_EditableTextBox[\s\S]*?IsReadOnly="\{TemplateBinding IsReadOnly\}"/);
  assert.match(settingsCode, /EnableEditableComboBoxClickToOpen\(ModelBox\)/);
  assert.match(settingsCode, /comboBox\.Loaded \+= AttachEditorHandler/);
  assert.match(settingsCode, /EnableEditableComboBoxClickToOpen\([\s\S]*?PreviewMouseLeftButtonDown \+= \(_, _\) => comboBox\.IsDropDownOpen = true/);
});

test('Appearance mode and theme selections persist and apply immediately without Save settings', async () => {
  const ui = await read('RunMind.Runner/RunnerSettingsWindow.xaml');
  const code = await read('RunMind.Runner/RunnerSettingsWindow.xaml.cs');
  const themeManager = await read('RunMind.Runner/RunnerThemeManager.cs');
  const synchronizer = await read('Shared/WindowThemeSynchronizer.cs');
  for (const name of ['AppearanceModeBox', 'LightThemeBox', 'DarkThemeBox']) {
    const element = ui.match(new RegExp(`<ComboBox x:Name="${name}"[^>]*>`));
    assert.ok(element, `${name} is declared`);
    assert.match(element[0], /SelectionChanged="Appearance_SelectionChanged"/);
  }
  assert.match(code, /private void Appearance_SelectionChanged/);
  assert.match(code, /RunnerThemeManager\.SaveAppearance\(mode, lightTheme, darkTheme\)/);
  assert.match(code, /RunnerThemeManager\.ApplyToAllOpenWindows\(\)/);
  assert.match(code, /_isInitializing/);
  assert.match(ui, /Appearance changes apply and save immediately/);
  assert.match(themeManager, /public static void ApplyToAllOpenWindows\(\)/);
  assert.match(themeManager, /mainWindow\.RefreshNativeTheme\(\)/);
  assert.match(synchronizer, /public void RefreshAppearance\(\)/);
});

test('Clicking a RunMind result hands off that result prompt to the real Standalone chat', async () => {
  const ui = await read('RunMind.Runner/MainWindow.xaml');
  const code = await read('RunMind.Runner/MainWindow.xaml.cs');
  assert.match(ui, /PreviewMouseLeftButtonUp="ResultsList_PreviewMouseLeftButtonUp"/);
  assert.match(code, /private async void ResultsList_PreviewMouseLeftButtonUp/);
  assert.match(code, /string\.Equals\(result\.PluginId, "runmind"[\s\S]*?OpenStandaloneChatAsync\(result\.Value\)/);
  assert.match(code, /case RunnerPluginAction\.ShowInlineAnswer:[\s\S]*?await OpenStandaloneChatAsync\(/);
  assert.match(code, /if \(ResultsList\.SelectedItem is RunnerPluginResult \{ PluginId: "runmind" \}\) return/);
  assert.match(code, /e\.ClickCount > 1/);
  assert.match(code, /Response = contextResponse/);
});

test('RunMind result activation supports prompt-only, completed and partial response handoff', async () => {
  const code = await read('RunMind.Runner/MainWindow.xaml.cs');
  assert.match(code, /var wasGenerating = _inlineIsRunning/);
  assert.match(code, /var partialInlineResponse = wasGenerating \? InlineAnswerText\.Text : string\.Empty/);
  assert.match(code, /var responseMatchesPrompt = string\.Equals\(_lastInlineQuery, contextPrompt, StringComparison\.Ordinal\)/);
  assert.match(code, /if \(responseMatchesPrompt && wasGenerating[\s\S]*?contextResponse = partialInlineResponse/);
  assert.match(code, /_lastInlineResponse = response/);
});
