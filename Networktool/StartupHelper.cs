// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.11
// License: Private

using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Networktool;

public static class StartupHelper
{
    private const string TaskName = "Networktool_AutoStart";

    // Uses Task Scheduler so the app launches elevated at logon.
    // Registry Run keys silently skip apps that require admin.
    public static void SetStartup(bool enable)
    {
        try
        {
            if (enable)
            {
                string exe = Application.ExecutablePath;
                // Create a logon task that runs with highest privileges
                string xml = $"""
                    <?xml version="1.0" encoding="UTF-16"?>
                    <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                      <Triggers>
                        <LogonTrigger>
                          <Enabled>true</Enabled>
                          <UserId>{Environment.UserDomainName}\{Environment.UserName}</UserId>
                        </LogonTrigger>
                      </Triggers>
                      <Principals>
                        <Principal id="Author">
                          <LogonType>InteractiveToken</LogonType>
                          <RunLevel>HighestAvailable</RunLevel>
                        </Principal>
                      </Principals>
                      <Settings>
                        <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                        <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                        <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                        <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                      </Settings>
                      <Actions>
                        <Exec>
                          <Command>{System.Security.SecurityElement.Escape(exe)}</Command>
                        </Exec>
                      </Actions>
                    </Task>
                    """;

                // Write XML to temp file and import via schtasks
                var tmp = System.IO.Path.GetTempFileName() + ".xml";
                System.IO.File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
                RunSchtasks($"/Create /F /TN \"{TaskName}\" /XML \"{tmp}\"");
                System.IO.File.Delete(tmp);
            }
            else
            {
                RunSchtasks($"/Delete /F /TN \"{TaskName}\"");
            }

            // Also remove old registry entry if present
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue("Networktool", false);
            }
            catch { }
        }
        catch { }
    }

    private static void RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
