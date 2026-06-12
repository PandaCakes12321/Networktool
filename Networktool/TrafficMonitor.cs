// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.08
// License: Private

using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Networktool;

public class TrafficStats
{
    public long DownBytesPerSec { get; init; }
    public long UpBytesPerSec   { get; init; }
    public long DownPkts        { get; init; }  // packets/sec
    public long UpPkts          { get; init; }
}

public class TrafficMonitor : IDisposable
{
    public event Action<TrafficStats>? Updated;

    private CancellationTokenSource _cts = new();
    private long _prevBytesIn, _prevBytesOut;
    private long _prevPktsIn,  _prevPktsOut;
    private NetworkInterface? _cachedNic;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        SampleOnce(); // initialise baseline
        Task.Run(() => RunLoop(_cts.Token));
    }

    public void Stop() => _cts.Cancel();

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch { break; }
            SampleOnce();
        }
    }

    // Pick the single best physical NIC to read from.
    // Priority: WiFi → Ethernet → anything Up that isn't loopback/tunnel/virtual.
    private static NetworkInterface? PickNic()
    {
        var all = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .Where(n => !n.Description.Contains("Virtual",    StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Description.Contains("Hyper-V",    StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Description.Contains("VMware",     StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Description.Contains("TAP",        StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Description.Contains("Pseudo",     StringComparison.OrdinalIgnoreCase))
            .ToList();

        return all.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            ?? all.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            ?? all.FirstOrDefault();
    }

    private void SampleOnce()
    {
        try
        {
            long bytesIn = 0, bytesOut = 0, pktsIn = 0, pktsOut = 0;

            // Re-pick only if cached NIC has gone down
            if (_cachedNic == null || _cachedNic.OperationalStatus != OperationalStatus.Up)
                _cachedNic = PickNic();
            var nic = _cachedNic;
            if (nic != null)
            {
                var s = nic.GetIPStatistics();
                bytesIn  = s.BytesReceived;
                bytesOut = s.BytesSent;
                pktsIn   = s.UnicastPacketsReceived + s.NonUnicastPacketsReceived;
                pktsOut  = s.UnicastPacketsSent     + s.NonUnicastPacketsSent;
            }

            if (_prevBytesIn == 0 && _prevBytesOut == 0)
            {
                // first sample — just store baseline, emit zeros
                _prevBytesIn  = bytesIn;
                _prevBytesOut = bytesOut;
                _prevPktsIn   = pktsIn;
                _prevPktsOut  = pktsOut;
                Updated?.Invoke(new TrafficStats());
                return;
            }

            long dIn   = Math.Max(0, bytesIn  - _prevBytesIn);
            long dOut  = Math.Max(0, bytesOut - _prevBytesOut);
            long dpIn  = Math.Max(0, pktsIn   - _prevPktsIn);
            long dpOut = Math.Max(0, pktsOut  - _prevPktsOut);

            _prevBytesIn  = bytesIn;
            _prevBytesOut = bytesOut;
            _prevPktsIn   = pktsIn;
            _prevPktsOut  = pktsOut;

            Updated?.Invoke(new TrafficStats
            {
                DownBytesPerSec = dIn,
                UpBytesPerSec   = dOut,
                DownPkts        = dpIn,
                UpPkts          = dpOut
            });
        }
        catch { }
    }

    public void Dispose() => Stop();
}
