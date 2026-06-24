using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NanoShell;

public class WindowAutoManager : IDisposable
{
    private IntPtr _hook;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    // WinEvent hook delegate — must be kept alive to avoid GC
    private MainWindow.WinEventDelegate _winEventDelegate;

    public WindowAutoManager(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        _winEventDelegate = WinEventProc;
        _hook = MainWindow.SetWinEventHook(
            MainWindow.EVENT_SYSTEM_FOREGROUND,
            MainWindow.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0, 0,
            MainWindow.WINEVENT_OUTOFCONTEXT
        );

        if (_hook == IntPtr.Zero)
        {
            Debug.WriteLine("WindowAutoManager: SetWinEventHook failed");
        }
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            MainWindow.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != MainWindow.OBJID_WINDOW || idChild != MainWindow.CHILDID_SELF)
            return;

        // Skip our own window
        if (hWnd == new WindowInteropHelper(Application.Current.MainWindow).Handle)
            return;

        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            ProcessWindow(hWnd);
        }));
    }

    private void ProcessWindow(IntPtr hWnd)
    {
        // 1. Check window styles
        int style = MainWindow.GetWindowLong(hWnd, MainWindow.GWL_STYLE);
        int exStyle = MainWindow.GetWindowLong(hWnd, MainWindow.GWL_EXSTYLE);

        // No resize handle → skip
        if (((uint)style & MainWindow.WS_SIZEBOX) == 0)
            return;

        // Tool window → skip
        if (((uint)exStyle & MainWindow.WS_EX_TOOLWINDOW) != 0)
            return;

        // 2. Check window size
        if (MainWindow.GetWindowRect(hWnd, out MainWindow.RECT rect))
        {
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
            if (width < 300 || height < 200)
                return;
        }

        // 3. Check if already maximized
        MainWindow.WINDOWPLACEMENT placement = new MainWindow.WINDOWPLACEMENT();
        placement.length = Marshal.SizeOf(placement);
        MainWindow.GetWindowPlacement(hWnd, ref placement);
        if (placement.showCmd == MainWindow.SW_MAXIMIZE)
            return;

        // 4. Console window → snap to top half
        if (MainWindow.InputSimulator.IsConsoleWindow(hWnd))
        {
            SnapToTopHalf(hWnd);
            return;
        }

        // 5. Default: maximize
        MainWindow.ShowWindow(hWnd, MainWindow.SW_MAXIMIZE);
    }

    private void SnapToTopHalf(IntPtr hWnd)
    {
        MainWindow.RECT workArea = new MainWindow.RECT();
        MainWindow.SystemParametersInfo(MainWindow.SPI_GETWORKAREA, 0, ref workArea, 0);

        int width = workArea.right - workArea.left;
        int height = (workArea.bottom - workArea.top) / 2;

        MainWindow.SetWindowPos(
            hWnd,
            MainWindow.HWND_TOP,
            workArea.left, workArea.top,
            width, height,
            MainWindow.SWP_SHOWWINDOW
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
