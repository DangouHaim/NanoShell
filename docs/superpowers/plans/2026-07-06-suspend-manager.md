# Suspend Manager — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace fragile process-lock/suspend subsystem with a predictable, async-first `SuspendManager` that guarantees balanced suspend/resume, handles explorer.exe safely, and has consistent naming.

**Architecture:** New `SuspendManager` class with per-PID state machine, `SemaphoreSlim(1,1)` locking, `ConcurrentDictionary<uint,int>` suspend tracking, and drain logic (`NtResumeProcess` loop). `ProcessLockService` → `SuspendableProcessService` (renamed, persistence fixed). `ExplorerWatchdogService` uses `SuspendManager` API (no static methods). Staged resume: explorer first, 250ms delay, then rest.

**Tech Stack:** .NET 8 WPF, Win32 P/Invoke (`ntdll.dll`, `kernel32.dll`, `user32.dll`), `System.Text.Json`

## Global Constraints

- Windows-only (`net8.0-windows`)
- No DI, no MVVM framework
- All `SuspendManager` methods must accept `CancellationToken`
- `SuspendManager` is the single authority for suspend/resume — no direct P/Invoke calls outside it
- All async operations must not block the UI thread

---

### Task 1: Create SuspendManager.cs

**Files:**
- Create: `NanoShell.Services/SuspendManager.cs`

**Interfaces:**
- Produces: `SuspendManager` class consumed by all other tasks

- [ ] **Step 1: Write the class skeleton**

Create `NanoShell.Services/SuspendManager.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NanoShell.Interop;

namespace NanoShell.Services;

public class SuspendManager
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NanoShell", "suspend_manager.log");

    private static readonly HashSet<string> _systemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "textinputhost",
        "tabtip",
    };

    private static void Log(string msg)
    {
        try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    private readonly ConcurrentDictionary<uint, int> _suspendCount = new();
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _locks = new();
    private readonly CancellationTokenSource _shutdownCts = new();

    public bool KeyboardInputActive { get; set; }
    public int ProcessSuspendCount(uint pid) => _suspendCount.GetValueOrDefault(pid, 0);
    public bool IsSuspended(uint pid) => _suspendCount.TryGetValue(pid, out var c) && c > 0;

    private SemaphoreSlim GetLock(uint pid)
        => _locks.GetOrAdd(pid, _ => new SemaphoreSlim(1, 1));

    private static List<(uint Pid, string Name, string ExePath, int ParentPid)> SnapshotProcesses()
    {
        var list = new List<(uint, string, string, int)>();
        IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == (IntPtr)(-1))
            return list;
        try
        {
            var entry = new PROCESSENTRY32();
            entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32>();
            if (!NativeMethods.Process32First(snap, ref entry))
                return list;
            do
            {
                string name = Path.GetFileNameWithoutExtension(entry.szExeFile);
                list.Add((entry.th32ProcessID, name, entry.szExeFile, (int)entry.th32ParentProcessID));
            } while (NativeMethods.Process32Next(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }
        return list;
    }

    public void CancelAll()
    {
        _shutdownCts.Cancel();
    }
}
```

- [ ] **Step 2: Implement SuspendProcessAsync**

```csharp
public async Task SuspendProcessAsync(uint pid, CancellationToken ct)
{
    if (_systemProcesses.Contains(Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant()))
        return;

    var lockObj = GetLock(pid);
    await lockObj.WaitAsync(ct);
    try
    {
        if (_suspendCount.TryGetValue(pid, out var count) && count > 0)
            return; // already suspended

        using var proc = Process.GetProcessById((int)pid);
        NativeMethods.NtSuspendProcess(proc.Handle);
        _suspendCount[pid] = 1;
        Log($"Suspended pid={pid} name={proc.ProcessName}");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Log($"SuspendProcessAsync pid={pid}: {ex.Message}");
    }
    finally
    {
        lockObj.Release();
    }
}
```

- [ ] **Step 3: Implement ResumeProcessAsync**

```csharp
public async Task ResumeProcessAsync(uint pid, CancellationToken ct)
{
    var lockObj = GetLock(pid);
    await lockObj.WaitAsync(ct);
    try
    {
        if (!_suspendCount.TryGetValue(pid, out var count) || count <= 0)
            return; // not suspended

        using var proc = Process.GetProcessById((int)pid);
        NativeMethods.NtResumeProcess(proc.Handle);
        _suspendCount.TryRemove(pid, out _);
        Log($"Resumed pid={pid} name={proc.ProcessName}");

        await DrainProcessAsync(pid, CancellationToken.None);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Log($"ResumeProcessAsync pid={pid}: {ex.Message}");
    }
    finally
    {
        lockObj.Release();
    }
}
```

- [ ] **Step 4: Implement DrainProcessAsync**

```csharp
public async Task DrainProcessAsync(uint pid, CancellationToken ct)
{
    const int maxDrain = 10;
    int drained = 0;

    try
    {
        using var proc = Process.GetProcessById((int)pid);
        IntPtr handle = proc.Handle;

        while (drained < maxDrain && NativeMethods.NtResumeProcess(handle) == 0)
        {
            ct.ThrowIfCancellationRequested();
            drained++;
        }

        if (drained > 0)
            Log($"Drained {drained} extra resume(s) for pid={pid}");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Log($"DrainProcessAsync pid={pid}: {ex.Message}");
    }
}
```

- [ ] **Step 5: Implement SuspendAllAsync and ResumeAllAsync**

```csharp
public async Task SuspendAllAsync(HashSet<string> suspendableTargets, CancellationToken ct)
{
    Log("SuspendAllAsync started");
    int selfPid = Environment.ProcessId;

    foreach (var (pid, name, _, _) in SnapshotProcesses())
    {
        ct.ThrowIfCancellationRequested();
        if (pid == selfPid) continue;
        string nameLower = name.ToLowerInvariant();
        if (_systemProcesses.Contains(nameLower)) continue;
        if (!suspendableTargets.Contains(nameLower)) continue;

        // Don't suspend explorer when keyboard input is active
        if (nameLower.Equals("explorer", StringComparison.OrdinalIgnoreCase) && KeyboardInputActive)
            continue;

        await SuspendProcessAsync(pid, ct);
    }

    // Two-pass to catch late spawns
    foreach (var (pid, name, _, _) in SnapshotProcesses())
    {
        ct.ThrowIfCancellationRequested();
        if (pid == selfPid) continue;
        string nameLower = name.ToLowerInvariant();
        if (_systemProcesses.Contains(nameLower)) continue;
        if (!suspendableTargets.Contains(nameLower)) continue;
        if (nameLower.Equals("explorer", StringComparison.OrdinalIgnoreCase) && KeyboardInputActive)
            continue;

        await SuspendProcessAsync(pid, ct);
    }

    Log("SuspendAllAsync completed");
}

public async Task ResumeAllAsync(CancellationToken ct)
{
    Log("ResumeAllAsync started");

    // Stage 1: resume explorer first
    var snapshot = _suspendCount.Keys.ToList();
    var explorerPids = snapshot.Where(pid =>
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }).ToList();

    foreach (var pid in explorerPids)
        await ResumeProcessAsync(pid, ct);

    if (explorerPids.Count > 0)
        await Task.Delay(250, ct);

    // Stage 2: resume all remaining
    var remaining = _suspendCount.Keys.ToList();
    await Task.WhenAll(remaining.Select(pid => ResumeProcessAsync(pid, ct)));

    Log("ResumeAllAsync completed");
}

public async Task ResumeByNameAsync(string name, CancellationToken ct)
{
    foreach (var proc in Process.GetProcessesByName(name))
    {
        uint pid = (uint)proc.Id;
        if (IsSuspended(pid))
            await ResumeProcessAsync(pid, ct);
    }
}

public async Task SuspendByNameAsync(string name, HashSet<int> pids, CancellationToken ct)
{
    foreach (var proc in Process.GetProcessesByName(name))
    {
        uint pid = (uint)proc.Id;
        if (pids.Contains((int)pid) && !IsSuspended(pid))
            await SuspendProcessAsync(pid, ct);
    }
}
```

- [ ] **Step 6: Build and verify no compilation errors**

```powershell
dotnet build NanoShell\NanoShell.csproj 2>&1
```

Expected: build succeeds, or minimal errors (unused imports in SuspendManager.cs, which will resolve as other tasks integrate it).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add SuspendManager with per-PID state machine and drain"
```

---

### Task 2: Rewrite ProcessLockService → SuspendableProcessService

**Files:**
- Rename: `NanoShell.Services/ProcessLockService.cs` → `NanoShell.Services/SuspendableProcessService.cs`
- Modify: `NanoShell.Services/SuspendableProcessService.cs` (full rewrite)

**Interfaces:**
- Consumes: `SuspendManager` (from Task 1)
- Produces: `SuspendableProcessService` class with `suspendable_processes.json` persistence

- [ ] **Step 1: Delete old file, create new file**

Delete `NanoShell.Services/ProcessLockService.cs`, create `NanoShell.Services/SuspendableProcessService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NanoShell.Interop;

namespace NanoShell.Services;

public class ProcessEntry
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public int ParentPid { get; set; }
    public string WindowTitle { get; set; } = "";
    public bool IsFrozen { get; set; }
    public bool IsSuspendable { get; set; }
    public bool IsBackground { get; set; }
}

public class SuspendableProcessService
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NanoShell", "suspend_manager.log");

    private static readonly HashSet<string> _systemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "textinputhost",
        "tabtip",
    };

    private static void Log(string msg)
    {
        try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    private readonly HashSet<string> _suspendableProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> _windowPids = new();
    private readonly string _configPath;
    private bool _windowsEnumerated;
    private readonly SuspendManager _suspendManager;

    public SuspendableProcessService(SuspendManager suspendManager)
    {
        _suspendManager = suspendManager;
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NanoShell");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "suspendable_processes.json");
        LoadAsync().GetAwaiter().GetResult(); // sync load at startup
    }

    public bool IsSuspendable(string nameLower)
        => _suspendableProcesses.Contains(nameLower);

    public IReadOnlySet<string> SuspendableTargets => _suspendableProcesses;

    private static List<(int Pid, string Name, string ExePath, int ParentPid)> SnapshotProcesses()
    {
        var list = new List<(int, string, string, int)>();
        IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == (IntPtr)(-1))
            return list;
        try
        {
            var entry = new PROCESSENTRY32();
            entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32>();
            if (!NativeMethods.Process32First(snap, ref entry))
                return list;
            do
            {
                string name = Path.GetFileNameWithoutExtension(entry.szExeFile);
                list.Add(((int)entry.th32ProcessID, name, entry.szExeFile, (int)entry.th32ParentProcessID));
            } while (NativeMethods.Process32Next(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }
        return list;
    }

    private void EnumerateWindows()
    {
        if (_windowsEnumerated) return;
        _windowPids.Clear();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (NativeMethods.IsWindowVisible(hwnd))
                _windowPids.Add(pid);
            return true;
        }, IntPtr.Zero);
        _windowsEnumerated = true;
    }

    public void InvalidateWindowCache() { _windowsEnumerated = false; }

    public List<ProcessEntry> EnumerateAll()
    {
        try
        {
            EnumerateWindows();
            var entries = new List<ProcessEntry>();
            int selfPid = Environment.ProcessId;

            foreach (var (pid, name, exePath, parentPid) in SnapshotProcesses())
            {
                try
                {
                    if (pid == selfPid) continue;
                    string nameLower = name.ToLowerInvariant();
                    if (_systemProcesses.Contains(nameLower)) continue;
                    bool hasWindow = _windowPids.Contains((uint)pid);
                    string title = hasWindow ? GetWindowTitle(pid) : "";
                    bool isBackground = !hasWindow
                        && !nameLower.Equals("explorer", StringComparison.OrdinalIgnoreCase);

                    entries.Add(new ProcessEntry
                    {
                        Pid = pid,
                        Name = name,
                        ExePath = GetProcessImagePath(pid) ?? exePath,
                        ParentPid = parentPid,
                        WindowTitle = title,
                        IsFrozen = _suspendManager.IsSuspended((uint)pid),
                        IsSuspendable = _suspendableProcesses.Contains(nameLower),
                        IsBackground = isBackground
                    });
                }
                catch (Exception exInner) { Log($"EnumerateAll skip pid={pid} {name}: {exInner.Message}"); }
            }

            return entries;
        }
        catch (Exception ex) { Log($"EnumerateAll top-level error: {ex.Message}"); return new List<ProcessEntry>(); }
    }

    private static string GetWindowTitle(int pid)
    {
        try { using var proc = Process.GetProcessById(pid); return proc.MainWindowTitle ?? ""; }
        catch { return ""; }
    }

    private static string? GetProcessImagePath(int pid)
    {
        try
        {
            IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    return sb.ToString();
                return null;
            }
            finally { NativeMethods.CloseHandle(hProcess); }
        }
        catch { return null; }
    }

    public async Task ToggleSuspendableAsync(string processName, bool suspendable)
    {
        string lower = processName.ToLowerInvariant();
        if (suspendable)
            _suspendableProcesses.Add(lower);
        else
            _suspendableProcesses.Remove(lower);
        await SaveAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_configPath)) return;
            string json = await File.ReadAllTextAsync(_configPath);
            var data = JsonSerializer.Deserialize<SuspendableData>(json);
            if (data?.Suspendable != null)
            {
                _suspendableProcesses.Clear();
                foreach (var name in data.Suspendable)
                    _suspendableProcesses.Add(name);
            }
        }
        catch (Exception ex) { Log($"LoadAsync error: {ex.Message}"); }
    }

    public async Task SaveAsync()
    {
        try
        {
            var data = new SuspendableData
            {
                Suspendable = new List<string>(_suspendableProcesses)
            };
            string json = JsonSerializer.Serialize(data);
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex) { Log($"SaveAsync error: {ex.Message}"); }
    }

    private class SuspendableData
    {
        public List<string>? Suspendable { get; set; }
    }
}
```

- [ ] **Step 2: Remove old file from project**

Since the `.csproj` likely uses SDK-style globbing (`<Project Sdk="...">`), deleting the old file is sufficient — it won't be compiled. Verify:

```powershell
Get-ChildItem -Recurse -Filter "ProcessLockService.cs" | ForEach-Object { $_.FullName }
```

Expected: no files found.

- [ ] **Step 3: Build and fix any namespace/using issues**

```powershell
dotnet build NanoShell\NanoShell.csproj 2>&1
```

Resolve any `ProcessLockService` references that still point to the old name.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: rename ProcessLockService to SuspendableProcessService, fix persistence"
```

---

### Task 3: Rewrite ExplorerWatchdogService

**Files:**
- Modify: `NanoShell.Services/ExplorerWatchdogService.cs`

**Interfaces:**
- Consumes: `SuspendManager` (from Task 1)
- Produces: `ExplorerWatchdogService` that uses `SuspendManager` API

- [ ] **Step 1: Rewrite ExplorerWatchdogService**

```csharp
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NanoShell.Services;

public class ExplorerWatchdogService
{
    private readonly SuspendManager _suspendManager;
    private CancellationTokenSource? _cts;
    private Task? _watchTask;

    public ExplorerWatchdogService(SuspendManager suspendManager)
    {
        _suspendManager = suspendManager;
    }

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _watchTask = Task.Run(() => WatchLoopAsync(token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _watchTask = null;
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);

                foreach (var proc in Process.GetProcessesByName("explorer"))
                {
                    ct.ThrowIfCancellationRequested();
                    uint pid = (uint)proc.Id;

                    try
                    {
                        bool isSuspended = _suspendManager.IsSuspended(pid);

                        if (isSuspended)
                        {
                            // Explorer is suspended — check if it's been too long
                            // The staged resume should handle it, but if we see it
                            // suspended outside expected window, resume it
                            await _suspendManager.ResumeProcessAsync(pid, ct);
                            await Task.Delay(500, ct);
                        }

                        // Check process responsiveness
                        if (!proc.Responding)
                        {
                            string? exePath = null;
                            try { exePath = proc.MainModule?.FileName; }
                            catch { }

                            try { proc.Kill(); }
                            catch { }

                            if (!string.IsNullOrEmpty(exePath))
                            {
                                await Task.Delay(1500, ct);
                                try { Process.Start(exePath); }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build NanoShell\NanoShell.csproj 2>&1
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor: rewrite ExplorerWatchdogService to use SuspendManager API"
```

---

### Task 4: Update MainWindow.xaml.cs

**Files:**
- Modify: `NanoShell.UI/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `SuspendManager`, `SuspendableProcessService` (from Tasks 1+2), `ExplorerWatchdogService` (from Task 3)

- [ ] **Step 1: Rewrite MainWindow.xaml.cs**

Add `using NanoShell.Services;` (already present). Replace the service fields and wiring:

```csharp
private readonly SuspendManager _suspendManager;           // NEW
private readonly SuspendableProcessService _suspendableService; // was ProcessLockService
private readonly ExplorerWatchdogService _explorerWatchdog;     // unchanged ref

// In constructor, after LockScreenService creation:
_suspendManager = new SuspendManager();
_suspendableService = new SuspendableProcessService(_suspendManager);
_explorerWatchdog = new ExplorerWatchdogService(_suspendManager); // pass SuspendManager
_lockScreenService.LockScreenRequested += OnLockScreenRequested;
_lockScreenService.LockScreenDismissed += OnLockScreenDismissed;

// In OnSourceInitialized, after _lockScreenService.Start():
_explorerWatchdog.Start(); // was StartWatching()

// In Window_Closed:
_suspendManager.CancelAll();
_ = _suspendManager.ResumeAllAsync(CancellationToken.None);
_ = _suspendableService.SaveAsync();
_explorerWatchdog.Stop(); // was StopWatching()

// Replace OnLockScreenRequested:
private async void OnLockScreenRequested(string wallpaperPath)
{
    await _suspendManager.SuspendAllAsync(
        (HashSet<string>)_suspendableService.SuspendableTargets,
        CancellationToken.None);
    var win = new LockScreenWindow(_lockScreenService, _suspendableService, _suspendManager, wallpaperPath);
    win.Show();
}

// Replace OnLockScreenDismissed:
private async void OnLockScreenDismissed()
{
    await _suspendManager.ResumeAllAsync(CancellationToken.None);
}
```

Full file after changes:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using NanoShell.Interop;
using NanoShell.Services;

namespace NanoShell.UI;

public partial class MainWindow : Window
{
    private static readonly Mutex _instanceMutex = new(false, "NanoShellSingleInstance");
    private static bool _ownsMutex;

    private readonly KeyboardService _keyboardService;
    private readonly AppBarService _appBarService;
    private readonly WindowStateService _windowStateService;
    private readonly WindowAutoManagerService _windowAutoManager;
    private readonly LockScreenService _lockScreenService;
    private readonly SuspendManager _suspendManager;
    private readonly SuspendableProcessService _suspendableService;
    private readonly ExplorerWatchdogService _explorerWatchdog;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = NativeMethods.GetWindowLong(hwnd, Constants.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, Constants.GWL_EXSTYLE, extendedStyle | Constants.WS_EX_NOACTIVATE | Constants.WS_EX_TOOLWINDOW);

        _windowAutoManager.Start();
        _lockScreenService.Start();
        _explorerWatchdog.Start();

        new StartupRegistrationService().RegisterAtLogon();
    }

    public MainWindow()
    {
        try
        {
            if (!_instanceMutex.WaitOne(TimeSpan.Zero, false))
            {
                Application.Current.Shutdown();
                return;
            }
        }
        catch (AbandonedMutexException)
        {
        }

        _ownsMutex = true;
        InitializeComponent();
        _keyboardService = new KeyboardService();
        _appBarService = new AppBarService();
        _windowStateService = new WindowStateService();
        _windowAutoManager = new WindowAutoManagerService(Dispatcher, _windowStateService);
        _lockScreenService = new LockScreenService(Dispatcher);
        _suspendManager = new SuspendManager();
        _suspendableService = new SuspendableProcessService(_suspendManager);
        _explorerWatchdog = new ExplorerWatchdogService(_suspendManager);
        _lockScreenService.LockScreenRequested += OnLockScreenRequested;
        _lockScreenService.LockScreenDismissed += OnLockScreenDismissed;

        Top = SystemParameters.PrimaryScreenHeight - 50;
        Width = SystemParameters.PrimaryScreenWidth;
        _appBarService.RegisterAppBar(Height);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _appBarService.RegisterAppBar(Height);
    }

    private async void Window_Closed(object sender, EventArgs e)
    {
        _suspendManager.CancelAll();
        await _suspendManager.ResumeAllAsync(CancellationToken.None);
        await _suspendableService.SaveAsync();
        _explorerWatchdog.Stop();
        _windowAutoManager?.Dispose();
        _lockScreenService?.Stop();
        _appBarService.RegisterAppBar();
        if (_ownsMutex)
            _instanceMutex.ReleaseMutex();
    }

    private async void OnLockScreenRequested(string wallpaperPath)
    {
        var targets = new HashSet<string>(_suspendableService.SuspendableTargets, StringComparer.OrdinalIgnoreCase);
        await _suspendManager.SuspendAllAsync(targets, CancellationToken.None);
        var win = new LockScreenWindow(_lockScreenService, _suspendableService, _suspendManager, wallpaperPath);
        win.Show();
    }

    private async void OnLockScreenDismissed()
    {
        await _suspendManager.ResumeAllAsync(CancellationToken.None);
    }

    private DateTime _lastBackTapTime = DateTime.MinValue;
    private DispatcherTimer _backTapTimer;

    private void HandleTapWithDoubleTap(ref DateTime lastTapTime, ref DispatcherTimer timer, Action singleTap, Action doubleTap)
    {
        timer?.Stop();
        var now = DateTime.Now;
        if ((now - lastTapTime).TotalMilliseconds < 300)
        {
            doubleTap();
            lastTapTime = DateTime.MinValue;
        }
        else
        {
            lastTapTime = now;
            DispatcherTimer localTimer = null;
            localTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Normal,
                (s, args) =>
                {
                    singleTap();
                    localTimer?.Stop();
                },
                Dispatcher.CurrentDispatcher);
            timer = localTimer;
            localTimer.Start();
        }
    }

    private void CancelPendingTap(ref DateTime lastTapTime, ref DispatcherTimer timer)
    {
        timer?.Stop();
        lastTapTime = DateTime.MinValue;
    }

    private void BtnBack_Gesture(object sender, StylusSystemGestureEventArgs e)
    {
        switch (e.SystemGesture)
        {
            case SystemGesture.Tap:
                HandleTapWithDoubleTap(ref _lastBackTapTime, ref _backTapTimer,
                    () => _keyboardService.SimulateKeyCombination(Key.LeftAlt, Key.Left),
                    () => _keyboardService.SimulateKeyPress(Key.Escape));
                break;
            case SystemGesture.RightTap:
                CancelPendingTap(ref _lastBackTapTime, ref _backTapTimer);
                _keyboardService.SimulateKeyCombination(Key.LeftAlt, Key.F4);
                break;
        }
        e.Handled = true;
    }

    private void BtnCloseAll_Gesture(object sender, StylusSystemGestureEventArgs e)
    {
        switch (e.SystemGesture)
        {
            case SystemGesture.Tap:
                _keyboardService.SimulateKeyCombination(Key.LWin, Key.D);
                break;
            case SystemGesture.RightTap:
                _appBarService.OpenTouchKeyboard();
                break;
        }
        e.Handled = true;
    }

    private DateTime _lastTaskViewTapTime = DateTime.MinValue;
    private DispatcherTimer _taskViewTapTimer;

    private void BtnTaskView_Gesture(object sender, StylusSystemGestureEventArgs e)
    {
        switch (e.SystemGesture)
        {
            case SystemGesture.Tap:
                HandleTapWithDoubleTap(ref _lastTaskViewTapTime, ref _taskViewTapTimer,
                    () => _keyboardService.SimulateKeyCombination(Key.LWin, Key.Tab),
                    () => _keyboardService.SimulateKeyCombination(Key.LeftAlt, Key.Tab));
                break;
            case SystemGesture.RightTap:
                CancelPendingTap(ref _lastTaskViewTapTime, ref _taskViewTapTimer);
                _appBarService.RegisterAppBar(Height);
                _windowStateService.ToggleMaximize();
                break;
        }
        e.Handled = true;
    }

    private void Panel_Gesture(object sender, StylusSystemGestureEventArgs e)
    {
        if (e.SystemGesture == SystemGesture.RightTap)
        {
            _keyboardService.SimulateKeyPress(Key.PrintScreen);
            e.Handled = true;
        }
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build NanoShell\NanoShell.csproj 2>&1
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor: wire SuspendManager and SuspendableProcessService in MainWindow"
```

---

### Task 5: Rename ProcessLockPanel → SuspendableProcessPanel

**Files:**
- Rename: `NanoShell.UI/ProcessLockPanel.xaml` → `NanoShell.UI/SuspendableProcessPanel.xaml`
- Rename: `NanoShell.UI/ProcessLockPanel.xaml.cs` → `NanoShell.UI/SuspendableProcessPanel.xaml.cs`
- Modify: `NanoShell.UI/SuspendableProcessPanel.xaml` (update `x:Class` and UI labels)
- Modify: `NanoShell.UI/SuspendableProcessPanel.xaml.cs` (update all references)

- [ ] **Step 1: Rename files**

```powershell
Remove-Item -LiteralPath "NanoShell.UI\ProcessLockPanel.xaml"
Remove-Item -LiteralPath "NanoShell.UI\ProcessLockPanel.xaml.cs"
# Create new files via write tool
```

Actually — SDK-style projects include files by glob, so renaming is just creating new files and deleting old ones. But since WPF XAML files use `x:Class` and code-behind linking, I need to create the new files properly.

For WPF SDK projects with `<Project Sdk="Microsoft.NET.Sdk">`, XAML files are compiled via the `ApplicationDefinition` and `Page` items. Since this project likely uses the default globbing, the new files will be picked up automatically.

Write `NanoShell.UI/SuspendableProcessPanel.xaml`:

```xml
<UserControl x:Class="NanoShell.UI.SuspendableProcessPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="440">
    <UserControl.Resources>
        <Style TargetType="Button">
            <Setter Property="Background" Value="#01000000"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}"
                                Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter Property="Background" Value="#22AAAAAA"/>
                            </Trigger>
                            <Trigger Property="IsFocused" Value="True">
                                <Setter Property="Background" Value="#01000000"/>
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter Property="Background" Value="#01000000"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ToggleSwitchStyle" TargetType="CheckBox">
            <Setter Property="Width" Value="52"/>
            <Setter Property="Height" Value="28"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="CheckBox">
                        <Border x:Name="BgBorder" CornerRadius="14" Background="#555" BorderThickness="0">
                            <Ellipse x:Name="Knob" Width="22" Height="22" Fill="White"
                                     HorizontalAlignment="Left" Margin="3,0,0,0"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="BgBorder" Property="Background" Value="#4CAF50"/>
                                <Setter TargetName="Knob" Property="Margin" Value="27,0,0,0"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="RowButtonStyle" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Padding" Value="10,12"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        </Style>
    </UserControl.Resources>

    <Border Background="#CC151515" CornerRadius="14,0,0,14">
        <Grid Margin="16">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <!-- Header -->
            <Grid Grid.Row="0" Margin="0,0,0,16">
                <TextBlock Text="Suspend on Lock" Foreground="White" FontSize="22" FontWeight="Light"
                           VerticalAlignment="Center"/>
                <Button x:Name="CloseButton" Content="✕" Foreground="#999" Background="Transparent"
                        BorderThickness="0" FontSize="20" Cursor="Hand" Width="36" Height="36"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Click="CloseButton_Click"/>
            </Grid>

            <!-- Search -->
            <TextBox x:Name="SearchBox" Grid.Row="1"
                     Foreground="White" Background="#333" BorderThickness="0"
                     Padding="12,10" Margin="0,0,0,12" FontSize="16"
                     GotFocus="SearchBox_GotFocus" LostFocus="SearchBox_LostFocus"
                     TextChanged="SearchBox_TextChanged"/>

            <!-- Filter pills -->
            <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,12">
                <Border x:Name="FilterAllBg" Background="#666" CornerRadius="6" Margin="0,0,8,0">
                    <Button x:Name="FilterAll" Content="All" Foreground="White"
                            Background="Transparent" BorderThickness="0" Padding="16,8"
                            Cursor="Hand" FontSize="14" Click="FilterAll_Click"/>
                </Border>
                <Border x:Name="FilterSuspendBg" Background="#444" CornerRadius="6">
                    <Button x:Name="FilterSuspend" Content="Suspend" Foreground="White"
                            Background="Transparent" BorderThickness="0" Padding="16,8"
                            Cursor="Hand" FontSize="14" Click="FilterSuspend_Click"/>
                </Border>
            </StackPanel>

            <!-- Process list -->
            <ScrollViewer Grid.Row="3" VerticalScrollBarVisibility="Hidden" PanningMode="Both">
                <StackPanel>
                    <TextBlock x:Name="RegularHeader" Text="REGULAR" Foreground="#888"
                               FontSize="12" Margin="0,6,0,4" FontWeight="SemiBold"
                               Visibility="Visible"/>
                    <ItemsControl x:Name="RegularList" Visibility="Visible">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Style="{StaticResource RowButtonStyle}"
                                        Click="Row_Click">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <Image Grid.Column="0" Width="24" Height="24"
                                               Source="{Binding Icon}" Margin="0,0,10,0"/>
                                        <TextBlock Grid.Column="1" Text="{Binding FreezeIcon}"
                                                   Foreground="{Binding FreezeColor}" FontSize="16"
                                                   VerticalAlignment="Center" Margin="0,0,8,0"/>
                                        <StackPanel Grid.Column="2" VerticalAlignment="Center">
                                            <TextBlock FontWeight="SemiBold" Foreground="White"
                                                       FontSize="16" TextTrimming="CharacterEllipsis">
                                                <Run Text="{Binding DisplayName, Mode=OneWay}"/>
                                                <Run Text="{Binding CountLabel, Mode=OneWay}" Foreground="#888" FontWeight="Normal" FontSize="13"/>
                                            </TextBlock>
                                            <TextBlock Text="{Binding WindowTitles}" Foreground="#888"
                                                       FontSize="13" TextTrimming="CharacterEllipsis"/>
                                        </StackPanel>
                                        <CheckBox Grid.Column="3" IsHitTestVisible="False"
                                                  Style="{StaticResource ToggleSwitchStyle}"
                                                  IsChecked="{Binding IsSuspendable, Mode=OneWay}"/>
                                    </Grid>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <Border Height="1" Background="#333" Margin="0,12"/>

                    <TextBlock x:Name="BackgroundHeader" Text="BACKGROUND" Foreground="#888"
                               FontSize="12" Margin="0,6,0,4" FontWeight="SemiBold"
                               Visibility="Visible"/>
                    <ItemsControl x:Name="BackgroundList" Visibility="Visible">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Style="{StaticResource RowButtonStyle}"
                                        Click="Row_Click">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <Image Grid.Column="0" Width="24" Height="24"
                                               Source="{Binding Icon}" Margin="0,0,10,0"/>
                                        <TextBlock Grid.Column="1" Text="{Binding FreezeIcon}"
                                                   Foreground="{Binding FreezeColor}" FontSize="16"
                                                   VerticalAlignment="Center" Margin="0,0,8,0"/>
                                        <StackPanel Grid.Column="2" VerticalAlignment="Center">
                                            <TextBlock FontWeight="SemiBold" Foreground="White"
                                                       FontSize="16" TextTrimming="CharacterEllipsis">
                                                <Run Text="{Binding DisplayName, Mode=OneWay}"/>
                                                <Run Text="{Binding CountLabel, Mode=OneWay}" Foreground="#888" FontWeight="Normal" FontSize="13"/>
                                            </TextBlock>
                                            <TextBlock Text="{Binding WindowTitles}" Foreground="#888"
                                                       FontSize="13" TextTrimming="CharacterEllipsis"/>
                                        </StackPanel>
                                        <CheckBox Grid.Column="3" IsHitTestVisible="False"
                                                  Style="{StaticResource ToggleSwitchStyle}"
                                                  IsChecked="{Binding IsSuspendable, Mode=OneWay}"/>
                                    </Grid>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ScrollViewer>
        </Grid>
    </Border>
</UserControl>
```

Write `NanoShell.UI/SuspendableProcessPanel.xaml.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NanoShell.Interop;
using NanoShell.Services;

namespace NanoShell.UI;

public partial class SuspendableProcessPanel : UserControl
{
    public event Action? CloseRequested;

    private readonly SuspendableProcessService _service;
    private readonly SuspendManager _suspendManager;
    private List<ProcessGroupViewModel> _allGroups = new();
    private string _filter = "all";
    private string _search = "";
    private bool _isRefreshing;
    private HashSet<int>? _thawedExplorerPids;

    public SuspendableProcessPanel(SuspendableProcessService service, SuspendManager suspendManager)
    {
        _service = service;
        _suspendManager = suspendManager;
        InitializeComponent();
    }

    public async Task ShowPanelAsync()
    {
        await RefreshAsync();
        Visibility = Visibility.Visible;
    }

    public async Task RefreshAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await Task.Run(() => _service.EnumerateAll());
            sw.Stop();
            Log($"RefreshAsync: EnumerateAll took {sw.ElapsedMilliseconds}ms, got {result.Count} entries");
            _allGroups = result
                .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProcessGroupViewModel(g.ToList()))
                .ToList();
            ProcessGroupViewModel.ResolveParentIcons(_allGroups);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Log($"RefreshAsync error: {ex}");
        }
        finally { _isRefreshing = false; }
    }

    private static void Log(string msg)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NanoShell", "suspend_manager.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    private void ApplyFilter()
    {
        var query = _allGroups.AsEnumerable();

        if (_filter == "suspend")
            query = query.Where(g => g.IsSuspendable);

        if (!string.IsNullOrEmpty(_search))
        {
            string s = _search.ToLowerInvariant();
            query = query.Where(g =>
                g.Name.ToLowerInvariant().Contains(s) ||
                g.WindowTitles.ToLowerInvariant().Contains(s));
        }

        var list = query
            .OrderByDescending(g => g.HasIcon)
            .ThenBy(g => g.Name)
            .ToList();

        RegularList.ItemsSource = list.Where(g => !g.IsBackground);
        BackgroundList.ItemsSource = list.Where(g => g.IsBackground);

        bool hasRegular = list.Any(g => !g.IsBackground);
        bool hasBackground = list.Any(g => g.IsBackground);

        RegularHeader.Visibility = hasRegular ? Visibility.Visible : Visibility.Collapsed;
        RegularList.Visibility = hasRegular ? Visibility.Visible : Visibility.Collapsed;
        BackgroundHeader.Visibility = hasBackground ? Visibility.Visible : Visibility.Collapsed;
        BackgroundList.Visibility = hasBackground ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text;
        ApplyFilter();
    }

    private async void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _suspendManager.KeyboardInputActive = true;
        var thawed = new HashSet<int>();
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
        {
            uint pid = (uint)proc.Id;
            if (_suspendManager.IsSuspended(pid))
            {
                await _suspendManager.ResumeProcessAsync(pid, CancellationToken.None);
                thawed.Add((int)pid);
            }
        }
        _thawedExplorerPids = thawed;
    }

    private async void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _suspendManager.KeyboardInputActive = false;
        if (_thawedExplorerPids != null && _thawedExplorerPids.Count > 0)
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                uint pid = (uint)proc.Id;
                if (_thawedExplorerPids.Contains((int)pid) && !_suspendManager.IsSuspended(pid))
                {
                    await _suspendManager.SuspendProcessAsync(pid, CancellationToken.None);
                }
            }
            _thawedExplorerPids = null;
        }
    }

    private void FilterAll_Click(object sender, RoutedEventArgs e)
    {
        _filter = "all";
        FilterAllBg.Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        FilterSuspendBg.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private void FilterSuspend_Click(object sender, RoutedEventArgs e)
    {
        _filter = "suspend";
        FilterSuspendBg.Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        FilterAllBg.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private async void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProcessGroupViewModel vm)
        {
            bool newVal = !vm.IsSuspendable;
            await _service.ToggleSuspendableAsync(vm.Name, newVal);
            foreach (var entry in vm.Entries)
            {
                uint pid = (uint)entry.Pid;
                if (newVal && !entry.IsFrozen)
                    await _suspendManager.SuspendProcessAsync(pid, CancellationToken.None);
                else if (!newVal && entry.IsFrozen)
                    await _suspendManager.ResumeProcessAsync(pid, CancellationToken.None);
            }
            await RefreshAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _suspendManager.KeyboardInputActive = false;
        CloseRequested?.Invoke();
    }
}

public class ProcessGroupViewModel
{
    private static readonly Dictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _defaultIcon;

    private readonly ImageSource? _icon;

    public List<ProcessEntry> Entries { get; }
    public string Name { get; }
    public string ExePath { get; }
    public int Count { get; }
    public bool IsSuspendable => Entries[0].IsSuspendable;
    public bool IsFrozen => Entries.Any(e => e.IsFrozen);
    public bool IsBackground => Entries.All(e => e.IsBackground);
    public int ParentPid => Entries[0].ParentPid;
    public bool HasIcon => _icon != null;

    public string DisplayName => Name + ".exe";
    public string CountLabel => Count > 1 ? $"×{Count}" : "";
    public string WindowTitles
    {
        get
        {
            var titles = Entries
                .Where(e => !string.IsNullOrEmpty(e.WindowTitle))
                .Select(e => e.WindowTitle)
                .Distinct()
                .Take(2)
                .ToList();
            if (titles.Count == 0 && IsBackground)
                return "Background";
            return string.Join(", ", titles);
        }
    }

    public string FreezeIcon => IsFrozen ? "\u2744" : "\u25CB";
    public Brush FreezeColor => IsFrozen
        ? new SolidColorBrush(Color.FromRgb(0x66, 0xCC, 0xFF))
        : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    public ImageSource? Icon => _icon ?? DefaultIcon;

    private static ImageSource DefaultIcon
    {
        get
        {
            if (_defaultIcon == null)
                _defaultIcon = CreateDefaultIcon();
            return _defaultIcon;
        }
    }

    public ProcessGroupViewModel(List<ProcessEntry> entries)
    {
        Entries = entries;
        Name = entries[0].Name;
        ExePath = entries[0].ExePath;
        Count = entries.Count;
        if (!string.IsNullOrEmpty(ExePath))
            _icon = GetOrLoadIcon(ExePath);
    }

    private static ImageSource? GetOrLoadIcon(string path)
    {
        if (_iconCache.TryGetValue(path, out var img))
            return img;
        img = LoadIcon(path);
        if (img != null)
            _iconCache[path] = img;
        return img;
    }

    public static void ResolveParentIcons(List<ProcessGroupViewModel> groups)
    {
        var pidMap = new Dictionary<int, ProcessGroupViewModel>();
        foreach (var g in groups)
            foreach (var e in g.Entries)
                pidMap[e.Pid] = g;

        foreach (var g in groups)
        {
            if (g._icon != null) continue;
            int parentPid = g.ParentPid;
            if (parentPid <= 0) continue;
            if (pidMap.TryGetValue(parentPid, out var parent) && parent._icon != null)
            {
                if (!string.IsNullOrEmpty(g.ExePath) && !_iconCache.ContainsKey(g.ExePath))
                    _iconCache[g.ExePath] = parent._icon;
            }
        }
    }

    private static ImageSource? LoadIcon(string exePath)
    {
        if (!File.Exists(exePath)) return null;
        try
        {
            uint count = NativeMethods.ExtractIconEx(exePath, 0, out _, out IntPtr hIconSmall, 1);
            if (count == 0 || hIconSmall == IntPtr.Zero) return null;
            try
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(hIconSmall,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            finally
            {
                NativeMethods.DestroyIcon(hIconSmall);
            }
        }
        catch { return null; }
    }

    private static ImageSource CreateDefaultIcon()
    {
        var geometry = Geometry.Parse(
            "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z " +
            "M7.07,18.28 C7.5,17.38 10.12,16.5 12,16.5 C13.88,16.5 16.5,17.38 16.93,18.28 " +
            "C15.57,19.36 13.86,20 12,20 C10.14,20 8.43,19.36 7.07,18.28 Z " +
            "M18.36,16.83 C16.93,15.09 14.66,14 12,14 C9.34,14 7.07,15.09 5.64,16.83 " +
            "C4.62,15.49 4,13.82 4,12 C4,7.59 7.59,4 12,4 C16.41,4 20,7.59 20,12 " +
            "C20,13.82 19.38,15.49 18.36,16.83 Z " +
            "M12,6 C10.9,6 10,6.9 10,8 C10,9.1 10.9,10 12,10 C13.1,10 14,9.1 14,8 C14,6.9 13.1,6 12,6 Z");
        var drawing = new GeometryDrawing
        {
            Geometry = geometry,
            Brush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99))
        };
        var group = new DrawingGroup { Children = { drawing } };
        group.Freeze();
        var bs = new DrawingImage(group);
        bs.Freeze();
        return bs;
    }
}
```

- [ ] **Step 2: Delete old ProcessLockPanel files and build**

```powershell
Remove-Item -LiteralPath "NanoShell.UI\ProcessLockPanel.xaml" -ErrorAction Stop
Remove-Item -LiteralPath "NanoShell.UI\ProcessLockPanel.xaml.cs" -ErrorAction Stop
dotnet build NanoShell\NanoShell.csproj 2>&1
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor: rename ProcessLockPanel to SuspendableProcessPanel, update labels"
```

---

### Task 6: Update LockScreenWindow

**Files:**
- Modify: `NanoShell.UI/LockScreenWindow.xaml` (tooltip text)
- Modify: `NanoShell.UI/LockScreenWindow.xaml.cs` (type references)

- [ ] **Step 1: Update LockScreenWindow.xaml gear button tooltip**

Change tooltip from `"Process Lock"` to `"Suspend settings"`:

Find:
```xml
<Button x:Name="GearButton" Content="⚙" ... ToolTip="Process Lock" .../>
```

Replace `ToolTip="Process Lock"` with `ToolTip="Suspend settings"` in the XAML.

- [ ] **Step 2: Update LockScreenWindow.xaml.cs**

Replace constructor and field types:

```csharp
private readonly SuspendableProcessService _suspendableService;
private readonly SuspendManager _suspendManager;
private SuspendableProcessPanel? _processPanel;

public LockScreenWindow(LockScreenService service, SuspendableProcessService suspendableService, SuspendManager suspendManager, string wallpaperPath)
{
    _service = service;
    _suspendableService = suspendableService;
    _suspendManager = suspendManager;
    _wallpaperPath = wallpaperPath;
    _watchDir = GetWatchDirectory(wallpaperPath);
    _mode = LockScreenMode.AOD;
    InitializeComponent();
    Opacity = 0;
    ShowAOD();
    TouchDown += OnTouchDown;
    TouchMove += OnTouchMove;
    TouchUp += OnTouchUp;
}
```

Update `GearButton_Click`:

```csharp
private async void GearButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        _activeTimer?.Stop();
        GearButton.Visibility = Visibility.Collapsed;
        PanelOverlay.Visibility = Visibility.Visible;
        SetNoActivate(false);

        if (_processPanel == null)
        {
            _processPanel = new SuspendableProcessPanel(_suspendableService, _suspendManager);
            _processPanel.CloseRequested += OnPanelCloseRequested;
            PanelHost.Content = _processPanel;
            await _processPanel.ShowPanelAsync();
        }
        else
        {
            await _processPanel.RefreshAsync();
        }
    }
    catch (Exception ex)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NanoShell", "suspend_manager.log");
        try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] GearButton_Click error: {ex}\n"); }
        catch { }
    }
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build NanoShell\NanoShell.csproj 2>&1
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: update LockScreenWindow for SuspendableProcessPanel rename"
```

---

### Task 7: Final Build and Verify

- [ ] **Step 1: Clean build**

```powershell
dotnet clean NanoShell\NanoShell.csproj
dotnet build NanoShell\NanoShell.csproj 2>&1
```

Expected: `Build succeeded.` with 0 warnings, 0 errors.

- [ ] **Step 2: Verify no stale references**

Search for any remaining `ProcessLockService`, `ProcessLockPanel`, `FreezeAll`, `ThawAll`, `_exceptions`, `IsFreezeTarget` references:

```powershell
Select-String -Pattern "ProcessLockService|ProcessLockPanel|FreezeAll|ThawAll|_exceptions|IsFreezeTarget|ToggleFreeze|suspend_exceptions" -Path "NanoShell.Services\*.cs","NanoShell.UI\*.cs" -SimpleMatch
```

Expected: no matches (all renamed).

Exclude false positives from `ProcessEntry.IsFrozen` (stays as-is — describes state, not action) and `FreezeIcon`/`FreezeColor` (UI display properties, fine to keep).

- [ ] **Step 3: Commit final cleanup if any**

```bash
git add -A
git commit -m "chore: final cleanup after suspend manager rename"
```
