using System;
using System.Runtime.InteropServices;

namespace NanoShell.Interop;

public static partial class NativeMethods
{
    [DllImport("shell32.dll")]
    public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern uint ExtractIconEx(string szFileName, int nIconIndex,
        out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);
}
