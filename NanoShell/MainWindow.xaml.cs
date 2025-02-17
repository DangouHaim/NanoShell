using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System;
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

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOWNORMAL = 1;   // Восстановить окно в нормальном размере
    private const int SW_MAXIMIZE = 3;     // Развернуть окно на весь экран
    private const byte VK_TAB = 0x09;
    private const byte VK_LMENU = 0xA4;
    private const byte VK_F4 = 0x73;
    private const byte VK_Q = 0x51;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_WINDOWS = 0x5B;

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
        Topmost = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Black;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = 50;
        Left = 0;
        Top = SystemParameters.PrimaryScreenHeight - 50;
        TouchDown += OnTouchDown;
    }


    private void OnTouchDown(object sender, TouchEventArgs e)
    {

    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Left);
    }
    
    private void BtnBack_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.SimulateKeyPress(Key.Escape);
    }
    
    private void BtnBack_Hold(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.F4);
    }
    
    private void BtnCloseAll_Click(object sender, RoutedEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LWin, Key.D);
    }
    
    private void BtnTaskView_Click(object sender, RoutedEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LWin, Key.Tab);
    }
    
    private void BtnTaskView_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Tab);
    }
    
    private void BtnTaskView_Hold(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.ToggleMaximize();
    }
    
    private void BtnMiddle_Hold(object sender, MouseButtonEventArgs e)
    {
        InputSimulator.OpenTouchKeyboard();
    }

    public static class InputSimulator
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        public static void SimulateKeyPress(Key key) { keybd_event((byte)KeyInterop.VirtualKeyFromKey(key), 0, 0, 0); keybd_event((byte)KeyInterop.VirtualKeyFromKey(key), 0, 2, 0); }

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
    }
}