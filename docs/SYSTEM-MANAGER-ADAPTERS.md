# RunMind System Manager — Application Adapters

System Manager is optional and disabled by default. Enable it from the System Manager toolbar flyout before adapter tools are exposed. The adapter inventory is shown on its own tab with bounded panel and list scrolling.

## Built-in adapters

- `vscode`: `open-file`, `open-folder`
- `windows-terminal`: `open-folder`
- `firefox`: `open-url` (HTTPS only)
- `notepad`: Windows Notepad `open-file`
- `file-explorer`: `open-folder`, `select-file`
- `blender`: `open-file`
- `runmind`: `send-prompt` (existing `--prompt` launch handoff only)

The Agent discovers matching executables through the local application catalog. If more than one executable matches, it reports candidates rather than guessing.

## Editing built-in adapters

Built-ins can be edited from the Application Adapters tab by editing their declarative JSON and saving. Changes are stored as user overrides under `%LOCALAPPDATA%\RunMind\SystemManager\Adapters\Overrides`; shipped defaults are not modified. Reset asks for confirmation and removes the override. Adapter identity and application matching remain fixed.

## Add a custom adapter

Install a UTF-8 JSON manifest through the UI or `system_manager_install_adapter`. Custom manifests are stored in `%LOCALAPPDATA%\RunMind\SystemManager\Adapters`.

```json
{
  "id": "my-editor",
  "displayName": "My Editor",
  "match": "my-editor",
  "operations": { "open-file": ["{target}"] }
}
```

Custom adapters support only `open-file`, `open-folder`, and `open-url`. Argument arrays may use only `{target}` and `{line}`. Scripts, shell commands, environment expansion, executable paths, and arbitrary code are not supported.

## Docker and Podman

`system_manager_container_action` supports `list`, `inspect`, `start`, `stop`, `restart`, and `logs`, with runtime `auto`, `docker`, or `podman`. It invokes the detected CLI directly with argument arrays; no shell is used. Container identifiers are restricted to a conservative character set. Start/stop/restart always require native confirmation. For lifecycle changes, follow up with `inspect` or `list` to verify the resulting state.

## Skills

Bundled skills include `system-management`, `container-management`, `runmind-management`, `coding`, `debugging`, `testing-and-quality`, and `code-review`. Skills provide operational guidance, never authorization. Native capability settings, approval prompts, argument validation, and operation implementations remain authoritative.

## RunMind conversation boundary

The current RunMind adapter supports prompt handoff through the existing `--prompt` startup argument. Conversation sessions and branch data are stored in WebView IndexedDB/local storage; native open-existing/create/export conversation IPC is not part of this release. Use the existing in-app conversation save/open controls for `.runmind` files.


## RunMind conversation operations

The built-in RunMind adapter supports three operations: `send-prompt` opens a RunMind window and submits a prompt, `open-session` opens an existing `.runmind` conversation file as a resumable session, and `new-conversation` opens a fresh chat window. Session files must be fully-qualified existing paths, contain a JSON object, and be no larger than 16 MB. The adapter validates the file before launch; the Standalone host validates it again before restoring the conversation. These operations launch the Standalone executable discovered by System Manager; they do not attach to or mutate an already-open window.
