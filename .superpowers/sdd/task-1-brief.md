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
