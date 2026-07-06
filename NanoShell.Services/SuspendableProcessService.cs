using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NanoShell.Interop;

namespace NanoShell.Services;

public class ProcessEntry
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public int ParentPid { get; set; }
    public string WindowTitle { get; set; } = "";
    public bool IsFrozen { get; set; }
    public bool IsSuspendable { get; set; }
    public bool IsBackground { get; set; }
}

public class SuspendableProcessService
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

    private readonly HashSet<string> _suspendableProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> _windowPids = new();
    private readonly string _configPath;
    private bool _windowsEnumerated;
    private readonly SuspendManager _suspendManager;

    public SuspendableProcessService(SuspendManager suspendManager)
    {
        _suspendManager = suspendManager;
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NanoShell");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "suspendable_processes.json");
        LoadSync();
    }

    public bool IsSuspendable(string nameLower)
        => _suspendableProcesses.Contains(nameLower);

    public IReadOnlySet<string> SuspendableTargets => _suspendableProcesses;

    private void LoadSync()
    {
        try
        {
            if (!File.Exists(_configPath)) return;
            string json = File.ReadAllText(_configPath);
            var data = JsonSerializer.Deserialize<SuspendableData>(json);
            if (data?.Suspendable != null)
            {
                _suspendableProcesses.Clear();
                foreach (var name in data.Suspendable)
                    _suspendableProcesses.Add(name);
            }
        }
        catch (Exception ex) { Log($"Load error: {ex.Message}"); }
    }

    private static List<(int Pid, string Name, string ExePath, int ParentPid)> SnapshotProcesses()
    {
        var list = new List<(int, string, string, int)>();
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
                list.Add(((int)entry.th32ProcessID, name, entry.szExeFile, (int)entry.th32ParentProcessID));
            } while (NativeMethods.Process32Next(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }
        return list;
    }

    private void EnumerateWindows()
    {
        if (_windowsEnumerated) return;
        _windowPids.Clear();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (NativeMethods.IsWindowVisible(hwnd))
                _windowPids.Add(pid);
            return true;
        }, IntPtr.Zero);
        _windowsEnumerated = true;
    }

    public void InvalidateWindowCache() { _windowsEnumerated = false; }

    public List<ProcessEntry> EnumerateAll()
    {
        try
        {
            EnumerateWindows();
            var entries = new List<ProcessEntry>();
            int selfPid = Environment.ProcessId;

            foreach (var (pid, name, exePath, parentPid) in SnapshotProcesses())
            {
                try
                {
                    if (pid == selfPid) continue;
                    string nameLower = name.ToLowerInvariant();
                    if (_systemProcesses.Contains(nameLower)) continue;
                    bool hasWindow = _windowPids.Contains((uint)pid);
                    string title = hasWindow ? GetWindowTitle(pid) : "";
                    bool isBackground = !hasWindow
                        && !nameLower.Equals("explorer", StringComparison.OrdinalIgnoreCase);

                    entries.Add(new ProcessEntry
                    {
                        Pid = pid,
                        Name = name,
                        ExePath = GetProcessImagePath(pid) ?? exePath,
                        ParentPid = parentPid,
                        WindowTitle = title,
                        IsFrozen = _suspendManager.IsSuspended((uint)pid),
                        IsSuspendable = _suspendableProcesses.Contains(nameLower),
                        IsBackground = isBackground
                    });
                }
                catch (Exception exInner) { Log($"EnumerateAll skip pid={pid} {name}: {exInner.Message}"); }
            }

            return entries;
        }
        catch (Exception ex) { Log($"EnumerateAll top-level error: {ex.Message}"); return new List<ProcessEntry>(); }
    }

    private static string GetWindowTitle(int pid)
    {
        try { using var proc = Process.GetProcessById(pid); return proc.MainWindowTitle ?? ""; }
        catch { return ""; }
    }

    private static string? GetProcessImagePath(int pid)
    {
        try
        {
            IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    return sb.ToString();
                return null;
            }
            finally { NativeMethods.CloseHandle(hProcess); }
        }
        catch { return null; }
    }

    public async Task ToggleSuspendableAsync(string processName, bool suspendable)
    {
        string lower = processName.ToLowerInvariant();
        if (suspendable)
            _suspendableProcesses.Add(lower);
        else
            _suspendableProcesses.Remove(lower);
        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        try
        {
            var data = new SuspendableData
            {
                Suspendable = new List<string>(_suspendableProcesses)
            };
            string json = JsonSerializer.Serialize(data);
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex) { Log($"Save error: {ex.Message}"); }
    }

    private class SuspendableData
    {
        public List<string>? Suspendable { get; set; }
    }
}
