# System Manager Health Intelligence

System Manager health intelligence turns separate Windows System Manager queries into an on-demand diagnostic report. It is designed to summarize evidence and point to useful next investigations, not to act as an always-running monitoring agent.

## Enable the required capabilities

In Agent Settings → System Manager:

1. Enable System Manager and **System Diagnostics**.
2. Enable only the categories you want the report to query:
   - **Windows service inspection** for service inventory, configuration and dependency trees.
   - **Network inventory and TCP/UDP listener ownership** for connection and process ownership details.
   - **Windows Event Log investigation** for Application/System event evidence.
   - **Application and service crash investigation** for WER, Application Error, .NET Runtime and SCM failure events.
   - **Finite CPU, memory, process and supported disk-I/O sampling** for resource measurements and process hotspots.
   - **Storage, volume and available drive reliability counters** for storage evidence.
   - **Targeted DNS, route and TCP endpoint diagnostics** only when you intend to perform a user-directed hostname/endpoint check.
3. Enable **Save bounded local health snapshots for comparisons** only when you want local historical comparisons. It is off by default.

The report can run with a subset of capabilities. It lists the coverage and any categories that could not be collected. A missing permission or unavailable provider is not represented as a healthy result.

## Service investigation

Use the service tool's `list`, `inspect`, `config`, `dependents` and `dependencies` actions. Inspection includes current state, PID where available, startup mode, service account, binary path, declared dependencies, recovery actions, failure flags and trigger information. The dependency tree is read-only, cycle-safe and bounded to 5 levels / 50 nodes. Load-order groups are displayed without recursively expanding them.

`start`, `stop` and `restart` remain separate state-changing actions. Each always requires fresh native confirmation. RunMind queries the actual service state after the operation and reports whether the requested state was verified. Prefer testing against a non-critical service; do not experiment on Windows security, boot, storage, networking or other critical services.

## Health report and recurrence

The `system_manager_health_report` Agent tool accepts:

- `days`: Event Log investigation window from 1 to 30 days (default 7).
- `duration_seconds`: finite CPU/memory/process/disk sample duration from 1 to 15 seconds (default 3).

The report combines the enabled service/network/hardware inventory, Application and System event evidence, crash/service investigation, a finite resource sample and storage health. It returns summary metrics, issue classifications, repeated failure signatures, event evidence, possible listener conflicts, resource hotspots and explicit coverage/warnings.

A repeated signature is highlighted at three or more matching observations. Service failures are grouped by service identity; application crashes by application/exception/module information; network failures by provider/event signature. This avoids calling several unrelated failures a recurrence.

## Event and resource correlation

Events from Application/System and crash investigation are grouped into five-minute windows. The resource sample has its own start/end timestamps; events within five minutes of that interval are reported as nearby evidence. CPU and memory pressure findings include the leading sampled processes by CPU or memory usage.

These are investigative clues only. Time overlap does not prove that an event caused pressure, that a process caused a service failure, or that a network event caused an application crash. This release does not attribute physical-disk queue length to individual processes.

## Network investigation

Network inventory separates bounded non-listening TCP connections from TCP listeners and UDP endpoint rows, with PID/process-name ownership. Executable paths are available only when process-detail inspection is enabled. TCP listeners with overlapping local bindings—including possible IPv4/IPv6 wildcard overlap—are reported as **possible** conflicts because socket reuse and IPv4/IPv6 dual-stack behavior can permit legitimate sharing.

For an actual connection failure, use the targeted network diagnostic with the intended hostname/IP. It resolves DNS and reports the Windows-selected route/interface/next hop when available. An optional TCP check attempts one selected host/port with bounded timeouts and mandatory fresh confirmation. It does not scan ranges or unrelated destinations.

## Historical comparison

With the history option enabled, each health report stores one compact local snapshot in `%LOCALAPPDATA%\RunMind\SystemManager\health-history.json`. Only the latest 30 summaries are retained, and the file is limited to 512 KB. The comparison reports CPU/memory/disk metric deltas, event/issue count deltas and observed service-state changes.

The file does not retain raw event messages, executable paths, network endpoints, command lines or raw process samples. Writes are serialized across RunMind hosts and performed via a temporary file. History is local to the Windows user profile and is not synchronized to a cloud service by this feature.

A snapshot is taken only when the user requests a report. Two snapshots do not describe what happened continuously between their timestamps.

## Known limitations

- Windows services, Event Log providers and hardware performance counters can expose different fields depending on Windows build, permissions, drivers and service configuration.
- Disk queue and physical-disk throughput depend on the Windows performance CIM provider. Missing or inaccessible counters are shown as unavailable.
- Process CPU/memory values are sampled observations; processes can start/exit during a sample and protected processes can deny inspection.
- Network listener overlap is a heuristic and may not be an actual bind failure.
- DNS resolution and route selection do not prove an application-layer service is healthy. The optional TCP check confirms only that a TCP connection to one selected endpoint could or could not be established at that moment.
- The health report is not a continuous monitor, root-cause engine, compliance certification or automatic remediation system.
