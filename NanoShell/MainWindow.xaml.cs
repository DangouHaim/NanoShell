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

    private const byte VK_TAB = 0x09;
    private const byte VK_LMENU = 0xA4;
    private const byte VK_F4 = 0x73;
    private const byte VK_Q = 0x51;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_WINDOWS = 0x5B;

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


    private void OnTouchDown(object sender, TouchEventArgs e) { /* Логика свайпа вверх */ }
    private void BtnBack_Click(object sender, RoutedEventArgs e) { InputSimulator.SimulateKeyPress(Key.Back); }
    private void BtnBack_DoubleClick(object sender, MouseButtonEventArgs e) { InputSimulator.SimulateKeyPress(Key.Escape); }
    private void BtnBack_Hold(object sender, MouseButtonEventArgs e) { InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.F4); }
    private void BtnCloseAll_Click(object sender, RoutedEventArgs e) { InputSimulator.SimulateWinD(); }
    private void BtnTaskView_Click(object sender, RoutedEventArgs e) { InputSimulator.SimulateKeyCombination(Key.LWin, Key.Tab); }
    private void BtnTaskView_DoubleClick(object sender, MouseButtonEventArgs e) { InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Tab); }
    private void BtnTaskView_Hold(object sender, MouseButtonEventArgs e) { InputSimulator.SimulateKeyCombination(Key.LWin, Key.Up); }
    private void BtnMiddle_Hold(object sender, MouseButtonEventArgs e) { InputSimulator.SimulateKeyCombination(Key.LeftCtrl, Key.Q); }

    private void SimulateKeyPress(Key key) { InputSimulator.SimulateKeyPress(key); }
    private void SimulateAltF4() { InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.F4); }
    private void SimulateWinTab() { InputSimulator.SimulateKeyCombination(Key.LWin, Key.Tab); }
    private void SimulateAltTab() { InputSimulator.SimulateKeyCombination(Key.LeftAlt, Key.Tab); }
    private void SimulateFullScreen() { InputSimulator.SimulateKeyCombination(Key.LWin, Key.Up); }
    private void SimulateCtrlQ() { InputSimulator.SimulateKeyCombination(Key.LeftCtrl, Key.Q); }

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

        public static void SimulateWinD()
        {
            keybd_event(0x5B, 0, 0, 0); // Win
            keybd_event(0x44, 0, 0, 0); // D
            keybd_event(0x44, 0, 2, 0);
            keybd_event(0x5B, 0, 2, 0);
        }
    }
}