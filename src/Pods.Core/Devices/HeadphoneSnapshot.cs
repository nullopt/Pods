using Pods.Core.Aap;
using Pods.Core.Apple;

namespace Pods.Core.Devices;

/// <summary>
/// What a device card shows. <see cref="Key"/> is the paired device's id. <see cref="IsInCase"/>: fresh
/// adverts say both pods are in the case. Battery values are already held through short unknown gaps.
/// <see cref="CanControlNoise"/>: an AAP session is running and the model has noise control;
/// <see cref="NoiseControl"/> is the mode it last reported (null until it does).
/// </summary>
public sealed record HeadphoneSnapshot(
    string Key,
    string Name,
    string ModelId,
    string Colour,
    DeviceState State,
    PodBattery? Battery,
    Playback? Playback,
    bool IsSingleBattery,
    ConnectionRequest Request = ConnectionRequest.None,
    bool IsInCase = false,
    bool CanControlNoise = false,
    NoiseControlMode? NoiseControl = null);
