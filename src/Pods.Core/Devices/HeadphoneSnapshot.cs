using Pods.Core.Apple;

namespace Pods.Core.Devices;

/// <summary>What a device card shows. Battery values are already held through short unknown gaps (SPEC §5.4).</summary>
public sealed record HeadphoneSnapshot(
    string Key,
    string Name,
    string ModelId,
    string Colour,
    DeviceState State,
    PodBattery? Battery,
    Playback? Playback,
    bool IsSingleBattery);
