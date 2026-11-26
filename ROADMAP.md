# RunMind Roadmap

## Current release: 2.6.1 — System Manager UI & Model Compatibility

RunMind 2.6.1 completes the adapter management surface and improves cross-model prompt compatibility and truthful action results. It preserves the generic application discovery and shared Agent architecture introduced in 2.6.0. It preserves the three supported hosts and the shared Agent Core/native bridge.

### Included in this source bundle

- Existing project indexing, workspace tools, Git, native PowerShell/.exe execution, MCP, Deep Research, workflows, task recovery, Project Profiles and Skills Manager are preserved.
- System Manager remains optional and disabled by default. Native master/group/capability checks remain authoritative.
- Application discovery uses a bounded local index with refresh on stale data or search miss. Sources include App Paths, Windows uninstall registration, Start Menu shortcuts, bounded common install locations, PATH and the Windows Apps folder. Matching ranks normalized names, aliases, executable names, prefixes, substrings and tokens.
- Packaged Windows applications are represented by validated AppUserModelIDs and activated through `shell:AppsFolder`. This confirms only that Windows accepted an activation request; it does not verify a visible window or action completion.
- Launch requests use a stable application ID from current discovery. Executable paths are revalidated natively. Existing desktop windows are reused and brought forward by default; foreground activation is reported as verified only when Windows reports the target window as foreground.
- If an existing process cannot be activated, RunMind does not silently create a duplicate. The user can request a new process explicitly.
- New-window requests are supported only for a bounded allowlist of applications with known command-line support. New-process behavior is best-effort because some applications redirect to an existing instance.
- Per-application launch behavior preferences (`ask`, `reuse`, `new-window`, `new-process`) are stored in the existing System Manager settings file. Changing a preference always requires native confirmation.
- Application close uses graceful main-window close requests and mandatory native confirmation. It never force-terminates a process; packaged applications are not force-closed.
- Declarative application adapters use the same stable application-ID launch path. The System Manager flyout now lists adapters and supports custom manifest installation/removal with native confirmation. The built-in RunMind adapter can start RunMind with a supplied prompt through its existing startup argument. No scripts or shell commands are introduced.
- System Manager policy is appended to the existing leading Agent system message instead of adding a second system message, improving compatibility with stricter model chat templates.
- Native launch results distinguish verified process startup, verified foreground activation, new-window request, existing-instance handoff and unverified navigation.
- `list_available_tools` reports the exact tool definitions exposed in the current Agent session. `list_agent_skills` and `load_agent_skill` work without requiring a project folder; project-scoped skills still require an authorized workspace.

### Explicit limitations

- The current source bundle does not contain the Bionic bundled-skill payload. The skill inventory therefore reports installed Global and authorized Project skills only; it does not invent a Built-in/Bionic scope.
- Packaged-app activation does not currently pass arbitrary command-line arguments and cannot verify visible windows, focus, or URL navigation.
- New-window support is limited to applications with known command-line flags. Other applications can use reuse or new-process behavior.
- Graceful close applies to accessible desktop main windows. It does not force-close background-only or packaged applications.
- Windows foreground activation is subject to Windows security restrictions; RunMind never uses focus-stealing workarounds.
- Playwright remains a project-installed CLI workflow, not an embedded browser runtime. API smoke testing remains workflow-guided through approved PowerShell.

### Next candidates

- Windows-targeted automated integration tests for discovery, launch, activation, close, and all three hosts.
- Better app-specific adapters where a real user workflow justifies them.
- Full Skill repository/archive import with referenced resources and update checks, if needed.
- Dedicated Playwright runtime integration and richer browser diagnostics, if justified by actual usage.

### Compatibility and security principles

- One shared Agent Core across all three hosts.
- No duplicated conversation, project-index, Skills, workflow, or permission engines.
- Native operations are explicit, bounded, and approval-gated where they change application state.
- System Manager is optional and disabled by default.
- No shell execution is hidden behind application discovery or ordinary read-only diagnostics.
- No arbitrary scripts, forced focus, GUI automation, administrator elevation, registry modification, or service control.


## 2.6.2 — System Management Skills & Capability Contracts

- Bundled on-demand system-management, container-management, and runmind-management skills.
- Shared Agent policy describes safe discovery, operation selection, permission boundaries, and verification.
- Adapter inventory reports descriptive operation contracts; native enforcement remains authoritative.
