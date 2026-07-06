using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NanoShell.Interop;

namespace NanoShell.Services;

public class WindowAutoManagerService : IDisposable
{
    private IntPtr _hook;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;
    private readonly WindowStateService _windowStateService;

    private WinEventDelegate _winEventDelegate = null!;

    public WindowAutoManagerService(Dispatcher dispatcher, WindowStateService windowStateService)
    {
        _dispatcher = dispatcher;
        _windowStateService = windowStateService;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero)
            return;
        _winEventDelegate = WinEventProc;
        _hook = NativeMethods.SetWinEventHook(
            Constants.EVENT_SYSTEM_FOREGROUND,
            Constants.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0, 0,
            Constants.WINEVENT_OUTOFCONTEXT
        );

        if (_hook == IntPtr.Zero)
            Debug.WriteLine("WindowAutoManagerService: SetWinEventHook failed");
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != Constants.OBJID_WINDOW || idChild != Constants.CHILDID_SELF)
            return;

        if (hWnd == new WindowInteropHelper(Application.Current.MainWindow).Handle)
            return;

        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            ProcessWindow(hWnd);
        }));
    }

    private void ProcessWindow(IntPtr hWnd)
    {
        int style = NativeMethods.GetWindowLong(hWnd, Constants.GWL_STYLE);
        int exStyle = NativeMethods.GetWindowLong(hWnd, Constants.GWL_EXSTYLE);

        if (((uint)style & Constants.WS_SIZEBOX) == 0)
            return;

        if (((uint)exStyle & Constants.WS_EX_TOOLWINDOW) != 0)
            return;

        if (!NativeMethods.GetWindowRect(hWnd, out RECT rect))
            return;
        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        if (width < 300 || height < 200)
            return;

        WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
        placement.length = Marshal.SizeOf(placement);
        if (!NativeMethods.GetWindowPlacement(hWnd, ref placement))
            return;
        if (placement.showCmd == Constants.SW_MAXIMIZE)
            return;

        if (_windowStateService.IsConsoleWindow(hWnd))
        {
            SnapToTopHalf(hWnd);
            return;
        }

        // Task Manager restores its saved position after being
        // maximized. Delay the maximize to avoid flicker.
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            if (proc.ProcessName.Equals("Taskmgr", StringComparison.OrdinalIgnoreCase))
            {
                var timer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(600),
                    DispatcherPriority.Normal,
                    (s, e) => { ((DispatcherTimer)s).Stop(); MaximizeToWorkArea(hWnd); },
                    _dispatcher);
                timer.Start();
                return;
            }
        }
        catch { }

        MaximizeToWorkArea(hWnd);
    }

    private void MaximizeToWorkArea(IntPtr hWnd)
    {
        NativeMethods.ShowWindow(hWnd, Constants.SW_MAXIMIZE);

        RECT workArea = new RECT();
        if (!NativeMethods.SystemParametersInfo(Constants.SPI_GETWORKAREA, 0, ref workArea, 0))
            return;

        NativeMethods.SetWindowPos(
            hWnd,
            Constants.HWND_TOP,
            workArea.left, workArea.top,
            workArea.right - workArea.left,
            workArea.bottom - workArea.top,
            Constants.SWP_NOACTIVATE | Constants.SWP_FRAMECHANGED | Constants.SWP_NOZORDER
        );
    }

    private void SnapToTopHalf(IntPtr hWnd)
    {
        RECT workArea = new RECT();
        if (!NativeMethods.SystemParametersInfo(Constants.SPI_GETWORKAREA, 0, ref workArea, 0))
            return;

        int width = workArea.right - workArea.left;
        int height = (workArea.bottom - workArea.top) / 2;

        NativeMethods.SetWindowPos(
            hWnd,
            Constants.HWND_TOP,
            workArea.left, workArea.top,
            width, height,
            Constants.SWP_SHOWWINDOW | Constants.SWP_NOACTIVATE
        );
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }
}
