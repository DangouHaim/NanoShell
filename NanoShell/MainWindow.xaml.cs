using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Interop;

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

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Left);
        e.Handled = true;
    }
    
    private void BtnBack_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.SimulateKeyPress(Key.Escape);
        e.Handled = true;
    }
    
    private void BtnBack_Hold(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.F4);
        e.Handled = true;
    }
    
    private void BtnCloseAll_Click(object sender, RoutedEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LWin, Key.D);
        e.Handled = true;
    }

    private void BtnCloseAll_Hold(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.OpenTouchKeyboard();
        e.Handled = true;
    }

    private void BtnTaskView_Click(object sender, RoutedEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LWin, Key.Tab);
        e.Handled = true;
    }
    
    private void BtnTaskView_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Tab);
        e.Handled = true;
    }
    
    private void BtnTaskView_Hold(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.RegisterAppBar(Height);
        InputSimulator.ToggleMaximize();
        e.Handled = true;
    }

    private void Pannel_Hold(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.SimulateKeyPress(Key.PrintScreen);
        e.Handled = true;
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