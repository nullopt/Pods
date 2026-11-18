using System.Threading.Channels;
using Pods.Core.Aap;

namespace Pods.Core.Tests.Aap;

/// <summary>Frames pushed with <see cref="Receive"/> are read in order; <see cref="Close"/> ends reading.</summary>
internal sealed class FakeAapChannel : IAapChannel
{
    readonly Channel<byte[]> incoming = Channel.CreateUnbounded<byte[]>();
    readonly Lock gate = new();
    readonly List<byte[]> written = [];

    public Exception? WriteError { get; set; }
    public bool Disposed { get; private set; }

    public IReadOnlyList<byte[]> Written { get { lock (gate) return [.. written]; } }

    public void Receive(string hex) => incoming.Writer.TryWrite(Convert.FromHexString(hex.Replace(" ", "")));

    public void Close() => incoming.Writer.TryComplete();

    public void Fail(Exception error) => incoming.Writer.TryComplete(error);

    public Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken cancel)
    {
        if (WriteError is { } error) return Task.FromException(error);
        lock (gate) written.Add(frame.ToArray());
        return Task.CompletedTask;
    }

    public async Task<byte[]?> ReadAsync(CancellationToken cancel)
    {
        try { return await incoming.Reader.ReadAsync(cancel); }
        catch (ChannelClosedException e) when (e.InnerException is null) { return null; }
        catch (ChannelClosedException e) { throw e.InnerException!; }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        incoming.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
