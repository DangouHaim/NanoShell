# NanoShell: Touch Interaction Design

## Goal
Replace all mouse-based input events with touchscreen gestures in the WPF taskbar shell.

## Approach
Use WPF's built-in `StylusSystemGesture` routed event to recognize touch gestures (`Tap`, `DoubleTap`, `RightTap`) — no manual timer-based gesture detection needed.

## Change summary

### What changes
| File | What |
|------|------|
| `MainWindow.xaml` | Replace `<Button>` → `<ContentControl>` (no built-in Click); remove all `Click`/`MouseDoubleClick`/`MouseRightButtonDown`/`MouseRightButtonUp`; add `StylusSystemGesture` handlers |
| `MainWindow.xaml.cs` | Remove 8 mouse handlers; add 4 gesture handlers that switch on `e.SystemGesture` |

### What stays
- `AreAnyTouchesOver` button highlight trigger (already touch-native)
- `InputSimulator` static class (keyboard simulation, app-bar registration)
- `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` (window never takes focus)
- All P/Invoke declarations

### Gesture mapping

| Button | `Tap` | `DoubleTap` | `RightTap` |
|--------|-------|-------------|------------|
| ← (Back) | `Alt+Left` | `Escape` | `Alt+F4` |
| O (Desktop) | `Win+D` | — | `tabtip.exe` |
| ⬜ (TaskView) | `Win+Tab` | `Alt+Tab` | Re-register app bar + toggle foreground maximize |
| DockPanel (panel) | — | — | `PrintScreen` |

### XAML pattern (each button)
```xaml
<ContentControl StylusSystemGesture="BtnBack_Gesture"
                Content="←" Foreground="White" FontSize="30"
                HorizontalAlignment="Left" Margin="50,0"
                Width="100" Height="50">
    <ContentControl.Template>
        <ControlTemplate TargetType="ContentControl">
            <Border Background="{TemplateBinding Background}"
                    CornerRadius="20">
                <ContentPresenter HorizontalAlignment="Center"
                                  VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property="AreAnyTouchesOver" Value="True">
                    <Setter Property="Background" Value="Gray"/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
    </ContentControl.Template>
</ContentControl>
```

### Code-behind pattern
```csharp
private void BtnBack_Gesture(object sender, StylusSystemGestureEventArgs e)
{
    switch (e.SystemGesture)
    {
        case SystemGesture.Tap:
            InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Left);
            break;
        case SystemGesture.DoubleTap:
            InputSimulator.SimulateKeyPress(Key.Escape);
            break;
        case SystemGesture.RightTap:
            InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.F4);
            break;
    }
    e.Handled = true;
}
```

## Constraints
- `ContentControl` does not fire `Click`, so no double-handling on tap
- `e.Handled = true` in every branch prevents event bubbling
- No mouse fallback — touch-only (as requested)
- Visual highlight via `AreAnyTouchesOver` trigger preserved

## Out of scope
- Multi-touch gestures (pinch, swipe) — not needed for a 3-button taskbar
- MVVM / DI — pure code-behind as before
