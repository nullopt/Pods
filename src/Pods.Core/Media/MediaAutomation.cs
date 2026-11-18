using Pods.Core.Apple;
using Pods.Core.Devices;
using Pods.Core.Settings;

namespace Pods.Core.Media;

public enum MediaActionKind { PausedForEar, Resumed, PausedOnDisconnect, PlayedOnConnect }

/// <summary>
/// Pauses media when an earbud comes out or the headset disconnects, resumes only what it paused
/// when both are back in, and can start playback when the headset connects. Acts only on changes
/// seen while a headset stays connected to this PC; a headset's first snapshot is a baseline.
/// </summary>
/// <remarks>
/// <see cref="Update"/> is called from one thread and only decides; the media calls run one at a
/// time, in order, on the thread pool. Only those queued actions touch what was paused.
/// </remarks>
public sealed class MediaAutomation(IMediaControl media)
{
    readonly Dictionary<string, (HeadphoneSnapshot Snapshot, MediaSettings Settings)> seen = [];
    readonly Dictionary<string, object> pausedBy = [];   // device key -> what it paused; queued actions only
    readonly Lock queueGate = new();
    Task tail = Task.CompletedTask;

    /// <summary>Raised after an action ran, with the device's key; on the thread pool.</summary>
    public event Action<string, MediaActionKind>? Acted;

    /// <summary>Raised when a media call failed; later events still act.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Call from one thread with every device (not only connected ones).</summary>
    public void Update(IReadOnlyList<HeadphoneSnapshot> snapshots, Func<string, MediaSettings> settingsFor)
    {
        foreach (var now in snapshots)
        {
            var settings = settingsFor(now.Key);
            if (seen.TryGetValue(now.Key, out var prev)) Compare(prev.Snapshot, now, settings);
            seen[now.Key] = (now, settings);
        }
        // A headset that vanished while connected (unpaired, adapter gone) counts as disconnected.
        foreach (var key in seen.Keys.Where(k => !snapshots.Any(s => s.Key == k)).ToList())
        {
            var (last, settings) = seen[key];
            seen.Remove(key);
            if (last.State == DeviceState.Connected) Enqueue(() => OnDisconnected(key, settings));
        }
    }

    /// <summary>Completes when the actions queued so far have run.</summary>
    public Task WhenIdle()
    {
        lock (queueGate) return tail;
    }

    void Compare(HeadphoneSnapshot prev, HeadphoneSnapshot now, MediaSettings settings)
    {
        var key = now.Key;
        bool was = prev.State == DeviceState.Connected, isNow = now.State == DeviceState.Connected;
        if (was && !isNow) Enqueue(() => OnDisconnected(key, settings));
        else if (!was && isNow && settings.PlayOnConnect) Enqueue(() => OnConnected(key));
        else if (was && isNow && prev.Playback is { } before && now.Playback is { } after && before != after)
        {
            if (after == Playback.AllIn) Enqueue(() => OnBackInEar(key));
            else if (before == Playback.AllIn && settings.EarDetection) Enqueue(() => OnOutOfEar(key));
        }
    }

    async Task OnOutOfEar(string key)
    {
        if (pausedBy.ContainsKey(key) || !await media.IsPlayingAsync()) return;
        if (await media.PauseAllAsync() is not { } paused) return;
        pausedBy[key] = paused;
        Acted?.Invoke(key, MediaActionKind.PausedForEar);
    }

    async Task OnBackInEar(string key)
    {
        if (!pausedBy.Remove(key, out var paused)) return;
        await media.ResumeAsync(paused);
        Acted?.Invoke(key, MediaActionKind.Resumed);
    }

    async Task OnDisconnected(string key, MediaSettings settings)
    {
        // Whatever happens, a reconnect later must not resume media paused before this disconnect.
        if (pausedBy.Remove(key)) return;
        if (!settings.PauseOnDisconnect || !await media.IsPlayingAsync()) return;
        if (await media.PauseAllAsync() is null) return;
        Acted?.Invoke(key, MediaActionKind.PausedOnDisconnect);
    }

    async Task OnConnected(string key)
    {
        if (await media.IsPlayingAsync()) return;
        media.PressPlayPause();
        Acted?.Invoke(key, MediaActionKind.PlayedOnConnect);
    }

    void Enqueue(Func<Task> action)
    {
        lock (queueGate) tail = tail.ContinueWith(_ => Run(action), TaskScheduler.Default).Unwrap();
    }

    async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Failed?.Invoke(ex); }
    }
}
