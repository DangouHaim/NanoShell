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
        {
            p.WaitForExit(5000);
        }
#endif
    }
}
