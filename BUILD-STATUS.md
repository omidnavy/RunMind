# RunMind 2.6.1 — System Manager UI & Model Compatibility

## Release bundle status

- Full-source baseline: RunMind 2.6.0 source bundle, updated in place for this release.
- Preserves Standalone, PowerToys Run, PowerToys Command Palette, the shared ChatWindow, Agent Core, native bridge, Skills, MCP, Deep Research, Git, workflows, Project Profiles and existing permissions.
- System Manager discovery now ranks flexible application matches across executable and packaged Windows applications, with bounded live refresh on stale index/search miss.
- Launches use a stable application ID from current discovery, not a model-supplied executable path. Existing desktop windows are reused/activated by default; failed foreground activation does not silently start a duplicate.
- Adds explicit new-window/new-process choices, persistent ask/reuse/new-window/new-process per-application preferences, and graceful close requests with mandatory native confirmation.
- Adds current-session tool inventory and makes Global skill discovery/loading available without a project folder while retaining workspace authorization for Project skills.
- Adds the adapter manager UI, confirmed custom adapter removal, and the first-party RunMind prompt adapter.
- Merges System Manager policy into the leading system message for compatibility with strict model chat templates; normalizes action outcome reporting.
- Fixes checkbox dimensions in settings fields.
- Synchronizes host/package metadata to 2.6.1 / 2.6.1.0.

## Validation performed in this environment

See `VALIDATION-REPORT.md` for exact checks and limitations. Windows/.NET compilation and live Windows behavior have not been verified in this environment. JavaScript syntax, HTML ID consistency, JSON/XML parsing, tool-to-native-dispatch wiring, C# lexical delimiter balance, release metadata, ZIP integrity, and SHA-256 are checked here.

## Target-machine validation still required

- Build the full solution with the .NET 10 Windows targeting pack and repository package dependencies.
- Test Calculator discovery through Windows Apps folder, Firefox/Firefox Developer Edition matching, Steam discovery, aliases, stale catalog refresh and ambiguous results.
- Test existing-window activation, Windows foreground restrictions, named-browser URL handoff, new-window/new-process behavior, packaged activation, graceful close, and per-app preferences.
- Verify tool inventory against actual tool definitions with/without workspace access and connected MCP servers.
- Verify global and project skill listing/loading with and without a project workspace; confirm unsupported bundled/Bionic skills are not falsely reported.
- Run all existing Standalone, PowerToys Run and PowerToys Command Palette smoke/regression tests.
- Re-run existing Git, workspace, indexing, conversation, Project Profile, Skills, MCP, Deep Research, workflow, and PowerShell/native execution checks.

## Historical release notes



## 2.5.1 update

- System Manager has its own toolbar flyout, separate from Agent Settings.
- Application discovery resolves Start Menu shortcut targets and filters missing executables.
- Application launch now uses direct executable startup and checks for a matching running process before returning success.
- Windows/.NET build and live application-launch behavior still require validation on a Windows target.


## 2.5.2 update

- Added a conditional Agent system instruction requiring fresh System Manager discovery and launch for every new user request; prior conversation results cannot satisfy a new action.
- Named-browser URL requests are directed through the selected executable with the URL as an argument, not through the Windows default URL handler.
- Application search requires a non-empty query at the tool schema, JavaScript executor, and native bridge boundaries.
- Discovery now includes Windows uninstall registry entries and executable candidates under registered install locations, improving coverage for apps such as Steam.
- Existing process PIDs are snapshotted before launch. A surviving old PID alone is no longer treated as a verified launch; launcher exit/handoff and process-running states are reported separately.
- Default-handler opens report request acceptance only; they do not claim an application, visible window, focus, or navigation was verified.


## 2.6.2 validation status

Source updates include three bundled Agent Skills, native skill discovery/loading, shared System Management prompt guidance, adapter operation metadata, and content packaging for all three hosts. Static validation is performed in the accompanying report. Windows/.NET compilation and runtime behavior require Windows testing.


## 2.6.3 implementation status

- Added Notepad and File Explorer built-in adapter definitions.
- Added native Docker/Podman CLI operation with strict action/runtime/container validation and mandatory confirmation for start/stop/restart.
- Added four coding skills and shared coding execution policy.
- Added four additional RunMind-native skills adapted from the supplied Bionic bundle: file-history, skill-management, create-skill, and install-skill.
- Fixed the shared adapter override record initializer and connected the adapter tab, Edit, and Reset UI actions.
- System Manager includes separate Settings and Application Adapters tabs, bounded panel/list scrolling, built-in adapter override storage, Edit/Reset actions, and custom adapter management. Full RunMind conversation management through the native adapter remains unimplemented; the existing `.runmind` save/open UI is still available.
- Windows/.NET compilation and runtime validation are still required.
