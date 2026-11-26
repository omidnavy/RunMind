# RunMind System Manager — Application Adapters

System Manager is optional and disabled by default. Enable it from the dedicated System Manager toolbar flyout before adapter tools are exposed.

## Built-in adapters

- `vscode`: `open-file`, `open-folder`
- `windows-terminal`: `open-folder`
- `firefox`: `open-url` (HTTPS only)
- `notepad-plus-plus`: `open-file`
- `blender`: `open-file`

The Agent discovers the matching executable through the local application catalog. If more than one executable matches, it reports the candidates rather than guessing.

## Add a custom adapter

You can ask the Agent to install an adapter manifest through `system_manager_install_adapter` (the native bridge displays a confirmation), or create a UTF-8 JSON file with a `.json` extension in:

`%LOCALAPPDATA%\RunMind\SystemManager\Adapters`

Example:

```json
{
  "id": "my-editor",
  "displayName": "My Editor",
  "match": "my-editor",
  "operations": {
    "open-file": ["{target}"],
    "open-folder": ["{target}"]
  }
}
```

Supported operations are `open-file`, `open-folder`, and `open-url`. Argument arrays may use only `{target}` and `{line}` placeholders. The adapter format does not support scripts, shell commands, environment expansion, executable paths, or arbitrary code. The executable must still be found in RunMind's application catalog.

Custom manifests are limited to 32 KB each, 100 files, 8 operations, 20 arguments per operation, and 1,024 characters per argument. Existing IDs cannot be overwritten by the install operation; remove the old JSON manifest manually before reinstalling it. Invalid manifests are ignored. Adapter actions still obey System Manager settings and launch approval policy. Restricted mode and Ask permissions display a native Windows confirmation; Balanced/Custom can bypass it only when the user explicitly selected Allow automatically.


## Agent Skills (2.6.2)

RunMind ships three bundled skills under `Skills/`: `system-management`, `container-management`, and `runmind-management`. They are copied beside each host executable and exposed with scope `builtin` by `list_agent_skills`. Load them on demand with `load_agent_skill`. Global and authorized project skills remain supported.

Skills are guidance, not authorization. Native System Manager capability settings, approval prompts, argument validation, and operation implementations remain the security boundary. Adapter inventory exposes descriptive operation metadata; it does not grant additional operations.
