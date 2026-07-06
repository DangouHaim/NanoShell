using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NanoShell.Interop;
using NanoShell.Services;

namespace NanoShell.UI;

public partial class SuspendableProcessPanel : UserControl
{
    public event Action? CloseRequested;

    private readonly SuspendableProcessService _service;
    private readonly SuspendManager _suspendManager;
    private List<ProcessGroupViewModel> _allGroups = new();
    private string _filter = "all";
    private string _search = "";
    private bool _isRefreshing;
    private HashSet<int>? _thawedExplorerPids;

    public SuspendableProcessPanel(SuspendableProcessService service, SuspendManager suspendManager)
    {
        _service = service;
        _suspendManager = suspendManager;
        InitializeComponent();
    }

    public async Task ShowPanelAsync()
    {
        await RefreshAsync();
        Visibility = Visibility.Visible;
    }

    public async Task RefreshAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await Task.Run(() => _service.EnumerateAll());
            sw.Stop();
            Log($"RefreshAsync: EnumerateAll took {sw.ElapsedMilliseconds}ms, got {result.Count} entries");
            _allGroups = result
                .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProcessGroupViewModel(g.ToList()))
                .ToList();
            ProcessGroupViewModel.ResolveParentIcons(_allGroups);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Log($"RefreshAsync error: {ex}");
        }
        finally { _isRefreshing = false; }
    }

    private static void Log(string msg)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NanoShell", "suspend_manager.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    private void ApplyFilter()
    {
        var query = _allGroups.AsEnumerable();

        if (_filter == "suspend")
            query = query.Where(g => g.IsSuspendable);

        if (!string.IsNullOrEmpty(_search))
        {
            string s = _search.ToLowerInvariant();
            query = query.Where(g =>
                g.Name.ToLowerInvariant().Contains(s) ||
                g.WindowTitles.ToLowerInvariant().Contains(s));
        }

        var list = query
            .OrderByDescending(g => g.HasIcon)
            .ThenBy(g => g.Name)
            .ToList();

        RegularList.ItemsSource = list.Where(g => !g.IsBackground);
        BackgroundList.ItemsSource = list.Where(g => g.IsBackground);

        bool hasRegular = list.Any(g => !g.IsBackground);
        bool hasBackground = list.Any(g => g.IsBackground);

        RegularHeader.Visibility = hasRegular ? Visibility.Visible : Visibility.Collapsed;
        RegularList.Visibility = hasRegular ? Visibility.Visible : Visibility.Collapsed;
        BackgroundHeader.Visibility = hasBackground ? Visibility.Visible : Visibility.Collapsed;
        BackgroundList.Visibility = hasBackground ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text;
        ApplyFilter();
    }

    private async void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _suspendManager.KeyboardInputActive = true;
        var thawed = new HashSet<int>();
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
        {
            uint pid = (uint)proc.Id;
            if (_suspendManager.IsSuspended(pid))
            {
                await _suspendManager.ResumeProcessAsync(pid, CancellationToken.None);
                thawed.Add((int)pid);
            }
        }
        _thawedExplorerPids = thawed;
    }

    private async void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _suspendManager.KeyboardInputActive = false;
        if (_thawedExplorerPids != null && _thawedExplorerPids.Count > 0)
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                uint pid = (uint)proc.Id;
                if (_thawedExplorerPids.Contains((int)pid) && !_suspendManager.IsSuspended(pid))
                {
                    await _suspendManager.SuspendProcessAsync(pid, CancellationToken.None);
                }
            }
            _thawedExplorerPids = null;
        }
    }

    private void FilterAll_Click(object sender, RoutedEventArgs e)
    {
        _filter = "all";
        FilterAllBg.Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        FilterSuspendBg.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private void FilterSuspend_Click(object sender, RoutedEventArgs e)
    {
        _filter = "suspend";
        FilterSuspendBg.Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        FilterAllBg.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private async void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProcessGroupViewModel vm)
        {
            bool newVal = !vm.IsSuspendable;
            await _service.ToggleSuspendableAsync(vm.Name, newVal);
            foreach (var entry in vm.Entries)
            {
                uint pid = (uint)entry.Pid;
                if (newVal && !entry.IsFrozen)
                    await _suspendManager.SuspendProcessAsync(pid, CancellationToken.None);
                else if (!newVal && entry.IsFrozen)
                    await _suspendManager.ResumeProcessAsync(pid, CancellationToken.None);
            }
            await RefreshAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _suspendManager.KeyboardInputActive = false;
        CloseRequested?.Invoke();
    }
}

public class ProcessGroupViewModel
{
    private static readonly Dictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _defaultIcon;

    private readonly ImageSource? _icon;

    public List<ProcessEntry> Entries { get; }
    public string Name { get; }
    public string ExePath { get; }
    public int Count { get; }
    public bool IsSuspendable => Entries[0].IsSuspendable;
    public bool IsFrozen => Entries.Any(e => e.IsFrozen);
    public bool IsBackground => Entries.All(e => e.IsBackground);
    public int ParentPid => Entries[0].ParentPid;
    public bool HasIcon => _icon != null;

    public string DisplayName => Name + ".exe";
    public string CountLabel => Count > 1 ? $"×{Count}" : "";
    public string WindowTitles
    {
        get
        {
            var titles = Entries
                .Where(e => !string.IsNullOrEmpty(e.WindowTitle))
                .Select(e => e.WindowTitle)
                .Distinct()
                .Take(2)
                .ToList();
            if (titles.Count == 0 && IsBackground)
                return "Background";
            return string.Join(", ", titles);
        }
    }

    public string FreezeIcon => IsFrozen ? "\u2744" : "\u25CB";
    public Brush FreezeColor => IsFrozen
        ? new SolidColorBrush(Color.FromRgb(0x66, 0xCC, 0xFF))
        : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    public ImageSource? Icon => _icon ?? DefaultIcon;

    private static ImageSource DefaultIcon
    {
        get
        {
            if (_defaultIcon == null)
                _defaultIcon = CreateDefaultIcon();
            return _defaultIcon;
        }
    }

    public ProcessGroupViewModel(List<ProcessEntry> entries)
    {
        Entries = entries;
        Name = entries[0].Name;
        ExePath = entries[0].ExePath;
        Count = entries.Count;
        if (!string.IsNullOrEmpty(ExePath))
            _icon = GetOrLoadIcon(ExePath);
    }

    private static ImageSource? GetOrLoadIcon(string path)
    {
        if (_iconCache.TryGetValue(path, out var img))
            return img;
        img = LoadIcon(path);
        if (img != null)
            _iconCache[path] = img;
        return img;
    }

    public static void ResolveParentIcons(List<ProcessGroupViewModel> groups)
    {
        var pidMap = new Dictionary<int, ProcessGroupViewModel>();
        foreach (var g in groups)
            foreach (var e in g.Entries)
                pidMap[e.Pid] = g;

        foreach (var g in groups)
        {
            if (g._icon != null) continue;
            int parentPid = g.ParentPid;
            if (parentPid <= 0) continue;
            if (pidMap.TryGetValue(parentPid, out var parent) && parent._icon != null)
            {
                if (!string.IsNullOrEmpty(g.ExePath) && !_iconCache.ContainsKey(g.ExePath))
                    _iconCache[g.ExePath] = parent._icon;
            }
        }
    }

    private static ImageSource? LoadIcon(string exePath)
    {
        if (!File.Exists(exePath)) return null;
        try
        {
            uint count = NativeMethods.ExtractIconEx(exePath, 0, out _, out IntPtr hIconSmall, 1);
            if (count == 0 || hIconSmall == IntPtr.Zero) return null;
            try
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(hIconSmall,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            finally
            {
                NativeMethods.DestroyIcon(hIconSmall);
            }
        }
        catch { return null; }
    }

    private static ImageSource CreateDefaultIcon()
    {
        var geometry = Geometry.Parse(
            "M12,2 C6.48,2 2,6.48 2,12 C2,17.52 6.48,22 12,22 C17.52,22 22,17.52 22,12 C22,6.48 17.52,2 12,2 Z " +
            "M7.07,18.28 C7.5,17.38 10.12,16.5 12,16.5 C13.88,16.5 16.5,17.38 16.93,18.28 " +
            "C15.57,19.36 13.86,20 12,20 C10.14,20 8.43,19.36 7.07,18.28 Z " +
            "M18.36,16.83 C16.93,15.09 14.66,14 12,14 C9.34,14 7.07,15.09 5.64,16.83 " +
            "C4.62,15.49 4,13.82 4,12 C4,7.59 7.59,4 12,4 C16.41,4 20,7.59 20,12 " +
            "C20,13.82 19.38,15.49 18.36,16.83 Z " +
            "M12,6 C10.9,6 10,6.9 10,8 C10,9.1 10.9,10 12,10 C13.1,10 14,9.1 14,8 C14,6.9 13.1,6 12,6 Z");
        var drawing = new GeometryDrawing
        {
            Geometry = geometry,
            Brush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99))
        };
        var group = new DrawingGroup { Children = { drawing } };
        group.Freeze();
        var bs = new DrawingImage(group);
        bs.Freeze();
        return bs;
    }
}
