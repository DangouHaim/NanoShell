# Task 3 Report: Create WindowAutoManager class

## Status: Complete

### Step 1: Create WindowAutoManager.cs
- Created `NanoShell/WindowAutoManager.cs` with the `WindowAutoManager` class implementing `IDisposable`
- Added `using System.Windows.Interop;` (was missing from brief, needed for `WindowInteropHelper`)
- Qualified `RECT` as `MainWindow.RECT` in `GetWindowRect` call (was missing from brief)

### Step 2: Make required members public in MainWindow.xaml.cs
Changed `private` → `public`/`public static` for:
- `SetWinEventHook`, `UnhookWinEvent`, `WinEventDelegate` (delegate, `public` only), `GetWindowLong`, `GetWindowRect`, `SetWindowPos`, `SystemParametersInfo`
- `RECT` struct (`public struct RECT`)
- Constants: `GWL_EXSTYLE`, `WS_EX_TOOLWINDOW`, `SW_SHOWNORMAL`, `SW_MAXIMIZE`, `SPI_GETWORKAREA`, `WINEVENT_OUTOFCONTEXT`, `EVENT_SYSTEM_FOREGROUND`, `OBJID_WINDOW`, `CHILDID_SELF`, `GWL_STYLE`, `WS_SIZEBOX`, `HWND_TOP`, `SWP_SHOWWINDOW`

### Step 3: Verify build
- `dotnet build` succeeded (0 errors)

### Step 4: Commit
- `c687549` feat: add WindowAutoManager with SetWinEventHook
