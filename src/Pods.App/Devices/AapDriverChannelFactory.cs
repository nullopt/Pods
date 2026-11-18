using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Pods.Core.Aap;
using Serilog;
using Windows.Devices.Enumeration;

namespace Pods.App.Devices;

/// <summary>
/// Opens AAP channels through an L2CAP client driver: the driver binds to the headset's AAP service,
/// registers a device interface in the headset's container, and opening that interface opens the
/// channel (PSM 0x1001). Each write sends one frame and each read returns one. Pods' own driver
/// (driver/) comes first; MagicAAP, which works the same way, is used when only it is installed.
/// Without either, every lookup finds nothing and AAP stays off.
/// </summary>
internal sealed partial class AapDriverChannelFactory(WindowsPairedDeviceSource paired) : IAapChannelFactory
{
    const string InterfaceEnabledKey = "System.Devices.InterfaceEnabled";

    static readonly (Guid Id, string Name)[] Drivers =
    [
        (new("496c4781-b0e8-48e3-ae88-6f645f4b809a"), "PodsAap"),
        (new("9eec98bb-3c54-45d4-a843-7900c4635e08"), "MagicAAP"),
    ];

    const uint GenericReadWrite = 0xC0000000;
    const uint ShareReadWrite = 3;
    const uint OpenExisting = 3;
    const uint FileFlagOverlapped = 0x40000000;

    public async Task<IAapChannel?> OpenAsync(string deviceKey, CancellationToken cancel)
    {
        if (paired.ContainerIdOf(deviceKey) is not { } container) return null;
        foreach (var (id, name) in Drivers)
        {
            var interfaces = await DeviceInformation.FindAllAsync(
                $"System.Devices.InterfaceClassGuid:=\"{{{id}}}\" AND System.Devices.ContainerId:=\"{{{container}}}\"",
                [InterfaceEnabledKey], DeviceInformationKind.DeviceInterface).AsTask(cancel);
            var path = interfaces.FirstOrDefault(i => i.Properties.GetValueOrDefault(InterfaceEnabledKey) is true)?.Id;
            if (path is null) continue;
            // Opening blocks until the headset accepts the channel (or Bluetooth gives up), so not on the caller's thread.
            var channel = await Task.Run(() => Open(path), cancel);
            Log.Information("AAP channel open to {Device} through {Driver}", deviceKey, name);
            return channel;
        }
        return null;
    }

    static DriverChannel Open(string path)
    {
        var handle = CreateFileW(path, GenericReadWrite, ShareReadWrite, 0, OpenExisting, FileFlagOverlapped, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Opening the AAP channel failed: {path}");
        return new DriverChannel(handle);
    }

    /// <summary>An unbuffered stream over the driver handle, so each read or write is one driver request.</summary>
    sealed class DriverChannel(SafeFileHandle handle) : IAapChannel
    {
        readonly FileStream stream = new(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
        readonly byte[] buffer = new byte[4096];   // AapSession reads one frame at a time

        public Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancel) => stream.WriteAsync(frame, cancel).AsTask();

        public async Task<byte[]?> ReadAsync(CancellationToken cancel)
        {
            var read = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false);
            return read == 0 ? null : buffer[..read];
        }

        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
