# Runner minor UI fix — 2026-11-24

## Fixes

1. Theme dropdowns now show `ThemeDefinition.Name` through a dedicated WPF `DataTemplate`, not the generated record `ToString()` representation. The item text follows the current theme foreground and uses ellipsis for long names.
2. The main Runner inline search input uses a 52-DIP height, centered content, no additional vertical padding, and ClearType rendering. Its surrounding search surface is unchanged.

## Validation

- Full Node regression suite: 282/282 passed.
- JavaScript syntax: 57/57 passed.
- XML/XAML/project/manifest parsing: 12/12 passed.
- Windows/.NET Release compilation and WPF interactive rendering were not available in this environment. Run `Build-Release-x64.bat` on Windows and visually verify the Light/Dark theme selectors and launcher search field.
