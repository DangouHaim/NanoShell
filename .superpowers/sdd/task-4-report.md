# Task 4: Integrate WindowAutoManager into MainWindow

## Changes
- **Modified:** `NanoShell/MainWindow.xaml.cs`

### What was done
1. Added private field `_windowAutoManager` at class level
2. In `OnSourceInitialized`: instantiated `WindowAutoManager(Dispatcher)` and called `.Start()`
3. In `Window_Closed`: added `_windowAutoManager?.Dispose()` before `RegisterAppBar()`

## Build result
- **Build:** Succeeded (0 errors, pre-existing warnings only)
- **Commit:** `68db4ef` — `feat: integrate WindowAutoManager into MainWindow lifecycle`
