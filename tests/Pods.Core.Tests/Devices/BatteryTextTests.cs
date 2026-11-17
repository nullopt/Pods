using Pods.Core.Devices;

namespace Pods.Core.Tests.Devices;

public class BatteryTextTests
{
    [Fact] public void BatteryText_UnknownIsDash() => Assert.Equal("—", BatteryText.Format(null));

    [Theory]
    [InlineData(95, "95%")]
    [InlineData(100, "100%")]
    [InlineData(5, "5%")]
    public void BatteryText_Percent(int percent, string expected) => Assert.Equal(expected, BatteryText.Format(percent));
}
