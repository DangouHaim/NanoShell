using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NanoShell;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern int SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, ref RECT pvParam, int fWinIni);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    // WinEvent hooks
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // Window queries
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    // Process query
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public Point ptMaxPosition;
        public Point ptRestore;
        public Rectangle rcNormalPosition;
    }

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOWNORMAL = 1;   // Восстановить окно в нормальном размере
    private const int SW_MAXIMIZE = 3;     // Развернуть окно на весь экран
    private const int SPI_SETWORKAREA = 47;
    private const int SPI_GETWORKAREA = 48;
    private const int SM_CXSCREEN = 0; // Ширина экрана в пикселях
    private const int SM_CYSCREEN = 1; // Высота экрана в пикселях
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;
    private const int GWL_STYLE = -16;
    private const uint WS_SIZEBOX = 0x00040000;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);  
    }

    public MainWindow()
    {
        InitializeComponent();
        Top = SystemParameters.PrimaryScreenHeight - 50;
        Width = SystemParameters.PrimaryScreenWidth;
        InputSimulator.RegisterAppBar(Height);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InputSimulator.RegisterAppBar(Height);
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        InputSimulator.RegisterAppBar();
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
                    () => InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Left),
                    () => InputSimulator.SimulateKeyPress(Key.Escape));
                break;
            case SystemGesture.RightTap:
                CancelPendingTap(ref _lastBackTapTime, ref _backTapTimer);
                InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.F4);
                break;
        }
        e.Handled = true;
    }

    private void BtnCloseAll_Gesture(object sender, StylusSystemGestureEventArgs e)
    {
        switch (e.SystemGesture)
        {
            case SystemGesture.Tap:
                InputSimulator.SimulateKeyCombination(Key.LWin, Key.D);
                break;
            case SystemGesture.RightTap:
                InputSimulator.OpenTouchKeyboard();
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
                    () => InputSimulator.SimulateKeyCombination(Key.LWin, Key.Tab),
                    () => InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Tab));
                break;
            case SystemGesture.RightTap:
                CancelPendingTap(ref _lastTaskViewTapTime, ref _taskViewTapTimer);
                InputSimulator.RegisterAppBar(Height);
                InputSimulator.ToggleMaximize();
                break;
        }
        e.Handled = true;
    }

    private void Panel_Gesture(object sender, StylusSystemGestureEventArgs e)
    {
        if (e.SystemGesture == SystemGesture.RightTap)
        {
            InputSimulator.SimulateKeyPress(Key.PrintScreen);
            e.Handled = true;
        }
    }

    public static class InputSimulator
    {
        private static APPBARDATA _appBarData;
        private static RECT _appBarArea;

        public static void SimulateKeyPress(Key key)
        {
            keybd_event((byte)KeyInterop.VirtualKeyFromKey(key), 0, 0, 0);
            keybd_event((byte)KeyInterop.VirtualKeyFromKey(key), 0, 2, 0);
        }

        public static void SimulateKeyCombination(Key key1, Key key2)
        {
            keybd_event((byte)KeyInterop.VirtualKeyFromKey(key1), 0, 0, 0);
            keybd_event((byte)KeyInterop.VirtualKeyFromKey(key2), 0, 0, 0);
            keybd_event((byte)KeyInterop.VirtualKeyFromKey(key2), 0, 2, 0);
            keybd_event((byte)KeyInterop.VirtualKeyFromKey(key1), 0, 2, 0);
        }

        public static void OpenTouchKeyboard()
        {
            try
            {
                // Запускаем сенсорную клавиатуру через команду
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "tabtip.exe",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch { }
        }

        public static void ToggleMaximize()
        {
            IntPtr hWnd = GetForegroundWindow();

            // Получаем текущее состояние окна
            WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(placement);
            GetWindowPlacement(hWnd, ref placement);

            // Проверяем, если окно развернуто
            if (placement.showCmd == SW_MAXIMIZE)
            {
                // Если окно развернуто, восстанавливаем его
                ShowWindow(hWnd, SW_SHOWNORMAL);
            }
            else
            {
                // Если окно не развернуто, разворачиваем его
                ShowWindow(hWnd, SW_MAXIMIZE);
            }
        }

        public static void RegisterAppBar(double height = 0)
        {
            // Получаем текущую рабочую область (до изменений)
            SystemParametersInfo(SPI_GETWORKAREA, 0, ref _appBarArea, 0);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);


            // Новый размер рабочего пространства
            RECT newWorkArea = new RECT
            {
                left = _appBarArea.left,
                top = _appBarArea.top,
                right = _appBarArea.right,
                bottom = screenHeight - ((int)Math.Floor(height) + 15) // Учитываем панель
            };

            // Устанавливаем новую область
            SystemParametersInfo(SPI_SETWORKAREA, 0, ref newWorkArea, 1);
        }
    }
}