# RunMind Roadmap

## Current release: 2.5.2 — System Manager Action Reliability

RunMind 2.5.2 hardens System Manager action semantics: each new user request requires fresh discovery and launch execution, application discovery includes uninstall registry entries, stale process existence is no longer treated as a confirmed handoff, and default-handler opens are explicitly unverified. The dedicated System Manager flyout from 2.5.1 is retained. Standalone, PowerToys Run, and PowerToys Command Palette remain supported.

### Included in this source bundle

- Existing project indexing, workspace tools, Git, native PowerShell/.exe execution, MCP, Deep Research, workflows, task recovery, and shared Skills Manager are preserved.
- Skill installation: existing local folder installer; direct HTTPS `SKILL.md` import; GitHub repository/tree/blob and GitLab tree/blob URLs resolved to raw `SKILL.md`; pasted valid Skill content is detected before normal chat submission and requires preview/confirmation. Global and project scopes are supported for these content-only imports.
- Skill imports validate frontmatter and size, reject duplicate names, stage global installs, retain source metadata, and never execute scripts. Direct URL imports currently import `SKILL.md` only; additional referenced resource files should be installed through the existing folder installer.
- System Manager is disabled by default and its Agent tools are withheld unless enabled. Application, process, and diagnostics groups have individual capability switches. The native bridge independently enforces the master/group/capability switches.
- Application discovery uses App Paths, Windows uninstall registry entries, Start Menu shortcuts and PATH, with a persisted local catalog and refresh on search miss. Searches require a non-empty application name and launches must reference a discovered executable. Named-browser URLs are passed to that browser; default-handler requests are explicitly not treated as verified application launches.
- Process listing and diagnostics are read-only. Process termination is an individual capability and always requires a native Windows confirmation; the bridge revalidates PID, process name and process start time, checks executable identity when available, and blocks a protected-process list. Application launch/open confirmations are enforced natively according to the selected profile. A new user request requires fresh discovery and launch calls; prior conversation results cannot satisfy it. Existing-instance handoff and default-handler acceptance are reported as unverified.
- System diagnostics can report OS/runtime data, PATH entries, Windows service status, network adapters, active TCP connections and listeners, depending on enabled capability switches.
- Added reusable workflows for project test execution, Playwright test-runner checks, API smoke testing, and dependency security audits. These workflows use the existing approved PowerShell/native execution path; they do not silently install dependencies or claim tests ran without confirmed command results.

### Explicit limitations

- Direct URL Skill import currently downloads only `SKILL.md`; it does not recursively download linked resources. GitHub repository folder URLs are supported when they resolve to a folder containing `SKILL.md`. Other Git hosts beyond GitHub/GitLab and archive import are not implemented in this build.
- The initial System Manager includes a small built-in adapter set and user-authored declarative JSON adapters that can be installed through the Agent or added manually. It does not include arbitrary adapter scripts, window automation, administrator elevation, service control, registry modification, or system configuration changes.
- Playwright support in this release is a reusable workflow that invokes the project's existing Playwright test runner through approved PowerShell. It is not an embedded browser automation runtime.
- API smoke tests are workflow-guided and use the existing approved PowerShell path; there is no separate credential vault or full API-client UI.
- The static `project_validate` tool does not itself execute builds, lint, tests, or audits. Use the approved native execution tools and report their actual exit codes/output.

## Next candidates

- More built-in application adapters and a graphical adapter manager.
- Full Skill repository/archive import with referenced resources, update checks, and source/version tracking.
- Dedicated Playwright runtime integration and richer browser diagnostics.
- Dedicated API testing interface and secret-safe credential handling.
- Database and Podman integrations.
- Advanced Agent/MCP orchestration.
- Standalone Launcher improvements after the chat and Agent capabilities mature.

## Compatibility principles

- One shared Agent Core across all three hosts.
- No duplicated conversation, project-index, Skills, workflow, or permission engines.
- Native operations are explicit, bounded, cancellable where applicable, and approval-gated.
- System Manager is optional and disabled by default.
- No shell execution is hidden behind application discovery or ordinary read-only diagnostics.
