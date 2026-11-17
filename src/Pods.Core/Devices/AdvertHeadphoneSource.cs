using Pods.Core.Apple;
using Pods.Core.Tracking;

namespace Pods.Core.Devices;

/// <summary>
/// Turns adverts into device snapshots: one matcher and tracker per catalogued model until Phase 2
/// links adverts to paired devices. A device appears once its matcher has accepted an advert, and
/// shows <see cref="DeviceState.Searching"/> with its last values while stale. The window tick runs
/// only between <see cref="Start"/> and <see cref="Stop"/> to keep idle cost at zero.
/// </summary>
public sealed class AdvertHeadphoneSource(IAppleAdvertSource adverts, TimeProvider time, MatcherOptions? options = null)
    : IHeadphoneSource, IDisposable
{
    public static readonly TimeSpan WindowLength = TimeSpan.FromSeconds(10);

    readonly MatcherOptions matcherOptions = options ?? new MatcherOptions();
    readonly Lock gate = new();
    readonly Dictionary<string, Entry> entries = [];
    readonly List<Entry> shown = [];
    ITimer? tick;

    public event Action<HeadphoneSnapshot>? Changed;

    public IReadOnlyList<HeadphoneSnapshot> Current
    {
        get { lock (gate) return [.. shown.Select(e => e.Published!)]; }
    }

    public void Start()
    {
        lock (gate)
        {
            if (tick is not null) return;
            adverts.AdvertReceived += OnAdvert;
            tick = time.CreateTimer(_ => Tick(), null, WindowLength, WindowLength);
        }
        adverts.Start();
    }

    public void Stop()
    {
        lock (gate)
        {
            if (tick is null) return;
            tick.Dispose();
            tick = null;
            adverts.AdvertReceived -= OnAdvert;
        }
        adverts.Stop();
    }

    public void Dispose()
    {
        Stop();
        lock (gate)
        {
            foreach (var entry in entries.Values) entry.Tracker.Dispose();
        }
    }

    void OnAdvert(AppleAdvert advert)
    {
        if (advert.Model is not { } model) return;
        Entry entry;
        lock (gate)
        {
            if (!entries.TryGetValue(advert.ModelId, out entry!))
            {
                entry = new Entry(model, new AdvertMatcher(matcherOptions, time), new HeadphoneTracker(time));
                var created = entry;
                entry.Tracker.BatteryChanged += _ => Publish(created);
                entry.Tracker.PlaybackChanged += _ => Publish(created);
                entries[advert.ModelId] = entry;
            }
            entry.Matcher.Observe(advert);
            if (!entry.Matcher.Accept(advert)) return;
            entry.Last = advert;
        }
        entry.Tracker.OnAdvert(advert);
        Publish(entry);
    }

    void Tick()
    {
        List<Entry> all;
        lock (gate) all = [.. entries.Values];
        foreach (var entry in all)
        {
            entry.Matcher.CloseWindow();
            Publish(entry);
        }
    }

    void Publish(Entry entry)
    {
        HeadphoneSnapshot snapshot;
        lock (gate)
        {
            if (entry.Last is not { } last) return;
            snapshot = new HeadphoneSnapshot(
                Key: entry.Model.Id,
                Name: entry.Model.Name,
                ModelId: entry.Model.Id,
                Colour: last.Colour,
                State: entry.Tracker.IsStale ? DeviceState.Searching : DeviceState.Disconnected,
                Battery: entry.Tracker.Battery,
                Playback: entry.Tracker.Playback,
                IsSingleBattery: ModelCatalog.MaxFamilyIds.Contains(entry.Model.Id));
            if (snapshot == entry.Published) return;
            if (entry.Published is null) shown.Add(entry);
            entry.Published = snapshot;
        }
        Changed?.Invoke(snapshot);
    }

    sealed class Entry(AppleModel model, AdvertMatcher matcher, HeadphoneTracker tracker)
    {
        public AppleModel Model { get; } = model;
        public AdvertMatcher Matcher { get; } = matcher;
        public HeadphoneTracker Tracker { get; } = tracker;
        public AppleAdvert? Last { get; set; }
        public HeadphoneSnapshot? Published { get; set; }
    }
}
