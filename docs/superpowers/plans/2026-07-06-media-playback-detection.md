# Media Playback Detection — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent screen lock during video playback using WinRT `GlobalSystemMediaTransportControlsSessionManager`.

**Architecture:** New `MediaPlaybackService` polls SMTC sessions for `PlaybackType.Video + PlaybackStatus.Playing`. `LockScreenService` gets an optional delegate — if video playing, idle timer resets and lock doesn't fire.

**Tech Stack:** .NET 8, WPF, WinRT (`Windows.Media.Control`), `net8.0-windows10.0.19041.0`

## Global Constraints

- All WinRT exceptions in `MediaPlaybackService` must be caught → return `false`
- No MVVM / DI — pure code-behind, manual wiring
- Follow existing code style (no comments, `NanoShell.Services` namespace for services)
- Only modify files listed below

---

### Task 1: MediaPlaybackService

**Files:**
- Create: `NanoShell.Services\MediaPlaybackService.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `MediaPlaybackService.IsVideoPlaying()` → `bool`

- [ ] **Step 1: Create the service class**

```csharp
using System;
using Windows.Media.Control;

namespace NanoShell.Services;

public sealed class MediaPlaybackService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public MediaPlaybackService()
    {
        try
        {
            var task = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
            task.Wait(5000);
            if (task.IsCompletedSuccessfully)
                _manager = task.Result;
        }
        catch
        {
            _manager = null;
        }
    }

    public bool IsVideoPlaying()
    {
        if (_manager is null) return false;

        try
        {
            var sessions = _manager.GetSessions();
            foreach (var session in sessions)
            {
                var info = session.GetPlaybackInfo();
                if (info is null) continue;
                if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                    info.PlaybackType == MediaPlaybackType.Video)
                {
                    return true;
                }
            }
        }
        catch
        {
            // SMTC may throw if session disappears mid-enumeration, etc.
        }

        return false;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build NanoShell.sln --nologo -clp:NoSummary`  
Expected: succeeds (pre-existing warnings only)

- [ ] **Step 3: Commit**

```bash
git add NanoShell.Services\MediaPlaybackService.cs
git commit -m "feat: add MediaPlaybackService wrapping WinRT SMTC"
```

---

### Task 2: LockScreenService — VideoPlaybackGuard

**Files:**
- Modify: `NanoShell.Services\LockScreenService.cs`

**Interfaces:**
- Consumes: `MediaPlaybackService` (via `Func<bool>`)
- Produces: modified `LockScreenService` constructor

- [ ] **Step 1: Add constructor parameter and field**

Change constructor from:
```csharp
public LockScreenService(Dispatcher dispatcher)
```
to:
```csharp
public LockScreenService(Dispatcher dispatcher, Func<bool>? videoPlaybackGuard = null)
```

Add field at top of class:
```csharp
private readonly Func<bool>? _videoPlaybackGuard;
```

Add line in constructor before `_dispatcher = dispatcher`:
```csharp
        _videoPlaybackGuard = videoPlaybackGuard;
```

- [ ] **Step 2: Guard the lock in OnPollTick**

In `OnPollTick`, before the `if (_locked) return;` guard, add the video check. After existing `if (_locked) return;`:

```csharp
        if (_videoPlaybackGuard?.Invoke() == true)
        {
            _lastInputTick = (uint)Environment.TickCount;
            NativeMethods.GetCursorPos(out _lastCursorPos);
            return;
        }
```

This resets the idle timer every second video is playing, preventing lock.

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build NanoShell.sln --nologo -clp:NoSummary`  
Expected: succeeds

- [ ] **Step 4: Commit**

```bash
git add NanoShell.Services\LockScreenService.cs
git commit -m "feat: add VideoPlaybackGuard to LockScreenService"
```

---

### Task 3: Wire MediaPlaybackService in MainWindow

**Files:**
- Modify: `NanoShell.UI\MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `MediaPlaybackService`, `LockScreenService` (with guard)
- Produces: running system

- [ ] **Step 1: Add field and instantiate before LockScreenService**

Add field alongside other service fields (after line 26):
```csharp
    private readonly MediaPlaybackService _mediaPlayback;
```

In constructor, after `_windowStateService = new WindowStateService();` (before `_lockScreenService`), add:
```csharp
        _mediaPlayback = new MediaPlaybackService();
```

Change `_lockScreenService` instantiation line from:
```csharp
        _lockScreenService = new LockScreenService(Dispatcher);
```
to:
```csharp
        _lockScreenService = new LockScreenService(Dispatcher, () => _mediaPlayback.IsVideoPlaying());
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build NanoShell.sln --nologo -clp:NoSummary`  
Expected: succeeds

- [ ] **Step 3: Commit**

```bash
git add NanoShell.UI\MainWindow.xaml.cs
git commit -m "feat: wire MediaPlaybackService into LockScreenService"
```

---

### Task 4: Full build & verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build NanoShell.sln --nologo -clp:NoSummary`  
Expected: succeeds, zero errors

- [ ] **Step 2: Manual verification checklist**

| Scenario | Expected |
|---|---|
| No media playing | Lock after 15s idle |
| YouTube playing in Edge/Chrome | No lock |
| Only music (Spotify, etc.) | Lock after 15s idle |
| Video paused | Lock after 15s idle |
| Video + music simultaneously | No lock (video detected) |
| SMTC unavailable (locked session) | Lock after 15s idle (graceful fallback) |

- [ ] **Step 3: Final commit (if any pending changes)**

```bash
git commit -m "chore: finalize media playback detection"
```
