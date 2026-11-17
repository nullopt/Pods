using Pods.Core.Apple;

namespace Pods.Core.Devices;

/// <summary>What a device card shows. <see cref="Key"/> is the paired device's id. Battery values are already held through short unknown gaps.</summary>
public sealed record HeadphoneSnapshot(
    string Key,
    string Name,
    string ModelId,
    string Colour,
    DeviceState State,
    PodBattery? Battery,
    Playback? Playback,
    bool IsSingleBattery,
    ConnectionRequest Request = ConnectionRequest.None);
