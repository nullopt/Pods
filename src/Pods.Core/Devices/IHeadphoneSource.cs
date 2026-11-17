namespace Pods.Core.Devices;

/// <summary>The devices the UI shows.</summary>
public interface IHeadphoneSource
{
    IReadOnlyList<HeadphoneSnapshot> Current { get; }
    bool IsAdapterAvailable { get; }

    /// <summary>Raised on any thread when <see cref="Current"/> or <see cref="IsAdapterAvailable"/> changed; re-read them.</summary>
    event Action? Changed;

    void Start();
    void Stop();
    void Connect(string key);
    void Disconnect(string key);
}
