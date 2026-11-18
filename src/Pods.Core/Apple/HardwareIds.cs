using System.Text.RegularExpressions;

namespace Pods.Core.Apple;

/// <summary>Reads the advert model ID from a paired device's Windows hardware IDs.</summary>
public static partial class HardwareIds
{
    const string AppleVendor = "0001004c";   // Bluetooth SIG vendor ID source (0001), Apple (004C)

    /// <summary>
    /// The catalogued model ID from an Apple hardware ID (the product ID byte-swapped is the advert
    /// model ID), else null. <paramref name="decided"/> is true once any hardware ID carries a vendor
    /// ID: the answer is then final. Before that Windows may still be installing the drivers.
    /// </summary>
    public static string? AppleModelId(IEnumerable<string> hardwareIds, out bool decided)
    {
        decided = false;
        foreach (var id in hardwareIds)
        {
            var match = VendorAndProduct().Match(id);
            if (!match.Success) continue;
            decided = true;
            if (!match.Groups[1].Value.Equals(AppleVendor, StringComparison.OrdinalIgnoreCase)) continue;
            var pid = match.Groups[2].Value.ToLowerInvariant();
            var modelId = pid[2..] + pid[..2];
            if (ModelCatalog.Find(modelId) is not null) return modelId;
        }
        return null;
    }

    [GeneratedRegex("VID&([0-9a-f]{8})_PID&([0-9a-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorAndProduct();
}
