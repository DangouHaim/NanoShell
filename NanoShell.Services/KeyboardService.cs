using System.Windows.Input;
using System.Windows.Interop;
using NanoShell.Interop;

namespace NanoShell.Services;

public class KeyboardService
{
    public void SimulateKeyPress(Key key)
    {
        byte vk = (byte)KeyInterop.VirtualKeyFromKey(key);
        NativeMethods.keybd_event(vk, 0, 0, 0);
        NativeMethods.keybd_event(vk, 0, 2, 0);
    }

    public void SimulateKeyCombination(Key key1, Key key2)
    {
        byte vk1 = (byte)KeyInterop.VirtualKeyFromKey(key1);
        byte vk2 = (byte)KeyInterop.VirtualKeyFromKey(key2);
        NativeMethods.keybd_event(vk1, 0, 0, 0);
        NativeMethods.keybd_event(vk2, 0, 0, 0);
        NativeMethods.keybd_event(vk2, 0, 2, 0);
        NativeMethods.keybd_event(vk1, 0, 2, 0);
    }
}
