# Command Palette Enter / Shift+Enter behavior

RunMind's inline form is rendered by PowerToys Command Palette as an Adaptive Card. The out-of-process extension receives a form submission after the host processes the key; it does not receive the underlying WinUI `TextBox.KeyDown` event. Therefore, changing `InlineAnswerPage.cs` or its Adaptive Card JSON cannot implement modifier-sensitive submission.

## Desired host behavior

For the focused multiline Adaptive Card `TextBox`:

- `Enter` submits the first `Action.Submit` / `Action.Execute` action after validating and collecting inputs.
- `Shift+Enter` remains unhandled by the form control, allowing the multiline `TextBox` to insert a newline.
- Single-line text boxes keep their current Enter-to-submit behavior.

## Host control to update

In the PowerToys source tree, inspect:

`src/modules/cmdpal/Microsoft.CmdPal.UI/Controls/ContentFormControl.xaml.cs`

Find the form key handler that currently gates submission on `e.OriginalSource is TextBox textBox && !textBox.AcceptsReturn`. The host change is to also submit when the `TextBox` is multiline and Shift is not down. Use the WinUI 3 API `Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)` and test the `Windows.UI.Core.CoreVirtualKeyStates.Down` flag. The logic should be equivalent to:

```csharp
var shiftDown = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
    & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

if (e.OriginalSource is TextBox textBox
    && (!textBox.AcceptsReturn || !shiftDown))
{
    // Keep the existing action selection, input validation,
    // ViewModel.HandleSubmit(...) and e.Handled logic here.
}
```

Do not mark the event handled when the focused input is multiline and Shift is down; the TextBox must receive it to insert the newline. Preserve the existing single-line behavior. Required imports are `Microsoft.UI.Input`, `Windows.UI.Core`, and `Windows.System` (the last may already be present).

## Build boundary

This is a PowerToys host change. `Build-Release-x64.bat` builds RunMind's three hosts and does not compile or replace PowerToys itself. To use the shortcut, the modified Command Palette host must be built and installed as part of a custom PowerToys build, or Microsoft must ship the behavior in a future release.

Reference: Microsoft documents `InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey)` as returning key state at the time of the current input message: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.input.inputkeyboardsource.getkeystateforcurrentthread
