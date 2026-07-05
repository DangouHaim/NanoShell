using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NanoShell.Interop;
using NanoShell.Services;

namespace NanoShell.UI;

public partial class ProcessLockPanel : UserControl
{
    public event Action? CloseRequested;

    private readonly ProcessLockService _service;
    private List<ProcessGroupViewModel> _allGroups = new();
    private string _filter = "all";
    private string _search = "";
    private bool _isRefreshing;

    public ProcessLockPanel(ProcessLockService service)
    {
        _service = service;
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
            Log("RefreshAsync: starting EnumerateAll");
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

    private void Log(string msg)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NanoShell", "process_lock.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    private void ApplyFilter()
    {
        var query = _allGroups.AsEnumerable();

        if (_filter == "freeze")
            query = query.Where(g => g.IsFreezeTarget);

        if (!string.IsNullOrEmpty(_search))
        {
            string s = _search.ToLowerInvariant();
            query = query.Where(g =>
                g.Name.ToLowerInvariant().Contains(s) ||
                g.WindowTitles.ToLowerInvariant().Contains(s));
        }

        var list = query.OrderBy(g => g.Name).ToList();

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

    private void FilterAll_Click(object sender, RoutedEventArgs e)
    {
        _filter = "all";
        FilterAllBg.Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        FilterFreezeBg.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private void FilterFreeze_Click(object sender, RoutedEventArgs e)
    {
        _filter = "freeze";
        FilterFreezeBg.Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        FilterAllBg.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private async void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProcessGroupViewModel vm)
        {
            bool newVal = !vm.IsFreezeTarget;
            _service.ToggleFreezeTarget(vm.Name, newVal);
            foreach (var entry in vm.Entries)
            {
                if (newVal && !entry.IsFrozen)
                    _service.ToggleFreeze(entry.Pid, true);
                else if (!newVal && entry.IsFrozen)
                    _service.ToggleFreeze(entry.Pid, false);
            }
            await RefreshAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }
}

public class ProcessGroupViewModel
{
    private static readonly Dictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly ImageSource? _icon;

    public List<ProcessEntry> Entries { get; }
    public string Name { get; }
    public string ExePath { get; }
    public int Count { get; }
    public bool IsFreezeTarget => Entries[0].IsFreezeTarget;
    public bool IsFrozen => Entries.Any(e => e.IsFrozen);
    public bool IsBackground => Entries.All(e => e.IsBackground);
    public int ParentPid => Entries[0].ParentPid;

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

    public ImageSource? Icon => _icon;

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
}
