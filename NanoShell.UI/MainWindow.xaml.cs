using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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
    private readonly ProcessLockService _processLockService;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = NativeMethods.GetWindowLong(hwnd, Constants.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, Constants.GWL_EXSTYLE, extendedStyle | Constants.WS_EX_NOACTIVATE | Constants.WS_EX_TOOLWINDOW);

        _windowAutoManager.Start();
        _lockScreenService.Start();

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
        _processLockService = new ProcessLockService();
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

    private void Window_Closed(object sender, EventArgs e)
    {
        _processLockService.ThawAll();
        _windowAutoManager?.Dispose();
        _lockScreenService?.Stop();
        _appBarService.RegisterAppBar();
        if (_ownsMutex)
            _instanceMutex.ReleaseMutex();
    }

    private async void OnLockScreenRequested(string wallpaperPath)
    {
        await _processLockService.FreezeAllAsync();
        var win = new LockScreenWindow(_lockScreenService, _processLockService, wallpaperPath);
        win.Show();
    }

    private void OnLockScreenDismissed()
    {
        _processLockService.ThawAll();
    }

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
