# NanoShell — agent instructions

## Project
Single-project WPF shell/taskbar replacement for Windows touchscreens.
Targets `net8.0-windows` (WinExe), Windows-only.

## Build & run
```powershell
dotnet build NanoShell\NanoShell.csproj
```
Or open `NanoShell.sln` in Visual Studio.

## Architecture
- **Entrypoint**: `App.xaml.cs` → `MainWindow.xaml` → `MainWindow.xaml.cs`
- **InputSimulator** (nested static class in `MainWindow.xaml.cs`): wraps Win32 `keybd_event`, `ShowWindow`, `SetWindowPlacement`, `SystemParametersInfo(SPI_SETWORKAREA)` and `SHAppBarMessage` for app-bar registration
- **P/Invoke**: `user32.dll` (window management, keyboard simulation), `shell32.dll` (app bar). All constants and structs (`WINDOWPLACEMENT`, `RECT`, `APPBARDATA`) defined locally.
- No DI, no MVVM framework — pure code-behind with Win32 interop.

## Key behaviors (don't break)
| Action | Effect |
|--------|--------|
| ← click | `Alt+Left` (browser back) |
| ← double-click | `Escape` |
| ← hold | `Alt+F4` |
| O click | `Win+D` (show desktop) |
| O hold | Launch `tabtip.exe` (touch keyboard) |
| ⬜ click | `Win+Tab` (task view) |
| ⬜ double-click | `Alt+Tab` |
| ⬜ hold | Re-register app bar + toggle foreground window maximize |
| Panel right-click hold | `PrintScreen` |

## Important quirks
- Window is **unclickable** (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` applied in `OnSourceInitialized`) — it never takes focus.
- Registers as a Windows **app bar** via `SHAppBarMessage` to reserve screen real estate at the bottom (height ~50px).
- Touch-optimized: button press highlight triggers on `AreAnyTouchesOver` (not `IsMouseOver`).
- No tests, no CI, no linter/formatting config.
- Two gitignore files at root: `.gitignore` (standard VS template) and `gitignore` (unused legacy). Git reads `.gitignore`.
- **Current branch**: `suspend` — LockScreen + AOD + ProcessLockPanel UI + ProcessLockService (NtSuspendProcess integration)
- **LockScreenService**: polls `GetLastInputInfo` + `GetCursorPos` (1s), 15s → LockScreenRequested, LockScreenDismissed event on swipe-up
- **ProcessLockService**: enumerate via `EnumWindows`, freeze/thaw via `NtSuspendProcess`/`NtResumeProcess`, exceptions stored in JSON at `%LOCALAPPDATA%\NanoShell\suspend_exceptions.json`
- **LockScreenWindow**: layered overlay with AOD (black + tap-to-show-time), Active (wallpaper + time/date + swipe-up dismiss), gear button opens ProcessLockPanel slide-in
