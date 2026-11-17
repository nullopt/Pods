using Pods.Core.Apple;

namespace Pods.Core.Tracking;

/// <param name="KnownAddressTtl">How long a known address survives; null = 15 min (SPEC §5.2).</param>
public sealed record MatcherOptions(short MinRssi = -60, short MaxRssi = 0, bool SearchByRssi = false, TimeSpan? KnownAddressTtl = null);

/// <summary>
/// Picks one device's rotating advert address (SPEC §5.2). The owner calls <see cref="CloseWindow"/>
/// every 10 s and <see cref="Observe"/> for every advert of the tracked model before <see cref="Accept"/>.
/// The selection sticks while its address stays in range, survives weak or empty windows, and
/// hands over at once to another in-range address after <see cref="HandoverSilence"/> of silence
/// (each pod advertises from its own address; docs/protocol-apple.md).
/// Thread-safe: adverts and the window tick may arrive on different threads.
/// </summary>
public sealed class AdvertMatcher(MatcherOptions options, TimeProvider time)
{
    public static readonly TimeSpan HandoverSilence = TimeSpan.FromSeconds(3);

    readonly TimeSpan ttl = options.KnownAddressTtl ?? TimeSpan.FromMinutes(15);
    readonly Dictionary<ulong, (int Count, long RssiSum)> window = [];
    readonly Dictionary<ulong, DateTimeOffset> known = [];
    readonly Lock gate = new();
    AppleAdvert? lastAccepted;
    ulong? selected;
    DateTimeOffset selectedSeen;

    public ulong? SelectedAddress { get { lock (gate) return selected; } }

    public void Observe(AppleAdvert advert)
    {
        lock (gate)
        {
            var (count, sum) = window.GetValueOrDefault(advert.Address);
            window[advert.Address] = (count + 1, sum + advert.Rssi);
        }
    }

    public void CloseWindow()
    {
        lock (gate)
        {
            if (window.Count > 0)
            {
                ulong? best = null;
                long bestAvg = long.MinValue;
                var selectedInRange = false;
                foreach (var (address, (count, sum)) in window)
                {
                    var avg = sum / count;
                    if (!InRange(avg)) continue;
                    if (address == selected) selectedInRange = true;
                    if (avg > bestAvg)
                    {
                        best = address;
                        bestAvg = avg;
                    }
                }
                if (!selectedInRange && best is not null)
                {
                    selected = best;
                    selectedSeen = time.GetUtcNow();
                }
                window.Clear();
            }

            var now = time.GetUtcNow();
            foreach (var address in known.Where(k => now - k.Value > ttl).Select(k => k.Key).ToList())
                known.Remove(address);
        }
    }

    public bool Accept(AppleAdvert advert)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            bool accepted;
            if (advert.Address == selected)
            {
                selectedSeen = now;
                accepted = true;
            }
            else if (selected is not null && now - selectedSeen >= HandoverSilence && InRange(advert.Rssi))
            {
                selected = advert.Address;
                selectedSeen = now;
                accepted = true;
            }
            else if (!options.SearchByRssi) accepted = false;
            else if (InRange(advert.Rssi)) accepted = true;
            else if (known.ContainsKey(advert.Address)) accepted = true;
            else if (lastAccepted is not null && advert.SameState(lastAccepted))
            {
                known[advert.Address] = now;
                accepted = true;
            }
            else accepted = false;

            if (accepted) lastAccepted = advert;
            return accepted;
        }
    }

    bool InRange(long rssi) => rssi >= options.MinRssi && rssi <= options.MaxRssi;
}
