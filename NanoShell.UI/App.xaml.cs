using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace NanoShell.UI;

public partial class App : Application
{
    private readonly string _logPath;

    public App()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NanoShell");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "crash.log");
        File.WriteAllText(_logPath, $"=== NanoShell crash log ===\nStarted at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void Log(string message)
    {
        try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n"); }
        catch { }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log($"UI THREAD CRASH: {e.Exception}");
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log($"APP DOMAIN CRASH: {e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        Log($"TASK CRASH: {e.Exception?.InnerException ?? e.Exception}");
        e.SetObserved();
    }
}
