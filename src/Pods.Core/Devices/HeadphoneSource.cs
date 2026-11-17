using Pods.Core.Apple;
using Pods.Core.Tracking;

namespace Pods.Core.Devices;

/// <summary>
/// One snapshot per paired, catalogued Apple device. Connection state comes from Windows; battery
/// comes from the adverts of the device's model, which carry no per-set identity, so each model's
/// stream is shown on one device: the connected one, else the one last seen connected, else the
/// first. Scanning and the window tick run only while started, the adapter is on and at least one
/// device is paired, so nothing runs while idle. While the adapter is off, cards keep their last
/// values and removals are ignored until it returns. A connect or disconnect request is pending
/// until Windows reports the requested state or <see cref="RequestTimeout"/> passes; a failure is
/// shown until the device's state next changes.
/// </summary>
public sealed class HeadphoneSource(
    IPairedDeviceSource paired, IAppleAdvertSource adverts, IConnectionControl control, TimeProvider time, MatcherOptions? options = null)
    : IHeadphoneSource, IDisposable
{
    public static readonly TimeSpan WindowLength = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    readonly MatcherOptions matcherOptions = options ?? new MatcherOptions();
    readonly Lock gate = new();
    readonly Dictionary<string, ModelStream> streams = [];
    readonly Dictionary<string, string> lastConnected = [];
    readonly Dictionary<string, PendingRequest> requests = [];
    List<PairedDevice> devices = [];
    List<HeadphoneSnapshot> published = [];
    bool adapterAvailable = true;
    bool started;
    bool scanning;
    ITimer? tick;

    public event Action? Changed;

    public IReadOnlyList<HeadphoneSnapshot> Current { get { lock (gate) return [.. published]; } }

    public bool IsAdapterAvailable { get { lock (gate) return adapterAvailable; } }

    public void Start()
    {
        lock (gate)
        {
            if (started) return;
            started = true;
            paired.Changed += OnPairedChanged;
        }
        paired.Start();
        OnPairedChanged();
    }

    public void Stop()
    {
        lock (gate)
        {
            if (!started) return;
            started = false;
            paired.Changed -= OnPairedChanged;
        }
        UpdateScanning();
        paired.Stop();
    }

    public void Connect(string key) => Request(key, ConnectionRequest.Connecting);

    public void Disconnect(string key) => Request(key, ConnectionRequest.Disconnecting);

    void Request(string key, ConnectionRequest kind)
    {
        lock (gate)
        {
            if (!adapterAvailable || !devices.Any(d => d.Id == key)) return;
            if (requests.TryGetValue(key, out var existing) && IsPending(existing.Value)) return;
            requests[key] = new PendingRequest(kind,
                time.CreateTimer(_ => Fail(key, kind), null, RequestTimeout, Timeout.InfiniteTimeSpan));
        }
        Publish();

        Task<bool> sent;
        try
        {
            sent = kind == ConnectionRequest.Connecting ? control.ConnectAsync(key) : control.DisconnectAsync(key);
        }
        catch (Exception)
        {
            Fail(key, kind);
            return;
        }
        _ = FailUnlessSent(sent, key, kind);
    }

    async Task FailUnlessSent(Task<bool> sent, string key, ConnectionRequest kind)
    {
        bool ok;
        try { ok = await sent.ConfigureAwait(false); }
        catch (Exception) { ok = false; }
        if (!ok) Fail(key, kind);
    }

    void Fail(string key, ConnectionRequest kind)
    {
        lock (gate)
        {
            if (!requests.TryGetValue(key, out var request) || request.Value != kind) return;
            request.Timer?.Dispose();
            var state = published.FirstOrDefault(s => s.Key == key)?.State;
            requests[key] = new PendingRequest(
                kind == ConnectionRequest.Connecting ? ConnectionRequest.ConnectFailed : ConnectionRequest.DisconnectFailed,
                Timer: null, FailedInState: state);
        }
        Publish();
    }

    static bool IsPending(ConnectionRequest value) =>
        value is ConnectionRequest.Connecting or ConnectionRequest.Disconnecting;

    public void Dispose()
    {
        Stop();
        lock (gate)
        {
            foreach (var stream in streams.Values) stream.Tracker.Dispose();
            foreach (var request in requests.Values) request.Timer?.Dispose();
            requests.Clear();
        }
    }

    void OnPairedChanged()
    {
        lock (gate)
        {
            adapterAvailable = paired.IsAdapterAvailable;
            var current = paired.Current.Where(d => ModelCatalog.Find(d.ModelId) is not null).ToList();
            if (adapterAvailable) devices = current;
            else devices = [.. devices.Select(d => current.FirstOrDefault(c => c.Id == d.Id) ?? d)];
            foreach (var device in devices.Where(d => d.IsConnected)) lastConnected[device.ModelId] = device.Id;
            foreach (var (key, request) in requests.ToList())
            {
                var device = devices.FirstOrDefault(d => d.Id == key);
                var done = device is null
                    || (request.Value == ConnectionRequest.Connecting && device.IsConnected)
                    || (request.Value == ConnectionRequest.Disconnecting && !device.IsConnected);
                if (!done) continue;
                request.Timer?.Dispose();
                requests.Remove(key);
            }
        }
        UpdateScanning();
        Publish();
    }

    void UpdateScanning()
    {
        bool start, stop;
        lock (gate)
        {
            var wanted = started && adapterAvailable && devices.Count > 0;
            start = wanted && !scanning;
            stop = !wanted && scanning;
            if (start)
            {
                adverts.AdvertReceived += OnAdvert;
                tick = time.CreateTimer(_ => Tick(), null, WindowLength, WindowLength);
            }
            else if (stop)
            {
                adverts.AdvertReceived -= OnAdvert;
                tick?.Dispose();
                tick = null;
            }
            scanning = wanted;
        }
        if (start) adverts.Start();
        if (stop) adverts.Stop();
    }

    void OnAdvert(AppleAdvert advert)
    {
        if (advert.Model is null) return;
        ModelStream stream;
        lock (gate)
        {
            if (!devices.Any(d => d.ModelId == advert.ModelId)) return;
            if (!streams.TryGetValue(advert.ModelId, out stream!))
            {
                stream = new ModelStream(new AdvertMatcher(matcherOptions, time), new HeadphoneTracker(time));
                stream.Tracker.BatteryChanged += _ => Publish();
                stream.Tracker.PlaybackChanged += _ => Publish();
                streams[advert.ModelId] = stream;
            }
            stream.Matcher.Observe(advert);
            if (!stream.Matcher.Accept(advert)) return;
            stream.Last = advert;
        }
        stream.Tracker.OnAdvert(advert);
        Publish();
    }

    void Tick()
    {
        List<ModelStream> all;
        lock (gate) all = [.. streams.Values];
        foreach (var stream in all) stream.Matcher.CloseWindow();
        Publish();
    }

    void Publish()
    {
        lock (gate)
        {
            var next = devices.Select(WithRequest).ToList();
            if (next.SequenceEqual(published)) return;
            published = next;
        }
        Changed?.Invoke();
    }

    /// <summary>Adds the device's request, dropping a failure once the state has moved on. Caller holds <see cref="gate"/>.</summary>
    HeadphoneSnapshot WithRequest(PairedDevice device)
    {
        var snapshot = Snapshot(device);
        if (!requests.TryGetValue(device.Id, out var request)) return snapshot;
        if (!IsPending(request.Value) && request.FailedInState != snapshot.State)
        {
            requests.Remove(device.Id);
            return snapshot;
        }
        return snapshot with { Request = request.Value };
    }

    /// <summary>Caller holds <see cref="gate"/>.</summary>
    HeadphoneSnapshot Snapshot(PairedDevice device)
    {
        var stream = Target(device.ModelId) == device.Id ? streams.GetValueOrDefault(device.ModelId) : null;
        var advert = stream?.Last;
        var state = !adapterAvailable ? DeviceState.SearchingError
            : device.IsConnected ? DeviceState.Connected
            : advert is not null && !stream!.Tracker.IsStale ? DeviceState.Disconnected
            : DeviceState.Searching;
        return new HeadphoneSnapshot(
            Key: device.Id,
            Name: device.Name,
            ModelId: device.ModelId,
            Colour: advert?.Colour ?? "",
            State: state,
            Battery: advert is null ? null : stream!.Tracker.Battery,
            Playback: advert is null ? null : stream!.Tracker.Playback,
            IsSingleBattery: ModelCatalog.MaxFamilyIds.Contains(device.ModelId));
    }

    /// <summary>The device a model's advert stream is shown on. Caller holds <see cref="gate"/>.</summary>
    string? Target(string modelId)
    {
        var sameModel = devices.Where(d => d.ModelId == modelId).ToList();
        return sameModel.FirstOrDefault(d => d.IsConnected)?.Id
            ?? (lastConnected.TryGetValue(modelId, out var last) && sameModel.Any(d => d.Id == last) ? last : null)
            ?? sameModel.FirstOrDefault()?.Id;
    }

    sealed record PendingRequest(ConnectionRequest Value, ITimer? Timer, DeviceState? FailedInState = null);

    sealed class ModelStream(AdvertMatcher matcher, HeadphoneTracker tracker)
    {
        public AdvertMatcher Matcher { get; } = matcher;
        public HeadphoneTracker Tracker { get; } = tracker;
        public AppleAdvert? Last { get; set; }
    }
}
