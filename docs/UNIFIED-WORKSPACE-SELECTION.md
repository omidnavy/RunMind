# Unified Workspace Selection

## Status in RunMind 2.7.0

The native host now owns the canonical workspace root. A conversation selects the folder once through the Windows folder dialog. The WebView receives a context ID, display name and opaque selection identity, then exposes a File System Access-compatible virtual directory/file handle backed by the native `workspace.fs` RPC.

The same per-conversation selection is used by Agent file operations, project indexing, Skills, Git when applicable, approved PowerShell/native execution, development-server management, API testing, Node debugging and Playwright. Native `working_directory` parameters are relative subdirectories and cannot widen the authorized root.

## Identity and concurrency

- Workspace state is persisted under Local AppData by conversation context ID, with a stable selection ID and a cross-host named mutex.
- Every native file operation re-reads the persisted entry; it does not rely on a mutable singleton `current workspace` field.
- Cached WebView handles are rebound when the selected identity changes. Old selection IDs are rejected rather than redirected to the new folder.
- A project-index entry is keyed by conversation and workspace selection identity, so changing the selected root invalidates old indexing results.
- Multiple chats/contexts in one host remain independent. Multiple RunMind hosts can share a selection when they use the same conversation context ID.

## File access and safety

`WorkspaceFileSystem.cs` accepts relative paths only. It rejects rooted paths, drive/alternate-data-stream syntax, dot/parent segments, and reparse-point traversal. Reads and writes use bounded 256 KB chunks with a 128 MB file limit. Writes use temporary files and atomic commit; the native bridge rechecks the workspace selection identity at commit and aborts stale sessions.

The JS adapter in `native-workspace.js` implements the directory/file handle methods used by the Agent's existing file operations. It exposes no absolute workspace path to JavaScript.

Existing file mutation approvals, backups, snapshots and project-relative tool boundaries remain in place. Process execution remains separate and requires fresh approval; this workspace bridge is path authorization, not a process sandbox.

## Selection behavior

- First project operation prompts for one native workspace selection.
- Later file/native operations in the same conversation reuse the persisted selection. `select_native_workspace` is idempotent and returns `alreadySelected: true` without showing another picker.
- An explicit change action opens the picker and creates a new selection identity.
- If status cannot be verified, RunMind fails closed; it does not assume no selection and reopen a duplicate picker.
- Legacy browser directory handles cannot reveal an absolute Windows path and are not used as native authority; a one-time native selection is required when migrating an old conversation without a persisted native workspace.

## Git and clone

Git uses the same context-bound workspace root. Repository clone persists the new destination as the conversation's workspace and rebinds Agent file access to that selection; no second folder picker is required after cloning.

## Remaining validation

The architecture and portable contract tests are implemented. The Windows three-host build and manual acceptance flow still need to be completed on the user's Windows installation. See `docs/P0-FOUNDATION-ACCEPTANCE.md`.
