using System;
using System.Runtime.InteropServices;
using System.Text;

[DllImport("wlanapi.dll")] static extern uint WlanOpenHandle(uint v, IntPtr r, out uint neg, out IntPtr h);
[DllImport("wlanapi.dll")] static extern uint WlanEnumInterfaces(IntPtr h, IntPtr r, out IntPtr list);
[DllImport("wlanapi.dll")] static extern uint WlanGetAvailableNetworkList(IntPtr h, ref Guid g, uint flags, IntPtr r, out IntPtr nets);
[DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
WlanEnumInterfaces(handle, IntPtr.Zero, out var ifList);
int ifCount = Marshal.ReadInt32(ifList);
var guidPtr = new IntPtr(ifList.ToInt64() + 8);
var guid = Marshal.PtrToStructure<Guid>(guidPtr);

WlanGetAvailableNetworkList(handle, ref guid, 3, IntPtr.Zero, out var netList);
int count = Marshal.ReadInt32(netList);
Console.WriteLine($"Networks: {count}");

// strProfileName = WCHAR[256] = 512 bytes, then DOT11_SSID: uint(4) + byte[32]
for (int j = 0; j < count; j++) {
    // use struct size reported by C
    var netPtr = new IntPtr(netList.ToInt64() + 8 + j * Marshal.SizeOf<WlanNet>());
    var net = Marshal.PtrToStructure<WlanNet>(netPtr);
    uint len = net.ssidLen;
    string ssid = len > 0 && len <= 32 ? Encoding.UTF8.GetString(net.ssid, 0, (int)len) : "(hidden)";
    Console.WriteLine($"  '{ssid}'  signal={net.signal}%  structSize={Marshal.SizeOf<WlanNet>()}");
}
WlanFreeMemory(netList);

[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
struct WlanNet {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string profile; // 512 bytes
    public uint ssidLen;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)] public byte[] ssid;
    public uint bssType;
    public uint bssidCount;
    public bool connectable;
    public uint reason;
    public uint phyCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=8)] public uint[] phyTypes;
    public bool morePhy;
    public uint signal;
}
