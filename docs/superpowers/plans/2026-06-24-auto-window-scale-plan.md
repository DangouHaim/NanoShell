# Auto Window Scale — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add automatic window scaling (maximize normal windows, snap console windows to top half) via `SetWinEventHook`.

**Architecture:** New `WindowAutoManager` class encapsulates Win32 event hook, filter rules, and scaling logic. Hook registered on app start, torn down on app close. Must use `Dispatcher.Invoke` since hook callback runs on Win32 event thread.

**Tech Stack:** .NET 8 WPF, Win32 P/Invoke (`user32.dll`, `kernel32.dll`)

**Global Constraints:**
- `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` already set on the shell window — must not interfere
- Shell window is unclickable — WindowAutoManager must not affect shell window itself
- Must handle console windows by class, console handle, and process name (3 methods)
- `SetWinEventHook` with `WINEVENT_OUTOFCONTEXT` flag only (safe on crash)

---

### Task 1: Add new P/Invoke imports to MainWindow.xaml.cs

**Files:**
- Modify: `NanoShell/MainWindow.xaml.cs`

**Interfaces:**
- Produces: All Win32 API declarations that `WindowAutoManager` will use

- [ ] **Step 1: Add imports inside `MainWindow` class, grouped with existing ones**

```csharp
// WinEvent hooks
private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

[DllImport("user32.dll")]
private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

[DllImport("user32.dll")]
private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

// Window queries
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

[DllImport("user32.dll")]
private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

[DllImport("user32.dll")]
private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

[DllImport("kernel32.dll")]
private static extern IntPtr GetConsoleWindow();

// Process query
[DllImport("user32.dll")]
private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

// Constants
private const uint WINEVENT_OUTOFCONTEXT = 0;
private const uint EVENT_SYSTEM_FOREGROUND = 3;
private const int OBJID_WINDOW = 0;
private const int CHILDID_SELF = 0;
private const int GWL_STYLE = -16;
private const uint WS_SIZEBOX = 0x00040000;
private const uint WS_EX_TOOLWINDOW = 0x00000080;
private const int SW_MAXIMIZE = 3;
private const int SW_SHOWNORMAL = 1;
private static readonly IntPtr HWND_TOP = IntPtr.Zero;
private const uint SWP_SHOWWINDOW = 0x0040;
private const uint SWP_NOZORDER = 0x0004;
private const uint SWP_NOACTIVATE = 0x0010;
```

- [ ] **Step 2: Commit**

```bash
git add NanoShell/MainWindow.xaml.cs
git commit -m "feat: add P/Invoke imports for auto window scale"
```

---

### Task 2: Add console detection helpers

**Files:**
- Modify: `NanoShell/MainWindow.xaml.cs` (add inside `InputSimulator` or as standalone static methods)

**Interfaces:**
- Produces: `IsConsoleWindow(IntPtr hWnd)` — returns true for console windows via class name, console handle, or process name

- [ ] **Step 1: Add console detection method**

```csharp
public static bool IsConsoleWindow(IntPtr hWnd)
{
    // 1. Check by window class
    StringBuilder className = new StringBuilder(256);
    GetClassName(hWnd, className, 256);
    string cls = className.ToString();
    if (cls == "ConsoleWindowClass" || cls == "CASCADIA_HOSTING_WINDOW_CLASS")
        return true;

    // 2. Check by console handle
    if (GetConsoleWindow() == hWnd)
        return true;

    // 3. Check by process name (fallback)
    GetWindowThreadProcessId(hWnd, out uint pid);
    try
    {
        string processName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
        string[] consoleProcesses = { "cmd", "powershell", "pwsh", "wt" };
        return Array.IndexOf(consoleProcesses, processName) >= 0;
    }
    catch
    {
        return false;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add NanoShell/MainWindow.xaml.cs
git commit -m "feat: add console window detection"
```

---

### Task 3: Create WindowAutoManager class

**Files:**
- Create: `NanoShell/WindowAutoManager.cs`

**Interfaces:**
- Produces: `WindowAutoManager` class with `Start()`, `Stop()`, and `Dispose()`

- [ ] **Step 1: Create WindowAutoManager.cs**

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace NanoShell;

public class WindowAutoManager : IDisposable
{
    private IntPtr _hook;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    // WinEvent hook delegate — must be kept alive to avoid GC
    private MainWindow.WinEventDelegate _winEventDelegate;

    public WindowAutoManager(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        _winEventDelegate = WinEventProc;
        _hook = MainWindow.SetWinEventHook(
            MainWindow.EVENT_SYSTEM_FOREGROUND,
            MainWindow.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0, 0,
            MainWindow.WINEVENT_OUTOFCONTEXT
        );

        if (_hook == IntPtr.Zero)
        {
            Debug.WriteLine("WindowAutoManager: SetWinEventHook failed");
        }
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            MainWindow.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != MainWindow.OBJID_WINDOW || idChild != MainWindow.CHILDID_SELF)
            return;

        // Skip our own window
        if (hWnd == new WindowInteropHelper(Application.Current.MainWindow).Handle)
            return;

        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            ProcessWindow(hWnd);
        }));
    }

    private void ProcessWindow(IntPtr hWnd)
    {
        // 1. Check window styles
        int style = MainWindow.GetWindowLong(hWnd, MainWindow.GWL_STYLE);
        int exStyle = MainWindow.GetWindowLong(hWnd, MainWindow.GWL_EXSTYLE);

        // No resize handle → skip
        if (((uint)style & MainWindow.WS_SIZEBOX) == 0)
            return;

        // Tool window → skip
        if (((uint)exStyle & MainWindow.WS_EX_TOOLWINDOW) != 0)
            return;

        // 2. Check window size
        if (MainWindow.GetWindowRect(hWnd, out RECT rect))
        {
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
            if (width < 300 || height < 200)
                return;
        }

        // 3. Check if already maximized
        MainWindow.WINDOWPLACEMENT placement = new MainWindow.WINDOWPLACEMENT();
        placement.length = Marshal.SizeOf(placement);
        MainWindow.GetWindowPlacement(hWnd, ref placement);
        if (placement.showCmd == MainWindow.SW_MAXIMIZE)
            return;

        // 4. Console window → snap to top half
        if (MainWindow.InputSimulator.IsConsoleWindow(hWnd))
        {
            SnapToTopHalf(hWnd);
            return;
        }

        // 5. Default: maximize
        MainWindow.ShowWindow(hWnd, MainWindow.SW_MAXIMIZE);
    }

    private void SnapToTopHalf(IntPtr hWnd)
    {
        MainWindow.RECT workArea = new MainWindow.RECT();
        MainWindow.SystemParametersInfo(MainWindow.SPI_GETWORKAREA, 0, ref workArea, 0);

        int width = workArea.right - workArea.left;
        int height = (workArea.bottom - workArea.top) / 2;

        MainWindow.SetWindowPos(
            hWnd,
            MainWindow.HWND_TOP,
            workArea.left, workArea.top,
            width, height,
            MainWindow.SWP_SHOWWINDOW
        );
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }
}
```

**Note:** Add `public static` to `SetWinEventHook`, `UnhookWinEvent`, `GetWindowLong`, `GetWindowRect`, `SystemParametersInfo`, `SPI_GETWORKAREA`, and the `RECT` struct declarations in `MainWindow.xaml.cs` so `WindowAutoManager` can access them. Also add `public static` to `WinEventDelegate`, `OBJID_WINDOW`, `CHILDID_SELF`, `GWL_STYLE`, `WS_SIZEBOX`, `SW_MAXIMIZE`, `SW_SHOWNORMAL`, `HWND_TOP`, `SWP_SHOWWINDOW`.

- [ ] **Step 2: Make required members public in MainWindow.xaml.cs**

Change `private` to `public static` for:
- `SetWinEventHook`
- `UnhookWinEvent`
- `WinEventDelegate`
- `GetWindowLong`
- `GetWindowRect`
- `SystemParametersInfo`
- `RECT` struct
- Constants: `WINEVENT_OUTOFCONTEXT`, `EVENT_SYSTEM_FOREGROUND`, `OBJID_WINDOW`, `CHILDID_SELF`, `GWL_STYLE`, `WS_SIZEBOX`, `WS_EX_TOOLWINDOW`, `SW_MAXIMIZE`, `SW_SHOWNORMAL`, `SPI_GETWORKAREA`, `HWND_TOP`, `SWP_SHOWWINDOW`

Also change `GetClassName` and `GetWindowThreadProcessId` from `private` to `private static` (they're already static, just need to ensure they're visible). Actually `GetConsoleWindow` too.

- [ ] **Step 3: Verify build**

```powershell
dotnet build NanoShell\NanoShell.csproj
```

- [ ] **Step 4: Commit**

```bash
git add NanoShell/WindowAutoManager.cs NanoShell/MainWindow.xaml.cs
git commit -m "feat: add WindowAutoManager with SetWinEventHook"
```

---

### Task 4: Integrate WindowAutoManager into MainWindow

**Files:**
- Modify: `NanoShell/MainWindow.xaml.cs`

- [ ] **Step 1: Add field and lifecycle calls in MainWindow**

```csharp
// Add field
private WindowAutoManager _windowAutoManager;

// In OnSourceInitialized, after setting extended style:
_windowAutoManager = new WindowAutoManager(Dispatcher);
_windowAutoManager.Start();

// In Window_Closed, before existing RegisterAppBar():
_windowAutoManager?.Dispose();
```

- [ ] **Step 2: Verify build**

```powershell
dotnet build NanoShell\NanoShell.csproj
```

- [ ] **Step 3: Commit**

```bash
git add NanoShell/MainWindow.xaml.cs
git commit -m "feat: integrate WindowAutoManager into MainWindow lifecycle"
```
