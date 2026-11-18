using Pods.Core.Aap;
using Pods.Core.Apple;

namespace Pods.Core.Tests.Aap;

public class AapParserTests
{
    static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    [Fact]
    public void Frames_MatchMagicPodsBytes()
    {
        Assert.Equal(Hex("00 00 04 00 01 00 02 00 00 00 00 00 00 00 00 00"), AapFrames.Handshake.ToArray());
        Assert.Equal(Hex("04 00 04 00 0F 00 FF FF FF FF"), AapFrames.EnableNotifications.ToArray());
        Assert.Equal(Hex("04 00 04 00 4D 00 0E 00 00 00 00 00 00 00"), AapFrames.ExtendedInit.ToArray());
        Assert.Equal(Hex("04 00 04 00 09 00 0D 02 00 00 00"), AapFrames.SetNoiseControl(NoiseControlMode.NoiseCancellation));
        Assert.Equal(Hex("04 00 04 00 09 00 0D 04 00 00 00"), AapFrames.SetNoiseControl(NoiseControlMode.Adaptive));
    }

    [Fact]
    public void Battery_ThreePartsWithCharging()
    {
        // right 87 discharging, left 91 discharging, case 40 charging
        var msg = Assert.IsType<AapBatteryMessage>(AapParser.Parse(Hex("04 00 04 00 04 00 03  02 01 57 02 01  04 01 5B 02 01  08 01 28 01 01")));
        Assert.Equal(new PodBattery(91, 87, 40, false, false, true), msg.Battery);
    }

    /// <summary>A capture from AirPods Pro 2 published by LibrePods ("AAP Definitions.md"), a second source for the format.</summary>
    [Fact]
    public void Battery_LibrePodsCapture()
    {
        var msg = Assert.IsType<AapBatteryMessage>(AapParser.Parse(Hex("04 00 04 00 04 00 03 02 01 64 02 01 04 01 63 01 01 08 01 11 02 01")));
        Assert.Equal(new PodBattery(Left: 99, Right: 100, Case: 17, LeftCharging: true, RightCharging: false, CaseCharging: false), msg.Battery);
    }

    [Fact]
    public void Battery_DisconnectedPartIsUnknown()
    {
        var msg = Assert.IsType<AapBatteryMessage>(AapParser.Parse(Hex("04 00 04 00 04 00 02  02 01 57 02 01  08 01 00 04 01")));
        Assert.Equal(87, msg.Battery.Right);
        Assert.Null(msg.Battery.Case);
        Assert.Null(msg.Battery.Left);
    }

    [Fact]
    public void Battery_SingleIsLeftSlot()
    {
        var msg = Assert.IsType<AapBatteryMessage>(AapParser.Parse(Hex("04 00 04 00 04 00 01  01 01 64 01 01")));
        Assert.Equal(new PodBattery(100, null, null, true, false, false), msg.Battery);
    }

    [Theory]
    [InlineData("04 00 04 00 04 00 03  02 01 57 02 01")]   // count says 3, one entry
    [InlineData("04 00 04 00 04 00 00")]                   // no entries
    [InlineData("04 00 04 00 04 00")]                      // no count
    public void Battery_ShortOrEmptyIsIgnored(string hex) => Assert.Null(AapParser.Parse(Hex(hex)));

    [Theory]
    [InlineData("04 00 04 00 09 00 0D 01 00 00 00", NoiseControlMode.Off)]
    [InlineData("04 00 04 00 09 00 0D 02 00 00 00", NoiseControlMode.NoiseCancellation)]
    [InlineData("04 00 04 00 09 00 0D 03 00 00 00", NoiseControlMode.Transparency)]
    [InlineData("04 00 04 00 09 00 0D 04 00 00 00", NoiseControlMode.Adaptive)]
    public void NoiseControl_Modes(string hex, NoiseControlMode mode) =>
        Assert.Equal(new AapNoiseControlMessage(mode), AapParser.Parse(Hex(hex)));

    [Theory]
    [InlineData("04 00 04 00 09 00 0D 05 00 00 00")]   // unknown mode
    [InlineData("04 00 04 00 09 00 28 01 00 00 00")]   // another setting (conversation awareness)
    public void Setting_OthersIgnored(string hex) => Assert.Null(AapParser.Parse(Hex(hex)));

    [Theory]
    [InlineData("00 00", Playback.AllIn)]
    [InlineData("00 01", Playback.Partial)]
    [InlineData("02 00", Playback.Partial)]
    [InlineData("01 01", Playback.NoneIn)]
    [InlineData("01 02", Playback.NoneIn)]
    [InlineData("02 02", Playback.NoneIn)]
    [InlineData("00 03", Playback.AllIn)]     // one pod present, in ear
    [InlineData("03 01", Playback.Partial)]   // one pod present, out of ear (as the advert does)
    public void Ear_Playback(string states, Playback expected)
    {
        var msg = Assert.IsType<AapEarMessage>(AapParser.Parse(Hex("04 00 04 00 06 00 " + states)));
        Assert.Equal(expected, msg.Playback);
    }

    [Fact]
    public void Ear_NeitherPresentIsNull() =>
        Assert.Null(Assert.IsType<AapEarMessage>(AapParser.Parse(Hex("04 00 04 00 06 00 03 03"))).Playback);

    [Theory]
    [InlineData("04 00 04 00 06 00 04 00")]   // unknown state
    [InlineData("04 00 04 00 06 00 00")]      // short
    [InlineData("01 00 04 00 06 00 00 00")]   // not a 04 00 04 00 frame
    [InlineData("04 00 04 00 2B 00 00 00")]   // unknown opcode
    [InlineData("")]
    public void Others_Ignored(string hex) => Assert.Null(AapParser.Parse(Hex(hex)));
}
