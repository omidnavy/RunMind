# RunMind Runner — Interaction fixes (2026-11-24)

## 1. ComboBox full-surface opening

The shared Runner `ComboBox` template now has a transparent toggle surface spanning both columns of the control. The selected-item presenter is hit-test transparent, so clicking the displayed value or the unused portion of the field reaches the same `IsDropDownOpen` toggle as the arrow. Hover/pressed states cover the whole field.

The editable model selector remains editable: its `PART_EditableTextBox` is layered above the toggle surface. Runner attaches a `PreviewMouseLeftButtonDown` handler to that generated editor when the control is loaded (including when its tab is first selected); clicking in the text portion opens the model list while preserving text entry. The handler is attached in `RunnerSettingsWindow` code-behind rather than in `App.xaml`, avoiding template event hookup ambiguity.

## 2. Immediate theme application and persistence

Appearance mode, Light theme and Dark theme ComboBoxes all use `Appearance_SelectionChanged`.

- Constructor initialization is guarded so loading the saved selection does not overwrite preferences.
- A user selection is immediately validated and persisted through the existing `AppearancePreferenceStore` message contract.
- Runner reapplies the palette across every currently open WPF window, including the launcher and settings dialog.
- The main window's `WindowThemeSynchronizer` refreshes its native caption theme from the saved preference.
- Appearance remains saved if the user closes Settings with Cancel. The Save settings button remains responsible for the other non-appearance Runner settings.

When Mode is System, the active Light/Dark palette follows the current Windows appearance; the selected palette for the inactive mode is saved for when that mode becomes active.

## 3. RunMind result single-click handoff

The unified results list handles `PreviewMouseLeftButtonUp`. When the clicked row belongs to the `runmind` plugin, Runner calls `OpenStandaloneChatAsync` using that result's own `Value` (the extracted prompt).

Enter uses the same handoff through `OpenSelectedResult`. RunMind is activated on the first click, so the second half of a double-click is ignored to prevent duplicate Standalone conversations. Other plugin results retain their normal double-click activation. The existing handoff routine transfers:

- the exact prompt stored on that RunMind result;
- the completed inline response when it belongs to that prompt;
- the currently displayed partial response if generation is still running;
- an error response when the inline request failed.

The prompt/answer are opened as transcript context in the real Standalone Full Agent, not regenerated. If no answer exists yet (for example, the user clicked during the debounce period), the prompt is still handed off and can be continued in Chat.

## Windows acceptance

1. Open every Runner select in Settings by clicking its displayed text/value, blank portion of the control and arrow. Confirm each opens the same dropdown.
2. Open the editable Default model selector by clicking its text area; confirm the list opens and that typing a model identifier still works.
3. Switch Appearance Mode between System, Light and Dark. Confirm the launcher and Settings window update before pressing Save settings.
4. Change the Light theme and Dark theme selections. Confirm the currently active palette updates immediately; switch Mode to verify the other palette is already saved.
5. Change a theme, click Cancel, reopen Settings and verify the new appearance preference remains saved.
6. In global mode, enter a prompt, wait for a completed inline answer, then single-click the RunMind result. Verify Standalone opens with that exact prompt and answer in the new conversation.
7. Repeat with the action keyword mode. Verify the keyword itself is not included as part of the handed-off prompt.
8. Click a RunMind result during generation. Verify the partial answer is handed off and no second generation occurs.
9. Keep an unrelated Standalone conversation open while clicking a RunMind result. Verify that conversation remains untouched and the handoff opens a new Full Agent conversation in the existing Standalone process.
10. Verify non-RunMind result clicks preserve their existing selection/activation behavior.

Portable source tests do not replace a Release x64 Windows build or interactive WPF acceptance. This environment does not include .NET SDK/MSBuild or a Windows desktop runtime.
