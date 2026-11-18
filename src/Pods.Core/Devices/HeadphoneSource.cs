using Pods.Core.Aap;
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
/// shown until the device's state next changes. Lid and auto-connect signals are raised as events;
/// the app decides from its settings whether to act on them. With an <see cref="AapHub"/>, connected
/// devices that have the AAP driver also report over AAP: its exact battery replaces the advert's
/// part by part while known, its ear state replaces the advert's, and noise control becomes available.
/// The source owns the hub and disposes it.
/// </summary>
public sealed class HeadphoneSource(
    IPairedDeviceSource paired, IAppleAdvertSource adverts, IConnectionControl control, TimeProvider time, MatcherOptions? options = null,
    AapHub? aap = null)
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

    public event Action<string>? AutoConnectReady;

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
        if (aap is not null) aap.Changed += Publish;
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
        if (aap is not null)
        {
            aap.Changed -= Publish;
            aap.Update([]);
        }
        UpdateScanning();
        paired.Stop();
    }

    public void Connect(string key) => Request(key, ConnectionRequest.Connecting);

    public void Disconnect(string key) => Request(key, ConnectionRequest.Disconnecting);

    public void SetNoiseControl(string key, NoiseControlMode mode) => aap?.SetNoiseControl(key, mode);

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
        aap?.Dispose();
    }

    void OnPairedChanged()
    {
        List<PairedDevice> forAap;
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
            forAap = started && adapterAvailable ? devices : [];
        }
        aap?.Update(forAap);
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
        string? lidDevice, connectDevice;
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
            connectDevice = TrackTakeOut(stream, advert);
            stream.Matcher.Observe(advert);
            accepted = stream.Matcher.Accept(advert);
            if (accepted) stream.Last = advert;
        }
        if (lidDevice is not null) (lidOpened ? LidOpened : LidClosed)?.Invoke(lidDevice);
        if (connectDevice is not null) AutoConnectReady?.Invoke(connectDevice);
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
        var aapState = state == DeviceState.Connected ? aap?.StateOf(device.Id) : null;
        var model = ModelCatalog.Find(device.ModelId);
        return new HeadphoneSnapshot(
            Key: device.Id,
            Name: device.Name,
            ModelId: device.ModelId,
            Colour: advert?.Colour ?? "",
            State: state,
            Battery: Overlay(aapState?.Battery, advert is null ? null : stream!.Tracker.Battery),
            Playback: aapState?.Playback ?? (advert is null ? null : stream!.Tracker.Playback),
            IsSingleBattery: ModelCatalog.SingleBatteryIds.Contains(device.ModelId),
            IsInCase: fresh && advert!.BothInCase,
            CanControlNoise: aapState is { Ready: true } && model is not null && model.Capabilities.HasFlag(AppleCapabilities.NoiseControl),
            NoiseControl: aapState?.NoiseControl);
    }

    /// <summary>AAP's exact values where it knows them, with their charging flags; the advert's elsewhere.</summary>
    static PodBattery? Overlay(PodBattery? exact, PodBattery? advert)
    {
        if (exact is not { } a) return advert;
        var b = advert ?? new PodBattery(null, null, null, false, false, false);
        return new PodBattery(
            a.Left ?? b.Left, a.Right ?? b.Right, a.Case ?? b.Case,
            a.Left is null ? b.LeftCharging : a.LeftCharging,
            a.Right is null ? b.RightCharging : a.RightCharging,
            a.Case is null ? b.CaseCharging : a.CaseCharging);
    }

    /// <summary>
    /// Follows the lid of a model's stream. Opens (once) on a lid-open advert strong enough. While open,
    /// only adverts from the addresses that reported the opening count: one of them saying the lid shut
    /// with both pods inside closes it, any of them keeps it from going silent, and
    /// <see cref="LidSilence"/> without them closes it. So earbuds taken out of the open case, the end
    /// of the AirPods Max's short transient, or another set nearby do not close it. Returns the device
    /// whose lid changed, if any. Caller holds <see cref="gate"/>.
    /// </summary>
    string? TrackLid(ModelStream stream, AppleAdvert advert, out bool opened)
    {
        opened = false;
        if (advert.LidPopup is not bool open) return null;
        if (stream.LidDevice is not null)
        {
            if (open) stream.LidAddresses.Add(advert.Address);
            if (!stream.LidAddresses.Contains(advert.Address)) return null;
            if (!open && advert.BothInCase) return CloseLid(stream);
            stream.LidTimer?.Change(LidSilence, Timeout.InfiniteTimeSpan);
            return null;
        }
        if (!open || advert.Rssi < PopupMinRssi || Target(advert.ModelId) is not { } target) return null;
        var generation = ++stream.LidGeneration;
        stream.LidDevice = target;
        stream.TakeOutPending = true;
        stream.LidAddresses.Add(advert.Address);
        stream.LidTimer = time.CreateTimer(_ => LidSilent(stream, generation), null, LidSilence, Timeout.InfiniteTimeSpan);
        opened = true;
        return target;
    }

    /// <summary>
    /// After a lid opening (see <see cref="TrackLid"/>), the first advert from one of the opening's
    /// addresses with the pods no longer both in the case means they were taken out. That is checked
    /// once per opening, so a failed connect is not retried on every advert and disconnecting by hand
    /// while the pods are out does not connect them straight back. Returns the device to connect, if
    /// its model can auto connect and it is not connected already. (The adverts' own "connect" bit,
    /// status bit 6, was measured on 2026-11-18 to appear only while pods go back into the case.)
    /// Caller holds <see cref="gate"/>.
    /// </summary>
    string? TrackTakeOut(ModelStream stream, AppleAdvert advert)
    {
        if (!stream.TakeOutPending || stream.LidDevice is not { } device
            || !stream.LidAddresses.Contains(advert.Address) || advert.BothInCase) return null;
        stream.TakeOutPending = false;
        var capable = advert.Model is { } model && model.Capabilities.HasFlag(AppleCapabilities.AutoConnect);
        return capable && devices.FirstOrDefault(d => d.Id == device) is { IsConnected: false } ? device : null;
    }

    /// <summary>Caller holds <see cref="gate"/>.</summary>
    static string? CloseLid(ModelStream stream)
    {
        var device = stream.LidDevice;
        stream.LidDevice = null;
        stream.LidAddresses.Clear();
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
        public HashSet<ulong> LidAddresses { get; } = [];   // the pods that reported the opening
        public int LidGeneration { get; set; }
        public bool TakeOutPending { get; set; }   // the open lid's pods have not been taken out yet
    }
}
