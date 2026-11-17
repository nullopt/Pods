namespace Pods.Core.Devices;

/// <summary>Paired Apple devices and the Bluetooth adapter's state.</summary>
public interface IPairedDeviceSource
{
    IReadOnlyList<PairedDevice> Current { get; }
    bool IsAdapterAvailable { get; }

    /// <summary>Raised on any thread after an add, update, removal or adapter change; re-read <see cref="Current"/>.</summary>
    event Action? Changed;

    void Start();
    void Stop();
}
