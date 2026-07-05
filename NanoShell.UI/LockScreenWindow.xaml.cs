using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NanoShell.Interop;
using NanoShell.Services;

namespace NanoShell.UI;

public enum LockScreenMode { Idle, AOD, Active }

public partial class LockScreenWindow : Window
{
    private readonly LockScreenService _service;
    private readonly ProcessLockService _processLock;
    private readonly string _wallpaperPath;
    private readonly string _watchDir;
    private LockScreenMode _mode;
    private Point _touchStart;
    private double _swipeDelta;
    private DispatcherTimer? _tapTimer;
    private DispatcherTimer? _activeTimer;
    private bool _waitingForDoubleTap;
    private FileSystemWatcher? _wallpaperWatcher;

    public LockScreenWindow(LockScreenService service, ProcessLockService? processLock, string wallpaperPath)
    {
        _service = service;
        _processLock = processLock ?? new ProcessLockService();
        _wallpaperPath = wallpaperPath;
        _watchDir = GetWatchDirectory(wallpaperPath);
        _mode = LockScreenMode.AOD;
        InitializeComponent();
        Opacity = 0;
        ShowAOD();
        TouchDown += OnTouchDown;
        TouchMove += OnTouchMove;
        TouchUp += OnTouchUp;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500));
        BeginAnimation(OpacityProperty, fadeIn);
        StartWallpaperWatcher();
    }

    private static string GetWatchDirectory(string wallpaperPath)
    {
        if (string.IsNullOrEmpty(wallpaperPath))
            return string.Empty;
        string? dir = Path.GetDirectoryName(wallpaperPath);
        return dir ?? string.Empty;
    }

    private void StartWallpaperWatcher()
    {
        if (string.IsNullOrEmpty(_watchDir) || !Directory.Exists(_watchDir))
            return;

        _wallpaperWatcher = new FileSystemWatcher
        {
            Path = _watchDir,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false
        };
        _wallpaperWatcher.Changed += OnWallpaperChanged;
        _wallpaperWatcher.Created += OnWallpaperChanged;
        _wallpaperWatcher.EnableRaisingEvents = true;
    }

    private void OnWallpaperChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_mode == LockScreenMode.Active)
                LoadWallpaper();
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, Constants.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, Constants.GWL_EXSTYLE,
            exStyle | Constants.WS_EX_NOACTIVATE | Constants.WS_EX_TOOLWINDOW);
    }

    public void SwitchToActive()
    {
        _mode = LockScreenMode.Active;
        Dispatcher.Invoke(ShowActive);
    }

    public void SwitchToAOD()
    {
        _mode = LockScreenMode.AOD;
        Dispatcher.Invoke(ShowAOD);
    }

    private void ShowAOD()
    {
        WallpaperImage.Source = null;
        WallpaperImage.Visibility = Visibility.Collapsed;
        GradientOverlay.Visibility = Visibility.Collapsed;
        TimeText.Visibility = Visibility.Collapsed;
        DateText.Visibility = Visibility.Collapsed;
        AODTimeText.Visibility = Visibility.Collapsed;
        GearButton.Visibility = Visibility.Collapsed;
        PanelOverlay.Visibility = Visibility.Collapsed;
        SolidBg.Visibility = Visibility.Visible;
        _swipeDelta = 0;
        RootGrid.RenderTransform = null;
    }

    private void ShowActive()
    {
        _activeTimer?.Stop();
        AODTimeText.Visibility = Visibility.Collapsed;
        SolidBg.Visibility = Visibility.Collapsed;
        WallpaperImage.Visibility = Visibility.Visible;
        LoadWallpaper();
        GradientOverlay.Visibility = Visibility.Visible;
        TimeText.Visibility = Visibility.Visible;
        DateText.Visibility = Visibility.Visible;
        GearButton.Visibility = Visibility.Visible;
        UpdateTime();
        _swipeDelta = 0;
        RootGrid.RenderTransform = null;

        _activeTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(7),
            DispatcherPriority.Normal,
            (s, e2) => SwitchToAOD(),
            Dispatcher.CurrentDispatcher);
        _activeTimer.Start();
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("dddd, d MMMM");
        AODTimeText.Text = now.ToString("HH:mm");
    }

    private void LoadWallpaper()
    {
        string path = LockScreenService.DiscoverWallpaper();
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;

            if (Path.HasExtension(path))
            {
                bitmap.UriSource = new Uri(path);
            }
            else
            {
                bitmap.StreamSource = new FileStream(path, FileMode.Open, FileAccess.Read);
            }

            bitmap.EndInit();
            WallpaperImage.Source = bitmap;
        }
        catch
        {
        }
    }

    private void OnTouchDown(object? sender, TouchEventArgs e)
    {
        if (PanelOverlay.Visibility == Visibility.Visible) return;

        _touchStart = e.GetTouchPoint(this).Position;
        _swipeDelta = 0;

        if (_mode == LockScreenMode.AOD)
        {
            if (_waitingForDoubleTap)
            {
                _waitingForDoubleTap = false;
                _tapTimer?.Stop();
                SwitchToActive();
                return;
            }

            _waitingForDoubleTap = true;
            _tapTimer?.Stop();
            _tapTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(300),
                DispatcherPriority.Normal,
                (s, args) =>
                {
                    _tapTimer?.Stop();
                    _waitingForDoubleTap = false;
                    ShowAODTime();
                },
                Dispatcher.CurrentDispatcher);
            _tapTimer.Start();
        }
    }

    private void ShowAODTime()
    {
        if (_mode != LockScreenMode.AOD) return;
        UpdateTime();
        AODTimeText.Visibility = Visibility.Visible;

        _tapTimer?.Stop();
        _tapTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(3),
            DispatcherPriority.Normal,
            (s, e) =>
            {
                AODTimeText.Visibility = Visibility.Collapsed;
                _tapTimer?.Stop();
            },
            Dispatcher.CurrentDispatcher);
        _tapTimer.Start();
    }

    private void OnTouchMove(object? sender, TouchEventArgs e)
    {
        if (_mode != LockScreenMode.Active || PanelOverlay.Visibility == Visibility.Visible) return;

        Point pos = e.GetTouchPoint(this).Position;
        _swipeDelta = pos.Y - _touchStart.Y;

        if (_swipeDelta < 0)
        {
            RootGrid.RenderTransform = new TranslateTransform(0, _swipeDelta);
        }
    }

    private void OnTouchUp(object? sender, TouchEventArgs e)
    {
        if (PanelOverlay.Visibility == Visibility.Visible) return;

        if (_mode == LockScreenMode.Active && _swipeDelta < -150)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, a) =>
            {
                _service.Dismissed();
                Close();
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
        else if (_mode == LockScreenMode.Active && _swipeDelta < 0)
        {
            var transform = new TranslateTransform(0, _swipeDelta);
            RootGrid.RenderTransform = transform;
            var anim = new DoubleAnimation(_swipeDelta, 0, TimeSpan.FromMilliseconds(200));
            anim.Completed += (s, a) => RootGrid.RenderTransform = null;
            transform.BeginAnimation(TranslateTransform.YProperty, anim);
        }
    }

    private async void GearButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _activeTimer?.Stop();
            PanelOverlay.Visibility = Visibility.Visible;

            if (_processPanel == null)
            {
                _processPanel = new ProcessLockPanel(_processLock);
                _processPanel.CloseRequested += OnPanelCloseRequested;
                PanelHost.Content = _processPanel;
                await _processPanel.ShowPanelAsync();
            }
            else
            {
                await _processPanel.RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NanoShell", "process_lock.log");
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] GearButton_Click error: {ex}\n"); }
            catch { }
        }
    }

    private void OnPanelCloseRequested()
    {
        PanelOverlay.Visibility = Visibility.Collapsed;
        if (_mode == LockScreenMode.Active)
        {
            _activeTimer?.Stop();
            _activeTimer = new DispatcherTimer(
                TimeSpan.FromSeconds(7),
                DispatcherPriority.Normal,
                (s, e2) => SwitchToAOD(),
                Dispatcher.CurrentDispatcher);
            _activeTimer.Start();
        }
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        OnPanelCloseRequested();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tapTimer?.Stop();
        _activeTimer?.Stop();
        _wallpaperWatcher?.Dispose();
        base.OnClosed(e);
    }

    private ProcessLockPanel? _processPanel;
}
