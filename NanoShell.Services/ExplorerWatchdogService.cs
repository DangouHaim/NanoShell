using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;
    private const string CabinetWClass = "CabinetWClass";

    private static void CloseFileExplorerWindows()
    {
        try
        {
            IntPtr hwnd = FindWindow(CabinetWClass, null);
            int maxTries = 20;
            while (hwnd != IntPtr.Zero && maxTries-- > 0)
            {
                SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                hwnd = FindWindow(CabinetWClass, null);
            }
        }
        catch { }
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);

                var explorerProcs = Process.GetProcessesByName("explorer");
                bool anyExplorerRunning = false;

                foreach (var proc in explorerProcs)
                {
                    ct.ThrowIfCancellationRequested();
                    uint pid = (uint)proc.Id;

                    try
                    {
                        anyExplorerRunning = true;

                        if (_suspendManager.IsSuspended(pid))
                        {
                            var suspendTime = _suspendManager.GetSuspendTime(pid);
                            if (suspendTime.HasValue)
                            {
                                var suspendAge = DateTime.UtcNow - suspendTime.Value;
                                if (suspendAge.TotalSeconds > 30)
                                {
                                    await _suspendManager.ResumeProcessAsync(pid, ct);
                                    await Task.Delay(500, ct);
                                }
                            }
                            // Intentionally suspended — skip health check
                            continue;
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
                                try { Process.Start("explorer.exe", "/NOUACCHECK"); }
                                catch { }

                                await Task.Delay(1000, ct);
                                CloseFileExplorerWindows();
                            }
                            break;
                        }
                    }
                    catch { }
                }

                if (!anyExplorerRunning)
                {
                    try { Process.Start("explorer.exe", "/NOUACCHECK"); }
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
