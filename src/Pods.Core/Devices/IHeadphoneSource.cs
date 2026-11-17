namespace Pods.Core.Devices;

/// <summary>The devices the UI shows. <see cref="Changed"/> may be raised on any thread.</summary>
public interface IHeadphoneSource
{
    IReadOnlyList<HeadphoneSnapshot> Current { get; }
    event Action<HeadphoneSnapshot>? Changed;
    void Start();
    void Stop();
}
