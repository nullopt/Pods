using Microsoft.Extensions.Time.Testing;
using Pods.Core.Apple;
using Pods.Core.Devices;
using Pods.Core.Tests.Apple;

namespace Pods.Core.Tests.Devices;

public sealed class AdvertHeadphoneSourceTests : IDisposable
{
    sealed class FakeAdvertSource : IAppleAdvertSource
    {
        public event Action<AppleAdvert>? AdvertReceived;
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void Raise(AppleAdvert advert) => AdvertReceived?.Invoke(advert);
    }

    readonly FakeTimeProvider time = new();
    readonly FakeAdvertSource adverts = new();
    readonly AdvertHeadphoneSource source;
    readonly List<HeadphoneSnapshot> changes = [];

    public AdvertHeadphoneSourceTests()
    {
        source = new AdvertHeadphoneSource(adverts, time);
        source.Changed += changes.Add;
        source.Start();
    }

    public void Dispose() => source.Dispose();

    static AppleAdvert Pro2(short rssi = -50, ulong address = 1) => AdvertDecoder.Decode(Frames.Pro2(0x01), rssi, address)!;
    static AppleAdvert Max(short rssi = -50, ulong address = 2) => AdvertDecoder.Decode(Frames.Make(0x0a, 0x20, 0x02, 0x09, 0x80), rssi, address)!;

    /// <summary>Adverts during the first window, the window closes, then one advert is accepted.</summary>
    void BringUp(Func<AppleAdvert> make)
    {
        adverts.Raise(make());
        adverts.Raise(make());
        time.Advance(AdvertHeadphoneSource.WindowLength);
        adverts.Raise(make());
    }

    [Fact]
    public void Start_StartsAdvertSource() => Assert.True(adverts.Started);

    [Fact]
    public void NoCard_BeforeFirstWindowCloses()
    {
        adverts.Raise(Pro2());
        adverts.Raise(Pro2());
        Assert.Empty(source.Current);
        Assert.Empty(changes);
    }

    [Fact]
    public void Card_AppearsAfterWindowSelectsDevice()
    {
        BringUp(() => Pro2());
        var card = Assert.Single(source.Current);
        Assert.Equal("1420", card.Key);
        Assert.Equal("AirPods Pro 2", card.Name);
        Assert.Equal(DeviceState.Disconnected, card.State);
        Assert.Equal(Pro2().Battery, card.Battery);
        Assert.False(card.IsSingleBattery);
        Assert.Equal(card, Assert.Single(changes));
    }

    [Fact]
    public void UnknownModel_GetsNoCard()
    {
        BringUp(() => AdvertDecoder.Decode(Frames.Make(0xff, 0xff, 0x01, 0x58, 0x13), -50, 3)!);
        Assert.Empty(source.Current);
    }

    [Fact]
    public void WeakDevice_GetsNoCard()
    {
        BringUp(() => Pro2(rssi: -81));
        Assert.Empty(source.Current);
    }

    [Fact]
    public void StaleDevice_TurnsSearchingAndKeepsBattery()
    {
        BringUp(() => Pro2());
        var before = Assert.Single(source.Current);
        time.Advance(TimeSpan.FromSeconds(70));
        var after = Assert.Single(source.Current);
        Assert.Equal(DeviceState.Searching, after.State);
        Assert.Equal(before.Battery, after.Battery);
        Assert.Equal(after, changes[^1]);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void SameSnapshot_RaisesNoEvent()
    {
        BringUp(() => Pro2());
        for (var i = 0; i < 5; i++) adverts.Raise(Pro2());
        Assert.Single(changes);
    }

    [Fact]
    public void TwoModels_GetTwoCards()
    {
        adverts.Raise(Pro2());
        adverts.Raise(Max());
        time.Advance(AdvertHeadphoneSource.WindowLength);
        adverts.Raise(Pro2());
        adverts.Raise(Max());
        Assert.Equal(["1420", "0a20"], source.Current.Select(c => c.Key));
    }

    [Fact]
    public void Max_IsSingleBattery()
    {
        BringUp(() => Max());
        var card = Assert.Single(source.Current);
        Assert.True(card.IsSingleBattery);
        Assert.Equal(95, card.Battery?.Left);
    }

    [Fact]
    public void Stop_StopsTicks()
    {
        BringUp(() => Pro2());
        source.Stop();
        time.Advance(TimeSpan.FromSeconds(70));
        Assert.Single(changes);
        Assert.True(adverts.Stopped);
    }
}
