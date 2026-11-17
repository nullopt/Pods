namespace Pods.Core.Apple;

/// <summary>Battery percentages (null = unknown) and charging flags.</summary>
public readonly record struct PodBattery(int? Left, int? Right, int? Case, bool LeftCharging, bool RightCharging, bool CaseCharging);

/// <summary>One decoded proximity-pairing advert. <see cref="Status"/> is after AirPods Max normalisation.</summary>
public sealed partial record AppleAdvert(
    ulong Address,
    short Rssi,
    string ModelId,
    AppleModel? Model,
    byte Status,
    PodBattery Battery,
    bool BothPresent,
    bool PrimaryIsLeft,
    bool LeftInEar,
    bool RightInEar,
    byte LidCounter,
    string Colour);
