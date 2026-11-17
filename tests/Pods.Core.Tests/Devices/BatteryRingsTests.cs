using Pods.Core.Devices;
using static Pods.Core.Tests.Devices.BatterySnapshots;
namespace Pods.Core.Tests.Devices;

public class BatteryRingsTests
{
    [Fact]
    public void Single_UsesRightWhenLeftNull() =>
        Assert.Equal([new BatteryRing(BatteryPartKind.Single, 40, false)], BatteryRings.Of(S("a", null, 40, null, single: true)));

    [Fact]
    public void EqualBuds_Combined() =>
        Assert.Equal([new BatteryRing(BatteryPartKind.Buds, 80, false), new BatteryRing(BatteryPartKind.Case, 50, false)],
            BatteryRings.Of(S("a", 80, 80, 50)));

    [Fact]
    public void DifferentBuds_Separate() =>
        Assert.Equal([BatteryPartKind.Left, BatteryPartKind.Right, BatteryPartKind.Case],
            BatteryRings.Of(S("a", 80, 70, 50)).Select(r => r.Kind));

    [Fact]
    public void ChargingDiffers_Separate() =>
        Assert.Equal([BatteryPartKind.Left, BatteryPartKind.Right, BatteryPartKind.Case],
            BatteryRings.Of(S("a", 80, 80, 50, lc: true)).Select(r => r.Kind));

    [Fact]
    public void NoBattery_NullLevels()
    {
        var rings = BatteryRings.Of(S("a", null, null, null, noBattery: true));
        Assert.Equal([BatteryPartKind.Left, BatteryPartKind.Right, BatteryPartKind.Case], rings.Select(r => r.Kind));
        Assert.All(rings, r => Assert.Null(r.Percent));
    }

    [Fact]
    public void IsLow_Rules()
    {
        Assert.True(BatteryRings.IsLow(20, false, 20));
        Assert.False(BatteryRings.IsLow(21, false, 20));
        Assert.False(BatteryRings.IsLow(10, true, 20));
        Assert.False(BatteryRings.IsLow(null, false, 20));
    }
}
