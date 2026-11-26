# RunMind 2.6.3 — Validation Report

**Release:** 2.6.3 — Developer Intelligence & System Manager
**Base:** RunMind 2.6.2 source bundle

## Implemented in this source bundle

- Replaced the Notepad++ built-in adapter with Windows Notepad.
- Added File Explorer folder-open and file-selection adapter operations.
- Added a dedicated Application Adapters tab, bounded System Manager panel height, and internal adapter-list scrolling.
- Added edit buttons for built-in adapters. Edits are stored as user overrides; reset restores shipped defaults. Built-in identity and application match remain fixed.
- Added native Docker/Podman CLI actions: list, inspect, start, stop, restart, and logs. Runtime selection supports auto/docker/podman; identifiers are validated; state-changing operations always require native confirmation. Commands use ProcessStartInfo.ArgumentList, not a shell.
- Added bundled `coding`, `debugging`, `testing-and-quality`, and `code-review` skills. Retained the existing three system-management skills.
- Added shared coding execution policy covering project inspection, minimal coherent changes, root-cause debugging, preservation of unrelated work, validation, and honest reporting.
- Updated host and package versions to 2.6.3.

## Checks performed

| Check | Result |
|---|---|
| JavaScript syntax (`node --check`) | Passed for all ChatWindow JavaScript files |
| JSON parsing | Passed for all JSON files |
| XML parsing | Passed for all project, XAML, and manifest files |
| Host version synchronization | Passed: Standalone, PowerToys Run, Command Palette, MSIX |
| Bundled skill files | Present; all seven skills included by each host project content glob |
| Adapter UI tab and bounded scrolling | Source assertions passed; no live WebView test |
| Built-in adapter override/reset wiring | Source inspection passed; no live Windows confirmation test |
| Container operation wiring | Source inspection passed; no live Docker/Podman runtime test |
| ZIP integrity | Passed (`unzip -t`) |

## Not implemented / known limitations

- RunMind conversation operations beyond the existing `--prompt` handoff (open existing conversation, create conversation, export/save, open project, send prompt to an existing running instance) are not implemented. The current conversation/branch state is stored in WebView IndexedDB/local storage and there is no native session-management IPC contract in the 2.6.2 base. The existing `.runmind` save/open UI remains available.
- The dedicated adapter tab and override UI have not been exercised in a live WebView2 session.
- Docker/Podman commands have not been exercised against real runtimes.
- No .NET SDK/compiler is available in this environment; Windows compilation and all three host runtime tests remain required.

## Windows validation required

1. Build all three hosts with the repository's Windows build script and .NET 10 SDK.
2. Open System Manager and confirm the Settings/Application Adapters tabs, bounded scroll behavior, edit/save override, and reset confirmation.
3. Confirm Notepad and File Explorer discovery on a clean Windows installation; test open-file, open-folder, and select-file.
4. With Docker and/or Podman installed, test list/inspect and confirm start/stop/restart each require confirmation and reflect in a subsequent inspect/list.
5. Load all seven bundled skills from each host.
6. Test the existing RunMind `--prompt` adapter handoff; do not treat it as existing-session conversation management.
