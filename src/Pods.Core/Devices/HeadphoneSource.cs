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

    /// <summary>A lid opening further away than this (weaker RSSI) is ignored, so a set across the room stays quiet.</summary>
    public const short PopupMinRssi = -65;

    /// <summary>The lid counts as closed once no lid-open advert has arrived for this long (the case sends several a second while open).</summary>
    public static readonly TimeSpan LidSilence = TimeSpan.FromSeconds(3);

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

    public event Action<string>? LidOpened;

    public event Action<string>? LidClosed;

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
        PendingRequest request;
        lock (gate)
        {
            if (!adapterAvailable || !devices.Any(d => d.Id == key)) return;
            if (requests.TryGetValue(key, out var existing) && IsPending(existing.Value)) return;
            request = new PendingRequest(kind);
            requests[key] = request;
            request.Timer = time.CreateTimer(_ => Fail(key, request), null, RequestTimeout, Timeout.InfiniteTimeSpan);
        }
        Publish();

        Task<bool> sent;
        try
        {
            sent = kind == ConnectionRequest.Connecting ? control.ConnectAsync(key) : control.DisconnectAsync(key);
        }
        catch (Exception)
        {
            Fail(key, request);
            return;
        }
        _ = FailUnlessSent(sent, key, request);
    }

    async Task FailUnlessSent(Task<bool> sent, string key, PendingRequest request)
    {
        bool ok;
        try { ok = await sent.ConfigureAwait(false); }
        catch (Exception) { ok = false; }
        if (!ok) Fail(key, request);
    }

    /// <summary>Fails this exact request if it is still pending; a late result or timer of an older request does nothing.</summary>
    void Fail(string key, PendingRequest request)
    {
        lock (gate)
        {
            if (!requests.TryGetValue(key, out var current) || !ReferenceEquals(current, request) || !IsPending(request.Value)) return;
            request.Timer?.Dispose();
            request.Timer = null;
            request.FailedInState = published.FirstOrDefault(s => s.Key == key)?.State;
            request.Value = request.Value == ConnectionRequest.Connecting ? ConnectionRequest.ConnectFailed : ConnectionRequest.DisconnectFailed;
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
            foreach (var stream in streams.Values)
            {
                stream.Tracker.Dispose();
                CloseLid(stream);
            }
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
        string? lidDevice;
        bool lidOpened, accepted;
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
            // Before the matcher: a case that was shut comes back on a new address the matcher may not accept yet.
            lidDevice = TrackLid(stream, advert, out lidOpened);
            stream.Matcher.Observe(advert);
            accepted = stream.Matcher.Accept(advert);
            if (accepted) stream.Last = advert;
        }
        if (lidDevice is not null) (lidOpened ? LidOpened : LidClosed)?.Invoke(lidDevice);
        if (!accepted) return;
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
        var fresh = advert is not null && !stream!.Tracker.IsStale;
        var state = !adapterAvailable ? DeviceState.SearchingError
            : device.IsConnected ? DeviceState.Connected
            : fresh ? DeviceState.Disconnected
            : DeviceState.Searching;
        return new HeadphoneSnapshot(
            Key: device.Id,
            Name: device.Name,
            ModelId: device.ModelId,
            Colour: advert?.Colour ?? "",
            State: state,
            Battery: advert is null ? null : stream!.Tracker.Battery,
            Playback: advert is null ? null : stream!.Tracker.Playback,
            IsSingleBattery: ModelCatalog.SingleBatteryIds.Contains(device.ModelId),
            IsInCase: fresh && advert!.BothInCase);
    }

    /// <summary>
    /// Follows the lid of a model's stream: opens (once) on a lid-open advert strong enough, closes on a
    /// lid-shut advert or after <see cref="LidSilence"/> without a lid-open one. Returns the device whose
    /// lid changed, if any. Caller holds <see cref="gate"/>.
    /// </summary>
    string? TrackLid(ModelStream stream, AppleAdvert advert, out bool opened)
    {
        opened = false;
        if (advert.LidPopup is not bool open) return null;
        if (!open) return CloseLid(stream);
        if (stream.LidDevice is not null)
        {
            stream.LidTimer?.Change(LidSilence, Timeout.InfiniteTimeSpan);
            return null;
        }
        if (advert.Rssi < PopupMinRssi || Target(advert.ModelId) is not { } target) return null;
        var generation = ++stream.LidGeneration;
        stream.LidDevice = target;
        stream.LidTimer = time.CreateTimer(_ => LidSilent(stream, generation), null, LidSilence, Timeout.InfiniteTimeSpan);
        opened = true;
        return target;
    }

    /// <summary>Caller holds <see cref="gate"/>.</summary>
    static string? CloseLid(ModelStream stream)
    {
        var device = stream.LidDevice;
        stream.LidDevice = null;
        stream.LidTimer?.Dispose();
        stream.LidTimer = null;
        return device;
    }

    void LidSilent(ModelStream stream, int generation)
    {
        string? device;
        lock (gate) device = stream.LidGeneration == generation ? CloseLid(stream) : null;
        if (device is not null) LidClosed?.Invoke(device);
    }

    /// <summary>The device a model's advert stream is shown on. Caller holds <see cref="gate"/>.</summary>
    string? Target(string modelId)
    {
        var sameModel = devices.Where(d => d.ModelId == modelId).ToList();
        return sameModel.FirstOrDefault(d => d.IsConnected)?.Id
            ?? (lastConnected.TryGetValue(modelId, out var last) && sameModel.Any(d => d.Id == last) ? last : null)
            ?? sameModel.FirstOrDefault()?.Id;
    }

    sealed class PendingRequest(ConnectionRequest value)
    {
        public ConnectionRequest Value { get; set; } = value;
        public ITimer? Timer { get; set; }
        public DeviceState? FailedInState { get; set; }
    }

    sealed class ModelStream(AdvertMatcher matcher, HeadphoneTracker tracker)
    {
        public AdvertMatcher Matcher { get; } = matcher;
        public HeadphoneTracker Tracker { get; } = tracker;
        public AppleAdvert? Last { get; set; }
        public string? LidDevice { get; set; }   // the device whose lid is open, null while shut
        public ITimer? LidTimer { get; set; }
        public int LidGeneration { get; set; }
    }
}
