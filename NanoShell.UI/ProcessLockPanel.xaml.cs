using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NanoShell.Services;

namespace NanoShell.UI;

public partial class ProcessLockPanel : UserControl
{
    private readonly ProcessLockService _service;
    private List<ProcessEntryViewModel> _allEntries = new();
    private string _filter = "all";
    private string _search = "";

    public ProcessLockPanel(ProcessLockService service)
    {
        _service = service;
        InitializeComponent();
        Refresh();
    }

    public void ShowPanel()
    {
        Refresh();
        Visibility = Visibility.Visible;
    }

    public void Refresh()
    {
        _allEntries = _service.EnumerateAll()
            .Select(e => new ProcessEntryViewModel(e)).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = _allEntries.AsEnumerable();

        if (_filter == "exceptions")
            query = query.Where(e => e.IsException);

        if (!string.IsNullOrEmpty(_search))
        {
            string s = _search.ToLowerInvariant();
            query = query.Where(e =>
                e.Name.ToLowerInvariant().Contains(s) ||
                e.WindowTitle.ToLowerInvariant().Contains(s));
        }

        var list = query.OrderBy(e => e.Name).ToList();

        RegularList.ItemsSource = list.Where(e => !e.IsBackground);
        BackgroundList.ItemsSource = list.Where(e => e.IsBackground);

        bool hasRegular = list.Any(e => !e.IsBackground);
        bool hasBackground = list.Any(e => e.IsBackground);

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
        FilterExceptionsBg.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private void FilterExceptions_Click(object sender, RoutedEventArgs e)
    {
        _filter = "exceptions";
        FilterExceptionsBg.Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        FilterAllBg.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private void Toggle_Checked(object sender, RoutedEventArgs e) => HandleToggle(sender, true);
    private void Toggle_Unchecked(object sender, RoutedEventArgs e) => HandleToggle(sender, false);

    private void HandleToggle(object sender, bool isException)
    {
        if (sender is CheckBox cb && cb.DataContext is ProcessEntryViewModel vm)
        {
            _service.ToggleException(vm.Name, isException);
            if (isException && vm.IsFrozen)
                _service.ToggleFreeze(vm.Pid, false);
            Refresh();
        }
    }

private void CloseButton_Click(object sender, RoutedEventArgs e)
{
    // Walk up to find the PanelOverlay grid and hide it
    DependencyObject? el = this;
    while (el != null && !(el is Grid && (string)el.GetValue(FrameworkElement.NameProperty) == "PanelOverlay"))
        el = VisualTreeHelper.GetParent(el);
    if (el is Grid overlay)
        overlay.Visibility = Visibility.Collapsed;
}
}

public class ProcessEntryViewModel
{
    private readonly ProcessEntry _entry;
    public ProcessEntryViewModel(ProcessEntry entry) => _entry = entry;

    public int Pid => _entry.Pid;
    public string Name => _entry.Name;
    public string WindowTitle => _entry.WindowTitle;
    public bool IsFrozen => _entry.IsFrozen;
    public bool IsException => _entry.IsException;
    public bool IsBackground => _entry.IsBackground;

    public string DisplayName => _entry.Name + ".exe";
    public string FreezeIcon => _entry.IsFrozen ? "\u2744" : "\u25CB";
    public Brush FreezeColor => _entry.IsFrozen
        ? new SolidColorBrush(Color.FromRgb(0x66, 0xCC, 0xFF))
        : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
}
