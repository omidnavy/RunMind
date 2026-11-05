# RunMind build and setup notes

## Source overlay

This release package is an overlay for the complete RunMind 1.10.7 source tree (or a later compatible 1.10.x tree). The trimmed development archive used to prepare it omitted the `Assets`, `lib`, `Vendor`, `OCR`, and other supporting resources. Keep those directories from the complete project when applying the overlay.

## RunMind 1.10.10 — multiline PowerToys Run prompt

- In PowerToys Settings → PowerToys Run → RunMind, **Custom system instructions** is now a native multiline text editor. Users can paste or type multiple paragraphs and press Enter for line breaks; PowerToys provides the vertical scrollbar.
- The setting retains the `InlineSystemPrompt` key, so the existing preference is preserved. Legacy literal `\n` line-break sequences are still normalized. The 12,000-character limit is enforced in the settings editor and runtime.
- Requires PowerToys Run 0.80.0 or later, which introduced the `MultilineTextbox` additional-option type.

## RunMind 1.10.9 — flyout scrolling hotfix

- All toolbar flyouts and the Conversations menu now allow uninterrupted scrolling. Scrolling inside a menu no longer triggers the document-level viewport-reposition handler.
- The shared placement routine preserves the outer panel and nested scroll offsets when the window is resized or the panel otherwise needs repositioning. It no longer removes the panel height limit during measurement, which was causing Chromium/WebView2 to clamp its scroll position back to zero.
- Flyout scroll boundaries are contained so the page behind the menu does not unexpectedly move.

## RunMind 1.10.8 — Custom instructions and generation settings

### PowerToys Run
- Open PowerToys Run settings, select RunMind, and use **Custom system instructions (optional)** for PowerToys inline requests only.
- The PowerToys Additional Options control is a single-line text field. Enter the literal sequence `\n` where a line break is required. The prompt is limited to 12,000 characters.
- Temperature, Top P, Max output tokens, and Seed are optional. Leave them empty to use provider defaults. Valid ranges are Temperature `0–2`, Top P `0–1`, Max output tokens `1–1,000,000`, and Seed `0–2,147,483,647`.
- These settings are independent from the chat application's local preferences and are not transferred when a pending query is opened in chat.

### Chat application
- Open the toolbar **Generation settings** menu.
- Edit **Custom system instructions** in the multiline editor (up to 16,000 characters). The character counter and preview show how the prompt is composed. **Clear custom instructions** is separate from **Reset generation settings**.
- The chat prompt is saved in the chat WebView's local preferences and applies to normal chat, Agent Mode, Deep Research, and specialist sub-agents. Active agent/research tasks retain the prompt snapshot with which they started.
- RunMind's built-in tool contracts, approval rules, operational constraints, and mode instructions remain active. User preferences do not grant permissions or replace those requirements.
- Temperature, Top P, Max output tokens, Context length where supported, and Seed are applied to regular and tool-calling requests. Some provider/model combinations reject optional parameters; RunMind only removes a parameter after an explicit validation error identifies it.

## Build

Use the complete Windows source tree and its existing build script:

```powershell
.\Build-Release-x64.bat
```

The build script, PowerToys reference assemblies, assets, vendor resources, and OCR data must be present at the paths expected by the project files. This trimmed overlay does not include those dependencies.

## Validation boundary

JavaScript syntax, request mapping, settings composition/persistence, XML/JSON metadata, and package integrity are checked during preparation. A Windows/.NET 10 compilation and live PowerToys, WebView2, Ollama, and LM Studio integration test must still be performed on Windows.
