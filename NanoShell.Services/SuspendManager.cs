using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NanoShell.Interop;

namespace NanoShell.Services;

public class SuspendManager
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NanoShell", "suspend_manager.log");

    private static readonly HashSet<string> _systemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "textinputhost",
        "tabtip",
    };

    private static void Log(string msg)
    {
        try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<uint, int> _suspendCount = new();
    private readonly CancellationTokenSource _shutdownCts = new();

    public bool KeyboardInputActive { get; set; }
    public int ProcessSuspendCount(uint pid) => _suspendCount.GetValueOrDefault(pid, 0);
    public bool IsSuspended(uint pid) => _suspendCount.TryGetValue(pid, out var c) && c > 0;

    private SemaphoreSlim GetLock(uint pid)
        => _locks.GetOrAdd(pid, _ => new SemaphoreSlim(1, 1));

    private static List<(uint Pid, string Name, string ExePath, int ParentPid)> SnapshotProcesses()
    {
        var list = new List<(uint, string, string, int)>();
        IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == (IntPtr)(-1))
            return list;
        try
        {
            var entry = new PROCESSENTRY32();
            entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32>();
            if (!NativeMethods.Process32First(snap, ref entry))
                return list;
            do
            {
                string name = Path.GetFileNameWithoutExtension(entry.szExeFile);
                list.Add((entry.th32ProcessID, name, entry.szExeFile, (int)entry.th32ParentProcessID));
            } while (NativeMethods.Process32Next(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }
        return list;
    }

    public void CancelAll()
    {
        try { _shutdownCts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public async Task SuspendProcessAsync(uint pid, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var combinedCt = linkedCts.Token;
        var lockObj = GetLock(pid);
        await lockObj.WaitAsync(combinedCt);
        try
        {
            if (_suspendCount.TryGetValue(pid, out var count) && count > 0)
                return;

            using var proc = Process.GetProcessById((int)pid);
            NativeMethods.NtSuspendProcess(proc.Handle);
            _suspendCount[pid] = 1;
            Log($"Suspended pid={pid} name={proc.ProcessName}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"SuspendProcessAsync pid={pid}: {ex.Message}");
        }
        finally
        {
            lockObj.Release();
        }
    }

    public async Task ResumeProcessAsync(uint pid, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var combinedCt = linkedCts.Token;
        var lockObj = GetLock(pid);
        await lockObj.WaitAsync(combinedCt);
        try
        {
            if (!_suspendCount.TryGetValue(pid, out var count) || count <= 0)
                return;

            using var proc = Process.GetProcessById((int)pid);
            NativeMethods.NtResumeProcess(proc.Handle);
            _suspendCount.TryRemove(pid, out _);
            if (_locks.TryRemove(pid, out var removedLock))
                removedLock.Dispose();
            Log($"Resumed pid={pid} name={proc.ProcessName}");

            await DrainProcessAsync(pid, combinedCt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"ResumeProcessAsync pid={pid}: {ex.Message}");
        }
        finally
        {
            lockObj.Release();
        }
    }

    public async Task DrainProcessAsync(uint pid, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var combinedCt = linkedCts.Token;
        const int maxDrain = 10;
        int drained = 0;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            IntPtr handle = proc.Handle;
            while (drained < maxDrain && NativeMethods.NtResumeProcess(handle) == 0)
            {
                combinedCt.ThrowIfCancellationRequested();
                drained++;
            }
            if (drained > 0)
                Log($"Drained {drained} extra resume(s) for pid={pid}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log($"DrainProcessAsync pid={pid}: {ex.Message}");
        }
    }

    public async Task SuspendAllAsync(HashSet<string> suspendableTargets, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var combinedCt = linkedCts.Token;
        Log("SuspendAllAsync started");
        int selfPid = Environment.ProcessId;

        foreach (var (pid, name, _, _) in SnapshotProcesses())
        {
            combinedCt.ThrowIfCancellationRequested();
            if (pid == selfPid) continue;
            string nameLower = name.ToLowerInvariant();
            if (_systemProcesses.Contains(nameLower)) continue;
            if (!suspendableTargets.Contains(nameLower)) continue;
            if (nameLower.Equals("explorer", StringComparison.OrdinalIgnoreCase) && KeyboardInputActive)
                continue;
            await SuspendProcessAsync(pid, combinedCt);
        }

        // Two-pass to catch late spawns
        foreach (var (pid, name, _, _) in SnapshotProcesses())
        {
            combinedCt.ThrowIfCancellationRequested();
            if (pid == selfPid) continue;
            string nameLower = name.ToLowerInvariant();
            if (_systemProcesses.Contains(nameLower)) continue;
            if (!suspendableTargets.Contains(nameLower)) continue;
            if (nameLower.Equals("explorer", StringComparison.OrdinalIgnoreCase) && KeyboardInputActive)
                continue;
            await SuspendProcessAsync(pid, combinedCt);
        }

        Log("SuspendAllAsync completed");
    }

    public async Task ResumeAllAsync(CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var combinedCt = linkedCts.Token;
        Log("ResumeAllAsync started");

        var snapshot = _suspendCount.Keys.ToList();
        var explorerPids = snapshot.Where(pid =>
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                return p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }).ToList();

        foreach (var pid in explorerPids)
            await ResumeProcessAsync(pid, combinedCt);

        if (explorerPids.Count > 0)
            await Task.Delay(250, combinedCt);

        var remaining = _suspendCount.Keys.ToList();
        await Task.WhenAll(remaining.Select(pid => ResumeProcessAsync(pid, combinedCt)));

        Log("ResumeAllAsync completed");
    }

    public async Task ResumeByNameAsync(string name, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var combinedCt = linkedCts.Token;
        foreach (var proc in Process.GetProcessesByName(name))
        {
            using var _ = proc;
            uint pid = (uint)proc.Id;
            if (IsSuspended(pid))
                await ResumeProcessAsync(pid, combinedCt);
        }
    }

    public async Task SuspendByNameAsync(string name, HashSet<int> pids, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var combinedCt = linkedCts.Token;
        foreach (var proc in Process.GetProcessesByName(name))
        {
            using var _ = proc;
            uint pid = (uint)proc.Id;
            if (pids.Contains((int)pid) && !IsSuspended(pid))
                await SuspendProcessAsync(pid, combinedCt);
        }
    }
}
