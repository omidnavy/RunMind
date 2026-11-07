## RunMind 2.0.9 — configurable PowerToys Run timeouts

PowerToys Run now exposes separate **Model discovery timeout** and **Provider connection timeout** settings. Their defaults remain 8 and 15 seconds, matching the previous hard-coded values. Both accept 1–300 seconds; missing or invalid saved values fall back to the defaults. Model discovery applies to the provider model-list request and response-body read. Provider connection applies to the initial generation request through its first response headers. The existing **Request timeout** remains the overall inline generation-request limit. These settings affect the PowerToys Run plugin only, not standalone chat.

This overlay contains changed files only. Extract it over the complete RunMind 2.0.8 source tree, retaining excluded resources such as `lib`, `assets`, `Vendor`, `OCR`, and `screenshots`, then build on Windows with `Build-Release-x64.bat` using the .NET 10 SDK and the PowerToys reference assemblies.
