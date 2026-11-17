#if DEBUG
using Microsoft.UI.Dispatching;
using Pods.Core.Apple;
using Pods.Core.Devices;

namespace Pods.App.Devices;

/// <summary>Demo devices for UI work without hardware (Debug builds only).</summary>
internal sealed class FakeHeadphoneSource(DispatcherQueue ui) : IHeadphoneSource
{
    readonly List<HeadphoneSnapshot> devices =
    [
        new("demo-pro2", "AirPods Pro 2", "1420", "00", DeviceState.Disconnected,
            new PodBattery(95, 85, 45, false, true, false), Playback.NoneIn, IsSingleBattery: false),
        new("demo-max", "AirPods Max", "0a20", "0f", DeviceState.Disconnected,
            new PodBattery(60, null, null, false, false, false), Playback.NoneIn, IsSingleBattery: true),
    ];
    DispatcherQueueTimer? timer;
    int step;

    public IReadOnlyList<HeadphoneSnapshot> Current => [.. devices];

    public bool IsAdapterAvailable => true;

    public event Action? Changed;

    public void Connect(string key) { }

    public void Disconnect(string key) { }

    public void Start()
    {
        timer = ui.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(3);
        timer.Tick += (_, _) => Advance();
        timer.Start();
    }

    public void Stop()
    {
        timer?.Stop();
        timer = null;
    }

    void Advance()
    {
        step++;
        var pro = devices[0];
        var b = pro.Battery!.Value;
        pro = pro with
        {
            Battery = (step % 3) switch
            {
                0 => b with { Left = Lower(b.Left) },
                1 => b with { Right = Lower(b.Right) },
                _ => b with { Case = Lower(b.Case) },
            },
            State = pro.State == DeviceState.Disconnected ? DeviceState.Searching : DeviceState.Disconnected,
        };
        devices[0] = pro;

        var max = devices[1];
        max = max with { Battery = max.Battery!.Value with { Left = Lower(max.Battery.Value.Left) } };
        devices[1] = max;
        Changed?.Invoke();
    }

    static int? Lower(int? value) => value is null or <= 10 ? 100 : value - 10;
}
#endif
