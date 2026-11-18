using Pods.Core.Apple;

namespace Pods.Core.Tests.Apple;

public sealed class HardwareIdsTests
{
    const string A2dp = @"BTHENUM\{0000110b-0000-1000-8000-00805f9b34fb}";

    [Fact]
    public void ApplePid_IsByteSwappedModelId()
    {
        Assert.Equal("1420", HardwareIds.AppleModelId([$"{A2dp}_VID&0001004c_PID&2014"], out var decided));
        Assert.True(decided);
    }

    [Fact]
    public void CaseIgnored()
    {
        Assert.Equal("1420", HardwareIds.AppleModelId([$"{A2dp}_vid&0001004C_pid&2014"], out _));
    }

    [Fact]
    public void FirstCataloguedIdWins_AmongOthers()
    {
        string[] ids = [@"BTHENUM\Dev_AABBCCDDEEFF", $"{A2dp}_VID&0001004c_PID&ffff", $"{A2dp}_VID&0001004c_PID&200e"];
        Assert.Equal("0e20", HardwareIds.AppleModelId(ids, out var decided));
        Assert.True(decided);
    }

    /// <summary>A mouse or keyboard: it will never qualify, so it need not be read again.</summary>
    [Fact]
    public void OtherVendor_DecidedNull()
    {
        Assert.Null(HardwareIds.AppleModelId([$"{A2dp}_VID&000105ac_PID&0220"], out var decided));
        Assert.True(decided);
    }

    [Fact]
    public void AppleButNotCatalogued_DecidedNull()
    {
        Assert.Null(HardwareIds.AppleModelId([$"{A2dp}_VID&0001004c_PID&ffff"], out var decided));
        Assert.True(decided);
    }

    /// <summary>No vendor ID yet: Windows may still be installing the drivers, so read it again later.</summary>
    [Fact]
    public void NoVendorId_Undecided()
    {
        Assert.Null(HardwareIds.AppleModelId([@"BTHENUM\Dev_AABBCCDDEEFF", @"BTHENUM\GENERIC"], out var decided));
        Assert.False(decided);
        Assert.Null(HardwareIds.AppleModelId([], out decided));
        Assert.False(decided);
    }
}
