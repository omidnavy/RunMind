# RunMind Runner — Window, Monitor, Sizing and Ranking

This document records the Runner window and settings behavior added to the consolidated 3.4.0 Runner source.

## Window interaction

- The main launcher uses WPF `WindowChrome` with a 42-DIP caption area and an 8-DIP resize border.
- The window is a normal, non-layered WPF window (`AllowsTransparency=false`) so native resizing and hit testing remain available.
- Header action buttons opt into hit testing in the chrome; the remaining caption area can be dragged.
- The minimum launcher size remains bounded so search and results remain usable when manually resized.

## Monitor targeting

- `OpenOnActiveMonitor` defaults to `true`.
- When the global hotkey is pressed, Runner obtains the current pointer position and selects the monitor containing that point.
- The window is centered in that monitor's working area, with a safe top offset.
- When the setting is disabled, Runner uses the primary monitor.
- The Runner manifest opts into Per-Monitor-V2 DPI awareness. Positioning and height calculations use the target display's effective DPI and work area.
- Monitor lookup and DPI calls are best-effort; failure must not prevent the launcher from opening.

## Dynamic height

- `AutoResizeToResults` defaults to `true`.
- The window adjusts its height from the current result count, up to eight visible rows. Additional results remain scrollable.
- Height is capped to the selected monitor's work area.
- When automatic sizing is disabled, Runner retains the user's manually chosen height.
- The user can still resize the window in either mode.

## Inputs and provider settings

- Single-line text boxes, password boxes and ComboBoxes use a consistent 38-DIP control height.
- The launcher search field is intentionally larger at 48 DIPs.
- The multiline system-prompt editor is intentionally taller and top-aligned.
- ComboBox selection, popup surface, items, hover/selected states and dropdown arrow use RunMind theme resources rather than the default white native popup.
- The settings navigation is reduced to General and Inline model. Generation/request controls live in Inline model.
- Ollama-specific endpoint, credentials, keep-alive and CPU controls are visible only for Ollama.
- LM Studio-specific endpoint, credentials and Nemotron compatibility controls are visible only for LM Studio.
- Shared settings such as timeouts, thinking and supported generation parameters remain common. Switching provider clears the model-discovery list so entries from the previous provider are not presented as current results.

## Global result ranking

- Plugin weights are integer values from 0 to 100; 50 is neutral.
- Higher values move that plugin's results earlier; lower values move them later.
- Weights apply only when RunMind global mode is active. Action-keyword mode retains the provider's normal ranking behavior.
- Scores are adjusted before the common 14-result cap so priority affects both ordering and which results survive.
- Weights are stored in the per-user Runner settings file and normalized on load.

## Windows acceptance checklist

1. Drag the window by its blank header area; confirm the header buttons remain clickable.
2. Resize from all four edges and corners; confirm minimum dimensions are respected.
3. With two or more monitors, move the pointer to each display and press the hotkey. Confirm the window opens centered on the display under the pointer.
4. Disable active-monitor targeting and confirm hotkey invocation always uses the primary display.
5. Test monitors with different Windows scaling factors (for example 100%, 125%, 150%); confirm the window remains on-screen and controls are not clipped.
6. Type searches returning 0, 1, several and more than 8 results. Confirm the height grows/shrinks and results beyond the visible rows scroll.
7. Disable automatic sizing, resize manually, then change queries. Confirm the manual height is retained.
8. Inspect TextBox, PasswordBox and ComboBox controls in light and dark themes. Confirm consistent heights, readable selected text and themed popup surfaces/items.
9. Switch Ollama ↔ LM Studio. Confirm only the selected provider's specific settings show, and model discovery entries from the previous provider disappear.
10. In global mode, set one plugin weight high and another low. Confirm the first appears earlier, including when the result cap is reached. Confirm weights do not alter action-keyword mode.

Portable source tests do not replace the Windows checks above. The current environment has no .NET SDK/MSBuild or Windows desktop runtime, so these changes must be compiled and exercised on Windows before certification.
