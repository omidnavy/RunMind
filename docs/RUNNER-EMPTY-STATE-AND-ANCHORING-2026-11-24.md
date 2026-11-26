# Runner — Empty State, Branding and Direction-Aware Growth

## UI simplification
- Removed the redundant QUICK AI subtitle, plugin inventory tagline and RUNMIND Enter chip. The launcher query itself remains the primary affordance.
- Reduced the search surface to a 54-DIP row with a 40-DIP, 17-DIP text input.
- Reduced the default launcher width to 680 DIPs while preserving manual resizing.

## Empty and no-match states
- An empty query shows compact quick-start suggestions: application search, calculator, RunMind and URL opening.
- Selecting a suggestion fills the main query and returns focus to search. The RunMind suggestion respects global versus action-keyword mode.
- A non-empty query with no matches hides the result panel entirely and reports the no-match state in the footer.
- A non-empty query with matches shows only the unified results list, without an extra RESULTS heading or count label.

## Direction-aware dynamic height
- When resizing, Runner detects whether its current window center is in the upper or lower half of its monitor.
- In the upper half, the top edge is kept stable and the window grows downward.
- In the lower half, the bottom edge is kept stable and the window grows upward.
- New bounds are clamped to the selected monitor work area.
- Auto-resize remains optional; manual resizing continues to work when disabled.

## Windows acceptance
- Test a lower-screen placement, start with one result and then trigger a longer result list. Confirm the bottom edge stays stable and the window expands upward.
- Repeat near the top of the screen and confirm the top edge stays stable.
- Confirm window stays within the monitor working area near taskbars and screen edges.
- Confirm the initial quick-start suggestions fill the query and RunMind honors the configured global/keyword mode.
- Confirm a non-matching query hides the result card region rather than showing an empty Results panel.
- Confirm search control remains visually balanced at 100%, 125%, 150% and 200% display scaling.
