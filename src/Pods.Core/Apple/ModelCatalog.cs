namespace Pods.Core.Apple;

[Flags]
public enum AppleCapabilities { None = 0, EarDetection = 1, LidPopup = 2, AutoConnect = 4 }

/// <param name="Id">Model ID from the proximity advert, lowercase hex (e.g. "1420").</param>
/// <param name="Name">Display name.</param>
public sealed record AppleModel(string Id, string Name, AppleCapabilities Capabilities);

/// <summary>Apple and Beats models recognised from proximity adverts.</summary>
public static class ModelCatalog
{
    const AppleCapabilities E = AppleCapabilities.EarDetection | AppleCapabilities.LidPopup | AppleCapabilities.AutoConnect;
    const AppleCapabilities M = AppleCapabilities.EarDetection | AppleCapabilities.LidPopup;
    const AppleCapabilities L = AppleCapabilities.LidPopup;
    const AppleCapabilities N = AppleCapabilities.None;

    /// <summary>AirPods Max family IDs; their adverts need a different status mapping.</summary>
    public static IReadOnlySet<string> MaxFamilyIds { get; } = new HashSet<string>(StringComparer.Ordinal) { "0a20", "1f20", "2d20" };

    public static IReadOnlyList<AppleModel> All { get; } =
    [
        new("0220", "AirPods (1st generation)", E),
        new("0f20", "AirPods (2nd generation)", E),
        new("1320", "AirPods (3rd generation)", E),
        new("1920", "AirPods 4", E),
        new("1b20", "AirPods 4 (ANC)", E),
        new("3620", "AirPods 5", E),
        new("3020", "AirPods 5 (variant)", E),
        new("0e20", "AirPods Pro", E),
        new("1420", "AirPods Pro 2", E),
        new("2720", "AirPods Pro 3", E),
        new("2420", "AirPods Pro 2 (USB-C)", E),
        new("0a20", "AirPods Max", M),
        new("1f20", "AirPods Max (USB-C)", M),
        new("2d20", "AirPods Max 2", M),
        new("0b20", "Powerbeats Pro", E),
        new("1d20", "Powerbeats Pro 2", E),
        new("2f20", "Powerbeats Fit", E),
        new("0320", "Powerbeats3", N),
        new("0620", "Beats Solo3", N),
        new("0c20", "Beats Solo Pro", N),
        new("0920", "Beats Studio3", N),
        new("1220", "Beats Fit Pro", E),
        new("1120", "Beats Studio Buds", L),
        new("1620", "Beats Studio Buds +", L),
        new("0d20", "Powerbeats (4th generation)", N),
        new("1720", "Beats Studio Pro", N),
        new("2620", "Beats Solo Buds", N),
        new("2520", "Beats Solo 4", N),
        new("1a20", "Beats Pill", N),
        new("0520", "BeatsX", N),
        new("1020", "Beats Flex", N),
    ];

    static readonly Dictionary<string, AppleModel> ById = All.ToDictionary(m => m.Id, StringComparer.Ordinal);

    /// <param name="modelId">Lowercase hex, e.g. "1420".</param>
    public static AppleModel? Find(string modelId) => ById.GetValueOrDefault(modelId);
}
