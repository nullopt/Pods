namespace Pods.Core.Settings;

/// <summary>The values offered in the settings drop-downs.</summary>
public static class SettingChoices
{
    static readonly int[] LowBatterySteps = [5, 10, 15, 20, 25, 30, 40, 50];

    /// <summary>Standard low-battery steps, plus <paramref name="current"/> if it is not one (a hand-edited file), so it can show as selected.</summary>
    public static IReadOnlyList<int> LowBatteryPercents(int current) =>
        LowBatterySteps.Contains(current) ? LowBatterySteps : [.. LowBatterySteps.Append(current).Order()];

    /// <summary>Log level names, quietest first.</summary>
    public static IReadOnlyList<string> LogLevels { get; } = ["Error", "Warning", "Information", "Debug"];
}
