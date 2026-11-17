using Pods.Core.Devices;
using static Pods.Core.Tests.Devices.BatterySnapshots;
namespace Pods.Core.Tests.Devices;

public class LowBatteryMonitorTests
{
    readonly LowBatteryMonitor m = new();
    static int Twenty(string _) => 20;
    IReadOnlyList<LowBatteryAlert> U(params HeadphoneSnapshot[] s) => m.Update(s, Twenty);

    [Fact]
    public void CrossingAlertsOnce()
    {
        Assert.Empty(U(S("a", 25, 90, 90)));
        Assert.Equal([new LowBatteryAlert("a", "Pods a", BatteryPartKind.Left, 20)], U(S("a", 20, 90, 90)));
        Assert.Empty(U(S("a", 19, 90, 90)));
    }

    [Fact]
    public void Wobble_NoRepeat()
    {
        Assert.Single(U(S("a", 20, 90, 90)));
        Assert.Empty(U(S("a", 21, 90, 90)));
        Assert.Empty(U(S("a", 20, 90, 90)));
    }

    [Fact]
    public void ChargingRearms()
    {
        Assert.Single(U(S("a", 20, 90, 90)));
        Assert.Empty(U(S("a", 20, 90, 90, lc: true)));
        Assert.Equal([new LowBatteryAlert("a", "Pods a", BatteryPartKind.Left, 19)], U(S("a", 19, 90, 90)));
    }

    [Fact]
    public void FirstSightLow_Alerts() =>
        Assert.Equal([new LowBatteryAlert("a", "Pods a", BatteryPartKind.Case, 10)], U(S("a", 90, 90, 10)));

    [Fact]
    public void NullLevel_NeverAlertsOrRearms()
    {
        Assert.Single(U(S("a", 20, 90, 90)));
        Assert.Empty(U(S("a", null, 90, 90)));
        Assert.Empty(U(S("a", 20, 90, 90)));
    }

    [Fact]
    public void Disconnected_Skipped_KeepsDisarmed()
    {
        Assert.Single(U(S("a", 20, 90, 90)));
        Assert.Empty(U(S("a", 20, 90, 90, state: DeviceState.Disconnected)));
        Assert.False(m.AnyLow);
        Assert.Empty(U(S("a", 20, 90, 90)));
        Assert.True(m.AnyLow);
    }

    [Fact]
    public void SingleBattery_RightSlot() =>
        Assert.Equal([new LowBatteryAlert("a", "Pods a", BatteryPartKind.Single, 15)], U(S("a", null, 15, null, single: true)));

    [Fact]
    public void AnyLow_TwoHeadsets()
    {
        U(S("a", 10, 90, 90), S("b", 90, 90, 90));
        Assert.True(m.AnyLow);
        U(S("a", 10, 90, 90, state: DeviceState.Disconnected), S("b", 90, 90, 90));
        Assert.False(m.AnyLow);
    }

    [Fact]
    public void ThresholdPerDevice()
    {
        var alerts = m.Update([S("a", 25, 90, 90), S("b", 25, 90, 90)], k => k == "a" ? 30 : 20);
        Assert.Equal(["a"], alerts.Select(a => a.Key));
    }
}
