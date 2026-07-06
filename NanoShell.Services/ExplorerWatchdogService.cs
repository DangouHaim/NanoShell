using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using NanoShell.Interop;

namespace NanoShell.Services;

public class ExplorerWatchdogService
{
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _timer;

    public ExplorerWatchdogService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void StartWatching()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Normal,
            (_, _) => CheckExplorer(),
            _dispatcher);
        _timer.Start();
    }

    public void StopWatching()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void CheckExplorer()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("explorer"))
            {
                if (!proc.Responding)
                {
                    // Force-thaw via NtResumeProcess
                    try
                    {
                        ProcessLockService.ForceThaw(proc.Handle);
                    }
                    catch { }

                    // Still not responding after thaw attempt? Restart.
                    if (!proc.Responding)
                    {
                        string? exePath = null;
                        try { exePath = proc.MainModule?.FileName; }
                        catch { }

                        try { proc.Kill(); }
                        catch { }

                        if (!string.IsNullOrEmpty(exePath))
                        {
                            Task.Delay(1000).ContinueWith(_ =>
                            {
                                try { Process.Start(exePath); }
                                catch { }
                            });
                        }
                    }
                }
            }
        }
        catch { }
    }
}
