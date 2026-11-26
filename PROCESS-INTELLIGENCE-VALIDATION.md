# RunMind 3.4.0 — Process Intelligence validation

Date: 2026-11-26

## Source baseline

The Process Intelligence UI polish was applied to the complete RunMind 3.4.0 Process Intelligence source package (which already includes the README Refresh). The refreshed README and its screenshot remain in the package, along with the previously integrated Live Resource Intelligence v2 implementation and other source/documentation files.

## Portable checks completed

| Check | Result |
|---|---:|
| Full Node regression suite (`node --test tests/*.test.cjs`) | **325 passed / 0 failed** |
| Focused Live Resource Intelligence tests | **18 passed / 0 failed** |
| JavaScript/CJS syntax (`node --check`) | **62 files passed** |
| JSON parsing | **4 files passed** |
| XML, XAML, MSBuild and manifest parsing | **11 files passed** |
| CSS parsing | **22 files, 0 parser errors, 850 top-level qualified rules** |
| Shared ChatWindow HTML IDs | **429 unique, 0 duplicates** |
| README and new Process Intelligence guide local links | **Passed** |

The checks cover the process-selection contract, selectable sorting subjects, a bounded independently scrolling process table, separate persistent modal behavior, movable/resizable modal source contract, permission gates, PID/name/start-time identity verification, on-demand metadata, service-host safeguards, fresh confirmation requirements, guarded restart logic, bounded live resource sampling, and retained existing regression behavior.

## UI polish included

- GPU memory usage is now a half-width chart beside GPU utilization.
- The process list has its own vertical scroll area capped at the smaller of 38% of the viewport height or 320 px; the table header remains visible while scrolling.
- Process sorting is selectable: CPU usage, working set, private memory, process name or PID.
- Process inspection now opens in a separate modal outside the System Manager flyout. Its header drags the window, the corner resizes it, and clicking outside, pressing Escape, closing the toolbar flyout or switching tabs does not dismiss it. Only the explicit Close button does.
- Process actions are disabled while new inspection data loads. A verified process exit leaves the modal open and disables further actions for that inspected process instance.

## Native Windows validation not completed

The build environment used for this update does not contain the .NET SDK/MSBuild or a Windows runtime. Therefore this package has **not** been compiled as a native .NET application here, and no claim is made that Windows process-token inspection, CIM service inventory, process termination or process restart has been exercised on Windows.

Before treating this source update as release-certified, run the Windows build and complete:

- `docs/PROCESS-INTELLIGENCE-WINDOWS-ACCEPTANCE.md`
- `docs/P7-WINDOWS-ACCEPTANCE.md`

In particular, verify that service-host inventory succeeds on the target Windows installation, that actions remain blocked when service inventory is unavailable, and that restart works only for an appropriate same-account/current-session test process.
