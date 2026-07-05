using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NanoShell.Interop;

namespace NanoShell.Services;

public class ProcessEntry
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public bool IsFrozen { get; set; }
    public bool IsException { get; set; }
    public bool IsBackground { get; set; }
}

public class ProcessLockService
{
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
        LoadExceptions();
    }

    public List<ProcessEntry> EnumerateAll()
    {
        EnumerateWindows();
        var entries = new List<ProcessEntry>();
        int selfPid = Environment.ProcessId;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id == selfPid) continue;
                _ = proc.Handle;
                string name = proc.ProcessName;
                string nameLower = name.ToLowerInvariant();
                bool hasWindow = _windowPids.Contains((uint)proc.Id);
                string title = hasWindow ? GetWindowTitle(proc) : "";
                bool isBackground = !hasWindow
                    && !nameLower.Equals("explorer", StringComparison.OrdinalIgnoreCase);

                entries.Add(new ProcessEntry
                {
                    Pid = proc.Id,
                    Name = name,
                    WindowTitle = title,
                    IsFrozen = _frozenPids.Contains(proc.Id),
                    IsException = _exceptions.Contains(nameLower),
                    IsBackground = isBackground
                });
            }
            catch { }
        }

        return entries;
    }

    private static string GetWindowTitle(Process proc)
    {
        try { return proc.MainWindowTitle ?? ""; }
        catch { return ""; }
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
        EnumerateWindows();
        int selfPid = Environment.ProcessId;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id == selfPid) continue;
                string name = proc.ProcessName.ToLowerInvariant();
                if (_exceptions.Contains(name)) continue;
                NativeMethods.NtSuspendProcess(proc.Handle);
                _frozenPids.Add(proc.Id);
            }
            catch { }
        }
    }

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

    public bool IsException(string processName)
        => _exceptions.Contains(processName.ToLowerInvariant());

    public void ToggleException(string processName, bool isException)
    {
        string lower = processName.ToLowerInvariant();
        if (isException)
            _exceptions.Add(lower);
        else
            _exceptions.Remove(lower);
        SaveExceptions();
    }

    private void LoadExceptions()
    {
        try
        {
            if (!File.Exists(_exceptionsPath)) return;
            string json = File.ReadAllText(_exceptionsPath);
            var data = JsonSerializer.Deserialize<ExceptionsData>(json);
            if (data?.Exceptions != null)
                foreach (var e in data.Exceptions)
                    _exceptions.Add(e.ToLowerInvariant());
        }
        catch { }
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
