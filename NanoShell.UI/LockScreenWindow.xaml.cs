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
    private readonly string _wallpaperPath;
    private LockScreenMode _mode;
    private Point _touchStart;
    private double _swipeDelta;
    private DispatcherTimer? _tapTimer;
    private DispatcherTimer? _activeTimer;
    private bool _waitingForDoubleTap;

    public LockScreenWindow(LockScreenService service, string wallpaperPath)
    {
        _service = service;
        _wallpaperPath = wallpaperPath;
        _mode = LockScreenMode.AOD;
        InitializeComponent();
        ShowAOD();
        TouchDown += OnTouchDown;
        TouchMove += OnTouchMove;
        TouchUp += OnTouchUp;
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
        WallpaperImage.Visibility = Visibility.Collapsed;
        GradientOverlay.Visibility = Visibility.Collapsed;
        TimeText.Visibility = Visibility.Collapsed;
        DateText.Visibility = Visibility.Collapsed;
        AODTimeText.Visibility = Visibility.Collapsed;
        Background = new SolidColorBrush(Colors.Black);
        _swipeDelta = 0;
        RootGrid.RenderTransform = null;
        Opacity = 1;
    }

    private void ShowActive()
    {
        LoadWallpaper();
        WallpaperImage.Visibility = Visibility.Visible;
        GradientOverlay.Visibility = Visibility.Visible;
        TimeText.Visibility = Visibility.Visible;
        DateText.Visibility = Visibility.Visible;
        UpdateTime();
        _swipeDelta = 0;
        RootGrid.RenderTransform = null;
        Opacity = 1;

        _activeTimer?.Stop();
        _activeTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(7),
            DispatcherPriority.Normal,
            (s, e) => SwitchToAOD(),
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
        if (string.IsNullOrEmpty(_wallpaperPath))
            return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;

            if (Path.HasExtension(_wallpaperPath))
            {
                bitmap.UriSource = new Uri(_wallpaperPath);
            }
            else
            {
                bitmap.StreamSource = new FileStream(_wallpaperPath, FileMode.Open, FileAccess.Read);
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
        if (_mode != LockScreenMode.Active) return;

        Point pos = e.GetTouchPoint(this).Position;
        _swipeDelta = pos.Y - _touchStart.Y;

        if (_swipeDelta < 0)
        {
            RootGrid.RenderTransform = new TranslateTransform(0, _swipeDelta);
        }
    }

    private void OnTouchUp(object? sender, TouchEventArgs e)
    {
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

    protected override void OnClosed(EventArgs e)
    {
        _tapTimer?.Stop();
        _activeTimer?.Stop();
        base.OnClosed(e);
    }
}
