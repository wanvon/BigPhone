using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;

namespace BigPhone;

/// <summary>
/// "Keep Big Phone running": a Windows scheduled task (for the current user, no admin needed) that runs
/// "BigPhone.exe --ensure" at log-on and every 5 minutes. --ensure starts Big Phone only if it isn't running,
/// and otherwise exits silently.
/// </summary>
public static class KeepAlive
{
    public const string TaskName = "Big Phone keep-alive";

    static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "BigPhone.exe");

    /// <summary>Creates or refreshes the task (refreshing keeps the path right if the folder was moved).</summary>
    public static bool Enable()
    {
        string user = WindowsIdentity.GetCurrent().Name;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Big Phone at log-on and restarts it if it was closed or crashed.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT15S</Delay></LogonTrigger>
                <TimeTrigger>
                  <StartBoundary>2026-01-01T00:00:00</StartBoundary><Enabled>true</Enabled>
                  <Repetition><Interval>PT5M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>
                </TimeTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <AllowHardTerminate>false</AllowHardTerminate>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(ExePath)}</Command><Arguments>--ensure</Arguments><WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(ExePath)!)}</WorkingDirectory></Exec>
              </Actions>
            </Task>
            """;
        string file = Path.Combine(Path.GetTempPath(), "bigphone-keepalive.xml");
        try
        {
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            var (code, output) = Schtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            Log.Write(code == 0 ? "Keep-alive task created/updated" : $"Keep-alive task could not be created: {output}");
            return code == 0;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    public static void Disable()
    {
        if (!IsInstalled()) return;
        var (code, output) = Schtasks($"/Delete /TN \"{TaskName}\" /F");
        Log.Write(code == 0 ? "Keep-alive task removed" : $"Keep-alive task could not be removed: {output}");
    }

    public static bool IsInstalled() => Schtasks($"/Query /TN \"{TaskName}\"").code == 0;

    /// <summary>Makes the scheduled task match the setting. Called at startup and after Settings are saved.</summary>
    public static void Sync(PhoneSettings s)
    {
        try
        {
            if (s.KeepRunning) Enable();
            else Disable();
        }
        catch (Exception ex) { Log.Write($"Keep-alive sync failed: {ex.Message}"); }
    }

    static (int code, string output) Schtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return (p.ExitCode, output.Trim());
    }
}
