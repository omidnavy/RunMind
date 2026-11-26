# Unified Workspace Selection — RunMind 2.7.0 P0

## User-facing contract

The user selects a project folder once for the active conversation. The native host becomes the canonical authority for that folder. Agent file operations, Git operations, PowerShell/native executables, development servers, API tests, Node debugging, and Playwright resolve against that same root.

A tool may accept `working_directory`, but it is a relative subdirectory of the selected root. It must never expand the authorized root, guess an absolute path from the directory name, or open another picker just because a native capability is invoked.

## Architecture

1. The host owns a canonical, normalized root for each conversation context and persists a workspace ID with it.
2. The WebView receives a context ID, workspace ID and display name. The absolute root remains native-side.
3. `workspace-native.js` supplies a FileSystemDirectoryHandle-compatible adapter over narrow native RPC operations. Agent file tools use this adapter rather than a separate browser directory authority in the Windows hosts.
4. Native process tools use the same context root. `working_directory` is resolved relative to that root.
5. Workspace identity changes when the selected root changes. Handles and in-flight write sessions carrying the previous identity are rejected/aborted rather than silently targeting the new folder.
6. The workspace record is persisted per conversation. Hosts revalidate the stored workspace ID so a change made in another RunMind host is observed. In-process synchronization and an exclusive metadata-file lock serialize updates across Standalone, PowerToys Run and Command Palette.
7. Browser-only contexts without the native host retain the browser File System Access API fallback; the native-hosted Windows applications use the single native selection path.

## File bridge constraints

- Paths are relative, normalized and limited to safe Windows path segments. Absolute paths, drive/alternate-stream syntax, traversal, reserved device names and invalid segments are rejected.
- Reparse points/symlinks are not followed. Ancestors are checked before access.
- Directory enumeration is bounded and skips reparse points.
- File reads/writes use bounded chunks; individual file transfers are capped at 100 MiB and chunks at 256 KiB.
- Writes use an isolated temporary file and a commit step; incomplete writes can be aborted. Existing Agent approval, backup and snapshot protections remain in place for mutations.
- Write sessions are bound to conversation and workspace identity. Changing the root invalidates stale handles and aborts pending writes for that context.

## Concurrency and host behavior

The process-local workspace state lock protects one host's in-memory cache. A separate exclusive metadata-file lock serializes persisted workspace changes across host processes. Every status/path operation refreshes its cached workspace identity from persisted metadata before trusting it. A root change made by another host therefore invalidates stale cached handles on the next operation.

Git no longer maintains an independent folder authority: repository operations use the selected workspace root and validate the repository within it. A clone activates its new repository only if the original workspace identity remains selected when the clone completes.

## Acceptance criteria

Use `docs/P0-WINDOWS-ACCEPTANCE.md` to verify:

- selecting a workspace once makes file tools and native project tools work without a second prompt;
- all three Windows hosts use the same behavior;
- nested `working_directory` values stay inside the selected root;
- changing roots invalidates old handles;
- a root change made in another host is observed;
- traversal, absolute paths, reserved names and reparse points are refused;
- file transfers and writes remain bounded and recoverable.

The portable contract suite checks the architecture and safety boundaries. Actual Windows runtime behavior still requires the three-host acceptance run.
