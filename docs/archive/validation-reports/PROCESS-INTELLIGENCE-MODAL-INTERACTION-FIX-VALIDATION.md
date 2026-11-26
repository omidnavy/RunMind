> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# RunMind 3.4.0 — Process Inspection Modal Interaction Fix

Date: 2026-11-26

## Fix

The process inspection modal is a top-level sibling of the System Manager toolbar flyout. The global document click handler previously treated interactions within that modal as outside clicks and dismissed the owning flyout. The click handler now returns early for events whose target is inside the visible process inspection modal. The modal's Escape handler now stops propagation as well as preventing the default action, keeping the document-level flyout-close handler from receiving Escape while the modal has focus.

The modal remains persistent. Its explicit Close button is still the user-facing close action; clicking the backdrop, dragging/resizing the modal, or pressing Escape does not dismiss it.

## Automated validation

- Focused Live Resource Intelligence / Process Intelligence tests: **19/19 passed**.
- Full repository Node.js regression suite (`node --test`): **326/326 passed**.
- JavaScript syntax validation: performed on the updated application script and bundled ChatWindow scripts.
- ZIP archive integrity: checked after packaging.

## Windows acceptance still required

The native .NET solution and WebView2 runtime were not built or exercised in this environment. On Windows, open Live Monitor, select a process, drag the inspection modal by its header and resize it. Confirm the Live Monitor flyout remains open throughout. Click within the modal and press Escape; it should remain open. Close it using its Close button.
