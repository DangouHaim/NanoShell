using System.Diagnostics;

namespace NanoShell.Services;

public class StartupRegistrationService
{
    public void RegisterAtLogon()
    {
#if !DEBUG
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return;

        string domain = Environment.UserDomainName;
        string user = Environment.UserName;

        if (TryUpdateTask(exePath))
            return;

        TryCreateTask(exePath, domain, user);
#endif
    }

    private static bool TryUpdateTask(string exePath)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/change /tn \"NanoShell2\" /tr \"{exePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process? p = Process.Start(psi);
        if (p == null)
            return false;

        return p.WaitForExit(5000) && p.ExitCode == 0;
    }

    private static void TryCreateTask(string exePath, string domain, string user)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/create /tn \"NanoShell2\" /tr \"{exePath}\" /sc onlogon /ru \"{domain}\\{user}\" /rl highest /f",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process? p = Process.Start(psi);
        if (p != null)
            p.WaitForExit(5000);

    }
}
