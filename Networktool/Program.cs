// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.08
// License: Private

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace Networktool;

static class Program
{
    private static readonly string CrashLog = Path.Combine(
        Path.GetTempPath(), $"Networktool_{DateTime.Now:yyyyMMdd}.log");

    private static void WriteCrash(string context, Exception ex)
    {
        try
        {
            var msg = $"[{DateTime.Now:HH:mm:ss.fff}] [CRASH] {context}: {ex}{Environment.NewLine}";
            File.AppendAllText(CrashLog, msg);
            MessageBox.Show($"Networktool crashed.\n\nError: {ex.Message}\n\nFull details in:\n{CrashLog}",
                "Crash", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }

    [STAThread]
    static void Main()
    {
        // Catch all unhandled exceptions — UI thread, background threads, and Tasks
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => WriteCrash("UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            WriteCrash("Background thread", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

        // Must run as administrator (wlanapi requires it)
        if (!IsAdmin())
        {
            MessageBox.Show(
                "Networktool requires administrator privileges.\n\nRight-click the exe and choose 'Run as administrator'.",
                "Admin Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Single-instance guard — if already running, bring it to front and exit.
        using var mutex = new Mutex(true, "Networktool_SingleInstance", out bool isNew);
        if (!isNew)
        {
            // Signal the existing instance to show itself via a named event
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Networktool_ShowWindow");
            showEvent.Set();
            return;
        }

        // Pin to the last physical core — away from core 0 where OS interrupts and game schedulers live.
        // Uses both HT siblings of the last core (2 LPs) so it can still burst across threads.
        try
        {
            int lp = Math.Min(Environment.ProcessorCount, 62); // clamp: 3L << 62 is safe; 63+ overflows
            long mask = lp >= 2 ? (3L << (lp - 2)) : 1L;
            Process.GetCurrentProcess().ProcessorAffinity = new IntPtr(mask);
        }
        catch { }

        ApplicationConfiguration.Initialize();

        // Listen for show-window signals from later launch attempts
        var showThread = new Thread(() =>
        {
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Networktool_ShowWindow");
            while (true)
            {
                showEvent.WaitOne();
                MainForm.Instance?.BeginInvoke(() => MainForm.Instance?.ShowFromTray());
            }
        }) { IsBackground = true };
        showThread.Start();

        Application.Run(new MainForm());
    }

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
