// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.10
// License: Private

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Networktool;

public class AppSettings
{
    public string PingTarget { get; set; } = "8.8.8.8";
    private int _pingIntervalMs = 2000;
    public int PingIntervalMs
    {
        get => _pingIntervalMs;
        set => _pingIntervalMs = Math.Max(500, value);
    }
    public int FailsBeforeSwap { get; set; } = 3;
    public bool AlwaysOnTop { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool AutoSwap { get; set; } = true;
    public List<string> HiddenNetworks { get; set; } = new();
    public List<string> HiddenBSSIDs   { get; set; } = new();
    public List<string> AutoSwapOrder { get; set; } = new();
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public int WindowWidth { get; set; } = 260;
    public int WindowHeight { get; set; } = 300;
    public int Opacity { get; set; } = 95;
    public bool TrafficShowBits  { get; set; } = true;  // true = bits (Mbps), false = bytes (MB/s)
    public bool ShowSignalBars   { get; set; } = true;
    public bool ShowSpeedGraph   { get; set; } = true;
    public bool ShowPingGraph    { get; set; } = true;

    // Colours — stored as ARGB ints so they serialise cleanly
    public int ColourOnline      { get; set; } = unchecked((int)0xFF32C850);  // green
    public int ColourOffline     { get; set; } = unchecked((int)0xFFC83232);  // red
    public int ColourTitleBar    { get; set; } = unchecked((int)0xFF191919);
    public int ColourBackground  { get; set; } = unchecked((int)0xFF0F0F0F);
    public int ColourGraphDl     { get; set; } = unchecked((int)0xFF3C8CDC);  // download blue
    public int ColourGraphUl     { get; set; } = unchecked((int)0xFFDC5096);  // upload pink
    public int ColourGraphPing   { get; set; } = unchecked((int)0xFF50C878);  // ping green

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Networktool", "settings.json");

    public bool IsHidden(string ssid, string bssid = "") =>
        HiddenNetworks.Any(h => string.Equals(h, ssid, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrEmpty(bssid) && HiddenBSSIDs.Any(h => string.Equals(h, bssid, StringComparison.OrdinalIgnoreCase)));

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
