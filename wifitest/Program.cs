using System;
using System.Threading.Tasks;
using Windows.Devices.WiFi;
using Windows.Devices.Enumeration;

var access = await WiFiAdapter.RequestAccessAsync();
Console.WriteLine($"Access: {access}");
var devices = await DeviceInformation.FindAllAsync(WiFiAdapter.GetDeviceSelector());
Console.WriteLine($"Adapters found: {devices.Count}");
if (devices.Count > 0) {
    var adapter = await WiFiAdapter.FromIdAsync(devices[0].Id);
    Console.WriteLine("Scanning...");
    await adapter.ScanAsync();
    Console.WriteLine($"Networks: {adapter.NetworkReport.AvailableNetworks.Count}");
    foreach (var n in adapter.NetworkReport.AvailableNetworks)
        Console.WriteLine($"  '{n.Ssid}' bars={n.SignalBars}");
}
