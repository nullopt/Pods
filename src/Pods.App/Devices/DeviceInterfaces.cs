using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;

namespace Pods.App.Devices;

/// <summary>Finds and opens the driver interfaces a paired device exposes in its container.</summary>
internal static partial class DeviceInterfaces
{
    const string InterfaceEnabledKey = "System.Devices.InterfaceEnabled";
    const uint GenericReadWrite = 0xC0000000;
    const uint ShareReadWrite = 3;
    const uint OpenExisting = 3;
    const uint FileFlagOverlapped = 0x40000000;

    /// <summary>Paths of the enabled interfaces of this class in the device's container.</summary>
    public static async Task<List<string>> FindEnabledAsync(Guid interfaceClass, Guid container, CancellationToken cancel = default)
    {
        var interfaces = await DeviceInformation.FindAllAsync(
            $"System.Devices.InterfaceClassGuid:=\"{{{interfaceClass}}}\" AND System.Devices.ContainerId:=\"{{{container}}}\"",
            [InterfaceEnabledKey], DeviceInformationKind.DeviceInterface).AsTask(cancel);
        return [.. interfaces.Where(i => i.Properties.GetValueOrDefault(InterfaceEnabledKey) is true).Select(i => i.Id)];
    }

    /// <summary>
    /// Opens the interface for reading and writing; the handle is invalid on failure, with the reason
    /// in <see cref="Marshal.GetLastPInvokeError"/>.
    /// </summary>
    public static SafeFileHandle Open(string path, bool overlapped = false) =>
        CreateFileW(path, GenericReadWrite, ShareReadWrite, 0, OpenExisting, overlapped ? FileFlagOverlapped : 0, 0);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
