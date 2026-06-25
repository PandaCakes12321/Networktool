<div align="center">
  <img src="Networktool/app_preview.png" alt="Networktool" width="96"/>

  # Networktool

  **Floating network monitor widget for Windows & Linux**

  *by Teffers — v1.11*

  ![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20%7C%20Linux-blue)
  ![.NET](https://img.shields.io/badge/.NET-9.0-purple)
  ![Python](https://img.shields.io/badge/Python-3.12-green)
  ![Version](https://img.shields.io/badge/version-1.11-brightgreen)
</div>

---

A lightweight always-on-top widget that sits on your desktop and keeps an eye on your network — ping latency, upload/download speed, available WiFi networks, and auto-swap between APs when your connection drops.

## Features

- **Live ping monitor** — tracks latency to a configurable target (default `8.8.8.8`) with a scrolling graph and peak line
- **Live traffic graphs** — real-time download/upload speed with scrolling EKG-style graphs (Mbps or MB/s)
- **Bandwidth totals** — tracks cumulative download, upload, and total data used per SSID across sessions, shown on each network button — right-click to clear
- **Auto network swap** — detects connection failures and automatically switches to the best available WiFi AP based on your priority list, with audio cues on fail and on successful swap
- **Per-BSSID filtering** — hide individual access points by MAC address, not just by SSID — handy for mesh networks
- **Internet connectivity indicator** — green/red dot on each network shows whether it advertises internet access
- **Signal strength bars** — visual signal quality indicator on each network button
- **Fully customisable** — colours, opacity, ping interval, graph visibility, signal bars, auto-swap priority order
- **System tray** — minimises to tray on close, single-click to restore, single instance
- **Start with Windows** — optional autostart via Task Scheduler
- **Low resource usage** — ~0.5–2% CPU idle (brief spike on startup), ~30–40 MB private memory (~90–100 MB working set), pinned to the last CPU core to avoid interfering with OS and game workloads

## Requirements

### Windows
- Windows 10 or Windows 11
- [.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- Must be run as **Administrator** (required for WiFi API access)

### Linux
- Linux with GTK3 and NetworkManager
- Python 3.12+ with dependencies: `PyGObject`, `dbus-python`, `psutil`, `gstreamer`
- See `LINUXV/networktool/` for source

## Usage

### Windows
1. Run `Networktool.exe` as **Administrator**
2. The widget appears on your screen — drag it anywhere
3. Click the **⚙** cog to open Settings
4. Click **⇄** to toggle auto-swap on/off
5. Right-click any network to hide it by SSID, by specific AP (BSSID), or to clear its bandwidth data
6. Close button (✕) hides to tray — single-click the tray icon to restore

### Linux
1. Run `python3 LINUXV/main.py` (may need `sudo` for NetworkManager access)
2. Same controls as the Windows version

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

### Windows
```
dotnet build Networktool/Networktool/Networktool.csproj -c Release
```
Requires .NET 9 SDK and Windows.

### Linux
No build step — pure Python. Just run:
```
python3 LINUXV/main.py
```
Requires Python 3.12+, PyGObject, dbus-python, psutil, and GStreamer.

---

<div align="center">
  Made by <b>Teffers</b>
</div>
