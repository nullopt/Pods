using Microsoft.Extensions.Time.Testing;
using Pods.Core.Apple;
using Pods.Core.Tests.Apple;
using Pods.Core.Tracking;
namespace Pods.Core.Tests.Tracking;

public class AdvertMatcherTests
{
    readonly FakeTimeProvider time = new();
    static AppleAdvert A(ulong addr, short rssi, byte b6 = 0x58) => AdvertDecoder.Decode(Frames.Pro2(0x01, b6: b6), rssi, addr)!;
    AdvertMatcher M(bool byRssi = false) => new(new MatcherOptions(SearchByRssi: byRssi), time);

    [Fact]
    public void CloseWindow_SelectsHighestAverage()
    {
        var m = M();
        foreach (var a in new[] { A(1, -50), A(1, -52), A(2, -45), A(2, -75) }) m.Observe(a);
        m.CloseWindow();
        Assert.Equal(1UL, m.SelectedAddress);
    }

    [Fact]
    public void CloseWindow_IgnoresAverageOutsideWindow()
    {
        var m = M();
        m.Observe(A(1, -70)); m.Observe(A(1, -72));
        m.CloseWindow();
        Assert.Null(m.SelectedAddress);
    }

    [Fact]
    public void CloseWindow_StartsFreshWindowAndEmptyWindowKeepsSelection()
    {
        var m = M();
        m.Observe(A(1, -50)); m.CloseWindow();
        m.Observe(A(2, -40)); m.CloseWindow();
        Assert.Equal(2UL, m.SelectedAddress);
        m.CloseWindow();
        Assert.Equal(2UL, m.SelectedAddress);
    }

    [Fact]
    public void Accept_SecondSetDoesNotTakeOver()
    {
        var m = M();
        m.Observe(A(1, -50)); m.CloseWindow();
        Assert.True(m.Accept(A(1, -50)));
        Assert.False(m.Accept(A(2, -40, b6: 0x33)));
        m.Observe(A(1, -55)); m.Observe(A(2, -40, b6: 0x33)); m.CloseWindow();   // stronger set seen for a whole window
        Assert.Equal(1UL, m.SelectedAddress);
        Assert.False(m.Accept(A(2, -40, b6: 0x33)));
    }

    [Fact]
    public void CloseWindow_WeakWindowKeepsSelection()
    {
        var m = M();
        m.Observe(A(1, -50)); m.CloseWindow();
        m.Observe(A(1, -70)); m.CloseWindow();   // still seen, but below MinRssi
        Assert.Equal(1UL, m.SelectedAddress);
    }

    [Fact]
    public void Accept_SwitchesToOtherPodWhenSelectedSilentThreeSeconds()
    {
        var m = M();
        m.Observe(A(1, -50)); m.CloseWindow();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(m.Accept(A(2, -50)));       // selected silent only 2 s
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(m.Accept(A(2, -50)));        // silent 3 s: the other pod takes over at once
        Assert.Equal(2UL, m.SelectedAddress);
        Assert.False(m.Accept(A(3, -80)));       // out of range never takes over
    }

    [Fact]
    public void Accept_SearchByRssiUsesRssiWindow()
    {
        var m = M(byRssi: true);
        Assert.True(m.Accept(A(2, -55)));
        Assert.False(m.Accept(A(3, -80, b6: 0x33)));
    }

    [Fact]
    public void Accept_KnownAddressPurgedOnWindowCloseAfterTtl()
    {
        var m = M(byRssi: true);
        Assert.True(m.Accept(A(1, -55)));              // RSSI window; does not add to known map
        Assert.True(m.Accept(A(4, -90)));              // same state as last accepted: address 4 becomes known
        time.Advance(TimeSpan.FromSeconds(890)); m.CloseWindow();
        Assert.True(m.Accept(A(4, -90, b6: 0x33)));    // still known (890 s < 15 min)
        time.Advance(TimeSpan.FromSeconds(20)); m.CloseWindow();
        Assert.False(m.Accept(A(4, -90, b6: 0x22)));   // purged at 910 s; different state
    }

    [Fact]
    public void Accept_RssiWindowAcceptDoesNotMakeAddressKnown()
    {
        var m = M(byRssi: true);
        Assert.True(m.Accept(A(1, -55)));              // first in-range advert: selected at once
        Assert.True(m.Accept(A(2, -55, b6: 0x22)));    // accepted through the RSSI window only
        Assert.False(m.Accept(A(2, -80, b6: 0x33)));   // so address 2 did not become known
    }

    [Fact]
    public void Accept_SameStateAsLastAcceptedAddsAddress()
    {
        var m = M(byRssi: true);
        Assert.True(m.Accept(A(1, -55)));
        Assert.True(m.Accept(A(4, -90)));            // same state as last accepted
        Assert.True(m.Accept(A(4, -90, b6: 0x33)));  // address now known
    }

    [Fact]
    public void Accept_FirstInRangeAdvertSelectedImmediately()
    {
        var m = M();
        var a = A(1, -50);
        m.Observe(a);
        Assert.True(m.Accept(a));
        Assert.Equal(1UL, m.SelectedAddress);
    }

    [Fact]
    public void Accept_WeakFirstAdvertWaitsForWindow()
    {
        var m = M();
        var a = A(1, -75);
        m.Observe(a);
        Assert.False(m.Accept(a));
        Assert.Null(m.SelectedAddress);
    }

    [Fact]
    public void CloseWindow_FirstWindowReplacesInstantPickWithStrongest()
    {
        var m = M();
        var weak = A(1, -58);
        m.Observe(weak);
        Assert.True(m.Accept(weak));
        m.Observe(A(2, -40)); m.Observe(A(2, -42));
        m.CloseWindow();
        Assert.Equal(2UL, m.SelectedAddress);
    }

    [Fact]
    public void CloseWindow_LaterWindowsKeepStickySelection()
    {
        var m = M();
        var first = A(1, -58);
        m.Observe(first);
        m.Accept(first);
        m.CloseWindow();                       // first window: 1 is the only in-range address
        m.Observe(A(1, -58)); m.Observe(A(2, -40));
        m.CloseWindow();                       // a stronger set later does not take over
        Assert.Equal(1UL, m.SelectedAddress);
    }
}
