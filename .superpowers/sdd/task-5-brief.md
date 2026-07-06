# Task 5: Rename ProcessLockPanel → SuspendableProcessPanel

**Files:**
- Delete: `NanoShell.UI/ProcessLockPanel.xaml`
- Delete: `NanoShell.UI/ProcessLockPanel.xaml.cs`
- Create: `NanoShell.UI/SuspendableProcessPanel.xaml`
- Create: `NanoShell.UI/SuspendableProcessPanel.xaml.cs`

**Interfaces:**
- Consumes: `SuspendableProcessService`, `SuspendManager`
- Produces: `SuspendableProcessPanel` consumed by `LockScreenWindow`

## Context

- Old files used `ProcessLockService` (deleted), `IsFreezeTarget`, `KeyboardInputActive` as static on `ProcessLockService`
- New files use `SuspendableProcessService` (constructor param), `SuspendManager` (constructor param), `IsSuspendable`, `_suspendManager.KeyboardInputActive`
- XAML `x:Class` changes from `NanoShell.UI.ProcessLockPanel` to `NanoShell.UI.SuspendableProcessPanel`
- UI labels: header "Processes" → "Suspend on Lock", filter "Freeze" → "Suspend", toggle binding `IsFreezeTarget` → `IsSuspendable`
- `ProcessGroupViewModel.IsFreezeTarget` → `IsSuspendable`

## Steps

### Step 1: Delete old ProcessLockPanel files

```powershell
Remove-Item -LiteralPath "NanoShell.UI\ProcessLockPanel.xaml" -ErrorAction Stop
Remove-Item -LiteralPath "NanoShell.UI\ProcessLockPanel.xaml.cs" -ErrorAction Stop
```

### Step 2: Create SuspendableProcessPanel.xaml

Create `NanoShell.UI/SuspendableProcessPanel.xaml`:

```xml
<UserControl x:Class="NanoShell.UI.SuspendableProcessPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="440">
    <UserControl.Resources>
        <Style TargetType="Button">
            <Setter Property="Background" Value="#01000000"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}"
                                Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter Property="Background" Value="#22AAAAAA"/>
                            </Trigger>
                            <Trigger Property="IsFocused" Value="True">
                                <Setter Property="Background" Value="#01000000"/>
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter Property="Background" Value="#01000000"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ToggleSwitchStyle" TargetType="CheckBox">
            <Setter Property="Width" Value="52"/>
            <Setter Property="Height" Value="28"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="CheckBox">
                        <Border x:Name="BgBorder" CornerRadius="14" Background="#555" BorderThickness="0">
                            <Ellipse x:Name="Knob" Width="22" Height="22" Fill="White"
                                     HorizontalAlignment="Left" Margin="3,0,0,0"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="BgBorder" Property="Background" Value="#4CAF50"/>
                                <Setter TargetName="Knob" Property="Margin" Value="27,0,0,0"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="RowButtonStyle" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
            <Setter Property="Padding" Value="10,12"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        </Style>
    </UserControl.Resources>

    <Border Background="#CC151515" CornerRadius="14,0,0,14">
        <Grid Margin="16">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <Grid Grid.Row="0" Margin="0,0,0,16">
                <TextBlock Text="Suspend on Lock" Foreground="White" FontSize="22" FontWeight="Light"
                           VerticalAlignment="Center"/>
                <Button x:Name="CloseButton" Content="✕" Foreground="#999" Background="Transparent"
                        BorderThickness="0" FontSize="20" Cursor="Hand" Width="36" Height="36"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Click="CloseButton_Click"/>
            </Grid>

            <TextBox x:Name="SearchBox" Grid.Row="1"
                     Foreground="White" Background="#333" BorderThickness="0"
                     Padding="12,10" Margin="0,0,0,12" FontSize="16"
                     GotFocus="SearchBox_GotFocus" LostFocus="SearchBox_LostFocus"
                     TextChanged="SearchBox_TextChanged"/>

            <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,12">
                <Border x:Name="FilterAllBg" Background="#666" CornerRadius="6" Margin="0,0,8,0">
                    <Button x:Name="FilterAll" Content="All" Foreground="White"
                            Background="Transparent" BorderThickness="0" Padding="16,8"
                            Cursor="Hand" FontSize="14" Click="FilterAll_Click"/>
                </Border>
                <Border x:Name="FilterSuspendBg" Background="#444" CornerRadius="6">
                    <Button x:Name="FilterSuspend" Content="Suspend" Foreground="White"
                            Background="Transparent" BorderThickness="0" Padding="16,8"
                            Cursor="Hand" FontSize="14" Click="FilterSuspend_Click"/>
                </Border>
            </StackPanel>

            <ScrollViewer Grid.Row="3" VerticalScrollBarVisibility="Hidden" PanningMode="Both">
                <StackPanel>
                    <TextBlock x:Name="RegularHeader" Text="REGULAR" Foreground="#888"
                               FontSize="12" Margin="0,6,0,4" FontWeight="SemiBold"
                               Visibility="Visible"/>
                    <ItemsControl x:Name="RegularList" Visibility="Visible">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Style="{StaticResource RowButtonStyle}"
                                        Click="Row_Click">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <Image Grid.Column="0" Width="24" Height="24"
                                               Source="{Binding Icon}" Margin="0,0,10,0"/>
                                        <TextBlock Grid.Column="1" Text="{Binding FreezeIcon}"
                                                   Foreground="{Binding FreezeColor}" FontSize="16"
                                                   VerticalAlignment="Center" Margin="0,0,8,0"/>
                                        <StackPanel Grid.Column="2" VerticalAlignment="Center">
                                            <TextBlock FontWeight="SemiBold" Foreground="White"
                                                       FontSize="16" TextTrimming="CharacterEllipsis">
                                                <Run Text="{Binding DisplayName, Mode=OneWay}"/>
                                                <Run Text="{Binding CountLabel, Mode=OneWay}" Foreground="#888" FontWeight="Normal" FontSize="13"/>
                                            </TextBlock>
                                            <TextBlock Text="{Binding WindowTitles}" Foreground="#888"
                                                       FontSize="13" TextTrimming="CharacterEllipsis"/>
                                        </StackPanel>
                                        <CheckBox Grid.Column="3" IsHitTestVisible="False"
                                                  Style="{StaticResource ToggleSwitchStyle}"
                                                  IsChecked="{Binding IsSuspendable, Mode=OneWay}"/>
                                    </Grid>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <Border Height="1" Background="#333" Margin="0,12"/>

                    <TextBlock x:Name="BackgroundHeader" Text="BACKGROUND" Foreground="#888"
                               FontSize="12" Margin="0,6,0,4" FontWeight="SemiBold"
                               Visibility="Visible"/>
                    <ItemsControl x:Name="BackgroundList" Visibility="Visible">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Style="{StaticResource RowButtonStyle}"
                                        Click="Row_Click">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <Image Grid.Column="0" Width="24" Height="24"
                                               Source="{Binding Icon}" Margin="0,0,10,0"/>
                                        <TextBlock Grid.Column="1" Text="{Binding FreezeIcon}"
                                                   Foreground="{Binding FreezeColor}" FontSize="16"
                                                   VerticalAlignment="Center" Margin="0,0,8,0"/>
                                        <StackPanel Grid.Column="2" VerticalAlignment="Center">
                                            <TextBlock FontWeight="SemiBold" Foreground="White"
                                                       FontSize="16" TextTrimming="CharacterEllipsis">
                                                <Run Text="{Binding DisplayName, Mode=OneWay}"/>
                                                <Run Text="{Binding CountLabel, Mode=OneWay}" Foreground="#888" FontWeight="Normal" FontSize="13"/>
                                            </TextBlock>
                                            <TextBlock Text="{Binding WindowTitles}" Foreground="#888"
                                                       FontSize="13" TextTrimming="CharacterEllipsis"/>
                                        </StackPanel>
                                        <CheckBox Grid.Column="3" IsHitTestVisible="False"
                                                  Style="{StaticResource ToggleSwitchStyle}"
                                                  IsChecked="{Binding IsSuspendable, Mode=OneWay}"/>
                                    </Grid>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </ScrollViewer>
        </Grid>
    </Border>
</UserControl>
```

### Step 3: Create SuspendableProcessPanel.xaml.cs

Create `NanoShell.UI/SuspendableProcessPanel.xaml.cs`:

```csharp
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
```

### Step 4: Build

```powershell
dotnet build NanoShell.UI\NanoShell.UI.csproj 2>&1
```

Expected: Build errors — LockScreenWindow still references old types. That's expected, Task 6 will fix it. Check that errors are ONLY from `LockScreenWindow.cs`, not from the new panel files.

### Step 5: Commit

```bash
git add -A
git commit -m "refactor: rename ProcessLockPanel to SuspendableProcessPanel, update labels"
```
