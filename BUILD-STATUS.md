# RunMind 2.5.1 — System Manager Flyout & Verified Application Launch

## Release bundle status

- Source baseline: verified RunMind 2.4.0 Developer Workbench & Native Automation bundle.
- Preserved Standalone, PowerToys Run, and PowerToys Command Palette hosts and the shared Agent Core/native bridge.
- Added developer workflows for project builds/tests, static analysis/lint, Playwright test-runner checks, API smoke tests, and dependency security audits. They use existing native execution tools and require explicit per-command approval.
- Extended Skills Manager with direct HTTPS `SKILL.md` imports, GitHub/GitLab repository-folder URL resolution, pasted Skill-content detection, preview/confirmation, Global/Project scope, frontmatter validation, duplicate protection, and source metadata. Scripts are never executed.
- Added optional Windows System Manager, disabled by default, with shared native settings, group/individual switches, application discovery/catalog, generic launch/open operations, declarative application adapters, process inspection/termination, and read-only system diagnostics.
- Added `Shared/SystemManager.cs` to all three host projects and synchronized project/package metadata to 2.5.1 / 2.5.1.0.

## Validation performed in this environment

See `VALIDATION-REPORT.md` for exact checks and limitations. JavaScript syntax, HTML ID consistency, JSON/XML parsing, tool-to-native-dispatch wiring, C# lexical delimiter balance, release metadata, ZIP integrity, and SHA-256 are checked here.

## Target-machine validation still required

- Build the full solution with the .NET 10 Windows targeting pack and repository package dependencies.
- Run Standalone, PowerToys Run, and PowerToys Command Palette and verify the shared System Manager settings are consistent across hosts.
- Test System Manager master/group/capability switches, Restricted/Balanced/Custom prompts, application discovery/launch, built-in/custom adapters, process listing, native termination confirmation, services and network diagnostics.
- Test direct HTTPS/GitHub/GitLab Skill import, pasted Skill auto-detection, preview/decline/confirm behavior, Global/Project installation, duplicate handling, and malicious/invalid URL rejection.
- Test build/test/lint/Playwright/API/dependency workflows against real projects with native execution approval.
- Re-run existing Git, workspace, indexing, conversation, Project Profile, Skills, MCP, Deep Research, and PowerShell/native execution regression checks.


## 2.5.1 update

- System Manager has its own toolbar flyout, separate from Agent Settings.
- Application discovery resolves Start Menu shortcut targets and filters missing executables.
- Application launch now uses direct executable startup and checks for a matching running process before returning success.
- Windows/.NET build and live application-launch behavior still require validation on a Windows target.
