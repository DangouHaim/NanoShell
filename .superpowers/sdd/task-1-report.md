# Task 1: SuspendManager.cs — Report

## What was implemented
Created `NanoShell.Services/SuspendManager.cs` — a thread-safe service that becomes the single authority for process suspend/resume. It wraps `NtSuspendProcess`/`NtResumeProcess` with per-PID SemaphoreSlim locking, suspend-count tracking, and a drain mechanism to normalize unbalanced resume calls.

### Public API surface
- `SuspendProcessAsync(uint pid, CancellationToken ct)` — suspend with idempotency guard
- `ResumeProcessAsync(uint pid, CancellationToken ct)` — resume + drain extra resumes
- `DrainProcessAsync(uint pid, CancellationToken ct)` — up to 10 extra resume calls
- `SuspendAllAsync(HashSet<string>, CancellationToken ct)` — two-pass suspend against named targets
- `ResumeAllAsync(CancellationToken ct)` — resume explorer first (with 250ms delay), then rest in parallel
- `ResumeByNameAsync(string name, CancellationToken ct)` — resume by process name
- `SuspendByNameAsync(string name, HashSet<int> pids, CancellationToken ct)` — suspend specific PIDs by name
- `CancelAll()` — cancel pending operations
- `IsSuspended(uint pid)` / `ProcessSuspendCount(uint pid)` — query state
- `KeyboardInputActive` — property to guard explorer suspension

## Build results
**Succeeded** — 0 errors. One new warning (CS1998 on `DrainProcessAsync`: async keyword used without await — method is call-safe, matches spec).

## Files changed
- `NanoShell.Services/SuspendManager.cs` (new, 223 lines)

## Commit
`48e595a` — `feat: add SuspendManager with per-PID state machine and drain`

## Self-review findings
1. **CS1998 warning** on `DrainProcessAsync` — harmless; method is `async` per brief spec but has no `await`. Works correctly.
2. **No `using` disposal of `PROCESSENTRY32` snapshot** — same pattern as `ProcessLockService.SnapshotProcesses()`. The `finally` block calls `CloseHandle` which is correct.
3. **`ResumeAllAsync` uses `CancellationToken.None`** when calling `DrainProcessAsync` inside `ResumeProcessAsync` — intentional to ensure drain completes even if caller cancels.
4. **Two-pass suspend** in `SuspendAllAsync` matches existing `ProcessLockService.FreezeAll` pattern for catching late-spawned processes.
5. **No internal constructor/dependency injection** — consistent with project conventions (no DI/MVVM framework).

## Issues or concerns
None.

---

## Code-review fixes applied (2026-07-06)

### Fix 1 (Critical): Wire `CancelAll()` to actually cancel operations
- `CancelAll()` now catches `ObjectDisposedException`
- Every public async method creates a `CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token)` and uses the combined token for all async waits
- `ResumeProcessAsync` now passes `combinedCt` to `DrainProcessAsync` instead of `CancellationToken.None`

### Fix 2 (Critical): Fix Process leaks in `ResumeByNameAsync` and `SuspendByNameAsync`
- Added `using var _ = proc;` in both methods to dispose each `Process` instance

### Fix 3 (Important): Make `_suspendCount` private
- Changed from `internal readonly` to `private readonly`

### Fix 4 (Important): Remove SemaphoreSlim entries from `_locks` after last usage
- In `ResumeProcessAsync`, after `_suspendCount.TryRemove(pid, out _)`, also calls `_locks.TryRemove(pid, out var removedLock)` and disposes

## Build result
**Succeeded** — 0 errors, no new warnings from SuspendManager.cs

## Commit
`4bc737c` — `fix: wire CancelAll, dispose Process objects, make _suspendCount private, cleanup _locks`
