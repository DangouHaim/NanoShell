# Task 1: Add P/Invoke imports for auto window scale

## What was implemented
Added new P/Invoke declarations and constants to `MainWindow.xaml.cs` for use by the upcoming `WindowAutoManager` class:

- **WinEventDelegate** delegate type for WinEvent hook callbacks
- **SetWinEventHook** / **UnhookWinEvent** — for tracking foreground window changes
- **GetClassName** — query window class names
- **GetWindowRect** — get window bounding rectangles
- **SetWindowPos** — reposition/resize windows
- **GetConsoleWindow** — detect console windows
- **GetWindowThreadProcessId** — associate windows with processes
- **Constants**: `WINEVENT_OUTOFCONTEXT`, `EVENT_SYSTEM_FOREGROUND`, `OBJID_WINDOW`, `CHILDID_SELF`, `GWL_STYLE`, `WS_SIZEBOX`, `HWND_TOP`, `SWP_SHOWWINDOW`, `SWP_NOZORDER`, `SWP_NOACTIVATE`

Duplicates (`WS_EX_TOOLWINDOW`, `SW_SHOWNORMAL`, `SW_MAXIMIZE`) were omitted as they already exist in the file.

## Files changed
- `NanoShell/MainWindow.xaml.cs` — added new P/Invokes (lines 45-69) and new constants (lines 111-120)

## Self-review findings
- All additions compile cleanly (0 errors, 8 pre-existing warnings)
- No naming conflicts with existing declarations
- Grouped logically with existing imports for readability

## Concerns
None.
