# Touch Interaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all mouse input events with touch gestures (`Tap`, `DoubleTap`, `RightTap`) in NanoShell.

**Architecture:** Replace WPF `<Button>` elements with `<ContentControl>` (no built-in `Click`), handle all gestures via `StylusSystemGesture` routed event. One handler per button that switches on `SystemGesture`.

**Tech Stack:** WPF .NET 8.0-windows, Win32 interop

## Global Constraints

- No tests — project has no test infrastructure (per AGENTS.md)
- No DI/MVVM — keep pure code-behind pattern
- Keyboard simulation (`InputSimulator`) unchanged
- App bar registration unchanged
- `AreAnyTouchesOver` highlight trigger preserved
- Touch-only — no mouse fallback

---

### Task 1: Rewrite MainWindow.xaml — Button → ContentControl with StylusSystemGesture

**Files:**
- Modify: `NanoShell/MainWindow.xaml` (entire file — replace 3 Button elements and DockPanel)

- [ ] **Step 1: Replace XAML Button elements with ContentControl**

Replace the 3 `<Button>` elements with `<ContentControl>` using the same visual template (rounded corners, white text, `AreAnyTouchesOver` highlight). Add `StylusSystemGesture` on each. Remove all mouse events (`Click`, `MouseDoubleClick`, `MouseRightButtonDown`, `MouseRightButtonUp`). Add `StylusSystemGesture` on DockPanel.

Target content (replace the entire Window content):

```xaml
<Window x:Class="NanoShell.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:local="clr-namespace:NanoShell"
        mc:Ignorable="d"
        Loaded="Window_Loaded"
        Closed="Window_Closed"
        ShowInTaskbar="False"
        WindowStartupLocation="Manual"
        Height="50"
        ResizeMode="NoResize"
        WindowStyle="None"
        Left="0"
        Background="Black"
        Topmost="True"
        >   
    <Grid Focusable="False">
        <Grid.Resources>
            <Style TargetType="ContentControl">
                <Setter Property="Background" Value="Black"/>
                <Setter Property="Foreground" Value="White"/>
                <Setter Property="FontSize" Value="30"/>
                <Setter Property="HorizontalContentAlignment" Value="Center"/>
                <Setter Property="VerticalContentAlignment" Value="Center"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ContentControl">
                            <Border Background="{TemplateBinding Background}" 
                                    CornerRadius="20"
                                    Width="100" Height="50">
                                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="AreAnyTouchesOver" Value="True">
                                    <Setter Property="Background" Value="Gray"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
        </Grid.Resources>
        <DockPanel StylusSystemGesture="Panel_Gesture" Background="Black"/>
        <ContentControl Content="←" StylusSystemGesture="BtnBack_Gesture" HorizontalAlignment="Left" Margin="50,0"/>
        <ContentControl Content="O" StylusSystemGesture="BtnCloseAll_Gesture" HorizontalAlignment="Center"/>
        <ContentControl Content="⬜" StylusSystemGesture="BtnTaskView_Gesture" HorizontalAlignment="Right" Margin="50,0"/>
    </Grid>
</Window>
```

- [ ] **Step 2: Verify no old mouse events remain**

Scan: no `Click=`, `MouseDoubleClick=`, `MouseRightButtonDown=`, `MouseRightButtonUp=` anywhere in the XAML.

- [ ] **Step 3: Build to check for XAML errors**

Run: `dotnet build NanoShell/NanoShell.csproj`
Expected: build fails because `BtnBack_Gesture`, `BtnCloseAll_Gesture`, `BtnTaskView_Gesture`, `Panel_Gesture` handlers don't exist in code-behind yet.

---

### Task 2: Rewrite MainWindow.xaml.cs — gesture handlers + remove mouse handlers

**Files:**
- Modify: `NanoShell/MainWindow.xaml.cs` (event handler section)

- [ ] **Step 1: Remove all old mouse event handlers**

Delete these 8 methods:
- `BtnBack_Click`
- `BtnBack_DoubleClick`
- `BtnBack_Hold`
- `BtnCloseAll_Click`
- `BtnCloseAll_Hold`
- `BtnTaskView_Click`
- `BtnTaskView_DoubleClick`
- `BtnTaskView_Hold`
- `Pannel_Hold`

- [ ] **Step 2: Add 4 StylusSystemGesture handlers**

Add these methods (e.g., after `Window_Closed`):

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

private void BtnCloseAll_Gesture(object sender, StylusSystemGestureEventArgs e)
{
    switch (e.SystemGesture)
    {
        case SystemGesture.Tap:
            InputSimulator.SimulateKeyCombination(Key.LWin, Key.D);
            break;
        case SystemGesture.RightTap:
            InputSimulator.OpenTouchKeyboard();
            break;
    }
    e.Handled = true;
}

private void BtnTaskView_Gesture(object sender, StylusSystemGestureEventArgs e)
{
    switch (e.SystemGesture)
    {
        case SystemGesture.Tap:
            InputSimulator.SimulateKeyCombination(Key.LWin, Key.Tab);
            break;
        case SystemGesture.DoubleTap:
            InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Tab);
            break;
        case SystemGesture.RightTap:
            InputSimulator.RegisterAppBar(Height);
            InputSimulator.ToggleMaximize();
            break;
    }
    e.Handled = true;
}

private void Panel_Gesture(object sender, StylusSystemGestureEventArgs e)
{
    if (e.SystemGesture == SystemGesture.RightTap)
    {
        InputSimulator.SimulateKeyPress(Key.PrintScreen);
        e.Handled = true;
    }
}
```

- [ ] **Step 3: Add missing using directive**

Add `using System.Windows.Input;` at top of file (already present).

- [ ] **Step 4: Build to verify**

Run: `dotnet build NanoShell/NanoShell.csproj`
Expected: Build succeeds with no errors.

---

### Task 3: Final verification

- [ ] **Step 1: Verify all gesture mappings from spec**

Check against AGENTS.md behavior table:

| Action | Gesture | Effect | Handler |
|--------|---------|--------|---------|
| ← Tap | `Tap` | `Alt+Left` | `BtnBack_Gesture` |
| ← DoubleTap | `DoubleTap` | `Escape` | `BtnBack_Gesture` |
| ← RightTap | `RightTap` | `Alt+F4` | `BtnBack_Gesture` |
| O Tap | `Tap` | `Win+D` | `BtnCloseAll_Gesture` |
| O RightTap | `RightTap` | `tabtip.exe` | `BtnCloseAll_Gesture` |
| ⬜ Tap | `Tap` | `Win+Tab` | `BtnTaskView_Gesture` |
| ⬜ DoubleTap | `DoubleTap` | `Alt+Tab` | `BtnTaskView_Gesture` |
| ⬜ RightTap | `RightTap` | Re-register + toggle maximize | `BtnTaskView_Gesture` |
| Panel RightTap | `RightTap` | `PrintScreen` | `Panel_Gesture` |

- [ ] **Step 2: No mouse events remain**

Confirm: grep for `Mouse` in `MainWindow.xaml` and `MainWindow.xaml.cs` returns only `AreAnyTouchesOver` (which is touch, not mouse).

- [ ] **Step 3: Final build**

Run: `dotnet build NanoShell/NanoShell.csproj`
Expected: Build succeeds, 0 warnings.
