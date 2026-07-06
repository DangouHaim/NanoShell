# Task 3: Rewrite ExplorerWatchdogService

**Files:**
- Modify: `NanoShell.Services/ExplorerWatchdogService.cs` (full rewrite)

**Interfaces:**
- Consumes: `SuspendManager` (Task 1 — `NanoShell.Services/SuspendManager.cs`)

## Context

- The current `ExplorerWatchdogService` uses a `DispatcherTimer` + `Task.Run` and calls the now-deleted `ProcessLockService.ThawProcess()` static method
- Task 2 patched it inline with `NativeMethods.NtResumeProcess` as a temporary fix — now you replace it fully
- The new version uses `SuspendManager` API (no direct P/Invoke)
- Runs on threadpool (not DispatcherTimer) in a dedicated Task loop

## Steps

### Step 1: Read current ExplorerWatchdogService.cs, then rewrite it

Read the current file for reference (it was already patched in Task 2).

Replace the entire content with:

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
                            await _suspendManager.ResumeProcessAsync(pid, ct);
                            await Task.Delay(500, ct);
                        }

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

### Step 2: Build

```powershell
dotnet build NanoShell.Services\NanoShell.Services.csproj 2>&1
```

Expected: Build succeeds, 0 errors.

### Step 3: Commit

```bash
git add -A
git commit -m "refactor: rewrite ExplorerWatchdogService to use SuspendManager API"
```
