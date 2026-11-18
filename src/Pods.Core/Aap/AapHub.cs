using Pods.Core.Apple;
using Pods.Core.Devices;

namespace Pods.Core.Aap;

/// <summary>
/// One AAP session per connected device that has a driver interface. A device gets a channel only
/// while Windows reports it connected: opening a channel to a disconnected device would page it and
/// pull it away from a phone. A device without a driver interface is not asked again until it
/// reconnects, so PCs without the driver do no extra work. A channel that fails to open or drops is
/// reopened after <see cref="RetryDelay"/> while the device stays connected.
/// </summary>
public sealed class AapHub(IAapChannelFactory factory, TimeProvider time) : IDisposable
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    readonly Lock gate = new();
    readonly Dictionary<string, Link> links = [];
    bool disposed;

    /// <summary>Raised on any thread when a device's <see cref="StateOf"/> changed.</summary>
    public event Action? Changed;

    /// <summary>A channel failed to open or dropped: device key and the error.</summary>
    public event Action<string, Exception>? Failed;

    /// <summary>Every frame in (true) or out (false), with the device key; for debug logging.</summary>
    public event Action<string, bool, ReadOnlyMemory<byte>>? Frame;

    /// <summary>The device's AAP state while its session is running, else null.</summary>
    public AapState? StateOf(string key)
    {
        lock (gate) return links.GetValueOrDefault(key)?.Session?.State;
    }

    /// <summary>Call with every paired device; sessions follow the connected, catalogued ones.</summary>
    public void Update(IReadOnlyList<PairedDevice> devices)
    {
        List<Link> stopped = [];
        lock (gate)
        {
            if (disposed) return;
            var wanted = devices.Where(d => d.IsConnected && ModelCatalog.Find(d.ModelId) is not null).ToDictionary(d => d.Id);
            foreach (var (key, link) in links.ToList())
            {
                if (wanted.ContainsKey(key)) continue;
                links.Remove(key);
                stopped.Add(link);
            }
            foreach (var device in wanted.Values.Where(d => !links.ContainsKey(d.Id)))
            {
                var link = new Link(device.Id, ModelCatalog.Find(device.ModelId)!);
                links[device.Id] = link;
                _ = RunAsync(link);   // catches everything itself
            }
        }
        foreach (var link in stopped) link.Cancel.Cancel();
        if (stopped.Any(l => l.Session is not null)) Changed?.Invoke();
    }

    public void SetNoiseControl(string key, NoiseControlMode mode)
    {
        Link? link;
        lock (gate) link = links.GetValueOrDefault(key);
        if (link?.Session is not { } session) return;
        _ = SendAsync(link, session.SetNoiseControlAsync(mode, link.Cancel.Token));
    }

    async Task SendAsync(Link link, Task send)
    {
        try { await send.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failed?.Invoke(link.Key, ex); }
    }

    async Task RunAsync(Link link)
    {
        var cancel = link.Cancel.Token;
        await Task.Yield();
        while (!cancel.IsCancellationRequested)
        {
            IAapChannel? channel = null;
            try
            {
                channel = await factory.OpenAsync(link.Key, cancel).ConfigureAwait(false);
                if (channel is null) return;   // no driver for this device
                var session = new AapSession(channel, link.Model);
                session.Changed += OnSessionChanged;
                session.Frame += (incoming, frame) => Frame?.Invoke(link.Key, incoming, frame);
                lock (gate) link.Session = session;
                await session.RunAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Failed?.Invoke(link.Key, ex);
            }
            finally
            {
                if (channel is not null) await DisposeQuietly(channel).ConfigureAwait(false);
                bool hadSession;
                lock (gate)
                {
                    hadSession = link.Session is not null;
                    link.Session = null;
                }
                if (hadSession) Changed?.Invoke();
            }
            try { await Task.Delay(RetryDelay, time, cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    void OnSessionChanged() => Changed?.Invoke();

    static async Task DisposeQuietly(IAapChannel channel)
    {
        try { await channel.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { }
    }

    public void Dispose()
    {
        List<Link> all;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            all = [.. links.Values];
            links.Clear();
        }
        foreach (var link in all) link.Cancel.Cancel();
    }

    sealed class Link(string key, AppleModel model)
    {
        public string Key { get; } = key;
        public AppleModel Model { get; } = model;
        public CancellationTokenSource Cancel { get; } = new();
        public AapSession? Session { get; set; }
    }
}
