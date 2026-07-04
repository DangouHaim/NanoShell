using System.Runtime.InteropServices;

namespace NanoShell.Interop;

public static partial class NativeMethods
{
    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtSuspendProcess(IntPtr hProcess);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtResumeProcess(IntPtr hProcess);
}
