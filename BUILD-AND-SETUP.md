# RunMind 2.4.0 — Build and setup

This guide covers the current full-source release and retains explicitly labeled historical notes where useful.

## Historical notes from RunMind 2.1.6 — Reliability, bounded processing, and workspace safety

All three hosts—Standalone, PowerToys Run, and PowerToys Command Palette—link the same canonical `Shared/ChatWindow.html` entry page, `Shared/ArtifactTools.js` helpers, and the external `Shared/ChatWindow/styles/`, `Shared/ChatWindow/theme/`, and `Shared/ChatWindow/scripts/` resources. The `theme/` folder is runtime-discovered: each `<id>.css` file starts with a `RunMind-Theme` metadata comment containing its `id`, display `name`, and `modes` array (`light`, `dark`, or both), followed by one palette rule per declared mode. The native bridge validates each mode independently and skips malformed/unreadable themes. Remove a file to remove that theme. No HTML/JavaScript registry or build-time generator is needed. The JavaScript files are grouped by responsibility (core UI, providers, Agent subsystems, research, conversations, and application startup) and loaded as ordered classic scripts to preserve the existing shared runtime state and initialization. Each host deploys the complete resource tree beside its chat window and uses the shared native `Shared/AgentHostBridge.cs` implementation for supported Windows capabilities. Command Palette runs its own chat process from its packaged extension executable; it does not launch or depend on the Standalone executable. The hosts use separate WebView2 profiles to avoid simultaneous profile locks. Appearance preferences are shared through the native preference file. Inline model settings are shared by the PowerToys Run and Command Palette hosts through `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json`; full chat preferences and browser-backed conversation data remain local to each WebView2 profile.

There is no `AgentHostAdapter.js` in this source snapshot. The existing native adapter is `Shared/AgentHostBridge.cs`; JavaScript tool definitions and orchestration are organized under `Shared/ChatWindow/scripts/`, with `ChatWindow.html` remaining the entry page. No speculative adapter file was added.

### Overlay installation

The historical **2.1.6 release ZIP** was a changed-files-only overlay. It was applied over the **complete RunMind 2.1.5 source tree**, preserving all existing files and directories. It contained only the files changed for 2.1.6; all earlier release fixes had to be present in the 2.1.5 baseline. Do not replace the source tree with the overlay ZIP. The supplied source archive does not contain every build-time resource referenced by the project; retain/provide the original `lib`, `Assets`, `Vendor`, `OCR`, and `screenshots` directories from the full project checkout. The existing `Shared/NativePageReader.cs` remains unchanged and its integration in both hosts is retained.

### 2.1.6 reliability and processing limits

The shared ChatWindow runtime separates its workspace and conversation IndexedDB connections, records failed/cancelled assistant turns, bounds comparison-model and follow-up context, and rate-limits AI comparison streaming renders. Workspace operations use per-path serialization and post-approval snapshot checks; directory scans, backup retention, CSV processing, and duplicate hashing have explicit budgets. Private-host validation normalizes trailing DNS dots, and authenticated requests to configured remote Reader endpoints reject HTTP redirects (configure the canonical endpoint URL). The existing Native Reader DNS/IP and redirect protections remain unchanged. The AI comparison modal traps keyboard focus. These changes do not migrate API credentials out of WebView storage and do not virtualize the main transcript.

### Build and requirements

- Build on Windows with the .NET 10 SDK.
- Retain the PowerToys reference assemblies under `lib\x64` for the PowerToys plugin project.
- The source ZIP includes the `Assets`, `lib\x64`, and `screenshots` folders. Pinned PDF.js, Tesseract.js, and English/Persian OCR model resources are generated under `Vendor` and `OCR\tessdata` by `Prepare-RunMind-OCR.ps1` on the first build; that initial preparation requires internet access. Once those generated files are present, subsequent builds can proceed offline.
- Install Git for Windows and ensure `git.exe` is available on `PATH` to use native Git tools.
- Build the PowerToys plugin, Command Palette extension project, and Standalone app using `Build-Release-x64.bat`. Install the PowerToys Run plugin separately with `Install-RunMind-PowerToys-Run.bat` after fully exiting PowerToys. Package/install or uninstall the Command Palette extension separately with `Install-RunMind-CommandPalette.bat`. The PowerToys plugin also requires the matching PowerToys environment for deployment and runtime testing.

## RunMind 2.2.7 source snapshot

This ZIP is a full source-project snapshot, not a changed-files-only overlay. It includes the application projects, shared sources, brand assets, PowerToys reference assemblies under `lib\x64`, screenshots, build/package/install scripts, and documentation. The generated PDF/OCR runtime resources are intentionally not prebundled: `Prepare-RunMind-OCR.ps1` downloads the pinned package versions and language models into `Vendor` and `OCR\tessdata` during the release build. The integration currently targets Windows x64. `Build-Release-x64.bat` is the single release entry point: it restores the solution including the `win-x64` runtime pack, cleans and builds Standalone, the PowerToys Run plugin, and the Command Palette extension, then creates and signs the Command Palette MSIX. It opts out of .NET CLI telemetry for all `dotnet` processes started by the script and suppresses first-run/logo output. It does not install any application. `Install-RunMind-CommandPalette.bat` trusts the public signing certificate when needed, installs the already-built MSIX, and makes a guarded attempt to configure the direct RunMind alias; it does not restore packages, run OCR preparation, compile, or package anything.

## Installing the PowerToys Run plugin

After a successful Release x64 build, close PowerToys completely using its system-tray menu (**Exit**), then run:

```bat
Install-RunMind-PowerToys-Run.bat
```

The installer checks for PowerToys processes before copying. If PowerToys is running, it warns and exits without changing the deployed plugin. When PowerToys is closed, it mirrors `Community.PowerToys.Run.Plugin.RunMind\bin\x64\Release` into `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\Community.PowerToys.Run.Plugin.RunMind`, removing obsolete files from previous deployments. Start PowerToys again after the installer reports success.

## PowerToys Command Palette extension (2.2.7)

`RunMind.CommandPalette` is an x64 MSIX-packaged WinRT COM extension. Its package manifest registers an out-of-process COM server and the `com.microsoft.commandpalette` app extension. PowerToys discovers the extension through the Windows package catalog; the COM class ID in the package manifest must match the extension's `[Guid]` and the `CreateInstance` class ID.

### Build and install without Visual Studio

Install the .NET 10 SDK and PowerToys with Command Palette enabled. The Command Palette project restores its pinned Windows SDK/MSIX build tools through NuGet; a separate Visual Studio deployment step is not required. From the repository root:

- `Build-Release-x64.bat` — prepare pinned OCR assets, clean and build the full Release x64 solution (all three hosts), then build and sign the Command Palette MSIX. This is the only build/release entry point.
- `Build-Release-x64.bat build` — same as the default release build.
- `Install-RunMind-CommandPalette.bat` — install the prebuilt `Release\CommandPalette\x64\RunMind.CommandPalette.msix`. This script does not invoke the .NET SDK or rebuild any project.
- `Install-RunMind-PowerToys-Run.bat` — copy the already-built PowerToys Run plugin output into the current user's PowerToys Run Plugins directory. It refuses to run while PowerToys processes are active and mirrors the output to remove stale files. This script does not build the solution.
- `Install-RunMind-CommandPalette.bat uninstall` — remove the current user's RunMind Command Palette package.

The release build creates or reuses a self-signed code-signing certificate in the current user's Personal certificate store. Its subject matches the package publisher, `CN=Omid Navy`, and the release output includes only its public `.cer` file alongside the MSIX. The private key stays in the current user's certificate store; no `.pfx` file or password is written into the repository or release output. On first install, Windows requests administrator approval once to trust that public certificate in the Local Machine **Trusted People** store. The certificate is retained after uninstall so later local releases do not need a new trust operation. This self-signed certificate is intended for this machine or controlled test devices, not public distribution.

`Build-RunMind-CommandPalette.ps1`, called by the main release script, creates the `.msix`, validates its package identity, Command Palette app-extension declaration, COM class ID, assets, and `Public` folder, and writes the signed package plus public `.cer` to `Release\CommandPalette\x64`. The separate deployment script verifies the package signature and publisher, establishes certificate trust if needed, installs the prebuilt package with `Add-AppxPackage -Path`, and then attempts the guarded RunMind direct-alias setup. Do not use `Add-AppxPackage -Register` for this normal workflow: that registers a loose development layout rather than installing the packaged app.

After installation, the installer attempts to create the direct **RunMind** alias for **Open chat with empty context** in the Command Palette host settings. It does this only when the host is closed, its existing alias collection is initialized, and the settings file can be identified unambiguously. Before writing, it creates a timestamped backup of `settings.json`. If it skips the change, close Command Palette and run `Configure-RunMind-CommandPalette-Alias.ps1` from the source folder. You can then open PowerToys Command Palette and run **Reload Command Palette extensions**. Then search for **RunMind** or **Open chat with empty context**. To configure the inline model, open Command Palette's Installed Extensions settings and select RunMind. There is intentionally no top-level **Configure RunMind** command; it previously opened the chat window instead of a real extension settings page. If the extension is still not listed, fully exit and restart PowerToys before investigating its extension logs.

The package manifest sets `AppListEntry="none"` so the COM host does not appear in Start or Windows Search. Keep the MSIX package installed: it registers the COM server and app extension that PowerToys needs. It remains visible in Windows Installed apps so it can be uninstalled. PowerToys starts the executable through the registered COM server. If the extension still fails to load, inspect `%LOCALAPPDATA%\RunMind\CommandPalette\Logs\com-server.log`. The log identifies whether Windows launched the process and which COM-server initialization stage completed. A missing log means the activation did not reach `Main`; a log ending before `ComServer.Start() completed` points to startup/registration, while a completed startup followed by a provider error points to extension-provider initialization.

The Command Palette host offers a branded **RunMind** fallback and **Open chat with empty context**. The inline fallback and pinned Dock page each keep independent history and provide **Start fresh** to clear that page without opening the full chat app. Type a natural-language query (or `RunMind <question>`) and select the **RunMind** fallback to open an inline conversation page inside Command Palette. The configured local model starts generating only after selection and returns the answer directly in that page; output streams into the page, and follow-up questions remain in the same conversation. **Open chat with context** transfers the complete ordered inline transcript to the independent Command Palette chat window. If generation is still running, the pending user question is transferred without an unfinished assistant response. The destination restores the transcript as the active conversation, so follow-up requests retain the inline history. No answer copying or manual context pasting is required. The Installed Extensions → RunMind settings page configures Ollama or LM Studio, discovers models from the selected provider, and also accepts any manually entered model ID. Its thinking/reasoning mode is shared by both providers; keep-alive and CPU threads remain Ollama-specific, while Nemotron conversational mode is LM Studio/model-specific. These settings are stored in `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json`; PowerToys Run and Command Palette use the same file. The host does not require a Standalone executable path or `RUNMIND_STANDALONE_EXE`.

The extension SDK does not expose an extension-level alias property. RunMind therefore uses a small post-install helper to configure the host-managed direct alias; it does not modify PowerToys binaries. The helper writes only the `RunMind` alias entry, preserves other aliases, and backs up the host settings file first. Command Palette must be fully closed while the helper runs.

The Command Palette extension SDK is versioned independently of RunMind. The project pins `Microsoft.CommandPalette.Extensions` 0.12.260812002, `Microsoft.Windows.CsWinRT` 2.3.1, `Microsoft.Windows.SDK.BuildTools.MSIX` 1.7.261102100, `Microsoft.Windows.SDK.BuildTools` 10.0.26100.9457, and `Shmuelie.WinRTServer` 2.2.1. Both Windows SDK build-tools packages are private to this project. The MSIX package generates the package, while the separate Windows SDK BuildTools package supplies the Windows SDK MSBuild infrastructure (`UAP.props`) needed by MSIX/PRI build targets. Keep the Command Palette target framework and package manifest minimum OS at 10.0.22621.0; the 26100 BuildTools package is a build-time dependency and does not raise the app's declared minimum Windows version. The Command Palette extension API is still pre-1.0 and may change; update packages only after checking PowerToys compatibility notes and retesting the package.

The Command Palette project resolves CsWinRT's Windows metadata from the `Microsoft.Windows.SDK.NET.Ref` targeting pack restored for its target framework. This avoids CsWinRT's separate `Platforms\UAP\<version>\Platform.xml` lookup, which can fail on incomplete or unreadable Windows SDK installations even when the matching .NET Windows metadata is available. The MSBuild target reads the resolved targeting-pack path; it does not hard-code a NuGet cache path or package version.

## Project Profiles and Workspace Context

Project Profiles are shared between Standalone and PowerToys Run in `%LOCALAPPDATA%\RunMind\ProjectProfiles`. Profiles capture project instructions, provider/model and connection ID, selected Agent settings, enabled Skill names, MCP server IDs, a workspace folder-name hint, and permission policy. Credential fields and browser folder handles are not copied; do not put secrets in freeform instructions.

Folder handles are browser capabilities stored in IndexedDB within the current WebView profile and keyed by Workspace Context ID. They are not shared across WebView profiles or exported. When moving to the other host or another device, use the folder reconnect/choose action. A Project Profile's folder name is only a hint; applying it never silently selects that folder. Applying a profile creates a new context and requires browser folder access and native Git repository selection to be granted again.

Saved conversations use `.runmind` format version 2 and continue to import version 1. MCP server IDs are references, not credentials; a server reconnects only when a matching locally trusted configuration exists and the user explicitly reconnects it.

### Git tools

Install Git for Windows and configure credentials through Windows Git Credential Manager when the remote requires authentication. Git clone supports HTTP/HTTPS URLs, including self-hosted Gogs/Gitea installations. The native folder picker asks where to clone or selects an existing repository. The selected native repository is scoped to the active conversation context; switching to another context requires selecting/cloning again.

Supported operations include status, diff, log, show, local branches, remotes, stage/unstage, commit, create/switch branch, merge, revert, cherry-pick, stash, fetch, pull, push, hard reset, clean, restore, and branch deletion. Fetch/pull/push require an explicit remote and branch. Git worktrees and linked `.git` directories are not supported in 2.1.0. There is no general terminal or arbitrary process-execution tool. Mutating operations use the shared approval policy; commits and destructive operations require fresh approval.

A cloned repository is authorized for native Git. To use browser-based file tools against it, separately call `request_folder_access` and choose the cloned directory in the WebView's folder picker. Browser folder grants and native Git selection are different capabilities.

### Agent Settings layout and flyouts

Agent Settings is organized into four tabs: **Agent**, **Project**, **Skills**, and **Security**. The redesign changes presentation and navigation only; existing setting controls and their save/apply/approval handlers retain their IDs and behavior. All toolbar flyouts remain attached to their trigger. A tall panel opens below when it fits, flips above only when needed, and otherwise scrolls within the larger side of the trigger rather than centering over and covering the toolbar.

### Branch Comparison

Branch Comparison has two modes. **Single response** is the default: it compares one question/answer turn from each branch, initially selecting each branch's latest turn. The Branch A and Branch B selectors are independent, so branches can have different turn counts and users can explicitly choose earlier turns. An unanswered turn is shown as unanswered; RunMind does not silently substitute an older response, and AI comparison remains disabled until both selected turns have answers.

**Whole branch** displays and sends the complete ordered history of both branches. It includes user prompts, assistant responses, per-message provider/model attribution, available text attachment content, and recorded model thinking in separately labeled expandable sections. The AI dossier and Markdown export retain the selected scope. Large histories may exceed a model's context limit; RunMind shows an approximate token estimate and does not silently omit turns.

### Agent Skills and Skills Manager

RunMind uses the open Agent Skills folder format. Global Skills live under `%LOCALAPPDATA%\RunMind\Skills\<skill-name>\SKILL.md`; project Skills live under `<workspace>\.agents\skills\<skill-name>\SKILL.md`. The required frontmatter fields are `name` and `description`.

Use **Agent Settings → Agent Skills** to switch between **Global skills** and **Current project skills**, refresh the installed list, inspect each Skill description, install a local Skill folder, or remove the selected installation after confirmation. Project operations require the current workspace folder to be authorized. The global and project scopes are independent: removing one does not remove a same-named Skill installed in the other scope. Installation does not overwrite an existing Skill of the same name.

Manual installation remains supported: place a Skill folder containing `SKILL.md` in `%LOCALAPPDATA%\RunMind\Skills\<skill-name>\` for global use, or in `<workspace>\.agents\skills\<skill-name>\` for project use. `list_agent_skills` discovers metadata; `load_agent_skill` loads the selected `SKILL.md` and up to 10 requested references on demand. `SKILL.md` and each reference are limited to 128 KB. Scripts/assets may be included in a Skill folder, but scripts are never executed automatically and Skills cannot grant tool permissions.

### Conversation storage and saved conversations

Unsaved conversation branches are temporary and scoped to the current chat window. `sessionStorage` holds the window's session identifier; IndexedDB stores the full-size temporary branch archive so it can be restored after a reload. A separate window gets a separate session. Starting a new conversation retires the previous temporary session. Normal window close removes the session archive, Deep Research checkpoints/history, and temporary Agent activity; if the process crashes or is terminated before cleanup runs, the next startup expires orphaned sessions after a 24-hour grace period. Research checkpoints are restored only by their owning window session; a new chat never claims another window's paused task.

Local Storage is not used for conversation branch transcripts. It remains available for application preferences and other persistent settings. IndexedDB is also still used for browser workspace folder handles; the temporary conversation cleanup does not delete workspace grants. Legacy 2.1.0 Local Storage branch snapshots are removed on first 2.1.1 startup. Legacy IndexedDB branch archives and unowned research checkpoints receive a 24-hour grace period; legacy research-history rows that have no owning-session identifier are removed during cleanup. Save a conversation explicitly as a `.runmind` file when it should be retained, reopened later, or moved between windows/hosts. Version 1 and version 2 `.runmind` files remain supported.

### Reliable downloads

`download_file` saves the original HTTP/HTTPS response bytes. With a selected browser workspace, RunMind first attempts browser fetch for files up to 10 MB; CORS, network restrictions, or size limits may trigger the native fallback. Without browser folder access, the desktop host uses the native Save dialog. Native downloads stream in 1 MB blocks to a `.runmind.part` file, support Range resume when a matching ETag or Last-Modified validator is available, calculate SHA-256, optionally compare an expected digest, show progress, support cancellation, and finalize without overwriting an existing file. The native downloader limits transfers to 4 GB / two hours and blocks private, loopback, link-local, and reserved network destinations. It does not use cookies or the system proxy. Downloads are never extracted or executed automatically.

### Permission defaults

Git reads default to **Allow**; local Git writes, remote Git operations, and downloads default to **Ask**. The policy can be changed for the active conversation and saved into a Project Profile. Destructive Git actions are always **Always ask** and cannot be relaxed. Native Git folder selection asks separately. Existing file-tool approvals and the NativePageReader bridge remain in place.

### Validation boundary

The overlay is validated against the reconstructed 2.1.1 source tree (including the 2.1.1 build correction) at source level (JavaScript syntax, XML/JSON, static integration invariants, exact file-diff reconstruction, and ZIP integrity). A Windows/.NET compile and live WebView2/PowerToys tests must still be performed in the target Windows environment; they are not represented as completed by this package.

## RunMind 2.0.9 — configurable PowerToys Run timeouts

PowerToys Run now exposes separate **Model discovery timeout** and **Provider connection timeout** settings. Their defaults remain 8 and 15 seconds, matching the previous hard-coded values. Both accept 1–300 seconds; missing or invalid saved values fall back to the defaults. Model discovery applies to the provider model-list request and response-body read. Provider connection applies to the initial generation request through its first response headers. The existing **Request timeout** remains the overall inline generation-request limit. These settings are part of the shared inline-model configuration used by PowerToys Run and Command Palette. They do not affect full chat sessions.

This overlay contains changed files only. Extract it over the complete RunMind 2.0.8 source tree, retaining excluded resources such as `lib`, `assets`, `Vendor`, `OCR`, and `screenshots`, then build on Windows with `Build-Release-x64.bat` using the .NET 10 SDK and the PowerToys reference assemblies.

### Think preference when editing prompts

Each newly sent user message records the Think toggle selected for that prompt. Editing the prompt restores that selection independently of the assistant's effective provider request flag, which can be suppressed by Agent/MCP/Deep Research mode. Cancelling an edit restores the previous unsent draft's Think selection. Older `.runmind` files without this optional field remain supported and use the paired assistant response's request flag/thinking content as a fallback.

### Branch comparison

Branch comparison selects the latest assistant response that follows each branch's own most recent user prompt. Different branch message counts are supported. If the latest prompt in either branch has no answer yet, RunMind explains which branch is incomplete rather than substituting an older response. Each comparison card shows the turn number and prompt preview.


### Command Palette provider authentication

The Command Palette extension settings have separate optional **API key** and **custom authentication header** fields for both Ollama and LM Studio. Secret inputs are masked in the form. The API key is sent using `Authorization: Bearer`. A custom header accepts a complete `Header-Name: value` line and is used for both model discovery and generation. If the custom header is also named `Authorization`, it replaces the automatically generated Bearer header. Credentials are not written to diagnostics, but are stored in the per-user RunMind settings JSON without DPAPI encryption; protect the Windows user profile containing the file. PowerToys Run settings saves explicitly preserve these Command Palette-only credential fields.

### Enter and Shift+Enter in inline conversations

The Command Palette extension uses the host's Adaptive Card form control. The current host submits Enter only when the focused text box is single-line (`AcceptsReturn` is false). The RunMind prompt needs a multiline text box, so Enter inserts a newline and the Send action must be clicked. Enter-to-send plus Shift+Enter-newline requires a PowerToys Command Palette host change; an out-of-process extension cannot handle that host keyboard event. See `docs/COMMAND-PALETTE-KEYBOARD-HOST-PATCH.md` for the exact host-side behavior needed.


## Project intelligence and developer workflows (2.3.0)

Open Agent settings → Project and authorize the project folder in the current host. Select **Build / refresh index** to create a local index. The index is scoped to the active Workspace Context and stored in the existing WebView IndexedDB database; it is not exported with conversation files or Project Profiles. Refresh after changes made outside RunMind. The index is deliberately bounded and heuristic: it captures source text, file metadata, common manifests, supported import declarations, selected symbol declarations, and dependency/framework hints, but it is not a compiler-grade semantic index or language server. Agent tasks can call `project_validate` to parse JSON/XML manifests and discover likely test files/scripts; this is static inspection, not command execution.

The same Project page provides built-in developer workflows and workspace-scoped custom workflows. They are instruction templates executed by the existing Agent runtime; approvals, cancellation, task history, Skills, workspace tools, and Git operations remain the existing mechanisms. There is still no general shell/process execution tool.

Indexing skips common dependency/build/cache folders and credential/secret paths, ignores binary/unsupported files, and skips individual files over 256 KiB. Common secret assignments, bearer tokens, credentialed URLs, private-key blocks, and recognizable token patterns are redacted before indexed text is stored. A refresh is limited to 10,000 files, 50,000 directory entries, depth 10, and 24 MiB of newly read data. A partial index reports the limit that stopped the scan. Rebuild after external project changes; unchanged files are reused using relative path, size, and last-modified timestamp.


## Native PowerShell and command execution (2.4.0)

1. In an Agent chat hosted by Standalone, PowerToys Run, or Command Palette, ask RunMind to call `select_native_workspace` and choose the project directory in the native folder picker. This is separate from the browser workspace grant because WebView2 does not expose an absolute path for its directory handle.
2. Ask the Agent to run a PowerShell script with `powershell_execute`, or an executable found on PATH with `run_native_command`. Every execution displays a fresh approval card.
3. The optional working directory is relative to the selected native workspace. The native executable tool accepts `.exe` only and passes arguments separately. Use PowerShell for scripts and package-manager commands that require shell resolution.
4. Commands run as the current Windows user. They are not sandboxed and can access resources available to that account. Do not approve untrusted scripts. Execution is cancellable, capped at 15 minutes, and captures up to 512 KB of output per stream.

Project indexing now starts automatically when a browser workspace is selected or restored. While the chat is visible, RunMind periodically scans workspace metadata every 60 seconds and incrementally refreshes changed records. Manual refresh remains available. This is polling, not an instantaneous filesystem watcher.
