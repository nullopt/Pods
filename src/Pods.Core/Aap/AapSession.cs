using Pods.Core.Apple;

namespace Pods.Core.Aap;

/// <summary>One open AAP channel to a headset: one write = one frame out, one read = one frame in.</summary>
public interface IAapChannel : IAsyncDisposable
{
    Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancel);

    /// <summary>The next incoming frame; null once the channel has closed.</summary>
    Task<byte[]?> ReadAsync(CancellationToken cancel);
}

public interface IAapChannelFactory
{
    /// <summary>Opens a channel to the paired device; null when it has no AAP driver interface. Throws when opening failed.</summary>
    Task<IAapChannel?> OpenAsync(string deviceKey, CancellationToken cancel);
}

/// <summary>What AAP has told us about a headset. <see cref="Ready"/>: the handshake was sent, so commands go through.</summary>
public sealed record AapState(bool Ready, PodBattery? Battery, Playback? Playback, NoiseControlMode? NoiseControl);

/// <summary>
/// Runs one channel: starts reading, sends the handshake, then keeps <see cref="State"/> from the
/// notifications until the channel closes. Writes are sent one at a time.
/// </summary>
public sealed class AapSession(IAapChannel channel, AppleModel model)
{
    readonly Lock gate = new();
    readonly SemaphoreSlim writing = new(1, 1);
    AapState state = new(false, null, null, null);

    /// <summary>Raised on the reading thread when <see cref="State"/> changed.</summary>
    public event Action? Changed;

    /// <summary>Raised for every frame in (true) and out (false), for debug logging.</summary>
    public event Action<bool, ReadOnlyMemory<byte>>? Frame;

    public AapState State { get { lock (gate) return state; } }

    /// <summary>Completes when the channel closes or <paramref name="cancel"/> fires; throws if reading or the handshake failed.</summary>
    public async Task RunAsync(CancellationToken cancel)
    {
        // Reading starts first, so nothing the headset answers to the handshake is missed.
        var reading = ReadLoopAsync(cancel);
        try
        {
            await WriteAsync(AapFrames.Handshake, cancel).ConfigureAwait(false);
            await WriteAsync(AapFrames.EnableNotifications, cancel).ConfigureAwait(false);
            if (model.Capabilities.HasFlag(AppleCapabilities.AdaptiveNoise))
                await WriteAsync(AapFrames.ExtendedInit, cancel).ConfigureAwait(false);
        }
        catch
        {
            // The caller closes the channel, which ends the read; its outcome no longer matters.
            _ = reading.ContinueWith(t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw;
        }
        Update(s => s with { Ready = true });
        await reading.ConfigureAwait(false);
    }

    async Task ReadLoopAsync(CancellationToken cancel)
    {
        await Task.Yield();
        while (await channel.ReadAsync(cancel).ConfigureAwait(false) is { } frame)
        {
            Frame?.Invoke(true, frame);
            switch (AapParser.Parse(frame))
            {
                case AapBatteryMessage b:
                    Update(s => s with { Battery = b.Battery });
                    break;
                case AapEarMessage e:
                    Update(s => s with { Playback = e.Playback });
                    break;
                case AapNoiseControlMessage n when model.Capabilities.HasFlag(AppleCapabilities.NoiseControl):
                    Update(s => s with { NoiseControl = n.Mode });
                    break;
            }
        }
    }

    /// <summary>Sends the mode and shows it at once; the headset confirms with a notification.</summary>
    public async Task SetNoiseControlAsync(NoiseControlMode mode, CancellationToken cancel)
    {
        if (!Supports(model, mode) || !State.Ready) return;
        await WriteAsync(AapFrames.SetNoiseControl(mode), cancel).ConfigureAwait(false);
        Update(s => s with { NoiseControl = mode });
    }

    /// <summary>The model can be switched to this mode.</summary>
    public static bool Supports(AppleModel model, NoiseControlMode mode) =>
        model.Capabilities.HasFlag(AppleCapabilities.NoiseControl)
        && (mode != NoiseControlMode.Adaptive || model.Capabilities.HasFlag(AppleCapabilities.AdaptiveNoise));

    async Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancel)
    {
        await writing.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            Frame?.Invoke(false, frame);
            await channel.WriteAsync(frame, cancel).ConfigureAwait(false);
        }
        finally
        {
            writing.Release();
        }
    }

    void Update(Func<AapState, AapState> change)
    {
        bool changed;
        lock (gate)
        {
            var next = change(state);
            changed = next != state;
            state = next;
        }
        if (changed) Changed?.Invoke();
    }
}
