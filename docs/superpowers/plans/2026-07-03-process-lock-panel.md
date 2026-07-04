# Process Lock Panel Implementation Plan

> **For agentic workers:** Executed inline in current session.

**Goal:** Add a gear button on the active lock screen that opens an animated process list panel with freeze/toggle per process and persisting exceptions.

**Architecture:** `ProcessLockService` handles enumeration, freeze/thaw, and exceptions persistence. `ProcessLockPanel` is a WPF UserControl hosted in `LockScreenWindow`. `MainWindow` delegates suspend/resume to `ProcessLockService`.

**Tech Stack:** .NET 8 WPF, `System.Text.Json`, Win32 `NtSuspendProcess`/`NtResumeProcess`, `EnumWindows`

---

### Task 1: ProcessLockService

**Files:**
- Create: `NanoShell.Services/ProcessLockService.cs`

**Consumes:** `NativeMethods.NtSuspendProcess`, `NativeMethods.NtResumeProcess` (already exist)

**Produces:** `ProcessEntry` model, `ProcessLockService` class

- **Step 1: Create ProcessEntry model and ProcessLockService skeleton**

```csharp
using System.Collections.Generic;
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

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
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
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
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
```

- **Step 2: Build to verify compilation**

Run: `dotnet build -c Release NanoShell.Services\NanoShell.Services.csproj`
Expected: Build succeeded, 0 errors

- **Step 3: Commit**

```bash
git add NanoShell.Services/ProcessLockService.cs
git commit -m "feat: add ProcessLockService with enumeration, freeze/thaw, exceptions persistence"
```

---

### Task 2: ProcessLockPanel UI

**Files:**
- Create: `NanoShell.UI/ProcessLockPanel.xaml`
- Create: `NanoShell.UI/ProcessLockPanel.xaml.cs`

**Consumes:** `ProcessLockService`, `ProcessEntry`

**Produces:** `ProcessLockPanel` UserControl

- **Step 1: Create ProcessLockPanel.xaml**

```xml
<UserControl x:Class="NanoShell.UI.ProcessLockPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="400">
    <Border Background="#CC151515" CornerRadius="12,0,0,12">
        <Grid Margin="16">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <!-- Header -->
            <Grid Grid.Row="0" Margin="0,0,0,12">
                <TextBlock Text="Processes" Foreground="White" FontSize="18" FontWeight="Light"
                           VerticalAlignment="Center"/>
                <Button x:Name="CloseButton" Content="✕" Foreground="#999" Background="Transparent"
                        BorderThickness="0" FontSize="16" Cursor="Hand"
                        HorizontalAlignment="Right" VerticalAlignment="Center"
                        Click="CloseButton_Click"/>
            </Grid>

            <!-- Search -->
            <TextBox x:Name="SearchBox" Grid.Row="1"
                     Text="" Foreground="White" Background="#333" BorderThickness="0"
                     Padding="8,6" Margin="0,0,0,8"
                     TextChanged="SearchBox_TextChanged"/>

            <!-- Filter pills -->
            <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,8">
                <Button x:Name="FilterAll" Content="All" Foreground="White"
                        Background="#444" BorderThickness="0" Padding="10,4" Margin="0,0,6,0"
                        Cursor="Hand" FontSize="12" Click="FilterAll_Click"/>
                <Button x:Name="FilterExceptions" Content="Exceptions" Foreground="White"
                        Background="#444" BorderThickness="0" Padding="10,4"
                        Cursor="Hand" FontSize="12" Click="FilterExceptions_Click"/>
            </StackPanel>

            <!-- Process list -->
            <ScrollViewer Grid.Row="3" VerticalScrollBarVisibility="Hidden" PanningMode="Both">
                <ItemsControl x:Name="ProcessList">
                    <ItemsControl.GroupStyle>
                        <GroupStyle>
                            <GroupStyle.HeaderTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Name}" Foreground="#888"
                                               FontSize="11" Margin="0,8,0,4" FontWeight="SemiBold"/>
                                </DataTemplate>
                            </GroupStyle.HeaderTemplate>
                        </GroupStyle>
                    </ItemsControl.GroupStyle>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Background="Transparent" Padding="4,3">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{Binding FreezeIcon}"
                                               Foreground="{Binding FreezeColor}" FontSize="12"
                                               VerticalAlignment="Center" Margin="0,0,6,0"/>
                                    <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                        <TextBlock Text="{Binding DisplayName}" Foreground="White"
                                                   FontSize="13" TextTrimming="CharacterEllipsis"/>
                                        <TextBlock Text="{Binding WindowTitle}" Foreground="#888"
                                                   FontSize="11" TextTrimming="CharacterEllipsis"/>
                                    </StackPanel>
                                    <ToggleButton Grid.Column="2"
                                                  IsChecked="{Binding IsException, Mode=TwoWay}"
                                                  Checked="Toggle_Checked"
                                                  Unchecked="Toggle_Unchecked"
                                                  FontSize="12" Padding="4,2"
                                                  Foreground="White"/>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>
    </Border>
</UserControl>
```

- **Step 2: Create ProcessLockPanel.xaml.cs**

```csharp
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using NanoShell.Services;

namespace NanoShell.UI;

public partial class ProcessLockPanel : UserControl
{
    private readonly ProcessLockService _service;
    private List<ProcessEntry> _allEntries = new();
    private string _filter = "all";
    private string _search = "";

    public ProcessLockPanel(ProcessLockService service)
    {
        _service = service;
        InitializeComponent();
        LoadProcesses();
    }

    public void LoadProcesses()
    {
        _allEntries = _service.EnumerateAll().Select(e => new ProcessEntryViewModel(e)).ToList();
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

        var grouped = query
            .OrderBy(e => e.IsBackground ? 1 : 0)
            .ThenBy(e => e.Name)
            .GroupBy(e => e.IsBackground ? "Background" : "Regular")
            .ToList();

        ProcessList.ItemsSource = grouped;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text;
        ApplyFilter();
    }

    private void FilterAll_Click(object sender, RoutedEventArgs e)
    {
        _filter = "all";
        FilterAll.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66));
        FilterExceptions.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private void FilterExceptions_Click(object sender, RoutedEventArgs e)
    {
        _filter = "exceptions";
        FilterExceptions.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66));
        FilterAll.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
        ApplyFilter();
    }

    private void Toggle_Checked(object sender, RoutedEventArgs e)
    {
        HandleToggle(sender, true);
    }

    private void Toggle_Unchecked(object sender, RoutedEventArgs e)
    {
        HandleToggle(sender, false);
    }

    private void HandleToggle(object sender, bool isException)
    {
        if (sender is ToggleButton btn && btn.DataContext is ProcessEntryViewModel vm)
        {
            _service.ToggleException(vm.Name, isException);
            if (isException && vm.IsFrozen)
                _service.ToggleFreeze(vm.Pid, false);
            LoadProcesses();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        var parent = Parent as FrameworkElement;
        var storyboard = parent?.FindResource("HidePanelStoryboard") as Storyboard;
        storyboard?.Begin();
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
    public string FreezeIcon => _entry.IsFrozen ? "❄" : "○";
    public System.Windows.Media.Brush FreezeColor =>
        _entry.IsFrozen
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0xCC, 0xFF))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
}
```

- **Step 3: Build to verify compilation**

Run: `dotnet build -c Release NanoShell.UI\NanoShell.UI.csproj`
Expected: Build succeeded, 0 errors

- **Step 4: Commit**

```bash
git add NanoShell.UI/ProcessLockPanel.xaml NanoShell.UI/ProcessLockPanel.xaml.cs
git commit -m "feat: add ProcessLockPanel UserControl with search, filter, toggle"
```

---

### Task 3: Integrate panel into LockScreenWindow

**Files:**
- Modify: `NanoShell.UI/LockScreenWindow.xaml`
- Modify: `NanoShell.UI/LockScreenWindow.xaml.cs`

**Consumes:** `ProcessLockPanel`, `ProcessLockService`

- **Step 1: Add gear button + panel overlay to LockScreenWindow.xaml**

Add after `AODTimeText` before closing `</Grid>`:

```xml
        <!-- Process panel overlay -->
        <Grid x:Name="ProcessPanelOverlay" Visibility="Collapsed">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <Rectangle Grid.Column="0" Fill="Transparent" MouseDown="Overlay_MouseDown"/>
            <ContentControl Grid.Column="1" x:Name="ProcessPanelHost"/>
        </Grid>

        <!-- Gear button -->
        <Button x:Name="GearButton" Content="⚙" Foreground="#666" Background="Transparent"
                BorderThickness="0" FontSize="22" Cursor="Hand"
                HorizontalAlignment="Right" VerticalAlignment="Top"
                Margin="0,12,12,0"
                Visibility="Collapsed"
                Click="GearButton_Click"/>
```

- **Step 2: Wire gear + panel in LockScreenWindow.xaml.cs**

```csharp
// Add fields
private readonly ProcessLockService _processLock;
private ProcessLockPanel? _processPanel;

// Add to constructor parameter
public LockScreenWindow(LockScreenService service, ProcessLockService processLock, string wallpaperPath)

// Store
_processLock = processLock;

// Show gear in ShowActive
GearButton.Visibility = Visibility.Visible;

// Hide in ShowAOD
GearButton.Visibility = Visibility.Collapsed;
ProcessPanelOverlay.Visibility = Visibility.Collapsed;

// Add event handlers
private void GearButton_Click(object sender, RoutedEventArgs e)
{
    if (ProcessPanelOverlay.Visibility == Visibility.Visible)
    {
        HideProcessPanel();
    }
    else
    {
        ShowProcessPanel();
    }
}

private void ShowProcessPanel()
{
    if (_processPanel == null)
    {
        _processPanel = new ProcessLockPanel(_processLock);
        ProcessPanelHost.Content = _processPanel;
    }
    else
    {
        _processPanel.LoadProcesses();
    }

    ProcessPanelOverlay.Visibility = Visibility.Visible;
    ProcessPanelOverlay.Opacity = 0;
    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
    ProcessPanelOverlay.BeginAnimation(OpacityProperty, fadeIn);
}

private void HideProcessPanel()
{
    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
    fadeOut.Completed += (s, a) => ProcessPanelOverlay.Visibility = Visibility.Collapsed;
    ProcessPanelOverlay.BeginAnimation(OpacityProperty, fadeOut);
}

private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
{
    HideProcessPanel();
}
```

- **Step 3: Update ShowActive to show gear, ShowAOD to hide gear**

In `ShowAOD()` add: `GearButton.Visibility = Visibility.Collapsed;`
In `ShowActive()` add: `GearButton.Visibility = Visibility.Visible;`

- **Step 4: Build to verify compilation**

Run: `dotnet build -c Release NanoShell.UI\NanoShell.UI.csproj`
Expected: Build succeeded, 0 errors

- **Step 5: Commit**

```bash
git add -A
git commit -m "feat: integrate ProcessLockPanel into LockScreenWindow with gear button"
```

---

### Task 4: Update MainWindow to use ProcessLockService

**Files:**
- Modify: `NanoShell.UI/MainWindow.xaml.cs`

- **Step 1: Add ProcessLockService field, create in constructor, pass to LockScreenWindow**

```csharp
// Add field
private readonly ProcessLockService _processLock;

// In constructor, after _lockScreenService creation:
_processLock = new ProcessLockService();

// Update OnLockScreenRequested to pass _processLock and use FreezeAll/ThawAll
private void OnLockScreenRequested(string wallpaperPath)
{
    _processLock.FreezeAll();
    var win = new LockScreenWindow(_lockScreenService, _processLock, wallpaperPath);
    win.Show();
}

private void OnLockScreenDismissed()
{
    _processLock.ThawAll();
}
```

- **Step 2: Remove old SuspendNotepad/ResumeNotepad and _suspendedProcesses**

Delete the `SuspendNotepad()`, `ResumeNotepad()` methods and the `_suspendedProcesses` field.

- **Step 3: Build to verify compilation**

Run: `dotnet build -c Release NanoShell.UI\NanoShell.UI.csproj`
Expected: Build succeeded, 0 errors

- **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: use ProcessLockService for suspend/resume instead of inline logic"
```

---

### Task 5: Build and verify

- **Step 1: Full Release build**

```bash
dotnet build -c Release NanoShell.UI\NanoShell.UI.csproj
```

Expected: Build succeeded, 0 errors

- **Step 2: Launch and verify**

```bash
Start-Process -FilePath "dotnet" -ArgumentList "run --project NanoShell.UI\NanoShell.UI.csproj -c Release"
```
