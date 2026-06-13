<div align="center">
  <img src="Networktool/app_preview.png" alt="Networktool" width="96"/>

  # Networktool

  **Floating network monitor widget for Windows**

  *by Teffers — v1.10*

  ![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
  ![.NET](https://img.shields.io/badge/.NET-9.0-purple)
  ![Version](https://img.shields.io/badge/version-1.10-brightgreen)
</div>

---

A lightweight always-on-top widget that sits on your desktop and keeps an eye on your network — ping latency, upload/download speed, available WiFi networks, and auto-swap between APs when your connection drops.

## Features

- **Live ping monitor** — tracks latency to a configurable target (default `8.8.8.8`) with a scrolling graph and peak line
- **Live traffic graphs** — real-time download/upload speed with scrolling EKG-style graphs (Mbps or MB/s)
- **Auto network swap** — detects connection failures and automatically switches to the best available WiFi AP based on your priority list
- **Per-BSSID filtering** — hide individual access points by MAC address, not just by SSID — handy for mesh networks
- **Internet connectivity indicator** — green/red dot on each network shows whether it advertises internet access
- **Signal strength bars** — visual signal quality indicator on each network button
- **Fully customisable** — colours, opacity, ping interval, graph visibility, signal bars, auto-swap priority order
- **System tray** — minimises to tray on close, single-click to restore, single instance
- **Start with Windows** — optional autostart via Task Scheduler
- **Low resource usage** — ~0.5% CPU idle, ~30 MB private memory (~90 MB working set), pinned to the last CPU core to avoid interfering with OS and game workloads

## Requirements

- Windows 10 or Windows 11
- [.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- Must be run as **Administrator** (required for WiFi API access)

## Usage

1. Run `Networktool.exe` as **Administrator**
2. The widget appears in the top-right of your screen — drag it anywhere
3. Click the **⚙** cog to open Settings
4. Right-click any network in the list to hide it by SSID or by specific AP (BSSID)
5. Close button (✕) hides to tray — single-click the tray icon to restore

## Settings

| Setting | Description |
|---|---|
| Ping target | IP or hostname to ping |
| Ping interval | How often to ping (minimum 500 ms) |
| Consecutive fails | How many failed pings before auto-swap triggers |
| Auto-swap | Enable/disable automatic network switching |
| Swap priority | Ordered list — top entry is preferred |
| Show speed graph | Toggle the download/upload graph |
| Show ping graph | Toggle the ping latency graph |
| Show signal bars | Toggle signal strength bars on network buttons |
| Transparency | Window opacity (10–100%) |
| Always on top | Keep widget above other windows |
| Start with Windows | Launch on login via Task Scheduler |

## Building from Source

```
dotnet build Networktool/Networktool/Networktool.csproj -c Release
```

Requires .NET 9 SDK and Windows.

---

<div align="center">
  Made by <b>Teffers</b>
</div>
