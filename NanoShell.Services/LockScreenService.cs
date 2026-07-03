using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using NanoShell.Interop;

namespace NanoShell.Services;

public class LockScreenService
{
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _pollTimer;
    private uint _lastInputTick;
    private POINT _lastCursorPos;
    private bool _locked;

    public event Action<string>? LockScreenRequested;

    public LockScreenService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        if (_pollTimer != null) return;
        _pollTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Normal,
            OnPollTick,
            _dispatcher);
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
        _locked = false;
    }

    public void Dismissed()
    {
        _locked = false;
        _lastInputTick = (uint)Environment.TickCount;
        NativeMethods.GetCursorPos(out _lastCursorPos);
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (_locked) return;

        uint tick = (uint)Environment.TickCount;
        if (_lastInputTick == 0)
        {
            _lastInputTick = tick;
            NativeMethods.GetCursorPos(out _lastCursorPos);
            return;
        }

        if (NativeMethods.GetCursorPos(out POINT pos))
        {
            if (pos.x != _lastCursorPos.x || pos.y != _lastCursorPos.y)
            {
                _lastCursorPos = pos;
                _lastInputTick = tick;
            }
        }

        uint lastOsTick = GetLastInputTick();
        if (lastOsTick > _lastInputTick)
            _lastInputTick = lastOsTick;

        int elapsed = unchecked((int)(tick - _lastInputTick));
        if (elapsed >= 15000)
        {
            _locked = true;
            LockScreenRequested?.Invoke(GetWallpaperPath());
        }
    }

    private static uint GetLastInputTick()
    {
        var info = new LASTINPUTINFO();
        info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>();
        NativeMethods.GetLastInputInfo(ref info);
        return info.dwTime;
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
