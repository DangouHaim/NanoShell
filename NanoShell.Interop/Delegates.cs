using System.Runtime.InteropServices;

namespace NanoShell.Interop;

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
