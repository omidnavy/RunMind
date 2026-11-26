# RunMind 2.5.1 — Validation Report

**Release:** 2.5.1 — System Manager Flyout & Verified Application Launch  
**Base:** RunMind 2.5.0.2 complete source bundle  
**Validation date:** 2026-11-17

## Changes in this bundle

- Moved Windows System Manager into its own toolbar flyout, separate from Agent Settings. Existing settings control IDs, shared native settings storage, capability switches, permission profiles, and native enforcement are retained.
- Reduced Agent Settings navigation to its four remaining sections: Agent, Project, Skills, and Security.
- Application discovery now resolves Start Menu `.lnk` files to their executable targets and excludes shortcuts whose targets are missing or invalid.
- Registry/App Paths values are normalized to executable paths; stale catalog entries are filtered against the current filesystem before being returned.
- Application launch uses the resolved executable directly with shell execution disabled, validates it still exists, and waits for process startup.
- A launch is reported as successful only when the matching executable process remains active or the request is handed off to an existing instance. If the process exits without a matching running instance, the result is `launched: false`, `verified: false`, with the exit code and reason.
- Agent tool instructions now explicitly prohibit claiming that an app opened unless the native result has both `launched: true` and `verified: true`.

## Static validation

| Check | Result |
|---|---|
| JavaScript syntax (`node --check`) for all ChatWindow scripts | Passed |
| JSON parsing | Passed |
| XML parsing for project files, XAML, and MSIX manifest | Passed |
| HTML element ID uniqueness | Passed |
| Dedicated System Manager flyout exists; old Agent Settings tab references removed | Passed |
| Existing System Manager control IDs retained | Passed |
| Version synchronization across all three hosts and MSIX manifest | Passed — 2.5.1 / 2.5.1.0 |
| Shared `SystemManager.cs` included by all three host projects | Passed |
| C# lexical delimiter scan for `SystemManager.cs` and `AgentHostBridge.cs` | Passed |
| ZIP archive integrity | Passed |
| SHA-256 checksum verification | Passed |

## Not performed

- No Windows/.NET compilation was performed in this environment (`dotnet`, `csc`, and `mcs` are unavailable).
- No live Windows UI or application launch tests were performed.
- The Start Menu COM shortcut resolver, Firefox Developer Edition discovery/launch, Steam single-instance handoff, and all three host flyouts require runtime validation on Windows.

This is a complete source bundle with static validation, not a claim of a compiled binary release. Please build the complete Release x64 solution on Windows and test both installed and missing applications before relying on the new launch verification behavior.
