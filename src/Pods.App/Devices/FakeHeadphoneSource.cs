#if DEBUG
using Microsoft.UI.Dispatching;
using Pods.Core.Apple;
using Pods.Core.Devices;

namespace Pods.App.Devices;

/// <summary>
/// Demo devices for UI work without hardware (Debug builds only). Connect and disconnect take 1.5 s.
/// Ids are 12 hex digits like real addresses, because per-device settings require that form.
/// </summary>
internal sealed class FakeHeadphoneSource(DispatcherQueue ui) : IHeadphoneSource
{
    readonly List<HeadphoneSnapshot> devices =
    [
        new("de0000000001", "AirPods Pro 2", "1420", "00", DeviceState.Connected,
            new PodBattery(95, 85, 45, false, true, false), Playback.NoneIn, IsSingleBattery: false, IsInCase: true),
        new("de0000000002", "AirPods Max", "0a20", "0f", DeviceState.Disconnected,
            new PodBattery(60, null, null, false, false, false), Playback.NoneIn, IsSingleBattery: true),
    ];
    DispatcherQueueTimer? timer;
    int step;

    public IReadOnlyList<HeadphoneSnapshot> Current => [.. devices];

    public bool IsAdapterAvailable => true;

    public event Action? Changed;

    public void Start()
    {
        timer = ui.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(3);
        timer.Tick += (_, _) => Drain();
        timer.Start();
    }

    public void Stop()
    {
        timer?.Stop();
        timer = null;
    }

    public void Connect(string key) => Request(key, ConnectionRequest.Connecting, DeviceState.Connected);

    public void Disconnect(string key) => Request(key, ConnectionRequest.Disconnecting, DeviceState.Disconnected);

    void Request(string key, ConnectionRequest request, DeviceState result)
    {
        var index = devices.FindIndex(d => d.Key == key);
        if (index < 0) return;
        devices[index] = devices[index] with { Request = request };
        Changed?.Invoke();
        var done = ui.CreateTimer();
        done.Interval = TimeSpan.FromSeconds(1.5);
        done.IsRepeating = false;
        done.Tick += (_, _) =>
        {
            devices[index] = devices[index] with { State = result, Request = ConnectionRequest.None };
            Changed?.Invoke();
        };
        done.Start();
    }

    void Drain()
    {
        step++;
        var pro = devices[0];
        var b = pro.Battery!.Value;
        devices[0] = pro with
        {
            Battery = (step % 3) switch
            {
                0 => b with { Left = Lower(b.Left) },
                1 => b with { Right = Lower(b.Right) },
                _ => b with { Case = Lower(b.Case) },
            },
        };
        var max = devices[1];
        devices[1] = max with { Battery = max.Battery!.Value with { Left = Lower(max.Battery.Value.Left) } };
        Changed?.Invoke();
    }

    static int? Lower(int? value) => value is null or <= 10 ? 100 : value - 10;
}
#endif
