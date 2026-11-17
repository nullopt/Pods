using Pods.Core.Apple;
namespace Pods.Core.Tests.Apple;

public class AdvertDerivedTests
{
    static AppleAdvert D(byte[] f, short rssi = -50, ulong addr = 1) => AdvertDecoder.Decode(f, rssi, addr)!;

    [Theory]
    [InlineData(0x0B, Playback.AllIn, AudioOutHint.AnyIn)]
    [InlineData(0x03, Playback.Partial, AudioOutHint.AnyIn)]
    [InlineData(0x01, Playback.NoneIn, AudioOutHint.NoneIn)]
    [InlineData(0x22, Playback.AllIn, AudioOutHint.AnyIn)]   // single pod, left present, in ear
    [InlineData(0x02, Playback.AllIn, AudioOutHint.AnyIn)]   // single pod, right present, in ear
    [InlineData(0x20, Playback.Partial, AudioOutHint.NoneIn)] // single pod, left present, not in ear
    [InlineData(0x08, Playback.Partial, AudioOutHint.NoneIn)] // single pod, right present; only the absent left's flag is set
    public void Playback_And_SwitchAudioOut(int status, Playback playback, AudioOutHint hint)
    {
        var a = D(Frames.Pro2((byte)status));
        Assert.Equal(playback, a.Playback);
        Assert.Equal(hint, a.SwitchAudioOut);
    }

    [Theory]
    [InlineData(0x05, 0x05, true)]
    [InlineData(0x05, 0x09, false)]
    [InlineData(0x01, 0x05, false)]
    public void LidPopup_Earbuds(int status, int cap, bool expected) =>
        Assert.Equal(expected, D(Frames.Pro2((byte)status, cap: (byte)cap)).LidPopup);

    [Theory]
    [InlineData(0x22, Playback.AllIn, AudioOutHint.AnyIn)]
    [InlineData(0x02, Playback.NoneIn, AudioOutHint.NoneIn)]
    public void Playback_Max(int status, Playback playback, AudioOutHint hint)
    {
        var a = D(Frames.Make(0x0a, 0x20, (byte)status, 0x09, 0x80));
        Assert.Equal((playback, hint), (a.Playback, a.SwitchAudioOut));
    }

    [Theory]
    [InlineData(0x83, true)]  // measured: taken out of the Smart Case
    [InlineData(0x86, true)]  // measured: taken off the head (same transient, not distinguishable)
    [InlineData(0x05, false)]
    [InlineData(0x80, true)]
    [InlineData(0x70, false)]
    public void LidPopup_Max(int cap, bool expected) =>
        Assert.Equal(expected, D(Frames.Make(0x0a, 0x20, 0x24, 0x58, 0x13, (byte)cap)).LidPopup);

    [Theory]
    [InlineData(0x41, true)]
    [InlineData(0x45, false)] // in-case flag set
    [InlineData(0x43, false)] // a pod in ear
    public void AutoConnect_Rule(int status, bool expected) =>
        Assert.Equal(expected, D(Frames.Pro2((byte)status)).AutoConnect);

    [Fact]
    public void CapabilityGating()
    {
        var max = D(Frames.Make(0x0a, 0x20, 0x24, 0x58, 0x13));
        Assert.Null(max.AutoConnect);
        var studio = D(Frames.Make(0x11, 0x20, 0x05, 0x58, 0x13, cap: 0x05));
        Assert.Null(studio.Playback);
        Assert.Null(studio.SwitchAudioOut);
        Assert.Null(studio.AutoConnect);
        Assert.True(studio.LidPopup);
        var unknown = D(Frames.Make(0xff, 0xff, 0x05, 0x58, 0x13, cap: 0x05));
        Assert.Null(unknown.LidPopup);
    }

    [Fact]
    public void SameState_IgnoresAddressAndRssi()
    {
        Assert.True(D(Frames.Pro2(0x01), -40, 1).SameState(D(Frames.Pro2(0x01), -90, 2)));
        Assert.False(D(Frames.Pro2(0x01, b6: 0x58)).SameState(D(Frames.Pro2(0x01, b6: 0x48))));
    }

    [Fact]
    public void BothInCase_NeverForCaselessModel()
    {
        // Beats Solo3 with the status bits that mean "both pods in case" on earbuds.
        var advert = AdvertDecoder.Decode(Frames.Make(0x06, 0x20, 0x15, 0x58, 0x13), -50, 1)!;
        Assert.False(advert.BothInCase);
    }
}
