using Microsoft.Extensions.Time.Testing;
using Pods.Core.Aap;
using Pods.Core.Devices;

namespace Pods.Core.Tests.Aap;

public sealed class AapHubTests : IDisposable
{
    sealed class FakeFactory : IAapChannelFactory
    {
        readonly Lock gate = new();
        readonly List<string> opened = [];
        public Func<string, IAapChannel?> Next { get; set; } = _ => new FakeAapChannel();
        public List<FakeAapChannel> Channels { get; } = [];
        public IReadOnlyList<string> Opened { get { lock (gate) return [.. opened]; } }

        public Task<IAapChannel?> OpenAsync(string deviceKey, CancellationToken cancel)
        {
            lock (gate) opened.Add(deviceKey);
            var channel = Next(deviceKey);
            if (channel is FakeAapChannel fake) lock (gate) Channels.Add(fake);
            return Task.FromResult(channel);
        }
    }

    const string ProKey = "aabbccddeeff";
    static readonly PairedDevice Pro2 = new(ProKey, "Pro 2", "1420", true);

    readonly FakeTimeProvider time = new();
    readonly FakeFactory factory = new();
    readonly AapHub hub;

    public AapHubTests() => hub = new AapHub(factory, time);

    public void Dispose() => hub.Dispose();

    static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    /// <summary>The hub starts its retry wait some time after the failure; advance until the retry has happened.</summary>
    async Task RetryUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            time.Advance(AapHub.RetryDelay);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Assert.True(condition());
    }

    [Fact]
    public async Task Connected_OpensSessionAndReportsState()
    {
        var changed = 0;
        hub.Changed += () => Interlocked.Increment(ref changed);
        hub.Update([Pro2]);
        await Until(() => hub.StateOf(ProKey)?.Ready == true);
        factory.Channels[0].Receive("04 00 04 00 04 00 01  02 01 57 02 01");
        await Until(() => hub.StateOf(ProKey)?.Battery?.Right == 87);
        Assert.True(changed >= 2);
    }

    [Fact]
    public async Task NotConnected_NeverOpens()
    {
        hub.Update([Pro2 with { IsConnected = false }, new PairedDevice("112233445566", "Other", "ffff", true)]);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(factory.Opened);
    }

    [Fact]
    public async Task Disconnected_ClosesChannelAndClearsState()
    {
        hub.Update([Pro2]);
        await Until(() => hub.StateOf(ProKey)?.Ready == true);
        hub.Update([Pro2 with { IsConnected = false }]);
        Assert.Null(hub.StateOf(ProKey));
        await Until(() => factory.Channels[0].Disposed);
    }

    [Fact]
    public async Task NoDriver_NotAskedAgainUntilReconnect()
    {
        factory.Next = _ => null;
        hub.Update([Pro2]);
        await Until(() => factory.Opened.Count == 1);
        time.Advance(AapHub.RetryDelay * 3);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(factory.Opened);
        Assert.Null(hub.StateOf(ProKey));

        hub.Update([Pro2 with { IsConnected = false }]);
        hub.Update([Pro2]);
        await Until(() => factory.Opened.Count == 2);
    }

    [Fact]
    public async Task Dropped_ReopensAfterRetryDelay()
    {
        var errors = 0;
        hub.Failed += (_, _) => Interlocked.Increment(ref errors);
        hub.Update([Pro2]);
        await Until(() => hub.StateOf(ProKey)?.Ready == true);
        factory.Channels[0].Fail(new IOException("dropped"));
        await Until(() => hub.StateOf(ProKey) is null);
        Assert.Equal(1, errors);
        Assert.Single(factory.Opened);
        await RetryUntil(() => hub.StateOf(ProKey)?.Ready == true);
        Assert.Equal(2, factory.Opened.Count);
    }

    [Fact]
    public async Task OpenFailure_Retries()
    {
        var calls = 0;
        factory.Next = _ => Interlocked.Increment(ref calls) == 1 ? throw new IOException("semaphore timeout") : new FakeAapChannel();
        hub.Update([Pro2]);
        await Until(() => factory.Opened.Count == 1);
        await RetryUntil(() => hub.StateOf(ProKey)?.Ready == true);
        Assert.Equal(2, factory.Opened.Count);
    }

    [Fact]
    public async Task SetNoiseControl_GoesToTheDevicesChannel()
    {
        hub.Update([Pro2]);
        await Until(() => hub.StateOf(ProKey)?.Ready == true);
        hub.SetNoiseControl(ProKey, NoiseControlMode.Transparency);
        await Until(() => hub.StateOf(ProKey)?.NoiseControl == NoiseControlMode.Transparency);
        Assert.Equal(AapFrames.SetNoiseControl(NoiseControlMode.Transparency), factory.Channels[0].Written[^1]);
    }

    [Fact]
    public async Task Dispose_ClosesEverything()
    {
        hub.Update([Pro2]);
        await Until(() => hub.StateOf(ProKey)?.Ready == true);
        hub.Dispose();
        await Until(() => factory.Channels[0].Disposed);
        hub.Update([Pro2]);
        Assert.Single(factory.Opened);
    }
}
