using Microsoft.Extensions.Time.Testing;
using Pods.Core.Apple;
using Pods.Core.Tests.Apple;
using Pods.Core.Tracking;
namespace Pods.Core.Tests.Tracking;

public class HeadphoneTrackerTests
{
    readonly FakeTimeProvider time = new();
    static AppleAdvert A(byte status = 0x0B, byte b6 = 0x58, byte b7 = 0x03) => AdvertDecoder.Decode(Frames.Pro2(status, b6, b7), -50, 1)!;

    [Fact]
    public void Battery_FirstReadingRaisesImmediately()
    {
        using var t = new HeadphoneTracker(time); var raised = 0; t.BatteryChanged += _ => raised++;
        t.OnAdvert(A());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Battery_EveryChangeRaisesImmediately()
    {
        using var t = new HeadphoneTracker(time); var raised = new List<PodBattery>(); t.BatteryChanged += raised.Add;
        t.OnAdvert(A(b6: 0x58));
        time.Advance(TimeSpan.FromSeconds(1)); t.OnAdvert(A(b6: 0x48));
        time.Advance(TimeSpan.FromSeconds(1)); t.OnAdvert(A(b6: 0x48, b7: 0x43));
        Assert.Equal(3, raised.Count);
        Assert.Equal(45, raised[1].Left);
        Assert.True(raised[2].CaseCharging);
    }

    [Fact]
    public void Battery_UnknownComponentHeldForThirtySeconds()
    {
        using var t = new HeadphoneTracker(time); var raised = new List<PodBattery>(); t.BatteryChanged += raised.Add;
        t.OnAdvert(A(b6: 0x58));
        time.Advance(TimeSpan.FromSeconds(5)); t.OnAdvert(A(b6: 0xF8));   // left turns unknown
        Assert.Equal(55, t.Battery!.Value.Left);
        Assert.Single(raised);
        time.Advance(TimeSpan.FromSeconds(30)); t.OnAdvert(A(b6: 0xF8));  // unknown for 30 s
        Assert.Null(t.Battery!.Value.Left);
        Assert.Equal(2, raised.Count);
    }

    [Fact]
    public void Battery_IdenticalValuesDoNotRaise()
    {
        using var t = new HeadphoneTracker(time); var raised = 0; t.BatteryChanged += _ => raised++;
        t.OnAdvert(A()); time.Advance(TimeSpan.FromSeconds(120)); t.OnAdvert(A());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Playback_FirstAdvertSetsValueWithoutEvent()
    {
        using var t = new HeadphoneTracker(time); var raised = 0; t.PlaybackChanged += _ => raised++;
        t.OnAdvert(A(0x0B));
        Assert.Equal(Playback.AllIn, t.Playback);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Playback_ChangeRaisesAfterOneSecond()
    {
        using var t = new HeadphoneTracker(time); var raised = new List<Playback>(); t.PlaybackChanged += raised.Add;
        t.OnAdvert(A(0x0B)); t.OnAdvert(A(0x01));
        time.Advance(TimeSpan.FromMilliseconds(999)); Assert.Empty(raised);
        time.Advance(TimeSpan.FromMilliseconds(1)); Assert.Equal(new[] { Playback.NoneIn }, raised);
    }

    [Fact]
    public void Playback_StaggeredRemovalRaisesOnce()
    {
        using var t = new HeadphoneTracker(time); var raised = new List<Playback>(); t.PlaybackChanged += raised.Add;
        t.OnAdvert(A(0x0B));
        t.OnAdvert(A(0x03)); time.Advance(TimeSpan.FromMilliseconds(300));
        t.OnAdvert(A(0x01)); time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { Playback.NoneIn }, raised);
    }

    [Fact]
    public void Playback_ReversalWithinDebounceRaisesNothing()
    {
        using var t = new HeadphoneTracker(time); var raised = 0; t.PlaybackChanged += _ => raised++;
        t.OnAdvert(A(0x0B)); t.OnAdvert(A(0x01));
        time.Advance(TimeSpan.FromMilliseconds(500)); t.OnAdvert(A(0x0B));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Stale_AfterSixtySecondsKeepsLastBattery()
    {
        using var t = new HeadphoneTracker(time);
        t.OnAdvert(A());
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.True(t.IsStale);
        Assert.Equal(55, t.Battery!.Value.Left);
        t.OnAdvert(A());
        Assert.False(t.IsStale);
    }
}
