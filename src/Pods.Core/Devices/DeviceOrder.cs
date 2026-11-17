namespace Pods.Core.Devices;

/// <summary>Display order for device lists.</summary>
public static class DeviceOrder
{
    /// <summary>Devices connected to this PC first; otherwise the source's order is kept.</summary>
    public static IReadOnlyList<HeadphoneSnapshot> ConnectedFirst(IEnumerable<HeadphoneSnapshot> devices) =>
        devices.OrderBy(d => d.State == DeviceState.Connected ? 0 : 1).ToList();
}
