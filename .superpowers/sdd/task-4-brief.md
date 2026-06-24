### Task 4: Integrate WindowAutoManager into MainWindow

**Files:**
- Modify: `NanoShell/MainWindow.xaml.cs`

- [ ] **Step 1: Add field and lifecycle calls in MainWindow**

```csharp
// Add field
private WindowAutoManager _windowAutoManager;

// In OnSourceInitialized, after setting extended style:
_windowAutoManager = new WindowAutoManager(Dispatcher);
_windowAutoManager.Start();

// In Window_Closed, before existing RegisterAppBar():
_windowAutoManager?.Dispose();
```

- [ ] **Step 2: Verify build**

```powershell
dotnet build NanoShell\NanoShell.csproj
```

- [ ] **Step 3: Commit**

```bash
git add NanoShell/MainWindow.xaml.cs
git commit -m "feat: integrate WindowAutoManager into MainWindow lifecycle"
```
