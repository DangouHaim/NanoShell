# Auto Window Scale — Design Spec

## Overview
Automatically scale/maximize newly opened windows on a touchscreen device, with special handling for console windows (top-half snap). Implemented as part of the NanoShell WPF shell process using native Win32 APIs.

## Architecture
Extract logic into `WindowAutoManager` class in a new file `WindowAutoManager.cs`. Responsibilities:
1. **Hook** — register a `SetWinEventHook` on `EVENT_SYSTEM_FOREGROUND` at startup
2. **Filter** — decide whether to scale an activation event
3. **Scaler** — apply maximize or console snap

Hooks start in `MainWindow.OnSourceInitialized`, unhook in `Window_Closed`.

## API: SetWinEventHook
- Events: `EVENT_SYSTEM_FOREGROUND` only
- Flags: `WINEVENT_OUTOFCONTEXT` — OS auto-cleans on process crash
- Callback runs on a Win32 event thread (not WPF UI thread) — must marshal via `Dispatcher.Invoke` for window operations
- Unhook via `UnhookWinEvent` on window close

## Filter rules (in order)
1. No `WS_SIZEBOX` → skip (cannot resize)
2. `WS_EX_TOOLWINDOW` → skip (toolbars, palettes)
3. Size < 300×200 → skip (splashes, small dialogs)
4. Class is `ConsoleWindowClass` / `CASCADIA_HOSTING_WINDOW_CLASS` → console snap
5. `GetConsoleWindow()` matches hWnd → console snap
6. Process is `cmd.exe`/`powershell.exe`/`pwsh.exe`/`wt.exe` → console snap (fallback)
7. Already maximized at creation (`WINDOWPLACEMENT.showCmd == SW_SHOWMAXIMIZED`) → skip (games, media players)
8. Otherwise → maximize

## Scaler: Maximize
Call `ShowWindow(hWnd, SW_MAXIMIZE)`. Reuses existing behavior from `InputSimulator.ToggleMaximize`.

## Scaler: Console snap
- Query work area via `SystemParametersInfo(SPI_GETWORKAREA)`
- Compute top half: `halfHeight = (rcWorkArea.bottom - rcWorkArea.top) / 2`
- `SetWindowPos(hWnd, HWND_TOP, rcWorkArea.left, rcWorkArea.top, rcWorkArea.right - rcWorkArea.left, halfHeight, SWP_SHOWWINDOW)`

## Dependencies
- New P/Invoke: `SetWinEventHook`, `UnhookWinEvent`, `GetWindowThreadProcessId`, `GetClassName`, `GetConsoleWindow`, `GetWindowRect`, `GetWindowLong` (already imported), `SetWindowPos`
- New constants: `WINEVENT_OUTOFCONTEXT`, `EVENT_SYSTEM_FOREGROUND`, `OBJID_WINDOW`, `CHILDID_SELF`, `SW_MAXIMIZE` (already), `WS_SIZEBOX`, `WS_EX_TOOLWINDOW` (already), `GWL_STYLE`, `GWL_EXSTYLE` (already)
- `System.Diagnostics.Process` for process name lookup

## Error handling
- If `SetWinEventHook` fails → log and degrade gracefully (no auto-scale)
- If any window query fails → skip that window silently

## Files changed
- New: `WindowAutoManager.cs`
- Modified: `MainWindow.xaml.cs` (hook start/stop, remove `ToggleMaximize` or keep for manual use)
