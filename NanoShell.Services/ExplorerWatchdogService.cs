using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NanoShell.Interop;

namespace NanoShell.Services;

public class ExplorerWatchdogService
{
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _cts;

    public ExplorerWatchdogService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void StartWatching()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(5),
            DispatcherPriority.Normal,
            (_, _) => _ = CheckExplorerAsync(),
            _dispatcher);
        _timer.Start();
    }

    public void StopWatching()
    {
        _cts?.Cancel();
        _timer?.Stop();
        _timer = null;
    }

    private async Task CheckExplorerAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                foreach (var proc in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        if (!proc.Responding)
                        {
                            int retries = 3;
                            while (retries-- > 0 && NativeMethods.NtResumeProcess(proc.Handle) == 0) { }
                            Thread.Sleep(500);

                            if (!proc.Responding)
                            {
                                string? exePath = null;
                                try { exePath = proc.MainModule?.FileName; }
                                catch { }

                                try { proc.Kill(); }
                                catch { }

                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    Thread.Sleep(1500);
                                    try { Process.Start(exePath); }
                                    catch { }
                                }
                            }
                        }
                    }
                    catch { }
                }
            });
        }
        catch { }
    }
}
