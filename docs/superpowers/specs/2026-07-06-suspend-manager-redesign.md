# Suspend Manager Redesign — Design Spec

## Goal
Replace the fragile process-lock/suspend subsystem with a predictable, stable, async-first `SuspendManager` that guarantees balanced suspend/resume, handles `explorer.exe` safely, and has clear naming throughout the codebase.

## Architecture

```
MainWindow.xaml.cs
  ├── SuspendManager (NEW)
  ├── SuspendableProcessService (was ProcessLockService)
  ├── ExplorerWatchdogService (refactored, uses SuspendManager API)
  └── LockScreenService (unchanged)

SuspendManager
  ├── Per-PID state machine (Running → Suspending → Suspended → Resuming)
  ├── SemaphoreSlim(1,1) per PID — serialized access
  ├── ConcurrentDictionary<uint, int> suspendCount — tracking
  ├── CancellationTokenSource — per operation
  └── Events: ProcessSuspended, ProcessResumed, ProcessDrained

SuspendableProcessService
  ├── HashSet<string> suspendableProcesses (was _exceptions, inverted semantics)
  ├── Persistence: %LOCALAPPDATA%\NanoShell\suspendable_processes.json
  ├── Load on startup, save on every toggle + on shutdown
  └── EnumerateAll() → list for UI panel

ExplorerWatchdogService
  ├── Dedicated Task loop on threadpool
  ├── Uses SuspendManager API (no direct P/Invoke)
  └── Periodic health check via process handle + query
```

## API Surface

### SuspendManager
```csharp
Task SuspendProcessAsync(uint pid, CancellationToken ct);
Task SuspendAllAsync(CancellationToken ct);       // iterate suspendableTargets
Task ResumeProcessAsync(uint pid, CancellationToken ct);
Task ResumeAllAsync(CancellationToken ct);         // staged: explorer → rest
Task ResumeByNameAsync(string name, CancellationToken ct);
Task DrainProcessAsync(uint pid, CancellationToken ct);  // NtResumeProcess loop until error

bool IsSuspended(uint pid);     // from suspendCount dictionary
bool KeyboardInputActive { get; set; }  // set by panel on search focus
```

### SuspendableProcessService
```csharp
Task<List<ProcessEntry>> EnumerateAllAsync();
bool IsSuspendable(string nameLower);   // check set
Task ToggleSuspendableAsync(string nameLower, bool suspendable);
Task SaveAsync();
IReadOnlySet<string> SuspendableTargets { get; }
```

### ExplorerWatchdogService
```csharp
void Start(CancellationToken ct);
void Stop();
```

## State Machine

```
        ┌──────────┐
        │ Running  │ ◄──────────┐
        └────┬─────┘            │
             │ SuspendAsync()   │ ResumeAsync()
             ▼                  │
      ┌──────────────┐          │
      │  Suspending   │─────────┘
      └──────┬───────┘
             ▼
      ┌──────────────┐
      │  Suspended    │
      └──────┬───────┘
             │ ResumeAsync()
             ▼
      ┌──────────────┐
      │  Resuming     │
      └──────┬───────┘
             ▼
        ┌──────────┐
        │ Running  │
        └──────────┘
```

Transitions use `SemaphoreSlim(1,1)` keyed by PID. Any attempt to suspend an already-suspended process or resume a non-suspended one is a no-op (checked via `suspendCount`).

## Staged Resume (Thaw)

`ResumeAllAsync` follows this order:

1. Collect all suspended PIDs at call time
2. Find explorer PIDs among them
3. Resume explorer PIDs first (with drain)
4. `Task.Delay(250)` — let explorer regain responsiveness
5. Resume remaining PIDs in parallel via `Task.WhenAll`

## Drain Logic

After each `ResumeProcessAsync` that succeeds against a PID, `DrainProcessAsync` runs:

```
while (NtResumeProcess(handle) == 0)  // STATUS_SUCCESS = one more level unwound
    count++;
if (count > 0) log warning: "Drained {count} extra resume(s) for pid={pid}"
suspendCount[pid] = 0;
```

Cap: max 10 drain iterations per PID (safety against infinite loop).

## Suspend Tracking

| Action | Effect |
|--------|--------|
| `SuspendProcessAsync` | `suspendCount[pid]++`, log |
| `ResumeProcessAsync` + `DrainProcessAsync` | `suspendCount[pid] = 0`, log |
| Shutdown/crash recovery | Drain on next launch for known PIDs (optional) |
| Process exit | Pid removed from dictionary on next access |

## Explorer Handling

- **On lock**: explorer is suspended only if `KeyboardInputActive == false` and explorer is in `suspendableProcesses`
- **On unlock**: explorer resumed first (staged), watchdog verifies within 2s
- **Watchdog**: every 3s, checks that explorer isn't stuck suspended. If suspended longer than expected, calls `SuspendManager.ResumeProcessAsync` then restart if still unresponsive
- **Search focus** (in SuspendableProcessPanel): temporarily resume explorer, refreeze on blur — same existing UX, but reuses SuspendManager API

## Shutdown Safety

`MainWindow.Window_Closed`:
1. Cancel `_cts` (abort any in-flight freeze/thaw)
2. `await SuspendManager.ResumeAllAsync(CancellationToken.None)` — thaw everything, no cancellation
3. `await SuspendableProcessService.SaveAsync()` — persist suspendable list
4. `ExplorerWatchdogService.Stop()`

`ResumeAllAsync` is idempotent: process not in `suspendCount` → skip.

## Rename Plan

| Present | Future |
|---------|--------|
| `ProcessLockService` | `SuspendableProcessService` |
| `ProcessLockPanel` | `SuspendableProcessPanel` |
| `_exceptions` field | `_suspendableProcesses` |
| `_exceptionsPath` | `_configPath` |
| `suspend_exceptions.json` | `suspendable_processes.json` |
| `process_lock.log` | `suspend_manager.log` |
| `FreezeAll()` / `FreezeByName()` | `SuspendAllAsync()` / `SuspendByNameAsync()` |
| `ThawAll()` / `ThawByName()` | `ResumeAllAsync()` / `ResumeByNameAsync()` |
| `ToggleFreeze()` | `ToggleSuspendAsync()` |
| `IsFreezeTarget` (VM) | `IsSuspendable` |
| `_frozenPids` | `_suspendedPids` |
| `OnLockScreenRequested` (async void) | `OnLockScreenRequestedAsync` (async Task, fire-and-forget wrapper) |
| `LockScreenWindow` gear tooltip "Process Lock" | "Suspend settings" |

## Files Changed

| File | Action |
|------|--------|
| `NanoShell.Services/SuspendManager.cs` | CREATE |
| `NanoShell.Services/ExplorerWatchdogService.cs` | REWRITE (use SuspendManager API) |
| `NanoShell.UI/MainWindow.xaml.cs` | REWRITE (wire SuspendManager) |
| `NanoShell.Services/ProcessLockService.cs` | RENAME → `SuspendableProcessService.cs` + REWRITE |
| `NanoShell.UI/ProcessLockPanel.xaml` | RENAME → `SuspendableProcessPanel.xaml` + UPDATE labels |
| `NanoShell.UI/ProcessLockPanel.xaml.cs` | RENAME → `SuspendableProcessPanel.xaml.cs` + UPDATE references |
| `NanoShell.UI/LockScreenWindow.xaml` | MODIFY (tooltip/text changes) |
| `NanoShell.UI/LockScreenWindow.xaml.cs` | MODIFY (type name references) |
| Various `.cs` files with `ProcessLockService` refs | UPDATE imports and type names |

## Key Decisions

- `SuspendManager` is the **single authority** on suspend/resume — no static P/Invoke calls outside it
- Explorer watchdog uses `SuspendManager` methods, eliminating race conditions between watchdog and panel/lock logic
- `suspendCount` dictionary tracks how many times each PID was suspended; drain ensures exact balance
- `suspendable_processes.json` persists across sessions (no delete-on-startup)
- Staged resume prevents the "locked desktop but nonfunctional" state by ensuring explorer wakes first
- All public methods accept `CancellationToken`; internal operations use a class-level `_cts` that can be cancelled on shutdown
