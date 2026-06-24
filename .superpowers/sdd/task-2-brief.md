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
