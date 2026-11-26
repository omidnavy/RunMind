# Runner unified search

RunMind Runner uses one search input and one ranked result list. Applications, files/folders, URLs, Calculator and RunMind contribute results through the launcher plugin registry.

## RunMind query modes

- **Action-keyword mode (default):** RunMind only receives a query prefixed with the configured keyword. The default is `llm`; for example `llm explain DNS caching`. The keyword is configurable in Runner Settings and accepts 1–32 ASCII letters, digits, hyphens or underscores.
- **Global mode:** RunMind receives every non-empty search query without a keyword. Other enabled launcher plugins continue contributing results to the same list. This may cause a model request for ordinary application/path/calculator searches; enable it only if that behavior is desired.
- RunMind inline generation is debounced and a newer query cancels the previous request.
- Model override syntax remains supported by the inline model client: `> model > prompt` after the activation prefix when keyword mode is enabled.

## Search input styling

The search TextBox explicitly binds foreground, caret, selection and selected-text colors to the active Runner theme. It uses a transparent inner control surface inside the themed rounded search border so WPF system selection colors do not leak into the input.

## Scope

These settings are Runner-local and do not alter PowerToys Run or Command Palette configuration. Full chat provider settings remain separate from Runner's inline Ollama/LM Studio provider profile.
