using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Pods.Core.Devices;
using Serilog;
using Windows.Devices.Enumeration;

namespace Pods.App.Devices;

/// <summary>
/// Connects and disconnects a paired headset through its Bluetooth audio kernel-streaming filters:
/// <c>IOCTL_KS_PROPERTY</c> with <c>KSPROPSETID_BtAudio</c>, reconnect (0) or disconnect (1), sent to
/// every enabled audio interface in the device's container. The filters stay openable while the
/// device is disconnected, so reconnect works from that state. Needs no admin rights.
/// </summary>
internal sealed partial class KsConnectionControl(WindowsPairedDeviceSource paired) : IConnectionControl
{
    const string InterfaceEnabledKey = "System.Devices.InterfaceEnabled";
    static readonly Guid KsCategoryAudio = new("6994AD04-93EF-11D0-A3CC-00A0C9223196");
    static readonly Guid KsPropSetIdBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    const uint IoctlKsProperty = 0x2F0003;   // CTL_CODE(FILE_DEVICE_KS, 0, METHOD_NEITHER, FILE_ANY_ACCESS)
    const uint KsPropertyTypeGet = 1;
    const uint OneShotReconnect = 0;
    const uint OneShotDisconnect = 1;
    const uint GenericReadWrite = 0xC0000000;
    const uint ShareReadWrite = 3;
    const uint OpenExisting = 3;

    public Task<bool> ConnectAsync(string id) => SendAsync(id, OneShotReconnect);

    public Task<bool> DisconnectAsync(string id) => SendAsync(id, OneShotDisconnect);

    async Task<bool> SendAsync(string id, uint property)
    {
        if (paired.ContainerIdOf(id) is not { } container)
        {
            Log.Warning("No container for device {Id}", id);
            return false;
        }
        var filters = await DeviceInformation.FindAllAsync(
            $"System.Devices.InterfaceClassGuid:=\"{{{KsCategoryAudio}}}\" AND System.Devices.ContainerId:=\"{{{container}}}\"",
            [InterfaceEnabledKey], DeviceInformationKind.DeviceInterface);
        var paths = filters.Where(f => f.Properties.GetValueOrDefault(InterfaceEnabledKey) is true).Select(f => f.Id).ToList();
        return await Task.Run(() =>
        {
            var sent = false;
            foreach (var path in paths) sent |= Send(path, property);
            if (paths.Count == 0) Log.Warning("No enabled audio interface for device {Id}", id);
            return sent;
        });
    }

    static bool Send(string path, uint property)
    {
        using var handle = CreateFileW(path, GenericReadWrite, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            Log.Warning("Opening audio interface failed ({Error}): {Path}", Marshal.GetLastPInvokeError(), path);
            return false;
        }
        Span<byte> request = stackalloc byte[24];   // KSPROPERTY: Set (GUID), Id, Flags
        KsPropSetIdBtAudio.TryWriteBytes(request);
        BitConverter.TryWriteBytes(request[16..], property);
        BitConverter.TryWriteBytes(request[20..], KsPropertyTypeGet);
        if (DeviceIoControl(handle, IoctlKsProperty, request, (uint)request.Length, 0, 0, out _, 0)) return true;
        Log.Warning("Audio interface request {Property} failed ({Error}): {Path}", property, Marshal.GetLastPInvokeError(), path);
        return false;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, ReadOnlySpan<byte> input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
}
