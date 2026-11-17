using Pods.Core.Devices;
using static Pods.Core.Tests.Devices.BatterySnapshots;
namespace Pods.Core.Tests.Devices;

public class DeviceOrderTests
{
    [Fact]
    public void ConnectedFirst_OthersKeepTheirOrder()
    {
        var list = new[]
        {
            S("a", 50, 50, 50, state: DeviceState.Disconnected),
            S("b", 50, 50, 50, state: DeviceState.Connected),
            S("c", 50, 50, 50, state: DeviceState.Searching),
            S("d", 50, 50, 50, state: DeviceState.Connected),
        };
        Assert.Equal(["b", "d", "a", "c"], DeviceOrder.ConnectedFirst(list).Select(s => s.Key));
    }
}
