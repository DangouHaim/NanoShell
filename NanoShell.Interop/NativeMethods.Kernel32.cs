using System.Runtime.InteropServices;

namespace NanoShell.Interop;

public static partial class NativeMethods
{
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();
}
