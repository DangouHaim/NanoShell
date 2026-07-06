using System.Diagnostics;
using NanoShell.Interop;

namespace NanoShell.Services;

public class AppBarService
{
    private APPBARDATA _appBarData;
    private RECT _appBarArea;
    private bool _registered;

    public void RegisterAppBar(double height = 0)
    {
        int screenWidth = NativeMethods.GetSystemMetrics(Constants.SM_CXSCREEN);
        int screenHeight = NativeMethods.GetSystemMetrics(Constants.SM_CYSCREEN);

        if (height == 0)
        {
            RECT fullArea = new RECT
            {
                left = 0,
                top = 0,
                right = screenWidth,
                bottom = screenHeight
            };
            NativeMethods.SystemParametersInfo(Constants.SPI_SETWORKAREA, 0, ref fullArea, 1);
            return;
        }

        NativeMethods.SystemParametersInfo(Constants.SPI_GETWORKAREA, 0, ref _appBarArea, 0);

        RECT newWorkArea = new RECT
        {
            left = _appBarArea.left,
            top = _appBarArea.top,
            right = _appBarArea.right,
            bottom = screenHeight - ((int)Math.Floor(height) + 15)
        };

        NativeMethods.SystemParametersInfo(Constants.SPI_SETWORKAREA, 0, ref newWorkArea, 1);
    }

    public void OpenTouchKeyboard()
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "tabtip.exe",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch { }
    }
}
