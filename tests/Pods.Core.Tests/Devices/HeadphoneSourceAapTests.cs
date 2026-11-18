using Microsoft.Extensions.Time.Testing;
using Pods.Core.Aap;
using Pods.Core.Apple;
using Pods.Core.Devices;
using Pods.Core.Tests.Aap;
using Pods.Core.Tests.Apple;

namespace Pods.Core.Tests.Devices;

public sealed class HeadphoneSourceAapTests : IDisposable
{
    sealed class Factory : IAapChannelFactory
    {
        public FakeAapChannel? Channel { get; private set; }
        public bool HasDriver { get; set; } = true;
        public int Opens;

        public Task<IAapChannel?> OpenAsync(string deviceKey, CancellationToken cancel)
        {
            Interlocked.Increment(ref Opens);
            if (!HasDriver) return Task.FromResult<IAapChannel?>(null);
            Channel = new FakeAapChannel();
            return Task.FromResult<IAapChannel?>(Channel);
        }
    }

    const string Key = "aabbccddeeff";
    static readonly PairedDevice Pro2 = new(Key, "Pro 2", "1420", true);

    readonly FakeTimeProvider time = new();
    readonly HeadphoneSourceTests.FakeAdvertSource adverts = new();
    readonly HeadphoneSourceTests.FakePairedSource paired = new();
    readonly Factory factory = new();
    readonly HeadphoneSource source;

    public HeadphoneSourceAapTests() =>
        source = new HeadphoneSource(paired, adverts, new HeadphoneSourceTests.FakeConnectionControl(), time, aap: new AapHub(factory, time));

    public void Dispose() => source.Dispose();

    HeadphoneSnapshot Card => source.Current.Single(c => c.Key == Key);

    static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    void BringUpAdverts()
    {
        AppleAdvert Make() => AdvertDecoder.Decode(Frames.Pro2(0x01), -50, 1)!;
        adverts.Raise(Make());
        adverts.Raise(Make());
        time.Advance(HeadphoneSource.WindowLength);
        adverts.Raise(Make());
    }

    [Fact]
    public async Task Connected_ExactBatteryReplacesAdvertPartByPart()
    {
        paired.Current = [Pro2];
        source.Start();
        BringUpAdverts();
        var advertBattery = Card.Battery!.Value;
        await Until(() => Card.CanControlNoise);
        factory.Channel!.Receive("04 00 04 00 04 00 01  04 01 5B 01 01");   // left 91, charging
        await Until(() => Card.Battery?.Left == 91);
        Assert.True(Card.Battery!.Value.LeftCharging);
        Assert.Equal(advertBattery.Right, Card.Battery!.Value.Right);
        Assert.Equal(advertBattery.Case, Card.Battery!.Value.Case);
    }

    [Fact]
    public async Task Connected_BatteryWithoutAnyAdvert()
    {
        paired.Current = [Pro2];
        source.Start();
        await Until(() => factory.Channel is not null);
        factory.Channel!.Receive("04 00 04 00 04 00 02  02 01 57 02 01  04 01 5B 02 01");
        await Until(() => Card.Battery is not null);
        Assert.Equal(new PodBattery(91, 87, null, false, false, false), Card.Battery);
    }

    [Fact]
    public async Task EarState_ReplacesAdvertPlayback()
    {
        paired.Current = [Pro2];
        source.Start();
        await Until(() => factory.Channel is not null);
        factory.Channel!.Receive("04 00 04 00 06 00 00 00");
        await Until(() => Card.Playback == Playback.AllIn);
        factory.Channel!.Receive("04 00 04 00 06 00 01 00");
        await Until(() => Card.Playback == Playback.Partial);
    }

    [Fact]
    public async Task NoiseControl_ReportedAndSet()
    {
        paired.Current = [Pro2];
        source.Start();
        await Until(() => Card.CanControlNoise);
        Assert.Null(Card.NoiseControl);
        factory.Channel!.Receive("04 00 04 00 09 00 0D 02 00 00 00");
        await Until(() => Card.NoiseControl == NoiseControlMode.NoiseCancellation);
        source.SetNoiseControl(Key, NoiseControlMode.Transparency);
        await Until(() => Card.NoiseControl == NoiseControlMode.Transparency);
        Assert.Equal(AapFrames.SetNoiseControl(NoiseControlMode.Transparency), factory.Channel.Written[^1]);
    }

    [Fact]
    public async Task Disconnect_ClosesSessionAndFallsBackToAdverts()
    {
        paired.Current = [Pro2];
        source.Start();
        BringUpAdverts();
        var advertBattery = Card.Battery;
        await Until(() => Card.CanControlNoise);
        factory.Channel!.Receive("04 00 04 00 04 00 01  04 01 5B 01 01");
        await Until(() => Card.Battery?.Left == 91);
        paired.Set(Pro2 with { IsConnected = false });
        Assert.False(Card.CanControlNoise);
        Assert.Equal(advertBattery, Card.Battery);
        await Until(() => factory.Channel.Disposed);
    }

    [Fact]
    public async Task NoDriver_SnapshotsAsBefore()
    {
        factory.HasDriver = false;
        paired.Current = [Pro2];
        source.Start();
        await Until(() => factory.Opens == 1);
        Assert.False(Card.CanControlNoise);
        Assert.Null(Card.NoiseControl);
        Assert.Null(Card.Battery);
    }

    [Fact]
    public async Task Stop_ClosesSessions()
    {
        paired.Current = [Pro2];
        source.Start();
        await Until(() => Card.CanControlNoise);
        source.Stop();
        await Until(() => factory.Channel!.Disposed);
    }
}
