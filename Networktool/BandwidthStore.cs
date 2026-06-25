// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.11
// License: Private

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Networktool;

public class BandwidthRecord
{
    public double GBDown  { get; set; }
    public double GBUp    { get; set; }
    public double GBTotal => GBDown + GBUp;
}

// Persisted envelope — wraps the record with the original SSID so we can
// round-trip correctly even if the filename was sanitised.
file class BwFile
{
    public string           Ssid   { get; set; } = "";
    public BandwidthRecord  Record { get; set; } = new();
}

// Tracks per-SSID bandwidth totals in memory (GB).
// Writes only the currently active SSID to disk — one tiny JSON file per SSID.
// Flush() writes the active SSID immediately; call it on network swap and on close.
public class BandwidthStore
{
    private static readonly string BwDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Networktool", "bw");

    private readonly Dictionary<string, BandwidthRecord> _data =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _activeSsid;

    public BandwidthStore() => Load();

    // Add bytes-per-second increments, converted to GB on the way in.
    public void Add(string ssid, long bytesDown, long bytesUp)
    {
        if (!_data.TryGetValue(ssid, out var rec))
        {
            rec = new BandwidthRecord();
            _data[ssid] = rec;
        }
        rec.GBDown += bytesDown / 1e9;
        rec.GBUp   += bytesUp   / 1e9;
        _activeSsid = ssid;
    }

    // Call when the connected network changes — flushes the old SSID first.
    public void OnNetworkChanged(string? newSsid)
    {
        if (_activeSsid != null && _activeSsid != newSsid)
            WriteOne(_activeSsid);
        _activeSsid = newSsid;
    }

    // Write only the active SSID to disk (60s timer target).
    public void Flush()
    {
        if (_activeSsid != null)
            WriteOne(_activeSsid);
    }

    public bool TryGet(string ssid, out BandwidthRecord rec) =>
        _data.TryGetValue(ssid, out rec!);

    public bool Contains(string ssid) => _data.ContainsKey(ssid);

    public void Clear(string ssid)
    {
        _data.Remove(ssid);
        try { File.Delete(FilePath(ssid)); } catch { }
    }

    // ── private ─────────────────────────────────────────────────────────────

    private void Load()
    {
        if (!Directory.Exists(BwDir)) return;
        foreach (var file in Directory.EnumerateFiles(BwDir, "*.json"))
        {
            try
            {
                var json    = File.ReadAllText(file);
                var envelope = JsonSerializer.Deserialize<BwFile>(json);
                if (envelope == null || string.IsNullOrEmpty(envelope.Ssid)) continue;
                _data[envelope.Ssid] = envelope.Record;
            }
            catch { }
        }
    }

    private void WriteOne(string ssid)
    {
        if (!_data.TryGetValue(ssid, out var rec)) return;
        try
        {
            Directory.CreateDirectory(BwDir);
            var envelope = new BwFile { Ssid = ssid, Record = rec };
            var json     = JsonSerializer.Serialize(envelope);
            File.WriteAllText(FilePath(ssid), json);
        }
        catch { }
    }

    private static string FilePath(string ssid) =>
        Path.Combine(BwDir, $"{Sanitise(ssid)}.json");

    private static string Sanitise(string ssid)
    {
        var clean = Regex.Replace(ssid, @"[^\w\-]", "_");
        return clean.Length > 0 ? clean : $"ssid_{Math.Abs(ssid.GetHashCode())}";
    }
}
