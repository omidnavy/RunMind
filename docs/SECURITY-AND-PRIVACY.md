# Security and privacy

This document describes the current source behavior and its limits. It is not a security certification or a substitute for reviewing the exact release build.

## Execution model

- Full Agent native capabilities are exposed through the shared host bridge. System Manager operations require the System Manager master switch, the relevant group/capability and any operation-specific native confirmation.
- Workspace operations are limited to the selected workspace and apply their documented path validation, approval and backup rules.
- Approved PowerShell/native process actions run with the current Windows account. They are not isolated in a sandbox. RunMind does not silently elevate to administrator.
- Runner is a restricted Quick Host and does not expose Full Agent workspace, developer, MCP, Skills or System Manager capabilities.
- Command execution, container orchestration, browser workflows and other tools have operation-specific limits; see their feature guides. A successful process invocation is not proof that the intended external state was reached.

## Credential storage observed in this source

| Credential/configuration | Current storage behavior |
|---|---|
| Full Chat provider API keys and custom auth headers | Stored in WebView2 `localStorage` in the corresponding host profile. Not DPAPI-encrypted by RunMind. |
| Jina/Reader credentials | Stored in WebView2 `localStorage`. Not DPAPI-encrypted by RunMind. |
| MCP bearer tokens | Stored with the MCP connection configuration in WebView2 `localStorage`. Not DPAPI-encrypted by RunMind. |
| PowerToys Run / Command Palette inline provider keys and custom headers | Shared per-user JSON at `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json`. Not DPAPI-encrypted by RunMind. |
| Runner inline provider credentials | Separate `%LOCALAPPDATA%\RunMind\Runner\inline-credentials.dpapi` store protected with Windows DPAPI CurrentUser scope. |
| Non-secret Runner inline settings | `%LOCALAPPDATA%\RunMind\Runner\inline-model-settings.json`. |

Masking a value in the UI or avoiding log output does not encrypt its on-disk representation. Anyone with sufficient access to the Windows account/profile may be able to access unprotected local settings. Protect the Windows account, disk and backups. Do not commit local settings, profiles, tokens or provider credentials.

**Outstanding hardening:** migrate Full Chat and shared inline credentials to a native DPAPI CurrentUser vault. Migration must be transactional, support rollback and multi-host access, verify reads/provider use before removing old values, and include backup/restore and failure-injection tests. Do not manually delete existing credentials as a substitute for migration.

## Data leaving the machine

- Local Ollama/LM Studio endpoints can keep model inference on the machine, subject to those applications' own behavior and configured network exposure.
- Configured hosted OpenAI-compatible providers receive the prompts/context sent to them.
- SearXNG receives search queries when web search is used.
- MCP servers receive the arguments/context sent to their tools and may return untrusted data.
- Browser/Playwright workflows may contact the sites selected by the user.

Use only providers, search services, MCP servers and websites you trust. Review the selected workspace and task context before sending sensitive material to a remote service.

## Untrusted content

Workspace files, repository instructions, web pages, MCP metadata/results, Skills and adapter metadata are data, not authority. They cannot override the user request, native permissions or approval policies. Skills provide guidance; they do not grant permissions or automatically execute arbitrary scripts.

## System Manager and process actions

Process inspection is on demand. Command lines can contain secrets; the UI warns users and does not add them to Live Resource telemetry/CSV. Process actions revalidate identity and require native confirmation. Service-hosted and protected Windows processes are blocked from process-level control. Restart cannot reproduce an original process token, working directory, handles or complete startup context. The user should inspect the target before confirming destructive actions.

## Live telemetry

Live Resource Intelligence is explicitly started, bounded and retained in memory for the active session. It is not an always-on daemon and does not persist raw samples as a telemetry database. GPU values depend on driver-exposed Windows performance counters. Per-process GPU attribution is not provided by the current counters.

## Reporting a security issue

Do not include passwords, API keys, bearer tokens, personal paths or unredacted command lines in public issues or logs. Share a minimal reproduction and sanitized evidence. No dedicated security-response email or disclosure program is configured in this source snapshot; do not assume a particular private reporting channel.
