# Task 2 Report: Rewrite ProcessLockService → SuspendableProcessService

## What was implemented

- **Deleted** `NanoShell.Services/ProcessLockService.cs`
- **Created** `NanoShell.Services/SuspendableProcessService.cs` with code from task brief
- **Fixed** `NanoShell.Services/ExplorerWatchdogService.cs` to replace `ProcessLockService.ThawProcess()` call with inline `NativeMethods.NtResumeProcess` (same project, blocked build)

## Key changes in the new service vs old

| Aspect | Old (ProcessLockService) | New (SuspendableProcessService) |
|--------|--------------------------|----------------------------------|
| Constructor | No params, deleted old exceptions file | Takes `SuspendManager`, loads from `suspendable_processes.json` |
| Config file | `suspend_exceptions.json` | `suspendable_processes.json` |
| Persistence | Sync `SaveExceptions`/`LoadExceptions` | Async `SaveAsync`/`LoadSync` with proper JSON model |
| Freeze/thaw | Built-in (`_frozenPids`, `NtSuspendProcess`/`NtResumeProcess`) | Deferred to `SuspendManager` |
| Model property | `IsFreezeTarget` | `IsSuspendable` |
| `KeyboardInputActive` | Static on class | On `SuspendManager` instance |
| Log path | `process_lock.log` | `suspend_manager.log` (shared with SuspendManager) |
| `ThawProcess(IntPtr)` static | Existed | Removed (no equivalent) |

## Files changed

4 files changed, 318 insertions(+), 199 deletions(-)
- `ProcessLockService.cs` → `SuspendableProcessService.cs` (rename + rewrite, 51% similarity)
- `ExplorerWatchdogService.cs` (+2 lines for import, +1 line for inline NtResumeProcess loop)

## Build result

**Succeeded** — `dotnet build NanoShell.Services\NanoShell.Services.csproj` — 0 errors, 4 warnings (all pre-existing)

## Self-review findings

1. **ExplorerWatchdogService cross-reference**: The old `ProcessLockService.ThawProcess()` static method was referenced by `ExplorerWatchdogService.cs` (same project). Fixed it inline with `NativeMethods.NtResumeProcess` to unblock the build. A more complete rework using `SuspendManager` API is expected in Task 3.

2. **Model property rename**: `ProcessEntry.IsFreezeTarget` → `IsSuspendable`. Downstream consumers in `NanoShell.UI` (ProcessLockPanel.xaml, ProcessLockPanel.xaml.cs) still reference `IsFreezeTarget` — will be updated in Tasks 4–6.

3. **Constructor injection change**: `ProcessLockService` had a parameterless constructor; `SuspendableProcessService` requires `SuspendManager`. Callers in `MainWindow.xaml.cs`, `LockScreenWindow.xaml.cs` still use `new ProcessLockService()` — will be updated in Tasks 4–6.

4. **Log path shared**: Both `SuspendManager` and `SuspendableProcessService` log to `suspend_manager.log`. This is intentional (unified logging) but could cause confusion. No functional issue.

## Issues or concerns

None blocking for Task 2 scope. The downstream consumers in `NanoShell.UI` project still reference `ProcessLockService` and `IsFreezeTarget` — these will break when building the full solution. Tasks 4–6 are expected to resolve those.
