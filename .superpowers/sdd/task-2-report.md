# Task 2 Report: Add console detection helpers

## Status
- **Complete**: Yes

## Commits
- `5c3b1f7` feat: add console window detection

## Changes
- **NanoShell/MainWindow.xaml.cs**: Added `using System.Text;` import. Added `public static bool IsConsoleWindow(IntPtr hWnd)` method inside the existing `InputSimulator` nested class. The method detects console windows via:
  1. Window class name check (`ConsoleWindowClass` or `CASCADIA_HOSTING_WINDOW_CLASS`)
  2. Direct console handle comparison via `GetConsoleWindow()`
  3. Fallback process name check (`cmd`, `powershell`, `pwsh`, `wt`)

## Build
- `dotnet build` succeeded (0 errors, warnings are pre-existing)
