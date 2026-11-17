using Pods.Core.Settings;
namespace Pods.Core.Tests.Settings;

public class SettingChoicesTests
{
    [Fact]
    public void LowBattery_StandardSteps() =>
        Assert.Equal([5, 10, 15, 20, 25, 30, 40, 50], SettingChoices.LowBatteryPercents(20));

    [Fact]
    public void LowBattery_IncludesOddCurrentInOrder() =>
        Assert.Equal([5, 10, 15, 20, 23, 25, 30, 40, 50], SettingChoices.LowBatteryPercents(23));

    [Fact]
    public void LogLevels_Fixed() =>
        Assert.Equal(["Error", "Warning", "Information", "Debug"], SettingChoices.LogLevels);
}
