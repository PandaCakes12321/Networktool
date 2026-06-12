// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.08
// License: Private

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Networktool;

public class MainForm : Form
{
    public static MainForm? Instance { get; private set; }

    private AppSettings _settings;
    private PingMonitor _ping;
    private TrafficMonitor _traffic = null!;
    private System.Windows.Forms.Timer _refreshTimer = null!;
    private NotifyIcon _trayIcon = null!;
    private volatile bool _isSwapping = false;
    private volatile bool _isScanning = false;
    private long _lastKnownPingMs = 0;

    // drag support
    private bool _dragging;
    private Point _dragStart;

    // UI panels
    private Panel _titleBar = null!;
    private Label _statusLabel = null!;
    private Panel _statusDot = null!;
    private Label _pingLabel = null!;
    private Label _failCountLabel = null!;
    private Label _pingTargetLabel = null!;
    private Label _trafficDownLabel = null!;
    private Label _trafficUpLabel = null!;
    private Label _trafficPktsLabel = null!;
    private ScrollingGraphPanel _trafficGraph = null!;
    private ScrollingGraphPanel _pingGraph = null!;
    private FlowLayoutPanel _networkPanel = null!;

    private List<WifiNetwork> _networks = new();

    public MainForm()
    {
        Instance = this;
        _settings = AppSettings.Load();
        _ping = new PingMonitor(_settings);
        BuildUI();
        SetupTray();
        ApplySettings();

        _ping.StatusChanged += OnPingStatusChanged;
        _ping.Start();

        _traffic = new TrafficMonitor();
        _traffic.Updated += OnTrafficUpdated;
        _traffic.Start();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += async (s, e) => await RefreshNetworksAsync();
        _refreshTimer.Start();

        Load += async (s, e) =>
        {
            DebugWindow.Info($"[Startup] Networktool started. Log: {DebugWindow.CurrentLogPath}");
            await RefreshNetworksAsync();
        };
        LocationChanged += (s, e) => SaveWindowBounds();
        SizeChanged += (s, e) => SaveWindowBounds();
    }

    private void BuildUI()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(_settings.ColourBackground);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        MinimumSize = new Size(180, 150);
        Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
        Location = ClampToScreen(new Point(_settings.WindowX, _settings.WindowY), Size);
        TopMost = _settings.AlwaysOnTop;

        // thin border via paint
        Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(60, 60, 60), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        // Title bar
        _titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = Color.FromArgb(_settings.ColourTitleBar),
            Cursor = Cursors.SizeAll
        };
        _titleBar.MouseDown += Drag_MouseDown;
        _titleBar.MouseMove += Drag_MouseMove;
        _titleBar.MouseUp += Drag_MouseUp;

        var titleLabel = new Label
        {
            Text = "NETWORKTOOL",
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 0, 0, 0),
            BackColor = Color.Transparent
        };
        titleLabel.MouseDown += Drag_MouseDown;
        titleLabel.MouseMove += Drag_MouseMove;
        titleLabel.MouseUp += Drag_MouseUp;

        var settingsBtn = new Button
        {
            Text = "⚙",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.Gray,
            Size = new Size(26, 28),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 11f)
        };
        settingsBtn.FlatAppearance.BorderSize = 0;
        settingsBtn.Click += OpenSettings;
        settingsBtn.Dock = DockStyle.Right;

        // Auto-swap toggle button
        var swapBtn = new Button
        {
            Text = "⇄",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(30, 28),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Dock = DockStyle.Right
        };
        swapBtn.FlatAppearance.BorderSize = 0;
        UpdateSwapBtn(swapBtn);
        swapBtn.Click += (s, e) =>
        {
            _settings.AutoSwap = !_settings.AutoSwap;
            _settings.Save();
            UpdateSwapBtn(swapBtn);
        };

        var closeBtn = new Button
        {
            Text = "✕",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(140, 140, 140),
            Size = new Size(26, 28),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9f),
            Dock = DockStyle.Right
        };
        closeBtn.FlatAppearance.BorderSize = 0;
        closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 40, 40);
        closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Color.White;
        closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Color.FromArgb(140, 140, 140);
        closeBtn.Click += (s, e) => Hide();

        _titleBar.Controls.Add(titleLabel);
        _titleBar.Controls.Add(closeBtn);
        _titleBar.Controls.Add(settingsBtn);
        _titleBar.Controls.Add(swapBtn);

        // Status row
        var statusRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Color.FromArgb(20, 20, 20),
            Padding = new Padding(8, 0, 8, 0)
        };

        _statusDot = new Panel
        {
            Size = new Size(14, 14),
            BackColor = Color.FromArgb(200, 50, 50),
            Location = new Point(8, 9)
        };
        _statusDot.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(new SolidBrush(_statusDot.BackColor), 0, 0, 13, 13);
        };
        _statusDot.BackColorChanged += (s, e) => _statusDot.Invalidate();

        _statusLabel = new Label
        {
            Text = "CHECKING...",
            ForeColor = Color.Silver,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(28, 0),
            Size = new Size(70, 32),
            BackColor = Color.Transparent
        };

        _failCountLabel = new Label
        {
            Text = "",
            ForeColor = Color.FromArgb(220, 50, 50),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(98, 0),
            Size = new Size(90, 32),
            BackColor = Color.Transparent,
            Visible = false
        };

        _pingTargetLabel = new Label
        {
            Text = $"ping {_settings.PingTarget}",
            ForeColor = Color.FromArgb(90, 90, 90),
            Font = new Font("Segoe UI", 7.5f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        _pingLabel = new Label
        {
            Text = "-- ms",
            ForeColor = Color.FromArgb(120, 200, 120),
            Font = new Font("Segoe UI", 8f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Right,
            Width = 55,
            BackColor = Color.Transparent
        };

        statusRow.Controls.Add(_statusDot);
        statusRow.Controls.Add(_statusLabel);
        statusRow.Controls.Add(_failCountLabel);
        statusRow.Controls.Add(_pingTargetLabel);
        statusRow.Controls.Add(_pingLabel);

        // Traffic row
        var trafficRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = 22,
            BackColor = Color.FromArgb(18, 18, 18),
            Padding = new Padding(8, 0, 8, 0)
        };

        _trafficDownLabel = new Label
        {
            Text = "↓ 0.00 Mb/s",
            ForeColor = Color.FromArgb(80, 180, 255),
            Font = new Font("Segoe UI", 7.5f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(8, 0),
            Size = new Size(88, 22),
            BackColor = Color.Transparent
        };
        _trafficUpLabel = new Label
        {
            Text = "↑ 0.00 Mb/s",
            ForeColor = Color.FromArgb(120, 220, 120),
            Font = new Font("Segoe UI", 7.5f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(96, 0),
            Size = new Size(88, 22),
            BackColor = Color.Transparent
        };
        _trafficPktsLabel = new Label
        {
            Text = "0 / 0 pkt/s",
            ForeColor = Color.FromArgb(110, 110, 110),
            Font = new Font("Segoe UI", 7f),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Right,
            Width = 90,
            BackColor = Color.Transparent
        };

        trafficRow.Controls.Add(_trafficDownLabel);
        trafficRow.Controls.Add(_trafficUpLabel);
        trafficRow.Controls.Add(_trafficPktsLabel);

        // Separator
        var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(40, 40, 40) };

        // Traffic scrolling graph (download=blue, upload=pink overlay)
        _trafficGraph = new ScrollingGraphPanel(
            colorA:    Color.FromArgb(_settings.ColourGraphDl),
            colorB:    Color.FromArgb(_settings.ColourGraphUl),
            dual:      true,
            scaleLevels: ScrollingGraphPanel.TrafficScaleLevels,
            formatter: ScrollingGraphPanel.FormatBytes)
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.FromArgb(12, 12, 12)
        };

        // Ping scrolling graph
        _pingGraph = new ScrollingGraphPanel(
            colorA:    Color.FromArgb(_settings.ColourGraphPing),
            colorB:    Color.FromArgb(0, 0, 0),
            dual:      false,
            scaleLevels: ScrollingGraphPanel.PingScaleLevels,
            formatter: v => $"{v}ms")
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Color.FromArgb(12, 12, 12)
        };

        // Networks header
        var netHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 20,
            BackColor = Color.FromArgb(25, 25, 25)
        };
        var netHeaderLabel = new Label
        {
            Text = "AVAILABLE NETWORKS",
            ForeColor = Color.FromArgb(80, 80, 80),
            Font = new Font("Segoe UI", 7f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = Color.Transparent
        };
        netHeader.Controls.Add(netHeaderLabel);

        // Networks scroll area — clip panel hides scrollbar entirely
        var networkClip = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 15, 15)
        };
        _networkPanel = new DoubleBufferedFlowPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoScroll = false,
            WrapContents = false,
            BackColor = Color.FromArgb(15, 15, 15),
            Padding = new Padding(4, 4, 4, 4),
            Location = new Point(0, 0)
        };
        networkClip.Controls.Add(_networkPanel);
        networkClip.Resize += (s, e) =>
        {
            _networkPanel.Width = networkClip.ClientSize.Width;
            ResizeNetworkButtons();
            ClampScroll();
        };
        networkClip.MouseWheel += (s, e) =>
        {
            _scrollOffset = Math.Max(0, _scrollOffset - e.Delta / 3);
            ClampScroll();
        };

        // Resize grip
        var grip = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 8,
            BackColor = Color.FromArgb(22, 22, 22),
            Cursor = Cursors.SizeNWSE
        };
        grip.Paint += (s, e) =>
        {
            e.Graphics.DrawLine(new Pen(Color.FromArgb(60, 60, 60)), grip.Width - 8, grip.Height - 2, grip.Width - 2, grip.Height - 8);
            e.Graphics.DrawLine(new Pen(Color.FromArgb(60, 60, 60)), grip.Width - 5, grip.Height - 2, grip.Width - 2, grip.Height - 5);
        };
        grip.MouseDown += ResizeGrip_MouseDown;

        Controls.Add(networkClip);
        Controls.Add(netHeader);
        Controls.Add(_pingGraph);
        Controls.Add(_trafficGraph);
        Controls.Add(sep);
        Controls.Add(trafficRow);
        Controls.Add(statusRow);
        Controls.Add(_titleBar);
        Controls.Add(grip);

        ResizeRedraw = true;
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(path)) return new Icon(path);
        }
        catch { }
        return null;
    }

    private void SetupTray()
    {
        var appIcon = LoadAppIcon();
        if (appIcon != null) Icon = appIcon;

        _trayIcon = new NotifyIcon
        {
            Text = "Networktool",
            Icon = appIcon ?? SystemIcons.Application,
            Visible = true
        };

        var trayMenu = new ContextMenuStrip();
        trayMenu.BackColor = Color.FromArgb(25, 25, 25);
        trayMenu.ForeColor = Color.White;
        trayMenu.Renderer = new DarkMenuRenderer();

        var showItem = new ToolStripMenuItem("Show");
        showItem.Click += (s, e) => ShowWindow();

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += OpenSettings;

        var logItem = new ToolStripMenuItem("Open Log File");
        logItem.Click += (s, e) =>
        {
            try { Process.Start(new ProcessStartInfo(DebugWindow.CurrentLogPath) { UseShellExecute = true }); }
            catch { }
        };

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => ExitApp();

        trayMenu.Items.AddRange(new ToolStripItem[] { showItem, settingsItem, logItem, new ToolStripSeparator(), exitItem });
        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.Click += (s, e) => ShowWindow();
        _trayIcon.DoubleClick += (s, e) => ShowWindow();
    }

    private void ApplySettings()
    {
        TopMost = _settings.AlwaysOnTop;
        Opacity = Math.Clamp(_settings.Opacity, 20, 100) / 100.0;
        _pingTargetLabel.Text = $"ping {_settings.PingTarget}";
        BackColor = Color.FromArgb(_settings.ColourBackground);
        _titleBar.BackColor = Color.FromArgb(_settings.ColourTitleBar);
        _trafficGraph.SetColours(Color.FromArgb(_settings.ColourGraphDl), Color.FromArgb(_settings.ColourGraphUl));
        _pingGraph.SetColours(Color.FromArgb(_settings.ColourGraphPing), Color.FromArgb(0, 0, 0));
        NetworkButton.ShowSignalBars = _settings.ShowSignalBars;
        foreach (Control c in _networkPanel.Controls)
            if (c is NetworkButton nb) nb.Invalidate();
        _trafficGraph.Visible = _settings.ShowSpeedGraph;
        _pingGraph.Visible    = _settings.ShowPingGraph;
    }

    private void UpdateSwapBtn(Button btn)
    {
        bool on = _settings.AutoSwap;
        btn.BackColor = on ? Color.FromArgb(0, 100, 50) : Color.FromArgb(50, 50, 50);
        btn.ForeColor = on ? Color.FromArgb(80, 255, 140) : Color.FromArgb(120, 120, 120);
        btn.FlatAppearance.MouseOverBackColor = on ? Color.FromArgb(0, 120, 60) : Color.FromArgb(65, 65, 65);
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        _refreshTimer.Start();   // resume scan on show
    }

    public void ShowFromTray() => ShowWindow();

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // Pause the network scan timer while hidden — saves CPU and netsh calls
        if (!Visible) _refreshTimer.Stop();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(1500, "Networktool", "Running in tray. Right-click to exit.", ToolTipIcon.Info);
            return;
        }
        SaveWindowBounds();
        _refreshTimer.Dispose();
        _ping.Dispose();
        _traffic.Dispose();
        _trayIcon.Dispose();
        base.OnFormClosing(e);
    }

    private void ExitApp()
    {
        SaveWindowBounds();
        _refreshTimer.Dispose();
        _ping.Dispose();
        _traffic.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    private void SaveWindowBounds()
    {
        _settings.WindowX = Location.X;
        _settings.WindowY = Location.Y;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.Save();
    }

    // Ensure the window lands on a visible screen — handles removed monitors / off-screen positions.
    private static Point ClampToScreen(Point pos, Size size)
    {
        var target = new System.Drawing.Rectangle(pos, size);
        foreach (var screen in Screen.AllScreens)
            if (screen.WorkingArea.IntersectsWith(target))
                return pos; // already visible, use as-is

        // Not visible on any screen — move to top-left of primary screen with a small margin
        var primary = Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        return new Point(
            Math.Max(primary.Left, Math.Min(pos.X, primary.Right  - size.Width)),
            Math.Max(primary.Top,  Math.Min(pos.Y, primary.Bottom - size.Height))
        );
    }

    private void OnPingStatusChanged(bool online, long ms)
    {
        if (InvokeRequired) { Invoke(() => OnPingStatusChanged(online, ms)); return; }

        var onlineCol  = Color.FromArgb(_settings.ColourOnline);
        var offlineCol = Color.FromArgb(_settings.ColourOffline);
        _statusDot.BackColor  = online ? onlineCol : offlineCol;
        _statusLabel.Text     = online ? "ONLINE" : "OFFLINE";
        _statusLabel.ForeColor = online ? onlineCol : offlineCol;
        _pingLabel.Text = ms >= 0 ? $"{ms} ms" : "-- ms";
        _pingLabel.ForeColor = ms < 0 ? Color.Gray : ms < 50 ? Color.FromArgb(80, 220, 80) : ms < 150 ? Color.Yellow : Color.OrangeRed;
        if (ms >= 0)
        {
            bool firstPing = _lastKnownPingMs == 0;
            _lastKnownPingMs = ms;
            if (firstPing) _pingGraph.Prefill(ms);  // fill blank startup slots
            _pingGraph.Push(ms);
        }
        else
        {
            // Timeout IS a real latency value (1500ms = ping timeout threshold)
            long pushVal = _lastKnownPingMs > 0 ? 1500 : 0;
            _pingGraph.Push(pushVal);
        }

        // Tint ping graph line based on latency
        _pingGraph.SetPrimaryColor(ms < 0    ? Color.FromArgb(80, 80, 80)
                                 : ms < 50  ? Color.FromArgb(80, 200, 120)
                                 : ms < 150 ? Color.FromArgb(220, 200, 60)
                                            : Color.FromArgb(220, 80, 60));

        int fails = _ping.FailCount;
        if (fails > 0)
        {
            _failCountLabel.Text = fails == 1 ? "1 PING FAIL" : $"{fails} PING FAILS";
            _failCountLabel.Visible = true;
        }
        else
        {
            _failCountLabel.Visible = false;
        }

        if (_ping.ShouldSwap && _settings.AutoSwap && !_isSwapping)
            _ = TryAutoSwapAsync();
    }

    private void OnTrafficUpdated(TrafficStats stats)
    {
        if (InvokeRequired) { Invoke(() => OnTrafficUpdated(stats)); return; }

        _trafficGraph.Push(stats.DownBytesPerSec, stats.UpBytesPerSec);

        bool bits = _settings.TrafficShowBits;
        _trafficDownLabel.Text = $"↓ {FormatSpeed(stats.DownBytesPerSec, bits)}";
        _trafficUpLabel.Text   = $"↑ {FormatSpeed(stats.UpBytesPerSec,   bits)}";
        _trafficPktsLabel.Text = $"{FormatPkts(stats.DownPkts)} / {FormatPkts(stats.UpPkts)} pkt/s";

        // colour brightness scales with speed (threshold in bytes/sec)
        _trafficDownLabel.ForeColor = stats.DownBytesPerSec > 5_000_000  ? Color.FromArgb(100, 200, 255)
                                    : stats.DownBytesPerSec > 1_000_000  ? Color.FromArgb(80,  180, 255)
                                                                          : Color.FromArgb(60,  130, 200);
        _trafficUpLabel.ForeColor   = stats.UpBytesPerSec   > 2_000_000  ? Color.FromArgb(80,  240, 100)
                                    : stats.UpBytesPerSec   > 500_000    ? Color.FromArgb(120, 220, 120)
                                                                          : Color.FromArgb(80,  160, 80);
    }

    // Auto-scaling speed formatter.
    // bits=true  → bps · Kbps · Mbps · Gbps   (ISP-style)
    // bits=false → B/s · KB/s · MB/s · GB/s   (file-transfer style)
    private static string FormatSpeed(long bytesPerSec, bool bits)
    {
        double val   = bits ? bytesPerSec * 8.0 : bytesPerSec;
        double kilo  = bits ? 1_000.0           : 1_024.0;
        string[] units = bits
            ? new[] { "bps", "Kbps", "Mbps", "Gbps" }
            : new[] { "B/s", "KB/s", "MB/s", "GB/s" };

        int tier = 0;
        while (val >= kilo && tier < units.Length - 1) { val /= kilo; tier++; }

        string num = val >= 100 ? $"{val:F0}"
                   : val >= 10  ? $"{val:F1}"
                                : $"{val:F2}";
        return $"{num} {units[tier]}";
    }

    private static string FormatPkts(long pkts)
    {
        if (pkts >= 1000) return $"{pkts / 1000.0:F1}k";
        return pkts.ToString();
    }

    private async Task TryAutoSwapAsync()
    {
        _isSwapping = true;
        try
        {
            var currentSsid = await WifiManager.GetConnectedSsidAsync();
            DebugWindow.Info($"[AutoSwap] triggered. Current SSID: '{currentSsid}'");

            // Build set of SSIDs currently visible in the live scan
            var visibleSsids = new HashSet<string>(_networks.Select(n => n.SSID), StringComparer.OrdinalIgnoreCase);

            // Priority order: honour the user's saved list, but only for networks that are
            // actually visible right now, saved, and not the one that just failed.
            var candidates = _settings.AutoSwapOrder
                .Where(n => !_settings.IsHidden(n)
                         && !string.Equals(n, currentSsid, StringComparison.OrdinalIgnoreCase)
                         && visibleSsids.Contains(n))
                .ToList();
            DebugWindow.Info($"[AutoSwap] priority candidates (visible): [{string.Join(", ", candidates)}]");

            if (!candidates.Any())
            {
                // Fallback: saved networks with internet, best signal first; captive/local-only as last resort
                var fallback = _networks.Where(n => n.IsSaved && !n.IsConnected && !_settings.IsHidden(n.SSID)).ToList();
                candidates = fallback
                    .Where(n => n.HasInternet)
                    .OrderByDescending(n => n.SignalPercent)
                    .Select(n => n.SSID)
                    .ToList();
                if (!candidates.Any())
                    candidates = fallback.OrderByDescending(n => n.SignalPercent).Select(n => n.SSID).ToList();
                DebugWindow.Info($"[AutoSwap] fallback candidates (signal): [{string.Join(", ", candidates)}]");
            }

            if (!candidates.Any())
            {
                DebugWindow.Warn("[AutoSwap] no candidates found — nothing to swap to");
                return;
            }

            foreach (var ssid in candidates)
            {
                DebugWindow.Info($"[AutoSwap] trying: '{ssid}'");
                var ok = await WifiManager.ConnectAsync(ssid);
                DebugWindow.Info($"[AutoSwap] ConnectAsync('{ssid}') => {ok}");
                if (ok)
                {
                    await Task.Delay(3000);
                    _ping.ResetFailCount();
                    if (InvokeRequired) Invoke(() => _failCountLabel.Visible = false);
                    else _failCountLabel.Visible = false;
                    SoundManager.PlaySwapSuccess();
                    DebugWindow.Info($"[AutoSwap] swapped to '{ssid}' successfully");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.Error($"[AutoSwap] exception: {ex.Message}");
        }
        finally
        {
            _isSwapping = false;
        }
    }

    private async Task RefreshNetworksAsync()
    {
        if (_isScanning) return;
        _isScanning = true;
        try { await RefreshNetworksInternalAsync(); } finally { _isScanning = false; }
    }

    private async Task RefreshNetworksInternalAsync()
    {
        var result = await WifiManager.GetNetworksAsync();
        if (result.Error != null) DebugWindow.Warn($"[Scan] {result.Error}");
        else DebugWindow.Info($"[Scan] {result.Networks.Count} networks found" + (result.LocationDenied ? " (location denied)" : ""));
        var nets = result.Networks;
        if (InvokeRequired) { Invoke(() => UpdateNetworkUI(nets, result.LocationDenied)); return; }
        UpdateNetworkUI(nets, result.LocationDenied);
    }

    private int _scrollOffset = 0;

    private void ClampScroll()
    {
        var clip = _networkPanel.Parent;
        if (clip == null) return;
        int contentH = _networkPanel.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical) + 8;
        _networkPanel.Height = Math.Max(contentH, clip.ClientSize.Height);
        int maxScroll = Math.Max(0, contentH - clip.ClientSize.Height);
        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, maxScroll));
        _networkPanel.Top = -_scrollOffset;
    }

    private int NetworkButtonWidth => Math.Max(80, (_networkPanel.Parent?.ClientSize.Width ?? _networkPanel.Width) - 8);

    private void ResizeNetworkButtons()
    {
        foreach (Control c in _networkPanel.Controls)
            if (c is NetworkButton nb)
                nb.Width = NetworkButtonWidth;
    }

    private void UpdateNetworkUI(List<WifiNetwork> nets, bool locationDenied = false)
    {
        _networks = nets;
        var width = NetworkButtonWidth;
        var visible = nets.Where(n => !_settings.IsHidden(n.SSID, n.BSSID)).ToList();

        _networkPanel.SuspendLayout();

        if (locationDenied)
        {
            _networkPanel.Controls.Clear();
            _networkPanel.Controls.Add(new Label
            {
                Text = "⚠ Location Services required.\nEnable in Windows Settings\n→ Privacy & Security → Location",
                ForeColor = Color.Orange, BackColor = Color.Transparent,
                AutoSize = false, Width = width, Height = 60,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8f)
            });
            _networkPanel.ResumeLayout(true);
            ClampScroll();
            return;
        }

        // Update in-place: reuse existing buttons, only add/remove what changed
        // Key = "SSID|BSSID" to handle multiple APs with the same SSID
        static string NetKey(WifiNetwork n) => $"{n.SSID}|{n.BSSID}";
        var existing = _networkPanel.Controls.OfType<NetworkButton>()
            .ToDictionary(b => NetKey(b.CurrentNetwork));

        var toKeep = new HashSet<string>(visible.Select(NetKey));

        // Remove buttons no longer needed
        foreach (var key in existing.Keys.Where(k => !toKeep.Contains(k)).ToList())
            _networkPanel.Controls.Remove(existing[key]);

        // Add or update
        for (int i = 0; i < visible.Count; i++)
        {
            var net = visible[i];
            if (existing.TryGetValue(NetKey(net), out var btn))
            {
                btn.Update(net, width);
            }
            else
            {
                btn = new NetworkButton(net, width);
                btn.LeftClicked += async (ssid) =>
                {
                    // Read live state from btn at click time — not the captured creation-time net
                    var current = btn.CurrentNetwork;
                    if (!current.IsConnected)
                    {
                        if (!current.IsSaved) await ConnectWithPasswordAsync(current);
                        else { await WifiManager.ConnectAsync(ssid); await Task.Delay(2000); await RefreshNetworksAsync(); }
                    }
                };
                btn.RightClicked += (ssid, screenPt) => ShowNetworkMenu(btn.CurrentNetwork, screenPt);
                _networkPanel.Controls.Add(btn);
            }
            // Ensure correct z-order position
            _networkPanel.Controls.SetChildIndex(btn, i);
        }

        _networkPanel.ResumeLayout(false);
        _networkPanel.PerformLayout();
        ClampScroll();
    }

    private void ShowNetworkMenu(WifiNetwork net, Point screenPt)
    {
        var menu = new ContextMenuStrip();
        menu.BackColor = Color.FromArgb(28, 28, 28);
        menu.ForeColor = Color.White;
        menu.Renderer = new DarkMenuRenderer();

        if (!net.IsConnected)
        {
            var connectItem = new ToolStripMenuItem(net.IsSaved ? "Connect" : "Connect (enter password)");
            connectItem.Click += async (s, e) =>
            {
                if (!net.IsSaved)
                    await ConnectWithPasswordAsync(net);
                else
                {
                    await WifiManager.ConnectAsync(net.SSID);
                    await Task.Delay(2000);
                    await RefreshNetworksAsync();
                }
            };
            menu.Items.Add(connectItem);
            menu.Items.Add(new ToolStripSeparator());
        }

        // Hide this AP only (by BSSID) — or hide all APs with same SSID
        if (!string.IsNullOrEmpty(net.BSSID))
        {
            var hideApItem = new ToolStripMenuItem($"Hide this AP  ({net.BSSID})");
            hideApItem.Click += (s, e) =>
            {
                if (!_settings.HiddenBSSIDs.Contains(net.BSSID))
                    _settings.HiddenBSSIDs.Add(net.BSSID);
                _settings.Save();
                UpdateNetworkUI(_networks);
            };
            menu.Items.Add(hideApItem);
        }

        var hideAllItem = new ToolStripMenuItem($"Hide all \"{net.SSID}\"");
        hideAllItem.Click += (s, e) =>
        {
            if (!_settings.IsHidden(net.SSID))
                _settings.HiddenNetworks.Add(net.SSID);
            _settings.Save();
            UpdateNetworkUI(_networks);
        };
        menu.Items.Add(hideAllItem);
        menu.Show(screenPt);
    }

    private async Task ConnectWithPasswordAsync(WifiNetwork net)
    {
        using var dlg = new PasswordDialog(net.SSID);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var ok = await WifiManager.ConnectWithPasswordAsync(net.SSID, dlg.Password);
        await Task.Delay(ok ? 2000 : 500);
        await RefreshNetworksAsync();
        if (!ok) MessageBox.Show("Could not connect. Check the password and try again.",
            "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OpenSettings(object? s, EventArgs e)
    {
        var allSsids = _networks.Select(n => n.SSID).ToList();
        var prevOpacity = _settings.Opacity;
        using var form = new SettingsForm(_settings, allSsids,
            onOpacityChanged: val => Opacity = Math.Clamp(val, 20, 100) / 100.0);
        var result = form.ShowDialog(this);
        if (result != DialogResult.OK)
        {
            // restore opacity if cancelled
            Opacity = Math.Clamp(prevOpacity, 20, 100) / 100.0;
        }
        ApplySettings();
        if (result == DialogResult.OK)
            _ = RefreshNetworksAsync();  // pick up hidden/priority changes immediately
        else
            UpdateNetworkUI(_networks);
        foreach (Control c in Controls)
            if (c is Panel p && p.Tag is Label lbl)
                lbl.Text = $"Ping: {_settings.PingTarget}";
    }

    // --- Drag ---
    private void Drag_MouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; }
    }
    private void Drag_MouseMove(object? s, MouseEventArgs e)
    {
        if (_dragging) Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
    }
    private void Drag_MouseUp(object? s, MouseEventArgs e) { _dragging = false; }

    // --- Resize grip ---
    private Point _resizeStart;
    private Size _resizeStartSize;
    private void ResizeGrip_MouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _resizeStart = Cursor.Position;
            _resizeStartSize = Size;
            ((Control)s!).Capture = true;
            ((Control)s!).MouseMove += ResizeGrip_MouseMove;
            ((Control)s!).MouseUp += ResizeGrip_MouseUp;
        }
    }
    private void ResizeGrip_MouseMove(object? s, MouseEventArgs e)
    {
        var cur = Cursor.Position;
        var dx = cur.X - _resizeStart.X;
        var dy = cur.Y - _resizeStart.Y;
        Size = new Size(Math.Max(MinimumSize.Width,  _resizeStartSize.Width  + dx),
                        Math.Max(MinimumSize.Height, _resizeStartSize.Height + dy));
    }
    private void ResizeGrip_MouseUp(object? s, MouseEventArgs e)
    {
        ((Control)s!).Capture = false;
        ((Control)s!).MouseMove -= ResizeGrip_MouseMove;
        ((Control)s!).MouseUp -= ResizeGrip_MouseUp;
    }
}

// --- Network button ---
public class NetworkButton : Control
{
    internal static bool ShowSignalBars = true;

    private WifiNetwork _net;
    public event Action<string>? LeftClicked;
    public event Action<string, Point>? RightClicked;
    private bool _hover;

    public string NetworkSSID => _net.SSID;
    public WifiNetwork CurrentNetwork => _net;

    public NetworkButton(WifiNetwork net, int width)
    {
        _net = net;
        Size = new Size(width, 40);
        Margin = new Padding(0, 1, 0, 1);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }

    public void Update(WifiNetwork net, int width)
    {
        _net = net;
        if (Width != width) Width = width;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) LeftClicked?.Invoke(_net.SSID);
        else if (e.Button == MouseButtons.Right) RightClicked?.Invoke(_net.SSID, PointToScreen(e.Location));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color bg;
        if (_net.IsConnected)
            bg = _hover ? Color.FromArgb(0, 90, 45) : Color.FromArgb(0, 70, 35);
        else
            bg = _hover ? Color.FromArgb(45, 45, 45) : Color.FromArgb(30, 30, 30);

        using (var brush = new SolidBrush(bg))
            g.FillRoundedRectangle(brush, new Rectangle(0, 0, Width - 1, Height - 1), 4);

        using (var pen = new Pen(_net.IsConnected ? Color.FromArgb(0, 160, 80) : Color.FromArgb(55, 55, 55)))
            g.DrawRoundedRectangle(pen, new Rectangle(0, 0, Width - 1, Height - 1), 4);

        // Signal bars — always anchored to right edge
        const int barsWidth = 22; // 4 bars * 4px + 6px padding
        const int barsRightPad = 5;
        int barsX = ShowSignalBars ? Width - barsWidth - barsRightPad : Width - barsRightPad;
        if (ShowSignalBars) DrawSignal(g, barsX, 7, _net.SignalPercent);

        // Internet dot — small circle left of SSID
        const int dotSize = 6;
        const int dotLeft = 6;
        int dotY = (Height - dotSize) / 2;
        var dotColor = _net.HasInternet ? Color.FromArgb(60, 200, 80) : Color.FromArgb(200, 60, 60);
        using (var dotBrush = new SolidBrush(dotColor))
            g.FillEllipse(dotBrush, dotLeft, dotY, dotSize, dotSize);

        // SSID — shifted right to make room for dot
        const int textLeft = dotLeft + dotSize + 4;
        int textRight = barsX - 4;
        var textColor = _net.IsConnected ? Color.FromArgb(80, 220, 120) : Color.FromArgb(200, 200, 200);
        using var font = new Font("Segoe UI", 8.5f, _net.IsConnected ? FontStyle.Bold : FontStyle.Regular);
        using var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

        // Measure SSID width to place the saved tag right after it
        var ssidSize = g.MeasureString(_net.SSID, font);
        float ssidDrawWidth = Math.Min(ssidSize.Width, textRight - textLeft);
        g.DrawString(_net.SSID, font, new SolidBrush(textColor), new RectangleF(textLeft, 0, textRight - textLeft, Height), sf);

        // Saved tag
        if (_net.IsSaved && !_net.IsConnected)
        {
            using var tagFont = new Font("Segoe UI", 6.5f);
            float tagX = textLeft + ssidDrawWidth + 3;
            if (tagX + 36 < barsX - 2)
            {
                var tagRect = new RectangleF(tagX, Height / 2f - 7, 36, 13);
                using var tagBrush = new SolidBrush(Color.FromArgb(0, 80, 40));
                g.FillRectangle(tagBrush, tagRect);
                using var tagSf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("saved", tagFont, new SolidBrush(Color.FromArgb(60, 180, 100)), tagRect, tagSf);
            }
        }

        // BSSID — small grey text at the bottom of the button
        if (!string.IsNullOrEmpty(_net.BSSID))
        {
            using var bssidFont = new Font("Consolas", 6f);
            using var bssidBr   = new SolidBrush(Color.FromArgb(70, 70, 70));
            using var bssidSf   = new StringFormat { LineAlignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(_net.BSSID, bssidFont, bssidBr,
                new RectangleF(textLeft, 0, barsX - textLeft - 2, Height - 2), bssidSf);
        }
    }

    private static void DrawSignal(Graphics g, int x, int y, int pct)
    {
        int bars = pct >= 75 ? 4 : pct >= 50 ? 3 : pct >= 25 ? 2 : pct > 0 ? 1 : 0;
        for (int i = 0; i < 4; i++)
        {
            int h = 4 + i * 3;
            var rect = new Rectangle(x + i * 4, y + (12 - h), 3, h);
            var col = i < bars ? Color.FromArgb(80, 200, 80) : Color.FromArgb(50, 50, 50);
            g.FillRectangle(new SolidBrush(col), rect);
        }
    }
}

// --- Continuously scrolling EKG-style filled line graph ---
// Positioning is purely time-based: each sample stores its arrival DateTime.
// The animation timer just triggers redraws — no scroll state to reset on Push().
public class ScrollingGraphPanel : Control
{
    private const int   Slots      = 90;
    private const float WindowSecs = 90f;   // seconds of history shown across full width

    private readonly long[]     _a         = new long[Slots];
    private readonly long[]     _b         = new long[Slots];
    private readonly DateTime[] _pushTimes = new DateTime[Slots];
    private readonly bool       _dual;
    private int _head = 0;

    private readonly long[]            _scaleLevels;
    private readonly Func<long,string> _fmt;
    private int  _scaleIdx = 0;
    private long _scale;

    private Color _colorA, _colorB;
    public void SetPrimaryColor(Color c) { _colorA = c; }
    public void SetColours(Color a, Color b) { _colorA = a; _colorB = b; }


    private readonly System.Windows.Forms.Timer _animTimer;
    private const int LabelW = 36;

    // ---- Scale tables ----
    public static readonly long[] TrafficScaleLevels =
    {
        10_000, 50_000, 100_000, 250_000, 500_000,
        1_000_000, 2_500_000, 5_000_000, 10_000_000,
        25_000_000, 50_000_000, 100_000_000, 500_000_000, 1_000_000_000
    };
    public static readonly long[] PingScaleLevels =
        { 25, 50, 100, 200, 500, 1000, 2000 };

    public static string FormatBytes(long bps)
    {
        if (bps >= 1_000_000_000) return $"{bps/1_000_000_000.0:F1}GB/s";
        if (bps >= 1_000_000)     return $"{bps/1_000_000.0:F1}MB/s";
        if (bps >= 1_000)         return $"{bps/1_000.0:F0}KB/s";
        return $"{bps}B/s";
    }

    public ScrollingGraphPanel(Color colorA, Color colorB, bool dual,
                               long[] scaleLevels, Func<long,string> formatter)
    {
        _colorA = colorA; _colorB = colorB; _dual = dual;
        _scaleLevels = scaleLevels; _fmt = formatter;
        _scale = scaleLevels[0];

        // Initialise all push times to far past so empty slots are off-screen left
        var farPast = DateTime.UtcNow.AddSeconds(-WindowSecs * 2);
        for (int i = 0; i < Slots; i++) _pushTimes[i] = farPast;

        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);

        // Timer just triggers redraws — all motion comes from real elapsed time.
        // 200ms (5fps) is sufficient: at typical scroll speed the graph moves <1px per frame.
        _animTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _animTimer.Tick += (s, e) =>
        {
            var f = FindForm();
            if (Visible && (f == null || f.WindowState != FormWindowState.Minimized))
                Invalidate();
        };
        _animTimer.Start();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) _animTimer.Start();
        else         _animTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animTimer.Dispose();
        base.Dispose(disposing);
    }

    // Pre-fill all slots spaced backwards in time so graph starts stable
    public void Prefill(long a, long b = 0)
    {
        var now = DateTime.UtcNow;
        float spacing = WindowSecs / Slots;
        for (int i = 0; i < Slots; i++)
        {
            _a[i] = a; _b[i] = b;
            _pushTimes[i] = now.AddSeconds(-(Slots - 1 - i) * spacing);
        }
        _head = 0;
        RecalcScale();
    }

    // No scroll state — just stamp the arrival time
    public void Push(long a, long b = 0)
    {
        _a[_head] = a;
        _b[_head] = b;
        _pushTimes[_head] = DateTime.UtcNow;
        _head = (_head + 1) % Slots;
        RecalcScale();
    }

    private void RecalcScale()
    {
        long max = 1;
        foreach (var v in _a) if (v > max) max = v;
        if (_dual) foreach (var v in _b) if (v > max) max = v;

        while (_scaleIdx < _scaleLevels.Length - 1 && max > _scaleLevels[_scaleIdx] * 75 / 100)
            _scaleIdx++;
        while (_scaleIdx > 0 && max < _scaleLevels[_scaleIdx - 1] * 35 / 100)
            _scaleIdx--;

        _scale = _scaleLevels[_scaleIdx];
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int h  = ClientSize.Height;
        int gx = LabelW;
        int gw = Math.Max(2, ClientSize.Width - LabelW);

        using var font  = new Font("Segoe UI", 6f);
        using var lblBr = new SolidBrush(Color.FromArgb(70, 70, 70));
        g.DrawString(_fmt(_scale), font, lblBr, 2, 2);

        using var gridPen = new Pen(Color.FromArgb(28, 28, 28));
        g.DrawLine(gridPen, gx, h / 2, gx + gw, h / 2);

        g.SetClip(new Rectangle(gx, 0, gw, h));

        if (_dual) DrawSeries(g, gx, gw, h, _b, _colorB, 120, 1.2f);
        DrawSeries(g, gx, gw, h, _a, _colorA, 85, 1.5f);

        // Peak-hold line — only slots visible in the current window
        long windowPeak = 1;
        var now2 = DateTime.UtcNow;
        for (int i = 0; i < Slots; i++)
        {
            if (_pushTimes[i] == default) continue;
            if ((now2 - _pushTimes[i]).TotalSeconds > WindowSecs) continue;
            if (_a[i] > windowPeak) windowPeak = _a[i];
            if (_dual && _b[i] > windowPeak) windowPeak = _b[i];
        }

        if (windowPeak > 1 && _scale > 0)
        {
            float peakY = (h - 2) * (1f - Math.Min(1f, (float)windowPeak / _scale)) + 1;
            using var peakPen = new Pen(Color.FromArgb(190, 210, 60, 60), 1f);
            peakPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            g.DrawLine(peakPen, gx, peakY, gx + gw, peakY);
            g.ResetClip();
            using var peakFont = new Font("Segoe UI", 6f);
            using var peakBr   = new SolidBrush(Color.FromArgb(190, 210, 80, 80));
            string peakTxt = _fmt(windowPeak);
            var sz = g.MeasureString(peakTxt, peakFont);
            float ly = Math.Max(0, Math.Min(peakY - sz.Height / 2, h - sz.Height));
            g.DrawString(peakTxt, peakFont, peakBr, 0, ly);
        }
        else { g.ResetClip(); }
    }

    private void DrawSeries(Graphics g, int gx, int gw, int gh,
                             long[] data, Color col, int fillAlpha, float lineW)
    {
        if (_scale == 0) return;

        float pxPerSec = gw / WindowSecs;
        var   now      = DateTime.UtcNow;

        // Collect visible slots oldest→newest.
        // Retain one point just off the left edge so GDI clips the line smoothly.
        var pts = new List<PointF>(Slots + 2);
        PointF? lastOffLeft = null;
        for (int i = 0; i < Slots; i++)
        {
            int   slot    = (_head - Slots + i + Slots * 2) % Slots;
            float secsAgo = (float)(now - _pushTimes[slot]).TotalSeconds;
            float x       = gx + gw - secsAgo * pxPerSec;
            float ratio   = Math.Min(1f, (float)data[slot] / _scale);
            var   pt      = new PointF(x, (gh - 2) * (1f - ratio) + 1);
            if (x < gx) { lastOffLeft = pt; continue; }
            if (lastOffLeft.HasValue) { pts.Add(lastOffLeft.Value); lastOffLeft = null; }
            pts.Add(pt);
        }

        if (pts.Count < 2) return;

        // Flat phantom anchored to right wall
        pts.Add(new PointF(gx + gw + 4, pts[pts.Count - 1].Y));

        var poly = new PointF[pts.Count + 2];
        poly[0] = new PointF(pts[0].X, gh);
        for (int i = 0; i < pts.Count; i++) poly[i + 1] = pts[i];
        poly[pts.Count + 1] = new PointF(pts[pts.Count - 1].X, gh);

        using var br  = new SolidBrush(Color.FromArgb(fillAlpha, col));
        using var pen = new Pen(col, lineW);
        g.FillPolygon(br, poly);
        g.DrawLines(pen, pts.ToArray());
    }
}

// --- Double-buffered panel to prevent flicker ---
public class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
}

public class DoubleBufferedFlowPanel : FlowLayoutPanel
{
    public DoubleBufferedFlowPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
}

// --- Dark menu renderer ---
public class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }
}

public class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => Color.FromArgb(50, 50, 50);
    public override Color MenuItemBorder => Color.FromArgb(70, 70, 70);
    public override Color MenuBorder => Color.FromArgb(60, 60, 60);
    public override Color ToolStripDropDownBackground => Color.FromArgb(25, 25, 25);
    public override Color ImageMarginGradientBegin => Color.FromArgb(25, 25, 25);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(25, 25, 25);
    public override Color ImageMarginGradientEnd => Color.FromArgb(25, 25, 25);
}

// --- Graphics extensions ---
public static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = GetRoundedPath(rect, radius);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect, int radius)
    {
        using var path = GetRoundedPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static System.Drawing.Drawing2D.GraphicsPath GetRoundedPath(Rectangle rect, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
