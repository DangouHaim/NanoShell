using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NanoShell.Services;

public class ExplorerWatchdogService
{
    private readonly SuspendManager _suspendManager;
    private CancellationTokenSource? _cts;
    private Task? _watchTask;

    public ExplorerWatchdogService(SuspendManager suspendManager)
    {
        _suspendManager = suspendManager;
    }

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _watchTask = Task.Run(() => WatchLoopAsync(token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _watchTask = null;
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);

                foreach (var proc in Process.GetProcessesByName("explorer"))
                {
                    ct.ThrowIfCancellationRequested();
                    uint pid = (uint)proc.Id;

                    try
                    {
                        bool isSuspended = _suspendManager.IsSuspended(pid);

                        if (isSuspended)
                        {
                            var suspendAge = DateTime.UtcNow - _suspendManager.LastSuspendAllTime;
                            if (suspendAge.TotalSeconds > 30)
                            {
                                await _suspendManager.ResumeProcessAsync(pid, ct);
                                await Task.Delay(500, ct);
                            }
                        }

                        if (!proc.Responding)
                        {
                            string? exePath = null;
                            try { exePath = proc.MainModule?.FileName; }
                            catch { }

                            try { proc.Kill(); }
                            catch { }

                            if (!string.IsNullOrEmpty(exePath))
                            {
                                await Task.Delay(1500, ct);
                                try { Process.Start(exePath); }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }
}
