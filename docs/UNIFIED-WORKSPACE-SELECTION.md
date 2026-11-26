# Unified Workspace Selection

## Requirement

A user selects a project workspace once for the Agent. Native capabilities must inherit that same workspace automatically. The user should not be asked to choose the same directory again for PowerShell, native executables, development servers, API testing, Node debugging, or Playwright.

`working_directory` remains optional and, when supplied, is a relative subdirectory of the one selected workspace. A tool must never widen the authorized root or silently switch to a different folder.

## Current implementation boundary

The Agent's file tools currently use the browser File System Access API (`FileSystemDirectoryHandle`). The native process bridge has a separate Windows folder picker and stores `_processWorkspaceRoot` independently.

A `FileSystemDirectoryHandle` exposes a display name and scoped file/directory operations, but it does not expose the absolute Windows path to JavaScript. Its `name` is not a unique path and must not be used to guess a directory. Therefore the native host cannot safely inherit the current browser selection by copying the folder name.

## Target design

1. The native host owns one canonical, normalized workspace root per conversation context. The folder is selected once through the OS folder picker.
2. The WebView receives a workspace context identifier and display metadata, not an unrestricted absolute path used as an authority.
3. Existing Agent file operations move behind a small native workspace RPC: list directory, read file, write/create, move/copy/rename, delete, metadata, and bounded traversal/search. Each path is resolved against the selected root and checked for traversal and reparse points.
4. Existing file mutation approval, backup, snapshot verification, content limits, and rollback behavior remain in place. The bridge is a storage implementation change, not a permissions reduction.
5. PowerShell, native executable, development-server, API, debugger, and Playwright tools use the same root resolver. `working_directory` is only a relative subdirectory. The separate `select_native_workspace` tool and its second folder picker are removed from the normal workflow.
6. Changing or clearing the Agent workspace invalidates the native root, active process-relative context, and file handles associated with the previous root. Existing RunMind-owned processes remain tracked for safe explicit shutdown.

## Migration boundary

Do not combine browser-handle path guessing, directory-name matching outside the selected root, or current-working-directory heuristics with this change. Those approaches can authorize the wrong directory. The migration is complete only when all Agent workspace file operations and all native project operations use the same host-owned root, and the three host applications compile and pass their integration tests.

## Current status

The current Phase 2 patch improves relative path normalization for the existing browser workspace and native workspace resolvers, but it does **not** yet remove the second native folder picker. The native-host-owned workspace and file RPC migration remains a required implementation item before this requirement can be marked complete.
