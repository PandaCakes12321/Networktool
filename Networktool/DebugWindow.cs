// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.10
// License: Private

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Networktool;

public class DebugWindow : Form
{
    private readonly RichTextBox _log;
    private static DebugWindow? _instance;

    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(),
        $"Networktool_{DateTime.Now:yyyyMMdd}.log");

    public static string CurrentLogPath => LogPath;

    private static void WriteToFile(string level, string msg)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {msg}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch { }
    }

    public static DebugWindow Instance => _instance ??= new DebugWindow();

    public DebugWindow()
    {
        _instance = this;
        Text = "Networktool Debug";
        Size = new Size(600, 400);
        BackColor = Color.FromArgb(10, 10, 10);
        ForeColor = Color.Lime;
        Font = new Font("Consolas", 8.5f);
        StartPosition = FormStartPosition.Manual;

        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 10, 10),
            ForeColor = Color.Lime,
            Font = new Font("Consolas", 8.5f),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            WordWrap = false
        };

        var clearBtn = new Button
        {
            Text = "Clear",
            Dock = DockStyle.Bottom,
            Height = 24,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gray,
            FlatStyle = FlatStyle.Flat
        };
        clearBtn.FlatAppearance.BorderSize = 0;
        clearBtn.Click += (s, e) => _log.Clear();

        Controls.Add(_log);
        Controls.Add(clearBtn);

        FormClosing += (s, e) => { e.Cancel = true; Hide(); };
    }

    public static void Log(string msg, Color? color = null)
    {
        var win = Instance;
        if (win.InvokeRequired) { win.Invoke(() => Log(msg, color)); return; }

        var c = color ?? Color.Lime;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        win._log.SelectionStart = win._log.TextLength;
        win._log.SelectionLength = 0;
        win._log.SelectionColor = Color.FromArgb(80, 80, 80);
        win._log.AppendText($"[{timestamp}] ");
        win._log.SelectionStart = win._log.TextLength;
        win._log.SelectionColor = c;
        win._log.AppendText(msg + "\n");
        win._log.ScrollToCaret();
    }

    public static void Info(string msg)  { Log(msg, Color.Lime); }
    public static void Warn(string msg)  { WriteToFile("WARN",  msg); Log(msg, Color.Yellow); }
    public static void Error(string msg) { WriteToFile("ERROR", msg); Log(msg, Color.OrangeRed); }
    public static void Data(string msg)  { Log(msg, Color.Cyan); }
}
