---
name: runmind-management
description: Manage the RunMind application, conversations, projects, and workspace using only capabilities explicitly exposed by the current RunMind integration.
---

# RunMind Management

Use the RunMind adapter's current operation inventory and the existing RunMind host integration. Do not launch a second Agent runtime or bypass RunMind permissions.

Before acting, discover which conversation and project operations are actually supported. Distinguish opening an existing conversation, creating a conversation, opening a project/workspace, saving, exporting, importing, and sending a prompt; these are separate operations and must not be inferred from one another.

Use stable conversation/project identifiers returned by RunMind. Confirm the destination before exporting or overwriting data. Never claim a conversation was opened, saved, exported, or modified unless the native result confirms it. If a requested operation is absent from the adapter metadata, state that limitation and do not invent a command-line switch, file format, or IPC endpoint.
