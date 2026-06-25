using System.Runtime.InteropServices;

namespace NanoShell.Interop;

public static partial class NativeMethods
{
    [DllImport("shell32.dll")]
    public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
