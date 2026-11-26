---
name: container-management
description: Manage local Docker or Podman containers using explicitly available container-management tools or adapters. Use for listing, inspecting, starting, stopping, restarting, and reviewing container logs.
---

# Container Management

First identify which runtime is available and which runtime owns the requested container. Docker and Podman contexts are not interchangeable. Do not assume Docker is installed or that Podman uses Docker's socket.

## Safe workflow
1. Discover runtime availability and list containers before acting when the target is ambiguous.
2. Identify the container by an exact name or ID; show multiple matches and ask the user to choose.
3. Inspect state before start/stop/restart. Explain the expected impact when relevant.
4. Treat list, inspect, status, and logs as read-only. Treat start, stop, restart, remove, and create as state-changing.
5. Require the native permission/confirmation mechanism for state-changing operations. Never bypass it with shell or PowerShell.
6. After a mutation, query the container again and report its observed state. Distinguish command acceptance from the resulting state.

Never stop or restart a container based only on a vague label such as 'development container' when several candidates exist. Do not invent runtime flags, container IDs, or health status. If no dedicated operation is exposed, explain that it is not currently supported rather than silently falling back to arbitrary command execution.
