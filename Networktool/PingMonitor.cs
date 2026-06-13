// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.09
// License: Private

using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Networktool;

public class PingMonitor : IDisposable
{
    public event Action<bool, long>? StatusChanged;

    private readonly AppSettings _settings;
    private CancellationTokenSource _cts = new();
    private volatile int _failCount = 0;

    public bool IsOnline { get; private set; } = true;
    public long LastPingMs { get; private set; } = -1;
    public int FailCount => _failCount;

    public PingMonitor(AppSettings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => RunLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
    }

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await DoPingAsync();
            try { await Task.Delay(_settings.PingIntervalMs, ct); } catch { break; }
        }
    }

    private async Task DoPingAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(_settings.PingTarget, 1500);
            if (reply.Status == IPStatus.Success)
            {
                _failCount = 0;
                IsOnline = true;
                LastPingMs = reply.RoundtripTime;
            }
            else
            {
                HandleFail();
            }
        }
        catch
        {
            HandleFail();
        }

        StatusChanged?.Invoke(IsOnline, LastPingMs);
    }

    private void HandleFail()
    {
        _failCount++;
        LastPingMs = -1;
        int offlineAt = Math.Min(2, _settings.FailsBeforeSwap);
        if (_failCount == offlineAt)
        {
            IsOnline = false;
            SoundManager.PlayPingFail();
        }
        if (_failCount >= _settings.FailsBeforeSwap)
            IsOnline = false;
    }

    public bool ShouldSwap => _failCount >= _settings.FailsBeforeSwap;

    public void ResetFailCount()
    {
        _failCount = 0;
        IsOnline = true;
        LastPingMs = -1;
    }

    public void Dispose() => Stop();
}
