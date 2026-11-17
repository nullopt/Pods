using Pods.Core.Apple;
namespace Pods.Core.Tests.Apple;

public class AdvertDecoderTests
{
    static AppleAdvert D(byte[] f, short rssi = -50, ulong addr = 1) => AdvertDecoder.Decode(f, rssi, addr)!;

    [Fact] public void Decode_RejectsWrongType() { var f = Frames.Pro2(0x01); f[0] = 0x10; Assert.Null(AdvertDecoder.Decode(f, -50, 1)); }
    [Fact] public void Decode_RejectsLengthByteMismatch() { var f = Frames.Pro2(0x01); f[1] = 0x18; Assert.Null(AdvertDecoder.Decode(f, -50, 1)); }
    [Fact] public void Decode_RejectsNon27ByteFrame() { var f = Frames.Pro2(0x01)[..26]; f[1] = 0x18; Assert.Null(AdvertDecoder.Decode(f, -50, 1)); }
    [Fact] public void Decode_RejectsRssiAtFloor() => Assert.Null(AdvertDecoder.Decode(Frames.Pro2(0x01), -127, 1));

    [Fact]
    public void Decode_ReadsModelIdAsLowercaseHex()
    {
        var a = D(Frames.Pro2(0x01));
        Assert.Equal("1420", a.ModelId);
        Assert.Equal("AirPods Pro 2", a.Model!.Name);
    }

    [Fact]
    public void Decode_NotFlippedBattery()
    {
        var a = D(Frames.Pro2(0x01, b6: 0x58, b7: 0x13));
        Assert.Equal(new PodBattery(55, 85, 35, LeftCharging: false, RightCharging: true, CaseCharging: false), a.Battery);
        Assert.False(a.PrimaryIsLeft);
        Assert.True(a.BothPresent);
    }

    [Fact]
    public void Decode_FlippedBattery()
    {
        var a = D(Frames.Pro2(0x21, b6: 0x58, b7: 0x13));
        Assert.Equal(new PodBattery(85, 55, 35, LeftCharging: true, RightCharging: false, CaseCharging: false), a.Battery);
        Assert.True(a.PrimaryIsLeft);
    }

    [Fact]
    public void Decode_BatteryNibbleRules()
    {
        var a = D(Frames.Pro2(0x01, b6: 0xFC, b7: 0x4A));
        Assert.Null(a.Battery.Left);      // nibble 15 = unknown, never 0
        Assert.Equal(100, a.Battery.Right); // nibble 12: 125 clamps to 100
        Assert.Equal(100, a.Battery.Case);  // nibble 10: 105 clamps to 100
        Assert.True(a.Battery.CaseCharging);
    }

    [Fact]
    public void Decode_PrimarySwapKeepsSides()
    {
        var before = D(Frames.Pro2(0x01, b6: 0x58));
        var after = D(Frames.Pro2(0x21, b6: 0x85));
        Assert.Equal((before.Battery.Left, before.Battery.Right), (after.Battery.Left, after.Battery.Right));
    }

    [Theory]
    [InlineData(0x03, false, true)]  // flag true: right = bit1
    [InlineData(0x23, true, false)]  // flag false: left = bit1
    [InlineData(0x0B, true, true)]
    [InlineData(0x01, false, false)]
    public void Decode_InEar(int status, bool left, bool right)
    {
        var a = D(Frames.Pro2((byte)status));
        Assert.Equal((left, right), (a.LeftInEar, a.RightInEar));
    }

    [Theory]
    [InlineData(0x22, true)]  // measured on head
    [InlineData(0x02, false)] // measured off head
    [InlineData(0x20, true)]
    [InlineData(0x00, false)]
    public void Decode_MaxOnHeadIsBit5(int status, bool onHead)
    {
        var a = D(Frames.Make(0x0a, 0x20, (byte)status, 0x09, 0x80));
        Assert.Equal(status, a.Status);
        Assert.Equal((onHead, onHead), (a.LeftInEar, a.RightInEar));
    }

    [Fact]
    public void Decode_MaxHasSingleBattery()
    {
        var a = D(Frames.Make(0x0a, 0x20, 0x02, 0x09, 0x80));
        Assert.Equal(new PodBattery(95, null, null, false, false, false), a.Battery);
    }

    [Fact]
    public void Decode_MaxChargingIsB7Bit4()
    {
        // Real frame captured while charging on Lightning, 2026-11-17 14:57:10 (iOS: 91%, charging).
        var a = D(Convert.FromHexString("0719010a20020990850f4043159341d73a2dca73cd60ca09967bc9"));
        Assert.Equal(new PodBattery(95, null, null, true, false, false), a.Battery);
    }

    [Fact]
    public void Decode_MaxColourRewrite()
    {
        Assert.Equal("31", D(Frames.Make(0x0a, 0x20, 0x24, 0x58, 0x13, colour: 0x11)).Colour);
        Assert.Equal("11", D(Frames.Make(0x14, 0x20, 0x01, 0x58, 0x13, colour: 0x11)).Colour);
    }

    [Fact] public void Decode_UnknownModelStillDecodes() => Assert.Null(D(Frames.Make(0xff, 0xff, 0x01, 0x58, 0x13)).Model);
}
