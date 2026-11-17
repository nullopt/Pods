namespace Pods.Core.Devices;

public static class BatteryText
{
    /// <summary>"95%", or an em dash when unknown; never "0%" for unknown (SPEC §6).</summary>
    public static string Format(int? percent) => percent is { } p ? $"{p}%" : "—";
}
