namespace NanoShell.Interop;

public static class Constants
{
    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;

    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_SIZEBOX = 0x00040000;

    public const int SW_SHOWNORMAL = 1;
    public const int SW_MAXIMIZE = 3;

    public const int SPI_SETWORKAREA = 47;
    public const int SPI_GETWORKAREA = 48;

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    public const uint WINEVENT_OUTOFCONTEXT = 0;
    public const uint EVENT_SYSTEM_FOREGROUND = 3;

    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;

    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
}
