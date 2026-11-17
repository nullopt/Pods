using Pods.Core.Apple;

namespace Pods.Core.Devices;

/// <summary>Decoded Apple proximity adverts, typically from the BLE watcher (SPEC §7).</summary>
public interface IAppleAdvertSource
{
    event Action<AppleAdvert>? AdvertReceived;
    void Start();
    void Stop();
}
