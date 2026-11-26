# Runner → Standalone Full Agent handoff

## Product behavior

Runner remains a lightweight native launcher and Quick Agent host. It does not embed or start a second chat frontend. The Open Chat command transfers the current prompt and any completed or partial inline answer to the actual RunMind Standalone application.

The transcript context is displayed as an initial user/assistant exchange. It is not automatically submitted again. Standalone starts with its normal `Standalone` host profile, so its regular full-chat provider connections, workspace features, Full Agent, MCP and Skills remain available there. Runner itself receives none of those capabilities.

## Deployment compatibility

Build and deploy the Standalone project from the same source package as Runner. The named-pipe listener and `--runner-handoff-file` argument are part of this Standalone source update. A previously installed Standalone binary without these additions cannot receive the current handoff protocol. PowerToys Run and Command Palette do not need to be upgraded for this Runner-to-Standalone handoff change.

## Running Standalone process

Runner first tries the current-user-only named pipe `RunMind.Standalone.Handoff.v1`. The Standalone process accepts a bounded JSON message, validates the timestamp and prompt/response limits, then opens a new full-agent conversation window in that existing Standalone process. The currently open Standalone conversation is preserved rather than overwritten. The new conversation window is activated.

The IPC protocol transfers only prompt/response context and error state. It cannot invoke tools or send native commands. The pipe is restricted with `PipeOptions.CurrentUserOnly`, and payloads are size-bounded.

## Standalone not running

Runner locates `RunMind.exe` using this order:

1. A running `RunMind` process (when discoverable).
2. Current-user or machine App Paths / uninstall registration.
3. The current-user cached executable path written by Standalone at startup.
4. Conventional per-user / Program Files installation locations.
5. Development build output paths near `RunMind.sln`.

Runner writes a bounded, short-lived handoff JSON file under `%LOCALAPPDATA%\RunMind\Runner\Handoffs` and launches the located executable with `--runner-handoff-file <path>`. Standalone consumes and deletes the file, validates its bounds and age, and initializes the normal Full Agent MainWindow with the transcript context.

If another Standalone process is detected during startup, the new process retries the current-user pipe briefly. If the existing listener still cannot accept the handoff, it continues with a genuine Standalone window containing the context instead of dropping it.

## Empty Runner state

The launcher intentionally displays no sample prompts, fabricated recent activity or suggestion cards. The result list stays collapsed until an actual launcher plugin returns a match.

## Windows acceptance

- With Standalone closed, use Open Chat from Runner and verify the real RunMind Standalone window opens with Full Agent capabilities.
- Hand off an inline prompt and completed response; verify both appear in the transcript and are not regenerated.
- Hand off while inline generation is in progress; verify partial output is transferred and Runner cancels its generation.
- With an unrelated Standalone conversation already open, hand off from Runner; verify the existing conversation remains intact and a new real Standalone conversation window opens in the existing Standalone process.
- Trigger handoff during Standalone startup; verify pipe retry routes the context when the listener becomes ready.
- Install Standalone in a non-default directory, launch it once, close it, and verify Runner can discover it from the current-user path cache.
- With no valid Standalone installation, verify Runner shows an actionable not-found message and does not create a substitute chat UI.
- Inspect Runner's build/publish output and verify it contains no WebView2 package or duplicate ChatWindow runtime. Standalone must continue to ship its normal WebView2 and ChatWindow assets.
