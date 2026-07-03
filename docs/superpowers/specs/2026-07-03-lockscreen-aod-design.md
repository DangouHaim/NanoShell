# Lock Screen + Always-On Display — Design Spec

## Goal
Add a lock screen with Always-On-Display (AOD) to NanoShell. After 15 s of inactivity a black fullscreen overlay appears (AOD). Double-tap switches to an active lock screen showing time + current Windows wallpaper. Swipe up to dismiss.

## Files to Create
- `NanoShell.Services/LockScreenService.cs` — state machine (AOD ↔ Active ↔ unlocked), inactivity timer, wallpaper discovery
- `NanoShell.UI/LockScreenWindow.xaml` — WPF window layout (black canvas, time label, wallpaper Image)
- `NanoShell.UI/LockScreenWindow.xaml.cs` — touch handling (tap, double-tap, swipe-to-dismiss), mode switching, animations

## Files to Modify
- `NanoShell.Interop/Constants.cs` — add `SPI_GETDESKWALLPAPER`, `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`
- `NanoShell.UI/MainWindow.xaml.cs` — create `LockScreenService`, wire inactivity timer trigger

## Architecture

```
MainWindow
  │
  ├── LockScreenService  (singleton, created in MainWindow ctor)
  │     ├── InactivityTimer (DispatcherTimer, 15 s)
  │     ├── LockTimer        (DispatcherTimer, 7 s)
  │     └── ShowLockScreen() / Dismiss()
  │
  └── LockScreenWindow  (separate Window, opened by LockScreenService)
        ├── AOD mode     (black bg, optional dim time on tap)
        └── Active mode  (wallpaper Image + large time, swipe dismiss)
```

### LockScreenService
| Field | Type | Purpose |
|-------|------|---------|
| `_inactivityTimer` | `DispatcherTimer` | 15 s countdown before AOD shows |
| `_lockTimer` | `DispatcherTimer` | 7 s countdown before active→AOD |
| `_lockWindow` | `LockScreenWindow` | the overlay window instance |
| `_mode` | `LockScreenMode` enum | `Idle` → `AOD` → `Active` |
| `_screenWidth/Height` | `int` | cached screen dimensions |

**Methods:**
- `Start()` — begin inactivity timer
- `Stop()` — cancel all timers, close window
- `OnInactivity()` — create/show LockScreenWindow in AOD mode, stop inactivity timer
- `Dismiss()` — close window, set mode=Idle, restart inactivity timer
- `OnTouchActivity()` — reset inactivity timer (called from MainWindow on any user interaction)

### LockScreenWindow (WPF Window)
- Fullscreen, borderless, black background
- `WindowStyle="None"`, `ShowInTaskbar="False"`, `Topmost="True"`
- In `OnSourceInitialized`: apply `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` (same as MainWindow)
- Two visual layers stacked:

1. **AOD layer** — black `Border` with a small dim `TextBlock` for time (visible only on tap, fades after ~3 s)
2. **Active layer** — full-size `<Image>` (wallpaper) with a large `TextBlock` (time) overlaid, semi-transparent gradient overlay for readability

**Touch / Gesture handling:**
- **Tap** (AOD mode): toggle time display visible, auto-hide after 3 s
- **Double-tap** (AOD mode): switch to Active mode
- **Active mode idle**: 7 s timer → back to AOD
- **Swipe up** (Active mode): track `TouchMove` Y delta; if delta > threshold (150 px) → BeginAnimation Opacity → 0 over 200 ms → `Dismiss()`; else on `TouchUp` → animate Y back to 0

**Dismiss triggers:**
- Successful swipe-up in Active mode
- Any tap while in Active mode? No — let's keep it simple: only swipe-up dismisses.
- Actually, the user said "любой тач" — so let me allow any touch to dismiss from Active mode if swiped enough. Tap alone could also dismiss from Active mode. Let me re-read the requirements:

> "Активный экран можно двигать пальцем свайпом вверх, если его сдвинуть достаточно то экран плавно исчезает и происходит разблокировка, если сдвинуть его свайпом слишком мало то он плавно вернется в исходное состояние если убрать палец."

So only swipe-up dismisses. Tap on active screen does nothing (or resets the 7s timer). OK.

## Wallpaper Discovery
Priority order to find the current lock screen / desktop wallpaper:

1. Windows Spotlight assets: `%LOCALAPPDATA%\Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState\Assets\*` — pick newest file ≥ 100 KB, add `.jpg` extension.
2. Desktop wallpaper via `SystemParametersInfo(SPI_GETDESKWALLPAPER, ...)` — returns full path.
3. Fallback: `C:\Windows\Web\Screen\img100.jpg`
4. If all fail: dark gradient (solid color).

The wallpaper is loaded as `BitmapImage` and assigned to the Active layer `<Image>`.

## Inactivity Detection  
LockScreenService restarts its 15 s timer on every `StylusSystemGesture` event forwarded from MainWindow. When the timer elapses, `OnInactivity()` fires.

## Data Flow
```
User touch → MainWindow.StylusSystemGesture
  → LockScreenService.OnTouchActivity() → reset 15 s timer
  → (if timer fires) → LockScreenService.OnInactivity()
    → new LockScreenWindow() → Show() in AOD mode
    → user double-taps → Active mode
    → user swipe-up → Dismiss() → Close() window
    → restart 15 s timer
```

## Key Decisions
1. **Separate window, not layered on MainWindow** — cleaner lifecycle, no interference with app-bar registration.
2. **No separate unlock action** — swipe-up dismissal IS the unlock. No password/PIN layer (this is a touchshell replacement, not a security lock screen).
3. **Wallpaper loaded once** when window opens, not polled. If user changes wallpaper while locked, it won't update until next lock cycle. Acceptable for v1.
4. **Touch via StylusSystemGesture + Manipulation events** — consistent with existing codebase.
5. **No DI** — service is `new`-ed in MainWindow constructor, following existing pattern.
