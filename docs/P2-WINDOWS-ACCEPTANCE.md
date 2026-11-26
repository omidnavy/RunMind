# RunMind 2.9.0 — P2 Windows acceptance checklist

Run on a Windows 11 machine with Node.js 24+ and .NET 10. Use the same source tree for Standalone, PowerToys Run and PowerToys Command Palette. This checklist supplements P0/P1 acceptance; it does not replace it.

## Build and version gate

- [ ] Run `node --version` and confirm Node.js 24 or later.
- [ ] Run `dotnet --info` and confirm .NET 10 SDK.
- [ ] Run `.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64`
- [ ] Confirm all three host projects build and package versions are 2.9.0; Command Palette package version is 2.9.0.0.
- [ ] Complete `docs/P0-WINDOWS-ACCEPTANCE.md` and `docs/P1-WINDOWS-ACCEPTANCE.md`.

## Settings and native capability checks — each host

- [ ] Install/launch each host and open System Manager settings.
- [ ] Confirm System Manager/Diagnostics are off by default on a fresh profile and every P2 capability is individually off.
- [ ] Enable only one P2 capability at a time; confirm only its corresponding Agent tool becomes available.
- [ ] Disable the global System Manager or Diagnostics group; confirm native operations reject requests even if a subordinate capability remains stored as enabled.
- [ ] Save/reopen settings and verify all P2 capability values persist without silently enabling a new permission.

## Windows Event Log investigation

- [ ] Enable Event Log Information only.
- [ ] Query Application for a recent bounded window; confirm provider, event ID, time, record ID and message preview are returned.
- [ ] Query System and Setup; confirm only those three channels are accepted.
- [ ] Try Security and a custom channel; confirm native rejection.
- [ ] Try hours below 1/above 720, max events below 1/above 200 and an invalid event ID; confirm validation rejects each.
- [ ] Query a time window with no matching entries; confirm an empty result is distinct from access/PowerShell failure.

## Application crash investigation

- [ ] Enable Crash Investigation only.
- [ ] Query 1, 7 and 30 day windows; verify the bounded time range and event cap.
- [ ] Where available, confirm Event IDs 1000, 1001, 1026, 7031 and 7034 appear with event source/time.
- [ ] Confirm missing localized application/module/exception fields remain empty rather than guessed.
- [ ] Confirm repeated patterns are labeled as correlations/evidence, not root-cause proof.

## Resource sampling

- [ ] Enable Resource Monitoring only.
- [ ] Run a 5-second sample with 1-second interval; confirm multiple timestamps and CPU/memory/process results.
- [ ] Run a 1-second sample and confirm the minimum duration works.
- [ ] Attempt 61 seconds, interval 0, interval 11 and interval longer than duration; confirm rejection.
- [ ] During a sample, open/close a test process; confirm results are snapshots/deltas and may omit short-lived processes.
- [ ] Confirm process truncation is explicit if the sample exceeds its 1,000-record cap.
- [ ] Cancel the Agent operation; confirm cancellation stops the native sampler and no background process/service remains.

## Scheduled Tasks

- [ ] Enable Scheduled Task Information only; confirm inventory works but mutation actions are not offered.
- [ ] Inspect inventory output; confirm task action arguments are not returned.
- [ ] Manually create a harmless test task using Windows Task Scheduler (RunMind does not create it).
- [ ] Enable Scheduled Task Management; use exact returned task path/name to disable and enable the test task.
- [ ] Confirm native Yes/No approval is always shown even when any permission profile is set to Allow.
- [ ] Start and stop the harmless task only when appropriate; verify actual resulting state.
- [ ] Decline a confirmation; verify task state remains unchanged.
- [ ] Try wildcard, partial task name, slash/backslash in the task name, forward-slash/path traversal or malformed task path; confirm rejection.
- [ ] Confirm no create/delete/task action/trigger/definition-edit operation is exposed.

## Storage health

- [ ] Enable Storage Health only.
- [ ] Confirm physical disks, volume capacity/free space and any supported health indicators appear.
- [ ] Confirm unsupported reliability counters show unavailable/null, not healthy/zero.
- [ ] Confirm a system without Storage cmdlets uses basic disk inventory fallback where available and emits warnings.
- [ ] Confirm no disk repair, surface scan or filesystem mutation is attempted.

## Windows security posture

- [ ] Enable Security Posture only.
- [ ] Confirm Defender, Firewall profiles, system-volume BitLocker, Secure Boot, TPM and UAC are reported independently.
- [ ] Confirm unavailable provider/module/firmware data is marked unavailable with a warning.
- [ ] Confirm no setting is changed, no malware scan is launched, and output is not described as a compliance certification.

## Targeted network diagnostics

- [ ] Enable Targeted Network Diagnostics only.
- [ ] Resolve a hostname without a port; confirm DNS and the best selected local route/interface/next hop are returned where NetTCPIP supports it.
- [ ] Confirm route lookup failure is reported as a warning and does not misstate DNS success or prevent read-only results.
- [ ] Resolve a literal IPv4/IPv6 address; confirm no DNS request is needed for a literal IP.
- [ ] Probe one known-open TCP port; confirm native confirmation appears and success is reported only after connect completes.
- [ ] Probe one known-closed port; confirm failure is reported with bounded error and no retry beyond the resolved address set.
- [ ] Decline the TCP confirmation; confirm no TCP connect is attempted.
- [ ] Try URL/path/credential/whitespace input, invalid port and timeout >10; confirm rejection.
- [ ] Confirm no port ranges, host ranges, subnet scans or batch targets are supported.

## SVG project asset support

- [ ] Open a workspace containing `index.html`, CSS `url(...)`, `assets/logo.svg`, and SVG `href` plus `xlink:href` references.
- [ ] Rebuild/refresh Project Understanding; confirm `.svg` appears in SVG analysis coverage.
- [ ] Open Project Mission Control; confirm each local asset edge includes the source file, normalized target and `targetIndexed` state.
- [ ] Confirm external/data/blob/hash-only references are not represented as workspace files and `../` traversal cannot escape the workspace.
- [ ] Confirm SVG is shown as a vector asset and no SVG code execution or non-JS language parser is introduced.

## Final gate

- [ ] Repeat the new P2 scenarios in Standalone, PowerToys Run and Command Palette.
- [ ] Confirm no capability appears when disabled and native rejection still occurs if a tool call is manually forged.
- [ ] Capture build transcript, host/version details and any Windows-specific failures.
- [ ] Mark P2 Windows acceptance complete only after all required checks pass; portable Node tests alone do not certify Windows runtime behavior.
