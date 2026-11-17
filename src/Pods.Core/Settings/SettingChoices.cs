namespace Pods.Core.Settings;

/// <summary>The values offered by the settings controls.</summary>
public static class SettingChoices
{
    /// <summary>The low battery slider's range and step, in percent.</summary>
    public const int LowBatteryMin = 5, LowBatteryMax = 50, LowBatteryStep = 5;

    /// <summary>Log level names, quietest first.</summary>
    public static IReadOnlyList<string> LogLevels { get; } = ["Error", "Warning", "Information", "Debug"];
}
