using Pods.Core.Apple;

namespace Pods.Core.Tracking;

/// <summary>
/// Per-device state from accepted adverts: battery with a 30 s hold for readings that turn
/// unknown, playback debounced by 1 s, and staleness after 60 s.
/// Events are raised outside the internal lock, on the caller's or the timer's thread.
/// </summary>
public sealed class HeadphoneTracker(TimeProvider time) : IDisposable
{
    static readonly TimeSpan UnknownHold = TimeSpan.FromSeconds(30);
    static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);
    static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    readonly Lock gate = new();
    PodBattery? battery;
    DateTimeOffset? leftUnknownSince, rightUnknownSince, caseUnknownSince;
    Playback? stable, pending;
    ITimer? debounceTimer;
    DateTimeOffset? lastAdvert;

    public event Action<PodBattery>? BatteryChanged;
    public event Action<Playback>? PlaybackChanged;

    public PodBattery? Battery { get { lock (gate) return battery; } }

    /// <summary>Last stable (debounced) value.</summary>
    public Playback? Playback { get { lock (gate) return stable; } }

    public bool IsStale { get { lock (gate) return lastAdvert is null || time.GetUtcNow() - lastAdvert > StaleAfter; } }

    /// <summary>Feed only adverts the matcher accepted for this device.</summary>
    public void OnAdvert(AppleAdvert advert)
    {
        PodBattery? batteryToRaise = null;
        lock (gate)
        {
            var now = time.GetUtcNow();
            lastAdvert = now;

            var incoming = advert.Battery;
            var merged = incoming with
            {
                Left = Hold(incoming.Left, battery?.Left, ref leftUnknownSince, now),
                Right = Hold(incoming.Right, battery?.Right, ref rightUnknownSince, now),
                Case = Hold(incoming.Case, battery?.Case, ref caseUnknownSince, now),
            };
            if (battery != merged)
            {
                battery = merged;
                batteryToRaise = merged;
            }

            UpdatePlayback(advert.Playback);
        }
        if (batteryToRaise is { } b) BatteryChanged?.Invoke(b);
    }

    static int? Hold(int? incoming, int? current, ref DateTimeOffset? unknownSince, DateTimeOffset now)
    {
        if (incoming is not null || current is null)
        {
            unknownSince = null;
            return incoming;
        }
        unknownSince ??= now;
        return now - unknownSince >= UnknownHold ? null : current;
    }

    void UpdatePlayback(Playback? value)
    {
        if (value is null) return;
        if (stable is null)
        {
            stable = value;
            return;
        }
        if (value == stable)
        {
            pending = null;
            debounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }
        pending = value;
        debounceTimer ??= time.CreateTimer(_ => CommitPending(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        debounceTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    void CommitPending()
    {
        Playback committed;
        lock (gate)
        {
            if (pending is not { } p) return;
            stable = p;
            pending = null;
            committed = p;
        }
        PlaybackChanged?.Invoke(committed);
    }

    public void Dispose()
    {
        lock (gate)
        {
            debounceTimer?.Dispose();
            debounceTimer = null;
        }
    }
}
