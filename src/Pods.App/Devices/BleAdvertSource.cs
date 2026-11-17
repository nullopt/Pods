using System.Runtime.InteropServices.WindowsRuntime;
using Pods.Core.Apple;
using Pods.Core.Devices;
using Serilog;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace Pods.App.Devices;

/// <summary>
/// Apple proximity-pairing adverts from the BLE watcher. The OS filters on the byte pattern
/// <c>4C 00 07</c>, so only type-0x07 Apple frames reach this code.
/// </summary>
internal sealed class BleAdvertSource : IAppleAdvertSource
{
    const ushort AppleCompanyId = 0x004C;
    readonly BluetoothLEAdvertisementWatcher watcher;
    long malformed;

    public BleAdvertSource()
    {
        watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        watcher.AdvertisementFilter.BytePatterns.Add(new BluetoothLEAdvertisementBytePattern
        {
            DataType = 0xFF,
            Offset = 0,
            Data = Buffer(0x4C, 0x00, 0x07),
        });
        watcher.Received += OnReceived;
        watcher.Stopped += (_, e) =>
        {
            if (e.Error != Windows.Devices.Bluetooth.BluetoothError.Success)
                Log.Warning("BLE watcher stopped: {Error}", e.Error);
        };
    }

    public event Action<AppleAdvert>? AdvertReceived;

    public void Start() => watcher.Start();

    public void Stop() => watcher.Stop();

    void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
    {
        foreach (var section in e.Advertisement.ManufacturerData)
        {
            if (section.CompanyId != AppleCompanyId) continue;
            var advert = AdvertDecoder.Decode(section.Data.ToArray(), e.RawSignalStrengthInDBm, e.BluetoothAddress);
            if (advert is null)
            {
                var count = Interlocked.Increment(ref malformed);
                Log.Debug("Dropped malformed Apple advert ({Count} so far)", count);
                continue;
            }
            AdvertReceived?.Invoke(advert);
        }
    }

    static IBuffer Buffer(params byte[] bytes)
    {
        using var writer = new DataWriter();
        writer.WriteBytes(bytes);
        return writer.DetachBuffer();
    }
}
