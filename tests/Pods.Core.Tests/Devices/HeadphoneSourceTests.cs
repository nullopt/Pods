using Microsoft.Extensions.Time.Testing;
using Pods.Core.Apple;
using Pods.Core.Devices;
using Pods.Core.Tests.Apple;

namespace Pods.Core.Tests.Devices;

public sealed class HeadphoneSourceTests : IDisposable
{
    sealed class FakeAdvertSource : IAppleAdvertSource
    {
        public event Action<AppleAdvert>? AdvertReceived;
        public bool Running { get; private set; }
        public int StartCount { get; private set; }
        public void Start() { Running = true; StartCount++; }
        public void Stop() => Running = false;
        public void Raise(AppleAdvert advert) => AdvertReceived?.Invoke(advert);
    }

    sealed class FakePairedSource : IPairedDeviceSource
    {
        public IReadOnlyList<PairedDevice> Current { get; set; } = [];
        public bool IsAdapterAvailable { get; set; } = true;
        public event Action? Changed;
        public void Start() { }
        public void Stop() { }
        public void Set(params PairedDevice[] devices) { Current = devices; Changed?.Invoke(); }
        public void Raise() => Changed?.Invoke();
    }

    internal sealed class FakeConnectionControl : IConnectionControl
    {
        public List<string> ConnectCalls { get; } = [];
        public List<string> DisconnectCalls { get; } = [];
        public Func<Task<bool>> Result { get; set; } = () => Task.FromResult(true);
        public Task<bool> ConnectAsync(string id) { ConnectCalls.Add(id); return Result(); }
        public Task<bool> DisconnectAsync(string id) { DisconnectCalls.Add(id); return Result(); }
    }

    const string MaxId = "08ff4427c5d0";
    const string Max2Id = "aabbccddeeff";
    static readonly PairedDevice MaxDevice = new(MaxId, "Billy's AirPods Max", "0a20", false);
    static readonly PairedDevice Max2Device = new(Max2Id, "Second Max", "0a20", false);

    readonly FakeTimeProvider time = new();
    readonly FakeAdvertSource adverts = new();
    readonly FakePairedSource paired = new();
    readonly FakeConnectionControl control = new();
    readonly HeadphoneSource source;
    int changes;

    public HeadphoneSourceTests()
    {
        source = new HeadphoneSource(paired, adverts, control, time);
        source.Changed += () => changes++;
    }

    public void Dispose() => source.Dispose();

    static AppleAdvert Pro2(short rssi = -50, ulong address = 1) => AdvertDecoder.Decode(Frames.Pro2(0x01), rssi, address)!;
    static AppleAdvert Max(short rssi = -50, ulong address = 2) => AdvertDecoder.Decode(Frames.Make(0x0a, 0x20, 0x02, 0x09, 0x80), rssi, address)!;

    /// <summary>Adverts during the first window, the window closes, then one advert is accepted.</summary>
    void BringUp(Func<AppleAdvert> make)
    {
        adverts.Raise(make());
        adverts.Raise(make());
        time.Advance(HeadphoneSource.WindowLength);
        adverts.Raise(make());
    }

    void StartWith(params PairedDevice[] devices)
    {
        paired.Current = devices;
        source.Start();
    }

    HeadphoneSnapshot Card(string id) => source.Current.Single(c => c.Key == id);

    [Fact]
    public void PairedDevice_GetsCardBeforeAnyAdvert()
    {
        StartWith(MaxDevice);
        var card = Assert.Single(source.Current);
        Assert.Equal(MaxId, card.Key);
        Assert.Equal("Billy's AirPods Max", card.Name);
        Assert.Equal("0a20", card.ModelId);
        Assert.Equal(DeviceState.Searching, card.State);
        Assert.Null(card.Battery);
        Assert.Equal("", card.Colour);
        Assert.True(card.IsSingleBattery);
    }

    [Fact]
    public void UncataloguedModel_GetsNoCard()
    {
        StartWith(new PairedDevice("c4c17d6ff526", "iPhone", "1d76", false));
        Assert.Empty(source.Current);
    }

    [Fact]
    public void Connected_IsConnectedState()
    {
        StartWith(MaxDevice with { IsConnected = true });
        Assert.Equal(DeviceState.Connected, Card(MaxId).State);
    }

    [Fact]
    public void AdvertsRouted_DisconnectedWithBattery()
    {
        StartWith(MaxDevice);
        BringUp(() => Max());
        var card = Card(MaxId);
        Assert.Equal(DeviceState.Disconnected, card.State);
        Assert.Equal(95, card.Battery?.Left);
    }

    [Fact]
    public void StaleAdverts_SearchingKeepsBattery()
    {
        StartWith(MaxDevice);
        BringUp(() => Max());
        time.Advance(TimeSpan.FromSeconds(70));
        var card = Card(MaxId);
        Assert.Equal(DeviceState.Searching, card.State);
        Assert.Equal(95, card.Battery?.Left);
    }

    [Fact]
    public void UnpairedModelAdverts_Ignored()
    {
        StartWith(MaxDevice);
        var before = changes;
        BringUp(() => Pro2());
        Assert.Equal([MaxId], source.Current.Select(c => c.Key));
        Assert.Equal(before, changes);
    }

    [Fact]
    public void Rename_UpdatesName()
    {
        StartWith(MaxDevice);
        var before = changes;
        paired.Set(MaxDevice with { Name = "Desk Max" });
        Assert.Equal("Desk Max", Card(MaxId).Name);
        Assert.Equal(before + 1, changes);
    }

    [Fact]
    public void Unpair_RemovesCard()
    {
        StartWith(MaxDevice);
        var before = changes;
        paired.Set();
        Assert.Empty(source.Current);
        Assert.Equal(before + 1, changes);
    }

    [Fact]
    public void TwoSameModel_RoutesToConnected()
    {
        StartWith(MaxDevice, Max2Device with { IsConnected = true });
        BringUp(() => Max());
        Assert.Null(Card(MaxId).Battery);
        Assert.Equal(95, Card(Max2Id).Battery?.Left);
    }

    [Fact]
    public void TwoSameModel_RoutesToLastConnected()
    {
        StartWith(MaxDevice, Max2Device with { IsConnected = true });
        paired.Set(MaxDevice, Max2Device);
        BringUp(() => Max());
        Assert.Null(Card(MaxId).Battery);
        Assert.Equal(95, Card(Max2Id).Battery?.Left);
    }

    [Fact]
    public void TwoSameModel_RoutesToFirstByDefault()
    {
        StartWith(MaxDevice, Max2Device);
        BringUp(() => Max());
        Assert.Equal(95, Card(MaxId).Battery?.Left);
        Assert.Null(Card(Max2Id).Battery);
    }

    [Fact]
    public void AdapterOff_AllSearchingError()
    {
        StartWith(MaxDevice with { IsConnected = true }, Max2Device);
        paired.IsAdapterAvailable = false;
        paired.Raise();
        Assert.False(source.IsAdapterAvailable);
        Assert.All(source.Current, c => Assert.Equal(DeviceState.SearchingError, c.State));
    }

    [Fact]
    public void AdapterOff_KeepsCardsAndIgnoresRemovals()
    {
        StartWith(MaxDevice);
        paired.IsAdapterAvailable = false;
        paired.Set();
        Assert.Equal(DeviceState.SearchingError, Card(MaxId).State);
        paired.IsAdapterAvailable = true;
        paired.Raise();
        Assert.Empty(source.Current);
    }

    [Fact]
    public void NoPairedDevice_DoesNotScan()
    {
        StartWith();
        Assert.False(adverts.Running);
    }

    [Fact]
    public void FirstPairedDevice_StartsScanning_LastStops()
    {
        StartWith();
        paired.Set(MaxDevice);
        Assert.True(adverts.Running);
        paired.Set();
        Assert.False(adverts.Running);
    }

    [Fact]
    public void AdapterOff_StopsScanning()
    {
        StartWith(MaxDevice);
        paired.IsAdapterAvailable = false;
        paired.Raise();
        Assert.False(adverts.Running);
    }

    [Fact]
    public void Restart_KeepsCardsUntilPairedListChanges()
    {
        StartWith(MaxDevice);
        BringUp(() => Max());
        var before = source.Current;
        var count = changes;
        source.Stop();
        Assert.False(adverts.Running);
        source.Start();
        Assert.True(adverts.Running);
        Assert.Equal(before, source.Current);
        Assert.Equal(count, changes);
    }

    [Fact]
    public void SameSnapshot_RaisesNoEvent()
    {
        StartWith(MaxDevice);
        BringUp(() => Max());
        var count = changes;
        for (var i = 0; i < 5; i++) adverts.Raise(Max());
        Assert.Equal(count, changes);
    }

    // Requests

    [Fact]
    public void Connect_SetsConnectingAndCallsControl()
    {
        control.Result = () => new TaskCompletionSource<bool>().Task;
        StartWith(MaxDevice);
        source.Connect(MaxId);
        Assert.Equal(ConnectionRequest.Connecting, Card(MaxId).Request);
        Assert.Equal([MaxId], control.ConnectCalls);
    }

    [Fact]
    public void Connect_CompletesOnConnectedEvent()
    {
        StartWith(MaxDevice);
        source.Connect(MaxId);
        paired.Set(MaxDevice with { IsConnected = true });
        Assert.Equal(DeviceState.Connected, Card(MaxId).State);
        Assert.Equal(ConnectionRequest.None, Card(MaxId).Request);
    }

    [Fact]
    public void Connect_TimesOutToFailed()
    {
        StartWith(MaxDevice);
        source.Connect(MaxId);
        time.Advance(HeadphoneSource.RequestTimeout);
        Assert.Equal(ConnectionRequest.ConnectFailed, Card(MaxId).Request);
    }

    [Fact]
    public void Connect_ControlFalse_Failed()
    {
        control.Result = () => Task.FromResult(false);
        StartWith(MaxDevice);
        source.Connect(MaxId);
        Assert.Equal(ConnectionRequest.ConnectFailed, Card(MaxId).Request);
    }

    [Fact]
    public void Connect_ControlThrows_Failed()
    {
        control.Result = () => throw new InvalidOperationException("boom");
        StartWith(MaxDevice);
        source.Connect(MaxId);
        Assert.Equal(ConnectionRequest.ConnectFailed, Card(MaxId).Request);
    }

    [Fact]
    public void Connect_ControlFaultedTask_Failed()
    {
        control.Result = () => Task.FromException<bool>(new InvalidOperationException("boom"));
        StartWith(MaxDevice);
        source.Connect(MaxId);
        Assert.Equal(ConnectionRequest.ConnectFailed, Card(MaxId).Request);
    }

    [Fact]
    public void Connect_WhilePending_IsIgnored()
    {
        StartWith(MaxDevice);
        source.Connect(MaxId);
        source.Connect(MaxId);
        Assert.Single(control.ConnectCalls);
    }

    [Fact]
    public void Connect_AdapterOff_IsIgnored()
    {
        StartWith(MaxDevice);
        paired.IsAdapterAvailable = false;
        paired.Raise();
        source.Connect(MaxId);
        Assert.Empty(control.ConnectCalls);
        Assert.Equal(ConnectionRequest.None, Card(MaxId).Request);
    }

    [Fact]
    public void Connect_UnknownKey_IsIgnored()
    {
        StartWith(MaxDevice);
        source.Connect("000000000000");
        Assert.Empty(control.ConnectCalls);
    }

    [Fact]
    public void Failed_ClearsOnNextStateChange()
    {
        control.Result = () => Task.FromResult(false);
        StartWith(MaxDevice);
        source.Connect(MaxId);
        Assert.Equal(ConnectionRequest.ConnectFailed, Card(MaxId).Request);
        BringUp(() => Max());
        Assert.Equal(DeviceState.Disconnected, Card(MaxId).State);
        Assert.Equal(ConnectionRequest.None, Card(MaxId).Request);
    }

    [Fact]
    public void Failed_CanRetry()
    {
        control.Result = () => Task.FromResult(false);
        StartWith(MaxDevice);
        source.Connect(MaxId);
        source.Connect(MaxId);
        Assert.Equal(2, control.ConnectCalls.Count);
    }

    [Fact]
    public void Disconnect_CompletesOnDisconnectedEvent()
    {
        StartWith(MaxDevice with { IsConnected = true });
        source.Disconnect(MaxId);
        Assert.Equal(ConnectionRequest.Disconnecting, Card(MaxId).Request);
        Assert.Equal([MaxId], control.DisconnectCalls);
        paired.Set(MaxDevice);
        Assert.Equal(ConnectionRequest.None, Card(MaxId).Request);
    }

    [Fact]
    public void Disconnect_TimesOutToFailed()
    {
        StartWith(MaxDevice with { IsConnected = true });
        source.Disconnect(MaxId);
        time.Advance(HeadphoneSource.RequestTimeout);
        Assert.Equal(ConnectionRequest.DisconnectFailed, Card(MaxId).Request);
    }

    [Fact]
    public void Connect_ThenUnpaired_NoTimeoutCrash()
    {
        StartWith(MaxDevice);
        source.Connect(MaxId);
        paired.Set();
        time.Advance(HeadphoneSource.RequestTimeout);
        Assert.Empty(source.Current);
    }

    [Fact]
    public void LateResultOfOldRequest_DoesNotFailNewRequest()
    {
        var first = new TaskCompletionSource<bool>();
        control.Result = () => first.Task;
        StartWith(MaxDevice);
        source.Connect(MaxId);
        time.Advance(HeadphoneSource.RequestTimeout);
        Assert.Equal(ConnectionRequest.ConnectFailed, Card(MaxId).Request);

        control.Result = () => new TaskCompletionSource<bool>().Task;
        source.Connect(MaxId);
        first.SetResult(false);
        SpinWait.SpinUntil(() => Card(MaxId).Request != ConnectionRequest.Connecting, TimeSpan.FromMilliseconds(500));
        Assert.Equal(ConnectionRequest.Connecting, Card(MaxId).Request);
        time.Advance(HeadphoneSource.RequestTimeout);
        Assert.Equal(ConnectionRequest.ConnectFailed, Card(MaxId).Request);
    }

    static readonly PairedDevice ProDevice = new("2c7600e3fda9", "Billy's AirPods Pro", "1420", false);

    [Fact]
    public void InCase_WhenBothPodsInCase()
    {
        StartWith(ProDevice);
        BringUp(() => AdvertDecoder.Decode(Frames.Pro2(0x15), -50, 1)!);
        Assert.True(Card(ProDevice.Id).IsInCase);
        time.Advance(TimeSpan.FromSeconds(70));
        Assert.False(Card(ProDevice.Id).IsInCase);
    }

    [Fact]
    public void NotInCase_WhenPodsOut()
    {
        StartWith(ProDevice);
        BringUp(() => AdvertDecoder.Decode(Frames.Pro2(0x01), -50, 1)!);
        Assert.False(Card(ProDevice.Id).IsInCase);
    }

    [Theory]
    [InlineData("0620")]   // Beats Solo3: over-ear, no case
    [InlineData("1020")]   // Beats Flex: neckband, no case
    [InlineData("0a20")]   // AirPods Max
    public void CaselessModel_IsSingleBattery(string modelId)
    {
        StartWith(new PairedDevice("112233445566", "Headphones", modelId, false));
        Assert.True(Card("112233445566").IsSingleBattery);
    }

    [Fact]
    public void EarbudsWithCase_AreNotSingleBattery()
    {
        StartWith(ProDevice);
        Assert.False(Card(ProDevice.Id).IsSingleBattery);
    }

    // Lid events

    const string Pro2Id = "aa0000000002";
    static string ProId => ProDevice.Id;
    static readonly PairedDevice Pro2Device = new(Pro2Id, "Second Pro", "1420", false);
    static AppleAdvert LidOpen(short rssi = -50, ulong address = 1) => AdvertDecoder.Decode(Frames.Pro2(0x75), rssi, address)!;
    // Both pods still in the case, lid counter past 8: the lid has shut (measured 2026-11-18).
    static AppleAdvert LidShut(short rssi = -50, ulong address = 1) => AdvertDecoder.Decode(Frames.Pro2(0x75, cap: 0x09), rssi, address)!;
    static AppleAdvert PodsOut(ulong address = 1) => AdvertDecoder.Decode(Frames.Pro2(0x21), -50, address)!;
    static AppleAdvert MaxLidTransient(ulong address = 2) => AdvertDecoder.Decode(Frames.Make(0x0a, 0x20, 0x02, 0x09, 0x80, cap: 0x83), -50, address)!;

    (List<string> Opened, List<string> Closed) WatchLid()
    {
        List<string> opened = [], closed = [];
        source.LidOpened += opened.Add;
        source.LidClosed += closed.Add;
        return (opened, closed);
    }

    [Fact]
    public void LidHelpers_DecodeAsOpenAndShut()
    {
        Assert.True(LidOpen().LidPopup);
        Assert.False(LidShut().LidPopup);
        Assert.True(LidShut().BothInCase);
        Assert.False(PodsOut().LidPopup);
        Assert.True(MaxLidTransient().LidPopup);
        Assert.False(Max().LidPopup);
    }

    [Fact]
    public void LidShutFromAnotherSet_Ignored()
    {
        StartWith(ProDevice);
        var (opened, closed) = WatchLid();
        adverts.Raise(LidOpen(address: 1));
        adverts.Raise(LidShut(address: 9));   // someone else's case, or earbuds worn nearby
        adverts.Raise(LidOpen(address: 1));
        Assert.Single(opened);
        Assert.Empty(closed);
    }

    [Fact]
    public void PodsTakenOut_LidStaysOpen()
    {
        StartWith(ProDevice);
        var (opened, closed) = WatchLid();
        adverts.Raise(LidOpen());
        for (var i = 0; i < 30; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(300));
            adverts.Raise(PodsOut());
        }
        adverts.Raise(LidOpen());   // one pod back in the open case
        Assert.Single(opened);
        Assert.Empty(closed);
    }

    [Fact]
    public void MaxTransientEnd_DoesNotClose()
    {
        StartWith(MaxDevice);
        var (opened, closed) = WatchLid();
        adverts.Raise(MaxLidTransient());
        adverts.Raise(Max());
        Assert.Equal([MaxId], opened);
        Assert.Empty(closed);
        time.Advance(HeadphoneSource.LidSilence);
        Assert.Equal([MaxId], closed);
    }

    [Fact]
    public void LidOpens_RaisesOpenedForDevice()
    {
        StartWith(ProDevice);
        var (opened, _) = WatchLid();
        adverts.Raise(LidOpen());
        Assert.Equal([ProId], opened);
    }

    [Fact]
    public void LidOpens_BeforeMatcherAccepts()
    {
        StartWith(ProDevice);
        var (opened, _) = WatchLid();
        adverts.Raise(LidOpen(-64));
        Assert.Equal([ProId], opened);
    }

    [Fact]
    public void Rssi_BelowMin_NoEvent()
    {
        StartWith(ProDevice);
        var (opened, _) = WatchLid();
        adverts.Raise(LidOpen(-66));
        Assert.Empty(opened);
    }

    [Fact]
    public void Rssi_AtMin_Raises()
    {
        StartWith(ProDevice);
        var (opened, _) = WatchLid();
        adverts.Raise(LidOpen(HeadphoneSource.PopupMinRssi));
        Assert.Equal(-65, HeadphoneSource.PopupMinRssi);
        Assert.Single(opened);
    }

    [Fact]
    public void LidStaysOpen_RaisesOnce()
    {
        StartWith(ProDevice);
        var (opened, closed) = WatchLid();
        for (var i = 0; i < 200; i++)
        {
            adverts.Raise(LidOpen());
            time.Advance(TimeSpan.FromMilliseconds(300));
        }
        Assert.Single(opened);
        Assert.Empty(closed);
    }

    [Fact]
    public void LidShutAdvert_RaisesClosed()
    {
        StartWith(ProDevice);
        var (_, closed) = WatchLid();
        adverts.Raise(LidOpen());
        adverts.Raise(LidShut());
        Assert.Equal([ProId], closed);
    }

    [Fact]
    public void Silence_RaisesClosed()
    {
        StartWith(ProDevice);
        var (_, closed) = WatchLid();
        adverts.Raise(LidOpen());
        time.Advance(TimeSpan.FromMilliseconds(2900));
        Assert.Empty(closed);
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal([ProId], closed);
    }

    [Fact]
    public void CloseAndReopen_RaisesTwice()
    {
        StartWith(ProDevice);
        var (opened, _) = WatchLid();
        adverts.Raise(LidOpen());
        adverts.Raise(LidShut());
        adverts.Raise(LidOpen());
        Assert.Equal(2, opened.Count);
    }

    [Fact]
    public void SilenceAndReopen_RaisesTwice()
    {
        StartWith(ProDevice);
        var (opened, _) = WatchLid();
        adverts.Raise(LidOpen());
        time.Advance(HeadphoneSource.LidSilence);
        adverts.Raise(LidOpen());
        Assert.Equal(2, opened.Count);
    }

    [Fact]
    public void ShutWithoutOpen_NoClosed()
    {
        StartWith(ProDevice);
        var (_, closed) = WatchLid();
        adverts.Raise(LidShut());
        Assert.Empty(closed);
    }

    [Fact]
    public void TwoSameModel_EventForTarget()
    {
        StartWith(ProDevice, Pro2Device with { IsConnected = true });
        var (opened, _) = WatchLid();
        adverts.Raise(LidOpen());
        Assert.Equal([Pro2Id], opened);
    }

    [Fact]
    public void UnpairedModel_NoLidEvent()
    {
        StartWith(MaxDevice);
        var (opened, _) = WatchLid();
        adverts.Raise(LidOpen());
        Assert.Empty(opened);
    }

    [Fact]
    public void NoLidCapability_NoEvent()
    {
        StartWith(ProDevice);
        var (opened, _) = WatchLid();
        var noCap = LidOpen() with { Model = ModelCatalog.Find("1420")! with { Capabilities = AppleCapabilities.None } };
        Assert.Null(noCap.LidPopup);
        adverts.Raise(noCap);
        Assert.Empty(opened);
    }
}
