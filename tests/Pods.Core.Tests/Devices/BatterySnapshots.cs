using Pods.Core.Apple;
using Pods.Core.Devices;
namespace Pods.Core.Tests.Devices;

static class BatterySnapshots
{
    public static HeadphoneSnapshot S(string key, int? left, int? right, int? @case,
        bool lc = false, bool rc = false, bool cc = false, bool single = false,
        DeviceState state = DeviceState.Connected, bool noBattery = false) =>
        new(key, "Pods " + key, single ? "0a20" : "1420", "00", state,
            noBattery ? null : new PodBattery(left, right, @case, lc, rc, cc), null, single);
}
