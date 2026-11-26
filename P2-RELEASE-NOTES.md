# RunMind 2.9.0 — P2 System Manager Intelligence

RunMind 2.9.0 preserves the complete P0 reliability foundation and P1 Project Intelligence/Mission Control experience, adds seven native Windows System Manager intelligence capabilities, and promotes SVG to a first-class project asset format.

## System Manager Intelligence

All P2 capabilities are individually opt-in, disabled in default settings, and checked again by the native host. They require System Manager to be enabled and the Diagnostics group to be enabled.

- **Windows Event Log investigation:** bounded read-only queries against Application, System and Setup logs. Security and arbitrary custom logs are excluded.
- **Application crash investigation:** bounded correlation of Application Error (1000), Windows Error Reporting (1001), .NET Runtime (1026), and Service Control Manager (7031/7034) records. Repeated signatures are supporting evidence, not confirmed root causes.
- **Resource sampling:** finite system CPU, process CPU-delta and working-set sampling. One request lasts at most 60 seconds and captures at most 60 samples; process snapshots are limited to 1,000 records. No persistent monitor/service is installed.
- **Scheduled Tasks:** bounded read-only inventory and exact-task enable/disable/start/stop. Every state change requires always-on native confirmation and a post-action state check. Creating/deleting tasks or editing actions/triggers is not supported.
- **Storage health:** physical disk, storage reliability-counter and volume inspection, with basic disk inventory fallback. Unsupported/missing reliability values are reported as unavailable, never healthy.
- **Windows security posture:** separate read-only snapshots of Defender, firewall profiles, system-volume BitLocker, Secure Boot, TPM and UAC. This is not a malware scan or compliance certification.
- **Targeted network diagnostics:** one hostname/IP, DNS resolution, Windows-selected local route/interface/next-hop when available, and optionally one TCP port. DNS, route and TCP phases are individually bounded; TCP attempts have a 10-second maximum total connect window, disclose the resolved addresses attempted (at most eight), and require fresh native confirmation. There is no scanning or port-range mode.

## SVG project asset support

SVG is indexed as a vector asset and included in analysis coverage. Project Understanding records normalized local references from HTML attributes, CSS `url(...)`, and SVG `href`/`xlink:href` attributes. Project Mission Control displays those relationships and whether the referenced target is indexed. SVG is not treated as executable code or a programming language. The supported language-aware project scope remains JavaScript, TypeScript, JSX/TSX, HTML and CSS; other programming languages remain generic text.

## Validation performed

- Portable Node.js suite: 192/192 passing at the final portable verification point.
- JavaScript/CJS/MJS syntax: 44/44 passed; repository JSON: 4/4 parsed; XML-family files: 6/6 parsed. ZIP integrity and SHA-256 are recorded in `BUILD-STATUS.md`.
- Windows .NET 10 compilation and live acceptance across Standalone, PowerToys Run and Command Palette must still be performed on the target Windows machine. No Windows certification is claimed by portable tests.
