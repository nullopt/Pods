namespace Pods.Core.Apple;

/// <summary>
/// What a model can do. <see cref="NoiseControl"/> (Off / Noise Cancellation / Transparency) and
/// <see cref="AdaptiveNoise"/> are set over AAP; the lists are MagicPods' (analysis 02 §2.3).
/// </summary>
[Flags]
public enum AppleCapabilities { None = 0, EarDetection = 1, LidPopup = 2, AutoConnect = 4, NoiseControl = 8, AdaptiveNoise = 16 }

/// <param name="Id">Model ID from the proximity advert, lowercase hex (e.g. "1420").</param>
/// <param name="Name">Display name.</param>
public sealed record AppleModel(string Id, string Name, AppleCapabilities Capabilities);

/// <summary>AirPods models recognised from proximity adverts.</summary>
public static class ModelCatalog
{
    const AppleCapabilities E = AppleCapabilities.EarDetection | AppleCapabilities.LidPopup | AppleCapabilities.AutoConnect;
    const AppleCapabilities M = AppleCapabilities.EarDetection | AppleCapabilities.LidPopup;
    const AppleCapabilities Anc = AppleCapabilities.NoiseControl;
    const AppleCapabilities Adaptive = AppleCapabilities.NoiseControl | AppleCapabilities.AdaptiveNoise;

    /// <summary>AirPods Max family IDs; their adverts need a different status mapping.</summary>
    public static IReadOnlySet<string> MaxFamilyIds { get; } = new HashSet<string>(StringComparer.Ordinal) { "0a20", "1f20", "2d20" };

    /// <summary>Headphones with one battery and no case: the AirPods Max family.</summary>
    public static IReadOnlySet<string> SingleBatteryIds => MaxFamilyIds;

    public static IReadOnlyList<AppleModel> All { get; } =
    [
        new("0220", "AirPods (1st generation)", E),
        new("0f20", "AirPods (2nd generation)", E),
        new("1320", "AirPods (3rd generation)", E),
        new("1920", "AirPods 4", E),
        new("1b20", "AirPods 4 (ANC)", E | Adaptive),
        new("3620", "AirPods 5", E | Adaptive),
        new("3020", "AirPods 5 (variant)", E | Adaptive),
        new("0e20", "AirPods Pro", E | Anc),
        new("1420", "AirPods Pro 2", E | Adaptive),
        new("2720", "AirPods Pro 3", E | Adaptive),
        new("2420", "AirPods Pro 2 (USB-C)", E | Adaptive),
        new("0a20", "AirPods Max", M | Anc),
        new("1f20", "AirPods Max (USB-C)", M | Anc),
        new("2d20", "AirPods Max 2", M | Adaptive),
    ];

    static readonly Dictionary<string, AppleModel> ById = All.ToDictionary(m => m.Id, StringComparer.Ordinal);

    /// <param name="modelId">Lowercase hex, e.g. "1420".</param>
    public static AppleModel? Find(string modelId) => ById.GetValueOrDefault(modelId);
}
