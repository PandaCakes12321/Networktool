// Networktool — Floating network monitor widget for Windows
// Author : Teffers
// Version: 1.10
// License: Private

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Networktool;

public class WifiNetwork
{
    public string SSID { get; set; } = "";
    public string BSSID { get; set; } = "";   // MAC of the specific AP, e.g. "AA:BB:CC:DD:EE:FF"
    public int SignalPercent { get; set; }
    public bool IsConnected { get; set; }
    public bool IsSaved { get; set; }
    public bool HasInternet { get; set; }
}

public class WifiScanResult
{
    public List<WifiNetwork> Networks { get; set; } = new();
    public bool LocationDenied { get; set; }
    public string? Error { get; set; }
}

public static class WifiManager
{
    #region wlanapi P/Invoke

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint dwFlags, IntPtr pReserved, out IntPtr ppAvailableNetworkList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanScan(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pDot11Ssid, IntPtr pIeData, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanConnect(IntPtr hClientHandle, ref Guid pInterfaceGuid, ref WLAN_CONNECTION_PARAMETERS pConnectionParameters, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, WLAN_INTF_OPCODE OpCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);


    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_INTERFACE_INFO_LIST
    {
        public uint dwNumberOfItems;
        public uint dwIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strInterfaceDescription;
        public int isState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_AVAILABLE_NETWORK_LIST
    {
        public uint dwNumberOfItems;
        public uint dwIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_AVAILABLE_NETWORK
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;       // WCHAR[256] = 512 bytes
        public uint uSSIDLength;            // 4
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ucSSID;               // 32
        public uint dot11BssType;           // 4
        public uint uNumberOfBssids;        // 4
        public bool bNetworkConnectable;    // 4
        public uint wlanNotConnectableReason; // 4
        public uint uNumberOfPhyTypes;      // 4
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] dot11PhyTypes;        // 32
        public bool bMorePhyTypes;          // 4
        public uint wlanSignalQuality;      // 4
        public bool bSecurityEnabled;       // 4
        public uint dot11DefaultAuthAlgorithm;   // 4
        public uint dot11DefaultCipherAlgorithm; // 4
        public uint dwFlags;                // 4
        public uint dwReserved;             // 4
        // total = 512+4+32+4+4+4+4+4+32+4+4+4+4+4+4+4 = 628 bytes

        public string GetSSID() =>
            uSSIDLength > 0 && uSSIDLength <= 32
                ? Encoding.UTF8.GetString(ucSSID, 0, (int)uSSIDLength)
                : "";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_CONNECTION_PARAMETERS
    {
        public uint wlanConnectionMode;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? strProfile;
        public IntPtr pDot11Ssid;
        public IntPtr pDesiredBssidList;
        public uint dot11BssType;
        public uint dwFlags;
    }

    private enum WLAN_INTF_OPCODE
    {
        wlan_intf_opcode_current_connection = 7
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_CONNECTION_ATTRIBUTES
    {
        public uint isState;
        public uint wlanConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;
        public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
        public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_ASSOCIATION_ATTRIBUTES
    {
        public uint uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ucSSID;
        public uint dot11BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] dot11Bssid;
        public uint dot11PhyType;
        public uint uDot11PhyIndex;
        public uint wlanSignalQuality;
        public uint ulRxRate;
        public uint ulTxRate;

        public string GetSSID() =>
            uSSIDLength > 0 && uSSIDLength <= 32
                ? Encoding.UTF8.GetString(ucSSID, 0, (int)uSSIDLength)
                : "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_SECURITY_ATTRIBUTES
    {
        public bool bSecurityEnabled;
        public bool bOneXEnabled;
        public uint dot11AuthAlgorithm;
        public uint dot11CipherAlgorithm;
    }

    #endregion

    public static async Task<WifiScanResult> GetNetworksAsync()
    {
        return await Task.Run(() =>
        {
            var scanResult = new WifiScanResult();
            try
            {
                uint ret = WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
                if (ret != 0) { scanResult.Error = $"WlanOpenHandle failed: {ret}"; return scanResult; }

                var ifListPtr = IntPtr.Zero;
                try
                {
                    WlanEnumInterfaces(handle, IntPtr.Zero, out ifListPtr);
                    var ifCount = (int)Marshal.ReadInt32(ifListPtr);
                    int ifInfoSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();

                    for (int i = 0; i < ifCount; i++)
                    {
                        var ifInfoPtr = new IntPtr(ifListPtr.ToInt64() + 8 + i * ifInfoSize);
                        var ifInfo = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ifInfoPtr);
                        var guid = ifInfo.InterfaceGuid;

                        WlanScan(handle, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                        // WlanScan is async — give the driver a moment to populate fresh results
                        Thread.Sleep(200);

                        var connectedSsid = GetConnectedSsidFromHandle(handle, ref guid);
                        var savedProfiles = GetSavedProfilesNative(handle, ref guid);

                        uint getRet = WlanGetAvailableNetworkList(handle, ref guid, 3, IntPtr.Zero, out var netListPtr);
                        if (getRet == 5) { scanResult.LocationDenied = true; break; }
                        if (getRet != 0) { scanResult.Error = $"WlanGetAvailableNetworkList: {getRet}"; continue; }

                        // Build SSID→(connectable, signalQuality) map from the network list
                        var connectableMap = new Dictionary<string, (bool connectable, int signal)>(StringComparer.OrdinalIgnoreCase);
                        try
                        {
                            int count = (int)Marshal.ReadInt32(netListPtr);
                            int netSize = Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>();
                            for (int j = 0; j < count; j++)
                            {
                                var netPtr = new IntPtr(netListPtr.ToInt64() + 8 + j * netSize);
                                var net = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(netPtr);
                                var ssid = net.GetSSID().Trim();
                                if (string.IsNullOrWhiteSpace(ssid)) continue;
                                if (connectableMap.TryGetValue(ssid, out var existing))
                                    connectableMap[ssid] = (existing.connectable || net.bNetworkConnectable, Math.Max(existing.signal, (int)net.wlanSignalQuality));
                                else
                                    connectableMap[ssid] = (net.bNetworkConnectable, (int)net.wlanSignalQuality);
                            }
                        }
                        finally { WlanFreeMemory(netListPtr); }

                        var bssidRows = GetBssidMapFromNetsh();
                        var seenBssid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var (ssid, bssid, signal) in bssidRows)
                        {
                            if (!seenBssid.Add(bssid)) continue;
                            if (!connectableMap.TryGetValue(ssid, out var info)) continue;
                            scanResult.Networks.Add(new WifiNetwork
                            {
                                SSID          = ssid,
                                BSSID         = bssid,
                                SignalPercent  = signal,
                                IsConnected   = string.Equals(ssid, connectedSsid, StringComparison.OrdinalIgnoreCase),
                                IsSaved       = savedProfiles.Contains(ssid, StringComparer.OrdinalIgnoreCase),
                                HasInternet   = info.connectable
                            });
                        }

                        // Any SSIDs in connectableMap with no BSSID rows → use wlanapi signal as fallback
                        var coveredSsids = new HashSet<string>(scanResult.Networks.Select(n => n.SSID), StringComparer.OrdinalIgnoreCase);
                        foreach (var (ssid, info) in connectableMap)
                        {
                            if (coveredSsids.Contains(ssid)) continue;
                            scanResult.Networks.Add(new WifiNetwork
                            {
                                SSID         = ssid,
                                BSSID        = "",
                                SignalPercent = info.signal,
                                IsConnected  = string.Equals(ssid, connectedSsid, StringComparison.OrdinalIgnoreCase),
                                IsSaved      = savedProfiles.Contains(ssid, StringComparer.OrdinalIgnoreCase),
                                HasInternet  = info.connectable
                            });
                        }
                    }

                    if (ifListPtr != IntPtr.Zero) WlanFreeMemory(ifListPtr);
                }
                catch { if (ifListPtr != IntPtr.Zero) WlanFreeMemory(ifListPtr); throw; }
                finally { WlanCloseHandle(handle, IntPtr.Zero); }
            }
            catch (Exception ex) { scanResult.Error = ex.Message; }

            // If connected network wasn't in scan results, inject it at top
            var connectedSsidFinal = scanResult.Networks.FirstOrDefault(n => n.IsConnected)?.SSID;
            if (connectedSsidFinal == null)
            {
                uint h2ret = WlanOpenHandle(2, IntPtr.Zero, out _, out var h2);
                if (h2ret == 0 && h2 != IntPtr.Zero)
                {
                    IntPtr il2 = IntPtr.Zero;
                    try
                    {
                        WlanEnumInterfaces(h2, IntPtr.Zero, out il2);
                        int ifCount2 = Marshal.ReadInt32(il2);
                        if (ifCount2 < 1) { WlanFreeMemory(il2); il2 = IntPtr.Zero; return scanResult; }
                        var ig2Ptr = new IntPtr(il2.ToInt64() + 8);
                        var ig2Info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ig2Ptr);
                        var ig2 = ig2Info.InterfaceGuid;
                        WlanFreeMemory(il2);
                        il2 = IntPtr.Zero;
                        var csid = GetConnectedSsidFromHandle(h2, ref ig2);
                        if (!string.IsNullOrWhiteSpace(csid))
                            scanResult.Networks.Insert(0, new WifiNetwork { SSID = csid, SignalPercent = 100, IsConnected = true, IsSaved = true, HasInternet = true });
                    }
                    catch { }
                    finally
                    {
                        if (il2 != IntPtr.Zero) WlanFreeMemory(il2);
                        WlanCloseHandle(h2, IntPtr.Zero);
                    }
                }
            }

            scanResult.Networks = scanResult.Networks
                .OrderByDescending(n => n.IsConnected)
                .ThenByDescending(n => n.IsSaved)
                .ThenBy(n => n.SSID, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return scanResult;
        });
    }

    private static string GetConnectedSsidFromHandle(IntPtr handle, ref Guid guid)
    {
        try
        {
            uint ret = WlanQueryInterface(handle, ref guid, WLAN_INTF_OPCODE.wlan_intf_opcode_current_connection,
                IntPtr.Zero, out _, out var dataPtr, IntPtr.Zero);
            if (ret != 0) return "";
            try
            {
                var attrs = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(dataPtr);
                return attrs.wlanAssociationAttributes.GetSSID();
            }
            finally { WlanFreeMemory(dataPtr); }
        }
        catch { return ""; }
    }

    // Fast synchronous SSID check — opens a handle, queries once, closes.
    // ~1 ms, no process spawn. Safe to call every second.
    public static string GetConnectedSsidNow()
    {
        var handle    = IntPtr.Zero;
        var ifListPtr = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0 || handle == IntPtr.Zero) return "";
            WlanEnumInterfaces(handle, IntPtr.Zero, out ifListPtr);
            int count = Marshal.ReadInt32(ifListPtr);
            if (count < 1) return "";
            var ifInfoPtr = new IntPtr(ifListPtr.ToInt64() + 8);
            var ifInfo    = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ifInfoPtr);
            var guid      = ifInfo.InterfaceGuid;
            return GetConnectedSsidFromHandle(handle, ref guid);
        }
        catch { return ""; }
        finally
        {
            if (ifListPtr != IntPtr.Zero) WlanFreeMemory(ifListPtr);
            if (handle    != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    public static async Task<string> GetConnectedSsidAsync()
    {
        return await Task.Run(() =>
        {
            var handle = IntPtr.Zero;
            var ifListPtr = IntPtr.Zero;
            try
            {
                WlanOpenHandle(2, IntPtr.Zero, out _, out handle);
                if (handle == IntPtr.Zero) return "";
                WlanEnumInterfaces(handle, IntPtr.Zero, out ifListPtr);
                var ifInfoPtr = new IntPtr(ifListPtr.ToInt64() + 8);
                var ifInfo = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ifInfoPtr);
                var guid = ifInfo.InterfaceGuid;
                return GetConnectedSsidFromHandle(handle, ref guid);
            }
            catch { return ""; }
            finally
            {
                if (ifListPtr != IntPtr.Zero) WlanFreeMemory(ifListPtr);
                if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
            }
        });
    }

    public static async Task<List<string>> GetSavedProfilesAsync()
    {
        return await Task.Run(() =>
        {
            var handle = IntPtr.Zero;
            var ifListPtr = IntPtr.Zero;
            try
            {
                WlanOpenHandle(2, IntPtr.Zero, out _, out handle);
                if (handle == IntPtr.Zero) return new List<string>();
                WlanEnumInterfaces(handle, IntPtr.Zero, out ifListPtr);
                var ifInfoPtr = new IntPtr(ifListPtr.ToInt64() + 8);
                var ifInfo = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ifInfoPtr);
                var guid = ifInfo.InterfaceGuid;
                return GetSavedProfilesNative(handle, ref guid);
            }
            catch { return new List<string>(); }
            finally
            {
                if (ifListPtr != IntPtr.Zero) WlanFreeMemory(ifListPtr);
                if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
            }
        });
    }

    public static async Task<bool> ConnectAsync(string ssid)
    {
        return await Task.Run(() =>
        {
            var handle = IntPtr.Zero;
            var ifListPtr = IntPtr.Zero;
            try
            {
                WlanOpenHandle(2, IntPtr.Zero, out _, out handle);
                if (handle == IntPtr.Zero) return false;
                WlanEnumInterfaces(handle, IntPtr.Zero, out ifListPtr);
                var ifInfoPtr = new IntPtr(ifListPtr.ToInt64() + 8);
                var ifInfo = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ifInfoPtr);
                var guid = ifInfo.InterfaceGuid;
                WlanFreeMemory(ifListPtr);
                ifListPtr = IntPtr.Zero;

                var connParams = new WLAN_CONNECTION_PARAMETERS
                {
                    wlanConnectionMode = 0,
                    strProfile = ssid,
                    pDot11Ssid = IntPtr.Zero,
                    pDesiredBssidList = IntPtr.Zero,
                    dot11BssType = 1,
                    dwFlags = 0
                };
                uint ret = WlanConnect(handle, ref guid, ref connParams, IntPtr.Zero);
                return ret == 0;
            }
            catch { return false; }
            finally
            {
                if (ifListPtr != IntPtr.Zero) WlanFreeMemory(ifListPtr);
                if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
            }
        });
    }

    public static async Task<bool> ConnectWithPasswordAsync(string ssid, string password)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Build a temporary WPA2-PSK profile XML and add it
                string profileXml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
  <name>{System.Security.SecurityElement.Escape(ssid)}</name>
  <SSIDConfig><SSID><name>{System.Security.SecurityElement.Escape(ssid)}</name></SSID></SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>
      <sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{System.Security.SecurityElement.Escape(password)}</keyMaterial></sharedKey>
    </security>
  </MSM>
</WLANProfile>";

                var handle = IntPtr.Zero;
                var ifListPtr = IntPtr.Zero;
                try
                {
                    WlanOpenHandle(2, IntPtr.Zero, out _, out handle);
                    if (handle == IntPtr.Zero) return false;
                    WlanEnumInterfaces(handle, IntPtr.Zero, out ifListPtr);
                    var ifInfoPtr = new IntPtr(ifListPtr.ToInt64() + 8);
                    var ifInfo = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(ifInfoPtr);
                    var guid = ifInfo.InterfaceGuid;
                    WlanFreeMemory(ifListPtr);
                    ifListPtr = IntPtr.Zero;

                    uint profileRet = WlanSetProfile(handle, ref guid, 0, profileXml, null, true, IntPtr.Zero, out _);
                    if (profileRet != 0) return false;

                    var connParams = new WLAN_CONNECTION_PARAMETERS
                    {
                        wlanConnectionMode = 0,
                        strProfile = ssid,
                        pDot11Ssid = IntPtr.Zero,
                        pDesiredBssidList = IntPtr.Zero,
                        dot11BssType = 1,
                        dwFlags = 0
                    };
                    uint ret = WlanConnect(handle, ref guid, ref connParams, IntPtr.Zero);
                    return ret == 0;
                }
                finally
                {
                    if (ifListPtr != IntPtr.Zero) WlanFreeMemory(ifListPtr);
                    if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
                }
            }
            catch { return false; }
        });
    }

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanSetProfile(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint dwFlags,
        string strProfileXml, string? strAllUserProfileSecurity, bool bOverwrite, IntPtr pReserved, out uint pdwReasonCode);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetProfileList(IntPtr hClientHandle, ref Guid pInterfaceGuid,
        IntPtr pReserved, out IntPtr ppProfileList);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_PROFILE_INFO
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;
        public uint dwFlags;
    }

    private static List<(string ssid, string bssid, int signal)> GetBssidMapFromNetsh()
    {
        var result = new List<(string, string, int)>();
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show networks mode=bssid")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);

            string currentSsid = "";
            string currentBssid = "";
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                var ssidMatch = Regex.Match(line, @"^SSID \d+\s*:\s*(.+)$");
                if (ssidMatch.Success) { currentSsid = ssidMatch.Groups[1].Value.Trim(); continue; }

                var bssidMatch = Regex.Match(line, @"^BSSID \d+\s*:\s*([0-9A-Fa-f]{2}(?:[:\-][0-9A-Fa-f]{2}){5})$");
                if (bssidMatch.Success)
                {
                    currentBssid = bssidMatch.Groups[1].Value.ToUpperInvariant().Replace('-', ':');
                    continue;
                }

                var sigMatch = Regex.Match(line, @"^Signal\s*:\s*(\d+)%$");
                if (sigMatch.Success && !string.IsNullOrEmpty(currentSsid) && !string.IsNullOrEmpty(currentBssid))
                {
                    result.Add((currentSsid, currentBssid, int.Parse(sigMatch.Groups[1].Value)));
                    currentBssid = "";
                }
            }
        }
        catch { }
        return result;
    }

    private static List<string> GetSavedProfilesNative(IntPtr handle, ref Guid guid)
    {
        var result = new List<string>();
        try
        {
            uint ret = WlanGetProfileList(handle, ref guid, IntPtr.Zero, out var profileListPtr);
            if (ret != 0) return result;
            try
            {
                int count = Marshal.ReadInt32(profileListPtr);
                int infoSize = Marshal.SizeOf<WLAN_PROFILE_INFO>();
                for (int i = 0; i < count; i++)
                {
                    var infoPtr = new IntPtr(profileListPtr.ToInt64() + 8 + i * infoSize);
                    var info = Marshal.PtrToStructure<WLAN_PROFILE_INFO>(infoPtr);
                    if (!string.IsNullOrWhiteSpace(info.strProfileName))
                        result.Add(info.strProfileName.Trim());
                }
            }
            finally { WlanFreeMemory(profileListPtr); }
        }
        catch { }
        return result;
    }
}
