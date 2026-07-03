# Lock Screen + Always-On Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add lock screen with AOD (always-on display) to NanoShell.

**Architecture:** Separate `LockScreenWindow` (fullscreen overlay, WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW) managed by `LockScreenService`. AOD after 15s idle (black screen, tap shows dim time). Double-tap → active lock screen (wallpaper + large time). Swipe-up dismisses with fade animation. Wallpaper from Registry or Windows spotlight assets.

**Tech Stack:** WPF, Win32 P/Invoke (user32), Microsoft.Win32.Registry for wallpaper path.

---
## Files

| Action | Path |
|--------|------|
| Modify | `NanoShell.Interop/Constants.cs` |
| Create | `NanoShell.Services/LockScreenService.cs` |
| Create | `NanoShell.UI/LockScreenWindow.xaml` |
| Create | `NanoShell.UI/LockScreenWindow.xaml.cs` |
| Modify | `NanoShell.UI/MainWindow.xaml.cs` |

---

### Task 1: Add SPI_GETDESKWALLPAPER Constant

**File:** `NanoShell.Interop/Constants.cs`

Add after line 17 (`SPI_GETWORKAREA`):
```csharp
    public const uint SPI_GETDESKWALLPAPER = 0x0073;
```

---

### Task 2: Create LockScreenService

**File:** `NanoShell.Services/LockScreenService.cs`

```csharp
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using NanoShell.UI;

namespace NanoShell.Services;

public enum LockScreenMode { Idle, AOD, Active }

public class LockScreenService
{
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _inactivityTimer;
    private LockScreenWindow? _lockWindow;
    public LockScreenMode Mode { get; private set; } = LockScreenMode.Idle;

    public LockScreenService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        if (_inactivityTimer != null) return;
        _inactivityTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(15),
            DispatcherPriority.Normal,
            OnInactivityTimer,
            _dispatcher);
        _inactivityTimer.Start();
    }

    public void Stop()
    {
        _inactivityTimer?.Stop();
        _inactivityTimer = null;
        Dismiss();
    }

    public void OnUserActivity()
    {
        if (Mode == LockScreenMode.Idle)
        {
            _inactivityTimer?.Stop();
            _inactivityTimer?.Start();
        }
    }

    private void OnInactivityTimer(object? sender, EventArgs e)
    {
        _inactivityTimer?.Stop();
        Mode = LockScreenMode.AOD;
        string wallpaper = GetWallpaperPath();
        _lockWindow = new LockScreenWindow(this, wallpaper);
        _lockWindow.Show();
    }

    public void Dismiss()
    {
        if (_lockWindow != null)
        {
            _lockWindow.Close();
            _lockWindow = null;
        }
        Mode = LockScreenMode.Idle;
        _inactivityTimer?.Start();
    }

    public void SwitchToActive()
    {
        Mode = LockScreenMode.Active;
        _lockWindow?.SwitchToActive();
    }

    public void SwitchToAOD()
    {
        Mode = LockScreenMode.AOD;
        _lockWindow?.SwitchToAOD();
    }

    private static string GetWallpaperPath()
    {
        string? path = Registry.CurrentUser
            .OpenSubKey(@"Control Panel\Desktop")
            ?.GetValue("Wallpaper") as string;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return path;

        string spotlightDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState\Assets");
        if (Directory.Exists(spotlightDir))
        {
            var newest = new DirectoryInfo(spotlightDir)
                .GetFiles()
                .Where(f => f.Length > 100 * 1024)
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();
            if (newest != null)
                return newest.FullName;
        }

        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"Web\Screen\img100.jpg");
        if (File.Exists(fallback))
            return fallback;

        return string.Empty;
    }
}
```

---

### Task 3: Create LockScreenWindow XAML

**File:** `NanoShell.UI/LockScreenWindow.xaml`

```xml
<Window x:Class="NanoShell.UI.LockScreenWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None"
        ShowInTaskbar="False"
        Topmost="True"
        WindowStartupLocation="Manual"
        Background="Black"
        Width="{x:Static SystemParameters.PrimaryScreenWidth}"
        Height="{x:Static SystemParameters.PrimaryScreenHeight}"
        Left="0" Top="0"
        Focusable="False"
        ResizeMode="NoResize"
        AllowsTransparency="False">
    <Grid x:Name="RootGrid">
        <Image x:Name="WallpaperImage" Stretch="UniformToFill" Visibility="Collapsed"/>
        <Border x:Name="GradientOverlay" Background="#80000000" Visibility="Collapsed"/>
        <TextBlock x:Name="TimeText"
                   Foreground="White"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   FontSize="72"
                   FontWeight="Light"
                   Visibility="Collapsed"/>
        <TextBlock x:Name="DateText"
                   Foreground="#CCC"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   FontSize="24"
                   Margin="0,80,0,0"
                   FontWeight="Light"
                   Visibility="Collapsed"/>
        <TextBlock x:Name="AODTimeText"
                   Foreground="#333"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   FontSize="36"
                   FontWeight="Light"
                   Visibility="Collapsed"/>
    </Grid>
</Window>
```

---

### Task 4: Create LockScreenWindow Code-Behind

**File:** `NanoShell.UI/LockScreenWindow.xaml.cs`

```csharp
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

public partial class LockScreenWindow : Window
{
    private readonly LockScreenService _service;
    private readonly string _wallpaperPath;
    private LockScreenMode _mode;
    private Point _touchStart;
    private double _swipeDelta;
    private DispatcherTimer? _aodTimer;
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

        var desktop = WindowsVersionInfo.GetWorkArea();
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
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
            (s, e) => _service.SwitchToAOD(),
            Dispatcher.CurrentDispatcher);
        _activeTimer.Start();
    }

    private void UpdateTime()
    {
        TimeText.Text = DateTime.Now.ToString("HH:mm");
        DateText.Text = DateTime.Now.ToString("dddd, d MMMM");
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

    private void OnTouchDown(object sender, TouchEventArgs e)
    {
        _touchStart = e.GetTouchPoint(this).Position;
        _swipeDelta = 0;

        if (_mode == LockScreenMode.AOD)
        {
            if (_waitingForDoubleTap)
            {
                _waitingForDoubleTap = false;
                _aodTimer?.Stop();
                _service.SwitchToActive();
                return;
            }

            _waitingForDoubleTap = true;
            _aodTimer?.Stop();
            _aodTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(300),
                DispatcherPriority.Normal,
                (s, args) =>
                {
                    _aodTimer?.Stop();
                    _waitingForDoubleTap = false;
                    ShowAODTime();
                },
                Dispatcher.CurrentDispatcher);
            _aodTimer.Start();
        }
    }

    private void ShowAODTime()
    {
        if (_mode != LockScreenMode.AOD) return;
        UpdateTime();
        AODTimeText.Text = TimeText.Text;
        AODTimeText.Visibility = Visibility.Visible;

        _aodTimer?.Stop();
        _aodTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(3),
            DispatcherPriority.Normal,
            (s, e) =>
            {
                AODTimeText.Visibility = Visibility.Collapsed;
                _aodTimer?.Stop();
            },
            Dispatcher.CurrentDispatcher);
        _aodTimer.Start();
    }

    private void OnTouchMove(object sender, TouchEventArgs e)
    {
        if (_mode != LockScreenMode.Active) return;

        Point pos = e.GetTouchPoint(this).Position;
        _swipeDelta = pos.Y - _touchStart.Y;

        if (_swipeDelta < 0)
        {
            RootGrid.RenderTransform = new TranslateTransform(0, _swipeDelta);
        }
    }

    private void OnTouchUp(object sender, TouchEventArgs e)
    {
        if (_mode == LockScreenMode.Active && _swipeDelta < -150)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, a) => _service.Dismiss();
            BeginAnimation(OpacityProperty, fadeOut);
        }
        else if (_mode == LockScreenMode.Active && _swipeDelta < 0)
        {
            var springBack = new DoubleAnimation(_swipeDelta, 0, TimeSpan.FromMilliseconds(200));
            springBack.Completed += (s, a) => RootGrid.RenderTransform = null;
            var transform = new TranslateTransform(0, _swipeDelta);
            RootGrid.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty, springBack);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _aodTimer?.Stop();
        _activeTimer?.Stop();
        base.OnClosed(e);
    }
}
```

**Note:** `WindowsVersionInfo` is not a real class. Replace with `SystemParameters.PrimaryScreenWidth / Height` which we already use. The GetWorkArea call is not needed. Let me remove that reference. The corrected version:

Remove the `WindowsVersionInfo` reference — just use `SystemParameters.PrimaryScreenWidth/Height` set in XAML and constructor. The corrected constructor:

```csharp
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
```

---

### Task 5: Wire Up LockScreenService in MainWindow

**File:** Modify `NanoShell.UI/MainWindow.xaml.cs`

1. Add field after existing service fields:

```csharp
    private readonly LockScreenService _lockScreenService;
```

2. In constructor, after `_windowAutoManager = new WindowAutoManagerService(Dispatcher, _windowStateService);` add:

```csharp
    _lockScreenService = new LockScreenService(Dispatcher);
```

3. In `OnSourceInitialized`, after `_windowAutoManager.Start();` add:

```csharp
    _lockScreenService.Start();
```

4. Forward touch activity. Add touch event handler — hook into the existing `StylusSystemGesture` events or add a global touch handler:

Add to `Window_Loaded` or at the end of the constructor:

```csharp
    TouchDown += (s, e) => _lockScreenService.OnUserActivity();
```

5. In `Window_Closed`, before the mutex release add:

```csharp
    _lockScreenService?.Stop();
```

---

### Task 6: Build and Verify

- [ ] **Step 1: Build**

Run: `dotnet build NanoShell.UI\NanoShell.UI.csproj -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run**

Kill existing process, start new one.
Expected: app starts, no crash.

- [ ] **Step 3: Wait 15 seconds**

Expected: AOD window appears (fullscreen black overlay).

- [ ] **Step 4: Tap AOD screen**

Expected: time shows briefly, then fades.

- [ ] **Step 5: Double-tap AOD screen**

Expected: switches to active lock screen with wallpaper + time.

- [ ] **Step 6: Swipe up past threshold**

Expected: fade animation → lock screen closes → returns to desktop.

