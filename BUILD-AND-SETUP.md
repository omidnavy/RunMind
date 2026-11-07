## RunMind 2.0.8 — reliable inline answer refresh

The completed PowerToys Run inline answer now returns through the standard delayed-query result path. Generation remains asynchronous outside PowerToys' per-plugin execution gate. When a response is ready, RunMind requests one requery of the exact originating search through the public PowerToys API; the already-completed response is then returned immediately from the delayed query without another model request. A final current-operation check prevents an older completion from replacing a newer search.

The live prompt preview, global search, `llm` keyword, pending **Open in chat** result, provider settings, and cancellation safeguards remain intact. The `IResultUpdated` event is no longer used for this result flow.

This overlay contains changed files only. Extract it over the complete RunMind 2.0.7 source tree, retaining excluded resources such as `lib`, `assets`, `Vendor`, `OCR`, and `screenshots`, then build on Windows with `Build-Release-x64.bat` using the .NET 10 SDK and the PowerToys reference assemblies.
