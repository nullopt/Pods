using Pods.Core.Devices;

namespace Pods.App.Devices;

// Replaced by the Windows paired-device source and KS connection control in the next commit.
internal sealed class EmptyPairedDeviceSource : IPairedDeviceSource
{
    public IReadOnlyList<PairedDevice> Current => [];
    public bool IsAdapterAvailable => true;
    public event Action? Changed { add { } remove { } }
    public void Start() { }
    public void Stop() { }
}

internal sealed class NoConnectionControl : IConnectionControl
{
    public Task<bool> ConnectAsync(string id) => Task.FromResult(false);
    public Task<bool> DisconnectAsync(string id) => Task.FromResult(false);
}
