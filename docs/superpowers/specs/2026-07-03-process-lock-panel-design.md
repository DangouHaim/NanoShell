# Process Lock Panel — Design Spec

## Goal
Add a gear button on the active lock screen that opens an animated process list panel. Users can view all running processes, see which are frozen, toggle freeze/thaw per process, and maintain an exceptions list that persists across sessions.

## Architecture

```
LockScreenWindow.xaml/.cs
  ├── Gear ⚙ button (top-right in Active mode)
  └── ProcessLockPanel.xaml/.cs (UserControl)
        ├── Search input
        ├── Filter toggle (All / Exceptions)
        ├── Section "Regular Processes"
        │     └── Items: [freeze icon] Name — Title [ToggleSwitch]
        └── Section "Background Processes"
              └── Items: [freeze icon] Name [ToggleSwitch]

ProcessLockService.cs
  ├── EnumerateAll() → List<ProcessEntry>
  ├── FreezeAll(exceptions)
  ├── ThawAll()
  ├── FreezeProcess(pid) / ThawProcess(pid)
  ├── LoadExceptions() / SaveExceptions()
  └── File: %LOCALAPPDATA%\NanoShell\suspend_exceptions.json
```

## Data Flow

1. Active lock screen → ⚙ button visible (semi-transparent)
2. Click ⚙ → `ProcessLockService.EnumerateAll()` → returns categorized list
3. Panel slides in from right (200ms opacity+translate)
4. Each toggle click → immediately calls `FreezeProcess`/`ThawProcess` + saves exceptions
5. Second ⚙ click or ✕ button → panel slides out
6. On lock (`LockScreenRequested`) → `FreezeAll(exceptions)` skips exceptions
7. On dismiss (`LockScreenDismissed`) → `ThawAll()` resumes all frozen

## ProcessEntry Model
```
class ProcessEntry:
  int Pid
  string Name          # exe name (lowercase)
  string WindowTitle   # main window title, empty if none
  bool IsFrozen        # currently suspended?
  bool IsException     # in exceptions list?
  bool IsBackground    # true if no window + no title
```

## Categorization
- **Regular**: has visible main window OR non-empty title; `explorer.exe` forced to Regular
- **Background**: desktop-less, services, console apps without window

## Exceptions Persistence
- File: `%LOCALAPPDATA%\NanoShell\suspend_exceptions.json`
- Format: `{"exceptions": ["explorer.exe", "devenv.exe"]}`
- Loaded on service creation, saved on every toggle change
- Case-insensitive matching (stored lowercase)

## UI
- Panel: dark background `#CC151515`, rounded corners, ~45% screen width
- Search: TextBox at top, filters by name or window title in real time
- Filter toggle: "All" / "Exceptions" pills
- Sections split by thin separator with header text
- Each row: freeze icon (❄ if frozen / ○ if not) | Process.exe — "Window Title" | ToggleSwitch
- ToggleSwitch: ON = exception (won't be frozen) / OFF = will be frozen
- Animation: `Opacity 0→1` + `TranslateTransform X (panel width)→0`, 200ms ease-out
- Close: ✕ button top-right of panel, or click gear again
- No scrollbars (touch scroll via ScrollViewer)

## Files Changed

| File | Action |
|------|--------|
| `NanoShell.Services/ProcessLockService.cs` | CREATE |
| `NanoShell.UI/ProcessLockPanel.xaml` | CREATE |
| `NanoShell.UI/ProcessLockPanel.xaml.cs` | CREATE |
| `NanoShell.UI/LockScreenWindow.xaml` | MODIFY (add gear + panel host) |
| `NanoShell.UI/LockScreenWindow.xaml.cs` | MODIFY (wire gear/panel) |
| `NanoShell.UI/MainWindow.xaml.cs` | MODIFY (suspend via ProcessLockService) |
| `NanoShell.Interop/NativeMethods.NtDll.cs` | MODIFY (add NtQuerySystemInformation if needed) |

## Key Decisions
- ProcessLockService is a standalone class, passed to LockScreenWindow via constructor (like LockScreenService)
- Toggle ON = exception = NOT frozen on lock; intuitive: "ON means leave me alone"
- Exceptions stored by process name (not PID) because PIDs change on restart
- explorer.exe forced to Regular because it's the shell — users expect to see it there
