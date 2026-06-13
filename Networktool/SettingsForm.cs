// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.09
// License: Private

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Networktool;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly List<string> _allNetworks;

    private TextBox _pingTargetBox = null!;
    private TrackBar _opacitySlider = null!;
    private CheckBox _alwaysOnTopBox = null!;
    private CheckBox _startWithWindowsBox = null!;
    private CheckBox _autoSwapBox = null!;
    private CheckBox _trafficBitsBox = null!;
    private CheckBox _showSignalBarsBox = null!;
    private CheckBox _showSpeedGraphBox = null!;
    private CheckBox _showPingGraphBox  = null!;
    private NumericUpDown _failsSpinner = null!;
    private NumericUpDown _pingIntervalSpinner = null!;
    private CheckedListBox _hiddenList = null!;
    private ListBox _priorityList = null!;

    // Colour swatches — key matches settings property name
    private readonly Dictionary<string, Color> _colours = new();

    private readonly Action<int>? _onOpacityChanged;

    public SettingsForm(AppSettings settings, List<string> allNetworks, Action<int>? onOpacityChanged = null)
    {
        _settings = settings;
        _allNetworks = allNetworks;
        _onOpacityChanged = onOpacityChanged;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Networktool Settings";
        BackColor = Color.FromArgb(20, 20, 20);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(440, 545);

        // Tab bar — manual implementation to avoid ugly default tabs
        var tabBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Color.FromArgb(30, 30, 30)
        };

        var pages = new Panel[]
        {
            BuildGeneralPage(),
            BuildColoursPage(),
            BuildNetworksPage(),
            BuildPriorityPage()
        };

        string[] tabNames = { "General", "Colours", "Hidden Networks", "Swap Priority" };
        int[] tabWidths   = {       90,        80,               120,              100  };
        int tabCount = tabNames.Length;
        var tabBtns = new Button[tabCount];

        for (int i = 0; i < tabCount; i++)
        {
            int idx = i;
            pages[i].Dock = DockStyle.Fill;
            pages[i].Visible = i == 0;

            int left = 0;
            for (int k = 0; k < i; k++) left += tabWidths[k];

            tabBtns[i] = new Button
            {
                Text = tabNames[i],
                FlatStyle = FlatStyle.Flat,
                Height = 32,
                Width = tabWidths[i],
                Left = left,
                Top = 0,
                BackColor = i == 0 ? Color.FromArgb(20, 20, 20) : Color.FromArgb(35, 35, 35),
                ForeColor = i == 0 ? Color.White : Color.Gray,
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
            tabBtns[i].FlatAppearance.BorderSize = 0;
            tabBtns[i].FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 40, 40);
            tabBtns[i].Click += (s, e) =>
            {
                for (int j = 0; j < tabCount; j++)
                {
                    pages[j].Visible = j == idx;
                    tabBtns[j].BackColor = j == idx ? Color.FromArgb(20, 20, 20) : Color.FromArgb(35, 35, 35);
                    tabBtns[j].ForeColor = j == idx ? Color.White : Color.Gray;
                }
            };
            tabBar.Controls.Add(tabBtns[i]);
        }

        // Content area
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            Padding = new Padding(12)
        };
        foreach (var p in pages) content.Controls.Add(p);

        // Bottom buttons
        var bottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = Color.FromArgb(25, 25, 25)
        };
        var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(45, 45, 45) };

        var btnSave = MakeBtn("Save", Color.FromArgb(0, 130, 60));
        var btnCancel = MakeBtn("Cancel", Color.FromArgb(55, 55, 55));
        btnSave.Bounds = new Rectangle(220, 9, 90, 28);
        btnCancel.Bounds = new Rectangle(320, 9, 90, 28);
        btnSave.DialogResult = DialogResult.OK;
        btnCancel.DialogResult = DialogResult.Cancel;
        btnSave.Click += (s, e) => SaveSettings();

        bottomBar.Controls.AddRange(new Control[] { sep, btnSave, btnCancel });

        Controls.Add(content);
        Controls.Add(tabBar);
        Controls.Add(bottomBar);
    }

    private Panel BuildGeneralPage()
    {
        var page = new Panel { BackColor = Color.FromArgb(20, 20, 20) };
        int y = 12;

        page.Controls.Add(Lbl("Ping target (IP or hostname):", 0, y)); y += 22;
        _pingTargetBox = new TextBox
        {
            Text = _settings.PingTarget,
            Bounds = new Rectangle(0, y, 220, 26),
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        page.Controls.Add(_pingTargetBox); y += 38;

        page.Controls.Add(Lbl("Consecutive fails before auto-swap:", 0, y)); y += 22;
        _failsSpinner = new NumericUpDown
        {
            Minimum = 1, Maximum = 20,
            Value = _settings.FailsBeforeSwap,
            Bounds = new Rectangle(0, y, 70, 26),
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White
        };
        page.Controls.Add(_failsSpinner); y += 38;

        page.Controls.Add(Lbl("Ping interval (ms):", 0, y)); y += 22;
        _pingIntervalSpinner = new NumericUpDown
        {
            Minimum = 500, Maximum = 30000, Increment = 500,
            Value = Math.Clamp(_settings.PingIntervalMs, 500, 30000),
            Bounds = new Rectangle(0, y, 90, 26),
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White
        };
        page.Controls.Add(_pingIntervalSpinner); y += 44;

        _alwaysOnTopBox = Chk("Always on top", 0, y, _settings.AlwaysOnTop); y += 30;
        _startWithWindowsBox = Chk("Start with Windows", 0, y, _settings.StartWithWindows); y += 30;
        _autoSwapBox = Chk("Auto-swap network when offline", 0, y, _settings.AutoSwap); y += 30;
        _trafficBitsBox = Chk("Speed in bits/s  (Mb/s)   —  uncheck for bytes/s  (MB/s)", 0, y, _settings.TrafficShowBits); y += 30;
        _showSignalBarsBox = Chk("Show signal strength bars", 0, y, _settings.ShowSignalBars); y += 30;
        _showSpeedGraphBox = Chk("Show speed graph  (download / upload)", 0, y, _settings.ShowSpeedGraph); y += 30;
        _showPingGraphBox  = Chk("Show ping graph", 0, y, _settings.ShowPingGraph); y += 38;

        var opacityLbl = Lbl($"Transparency:  {_settings.Opacity}%", 0, y); y += 22;
        _opacitySlider = new TrackBar
        {
            Minimum = 20, Maximum = 100, Value = _settings.Opacity,
            Bounds = new Rectangle(0, y, 340, 36),
            TickFrequency = 10, SmallChange = 5, LargeChange = 10,
            BackColor = Color.FromArgb(20, 20, 20)
        };
        _opacitySlider.ValueChanged += (s, e) =>
        {
            opacityLbl.Text = $"Transparency:  {_opacitySlider.Value}%";
            _onOpacityChanged?.Invoke(_opacitySlider.Value);
        };

        page.Controls.AddRange(new Control[] { _alwaysOnTopBox, _startWithWindowsBox, _autoSwapBox, _trafficBitsBox, _showSignalBarsBox, _showSpeedGraphBox, _showPingGraphBox, opacityLbl, _opacitySlider });
        return page;
    }

    private Panel BuildColoursPage()
    {
        var page = new Panel { BackColor = Color.FromArgb(20, 20, 20) };
        int y = 12;

        void AddSwatch(string label, string key, int argb)
        {
            _colours[key] = Color.FromArgb(argb);
            page.Controls.Add(Lbl(label, 0, y));

            var swatch = new Button
            {
                Bounds = new Rectangle(200, y - 2, 50, 22),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(argb),
                Cursor = Cursors.Hand,
                Tag = key
            };
            swatch.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            swatch.FlatAppearance.BorderSize = 1;
            swatch.Click += (s, e) =>
            {
                using var dlg = new ColorDialog
                {
                    Color = swatch.BackColor,
                    FullOpen = true,
                    AnyColor = true
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    swatch.BackColor = dlg.Color;
                    _colours[key] = dlg.Color;
                }
            };
            page.Controls.Add(swatch);
            y += 30;
        }

        AddSwatch("Online colour",         "ColourOnline",     _settings.ColourOnline);
        AddSwatch("Offline colour",        "ColourOffline",    _settings.ColourOffline);
        AddSwatch("Title bar colour",      "ColourTitleBar",   _settings.ColourTitleBar);
        AddSwatch("Background colour",     "ColourBackground", _settings.ColourBackground);
        AddSwatch("Graph — download",      "ColourGraphDl",    _settings.ColourGraphDl);
        AddSwatch("Graph — upload",        "ColourGraphUl",    _settings.ColourGraphUl);
        AddSwatch("Graph — ping",          "ColourGraphPing",  _settings.ColourGraphPing);

        page.Controls.Add(Lbl("Click a swatch to change colour.", 0, y + 6));
        return page;
    }

    private Panel BuildNetworksPage()
    {
        var page = new Panel { BackColor = Color.FromArgb(20, 20, 20) };
        page.Controls.Add(Lbl("Checked = hidden.  Uncheck to unhide.", 0, 0));

        _hiddenList = new CheckedListBox
        {
            Bounds = new Rectangle(0, 24, 390, 340),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            CheckOnClick = true
        };

        // SSIDs from current scan
        foreach (var net in _allNetworks)
            _hiddenList.Items.Add(net, _settings.IsHidden(net));

        // Hidden BSSIDs not already shown via SSID — shown as "BSSID: AA:BB:..."
        foreach (var bssid in _settings.HiddenBSSIDs)
            _hiddenList.Items.Add($"BSSID: {bssid}", true);

        // Any hidden SSIDs that aren't in the current scan (keep them visible so user can unhide)
        foreach (var ssid in _settings.HiddenNetworks)
            if (!_allNetworks.Contains(ssid, StringComparer.OrdinalIgnoreCase))
                _hiddenList.Items.Add(ssid, true);

        page.Controls.Add(_hiddenList);
        return page;
    }

    private Panel BuildPriorityPage()
    {
        var page = new Panel { BackColor = Color.FromArgb(20, 20, 20) };
        page.Controls.Add(Lbl("Auto-swap tries top entries first:", 0, 0));

        _priorityList = new ListBox
        {
            Bounds = new Rectangle(0, 24, 290, 320),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var visible = _allNetworks.Where(x => !_settings.IsHidden(x)).ToList();
        var ordered = _settings.AutoSwapOrder.Where(x => visible.Contains(x)).ToList();
        var rest = visible.Where(x => !ordered.Contains(x)).ToList();
        foreach (var n in ordered.Concat(rest))
            _priorityList.Items.Add(n);

        var upBtn = MakeBtn("▲  Up", Color.FromArgb(45, 45, 45));
        var dnBtn = MakeBtn("▼  Down", Color.FromArgb(45, 45, 45));
        upBtn.Bounds = new Rectangle(298, 24, 90, 30);
        dnBtn.Bounds = new Rectangle(298, 60, 90, 30);
        upBtn.Click += (s, e) => MoveItem(-1);
        dnBtn.Click += (s, e) => MoveItem(1);

        page.Controls.AddRange(new Control[] { _priorityList, upBtn, dnBtn });
        return page;
    }

    private void MoveItem(int dir)
    {
        int i = _priorityList.SelectedIndex;
        if (i < 0) return;
        int j = i + dir;
        if (j < 0 || j >= _priorityList.Items.Count) return;
        var item = _priorityList.Items[i];
        _priorityList.Items.RemoveAt(i);
        _priorityList.Items.Insert(j, item);
        _priorityList.SelectedIndex = j;
    }

    private void SaveSettings()
    {
        _settings.PingTarget = _pingTargetBox.Text.Trim();
        _settings.FailsBeforeSwap = (int)_failsSpinner.Value;
        _settings.PingIntervalMs = (int)_pingIntervalSpinner.Value;
        _settings.AlwaysOnTop = _alwaysOnTopBox.Checked;
        _settings.StartWithWindows = _startWithWindowsBox.Checked;
        _settings.AutoSwap = _autoSwapBox.Checked;
        _settings.TrafficShowBits = _trafficBitsBox.Checked;
        _settings.ShowSignalBars  = _showSignalBarsBox.Checked;
        _settings.ShowSpeedGraph  = _showSpeedGraphBox.Checked;
        _settings.ShowPingGraph   = _showPingGraphBox.Checked;
        _settings.Opacity = _opacitySlider.Value;

        // Colours
        if (_colours.TryGetValue("ColourOnline",     out var c)) _settings.ColourOnline     = c.ToArgb();
        if (_colours.TryGetValue("ColourOffline",    out c))     _settings.ColourOffline    = c.ToArgb();
        if (_colours.TryGetValue("ColourTitleBar",   out c))     _settings.ColourTitleBar   = c.ToArgb();
        if (_colours.TryGetValue("ColourBackground", out c))     _settings.ColourBackground = c.ToArgb();
        if (_colours.TryGetValue("ColourGraphDl",    out c))     _settings.ColourGraphDl    = c.ToArgb();
        if (_colours.TryGetValue("ColourGraphUl",    out c))     _settings.ColourGraphUl    = c.ToArgb();
        if (_colours.TryGetValue("ColourGraphPing",  out c))     _settings.ColourGraphPing  = c.ToArgb();

        _settings.HiddenNetworks.Clear();
        _settings.HiddenBSSIDs.Clear();
        for (int i = 0; i < _hiddenList.Items.Count; i++)
        {
            if (!_hiddenList.GetItemChecked(i)) continue;
            var item = _hiddenList.Items[i].ToString()!;
            if (item.StartsWith("BSSID: "))
                _settings.HiddenBSSIDs.Add(item.Substring(7));
            else
                _settings.HiddenNetworks.Add(item);
        }

        _settings.AutoSwapOrder = _priorityList.Items.Cast<object>().Select(x => x.ToString()!).ToList();

        StartupHelper.SetStartup(_settings.StartWithWindows);
        _settings.Save();
    }

    private static Button MakeBtn(string text, Color bg)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
        return b;
    }

    private static Label Lbl(string text, int x, int y) =>
        new Label { Text = text, AutoSize = true, Location = new Point(x, y), ForeColor = Color.Silver, BackColor = Color.Transparent };

    private static CheckBox Chk(string text, int x, int y, bool val) =>
        new CheckBox { Text = text, Location = new Point(x, y), Checked = val, AutoSize = true, ForeColor = Color.White, BackColor = Color.Transparent };
}
