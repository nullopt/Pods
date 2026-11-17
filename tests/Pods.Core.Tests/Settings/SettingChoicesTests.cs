using Pods.Core.Settings;
namespace Pods.Core.Tests.Settings;

public class SettingChoicesTests
{
    [Fact]
    public void LogLevels_Fixed() =>
        Assert.Equal(["Error", "Warning", "Information", "Debug"], SettingChoices.LogLevels);
}
