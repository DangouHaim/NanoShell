using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NanoShell.Interop;

namespace NanoShell.Services;

public class WindowStateService
{
    public void ToggleMaximize()
    {
        IntPtr hWnd = NativeMethods.GetForegroundWindow();

        WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
        placement.length = Marshal.SizeOf(placement);
        NativeMethods.GetWindowPlacement(hWnd, ref placement);

        if (placement.showCmd == Constants.SW_MAXIMIZE)
            NativeMethods.ShowWindow(hWnd, Constants.SW_SHOWNORMAL);
        else
            NativeMethods.ShowWindow(hWnd, Constants.SW_MAXIMIZE);
    }

    public bool IsConsoleWindow(IntPtr hWnd)
    {
        StringBuilder className = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, className, 256);
        string cls = className.ToString();
        if (cls == "ConsoleWindowClass" || cls == "CASCADIA_HOSTING_WINDOW_CLASS")
            return true;

        if (NativeMethods.GetConsoleWindow() == hWnd)
            return true;

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        try
        {
            string processName = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
            string[] consoleProcesses = { "cmd", "powershell", "pwsh", "wt" };
            return Array.IndexOf(consoleProcesses, processName) >= 0;
        }
        catch
        {
            return false;
        }
    }
}
