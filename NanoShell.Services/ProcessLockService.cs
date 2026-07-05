using System.Diagnostics;
using System.IO;
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
    public bool IsFreezeTarget { get; set; }
    public bool IsBackground { get; set; }
}

public class ProcessLockService
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NanoShell", "process_lock.log");

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

    private readonly HashSet<string> _exceptions = new();
    private readonly HashSet<int> _frozenPids = new();
    private readonly HashSet<uint> _windowPids = new();
    private readonly string _exceptionsPath;
    private bool _windowsEnumerated;

    public ProcessLockService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NanoShell");
        Directory.CreateDirectory(dir);
        _exceptionsPath = Path.Combine(dir, "suspend_exceptions.json");
        try { if (File.Exists(_exceptionsPath)) File.Delete(_exceptionsPath); }
        catch { }
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
                        IsFrozen = _frozenPids.Contains(pid),
                        IsFreezeTarget = _exceptions.Contains(nameLower),
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

    public void FreezeAll()
    {
        try
        {
            EnumerateWindows();
            int selfPid = Environment.ProcessId;

            foreach (var (pid, name, _, _) in SnapshotProcesses())
            {
                try
                {
                    if (pid == selfPid) continue;
                    string nameLower = name.ToLowerInvariant();
                    if (_systemProcesses.Contains(nameLower)) continue;
                    if (!_exceptions.Contains(nameLower)) continue;
                    using var proc = Process.GetProcessById(pid);
                    NativeMethods.NtSuspendProcess(proc.Handle);
                    _frozenPids.Add(pid);
                }
                catch (Exception exFreeze) { Log($"FreezeAll skip pid={pid}: {exFreeze.Message}"); }
            }
        }
        catch (Exception ex) { Log($"FreezeAll top-level error: {ex.Message}"); }
    }

    public Task FreezeAllAsync() => Task.Run(FreezeAll);

    public void ThawAll()
    {
        foreach (int pid in _frozenPids)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                NativeMethods.NtResumeProcess(proc.Handle);
            }
            catch { }
        }
        _frozenPids.Clear();
        _windowsEnumerated = false;
    }

    public void ToggleFreeze(int pid, bool freeze)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (freeze)
            {
                NativeMethods.NtSuspendProcess(proc.Handle);
                _frozenPids.Add(pid);
            }
            else
            {
                NativeMethods.NtResumeProcess(proc.Handle);
                _frozenPids.Remove(pid);
            }
        }
        catch { }
    }

    public bool IsFreezeTarget(string processName)
        => _exceptions.Contains(processName.ToLowerInvariant());

    public void ToggleFreezeTarget(string processName, bool isException)
    {
        string lower = processName.ToLowerInvariant();
        if (isException)
            _exceptions.Add(lower);
        else
            _exceptions.Remove(lower);
        SaveExceptions();
    }

    private void SaveExceptions()
    {
        try
        {
            var data = new ExceptionsData
            {
                Exceptions = new List<string>(_exceptions)
            };
            string json = JsonSerializer.Serialize(data);
            File.WriteAllText(_exceptionsPath, json);
        }
        catch { }
    }

    private class ExceptionsData
    {
        public List<string>? Exceptions { get; set; }
    }
}