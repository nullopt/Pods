namespace Pods.Core.Devices;

/// <summary>The devices the UI shows.</summary>
public interface IHeadphoneSource
{
    IReadOnlyList<HeadphoneSnapshot> Current { get; }
    bool IsAdapterAvailable { get; }

    /// <summary>Raised on any thread when <see cref="Current"/> or <see cref="IsAdapterAvailable"/> changed; re-read them.</summary>
    event Action? Changed;

    /// <summary>Raised on any thread, once per opening, when a device's case lid opens close enough to the PC; the argument is its key.</summary>
    event Action<string>? LidOpened;

    /// <summary>Raised on any thread when a lid reported by <see cref="LidOpened"/> closes (or its adverts stop).</summary>
    event Action<string>? LidClosed;

    void Start();
    void Stop();
    void Connect(string key);
    void Disconnect(string key);
}
