# Task 4: Update MainWindow.xaml.cs

**Files:**
- Modify: `NanoShell.UI/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `SuspendManager`, `SuspendableProcessService`, `ExplorerWatchdogService`, `LockScreenWindow`

## Context

This is the wiring task. `MainWindow.cs` needs to:
- Replace `ProcessLockService` with `SuspendManager` + `SuspendableProcessService`
- Replace `ExplorerWatchdogService.StartWatching()` → `Start()`
- Update `OnLockScreenRequested` to use `SuspendManager.SuspendAllAsync()`
- Update `OnLockScreenDismissed` to use `SuspendManager.ResumeAllAsync()`
- Update `LockScreenWindow` constructor call (now takes 4 params: `LockScreenService`, `SuspendableProcessService`, `SuspendManager`, `string`)
- Add shutdown safety: `CancelAll()` then `ResumeAllAsync(CancellationToken.None)` in `Window_Closed`

## Steps

### Step 1: Read current MainWindow.xaml.cs, then rewrite

Read `NanoShell.UI/MainWindow.xaml.cs`. Replace with:

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

    // ---- Back button ----

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

### Step 2: Build

```powershell
dotnet build NanoShell.UI\NanoShell.UI.csproj 2>&1
```

Expected: Build fails — LockScreenWindow still uses old `ProcessLockService` type. That's expected — Task 6 will fix it. Check that the errors are ONLY from LockScreenWindow, not from MainWindow.

### Step 3: Commit even if build fails (known dependency on Task 6)

```bash
git add -A
git commit -m "refactor: wire SuspendManager and SuspendableProcessService in MainWindow"
```
