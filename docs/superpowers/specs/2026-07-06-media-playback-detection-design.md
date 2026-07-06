# Media Playback Detection — Design Spec

## Problem

LockScreenService locks after 15s of inactivity. Video playback (YouTube, Netflix, VLC, etc.) should suppress locking — screen must stay on during video. Music-only playback should still allow locking.

## Design

### Architecture

```
MediaPlaybackService (new, NanoShell.Services)
  ↓ WinRT SMTC
GlobalSystemMediaTransportControlsSessionManager
```

- `MediaPlaybackService` wraps the WinRT `GlobalSystemMediaTransportControlsSessionManager` API
- Exposes `IsVideoPlaying` (synchronous bool)
- `LockScreenService` gets an optional `Func<bool>? VideoPlaybackGuard` parameter
- On each poll tick, before firing `LockScreenRequested`, checks `VideoPlaybackGuard`. If true → resets idle timer → lock does not fire

### Component Responsibilities

| Component | File | Responsibility |
|---|---|---|
| `MediaPlaybackService` | `NanoShell.Services\MediaPlaybackService.cs` | Init SMTC manager, enumerate sessions, check PlaybackType.Video + PlaybackStatus.Playing |
| `LockScreenService` | `NanoShell.Services\LockScreenService.cs` | Accept optional `VideoPlaybackGuard` delegate; check before locking |
| `MainWindow.xaml.cs` | `NanoShell.UI\MainWindow.xaml.cs` | Instantiate `MediaPlaybackService`, wire into `LockScreenService` |

### Data Flow

1. `MediaPlaybackService` starts → calls `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`
2. On each `IsVideoPlaying` call → `manager.GetSessions()` → filter:
   - `session.GetPlaybackInfo().PlaybackStatus == Playing`
   - `session.GetPlaybackInfo().PlaybackType == Video`
3. Returns `true` if any session matches, `false` otherwise (including if SMTC unavailable)
4. `LockScreenService.OnPollTick` (1s timer):
   - If `VideoPlaybackGuard?.Invoke() == true` → `_lastInputTick = tick` (resets idle timer)
   - Otherwise, normal idle timeout logic proceeds

### Error Handling

- SMTC may throw `UnauthorizedAccessException`, `COMException`, etc. on locked/restricted systems
- `IsVideoPlaying` catches all exceptions → returns `false` (lock can proceed)
- System never crashes from SMTC failure

### Edge Cases

- Multiple media sessions (e.g., browser + music player) — any `PlaybackType.Video` session playing → no lock
- Music only (`PlaybackType.Music`) → lock allowed
- App reports `PlaybackType.Unknown` → lock allowed (conservative)
- No media sessions → lock allowed
- SMTC unavailable (locked workstation, Terminal Services) → lock allowed

### Testing

No CI/tests in project. Verification will be manual:
1. Play YouTube in browser → verify no lock after 15s
2. Play only music → verify lock after 15s
3. No media → verify lock after 15s
4. Pause video → verify lock after 15s

## Files Changed

| File | Change |
|---|---|
| `NanoShell.Services\NanoShell.Services.csproj` | `TargetFramework` → `net8.0-windows10.0.19041.0` (already done) |
| `NanoShell.UI\NanoShell.UI.csproj` | Same (already done) |
| `NanoShell.Interop\NanoShell.Interop.csproj` | Same (already done) |
| `NanoShell.Services\MediaPlaybackService.cs` | New file |
| `NanoShell.Services\LockScreenService.cs` | Add `VideoPlaybackGuard` parameter |
| `NanoShell.UI\MainWindow.xaml.cs` | Wire `MediaPlaybackService` |
