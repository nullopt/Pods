using Pods.Core.Settings;

namespace Pods.Core.Tests.Settings;

public sealed class LidPopupSettingTests : IDisposable
{
    const string Dev = "a1b2c3d4e5f6";
    readonly string dir = Path.Combine(Path.GetTempPath(), "pods-lid-" + Guid.NewGuid().ToString("N"));

    public LidPopupSettingTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Default_OnForPro2() => Assert.True(LidPopupSetting.IsOn(SettingsStore.Load(dir), Dev, "1420"));

    [Fact]
    public void Default_OffForMax() => Assert.False(LidPopupSetting.IsOn(SettingsStore.Load(dir), Dev, "0a20"));

    [Fact]
    public void Set_OnForMax_Persists()
    {
        LidPopupSetting.Set(SettingsStore.Load(dir), Dev, true);
        Assert.True(LidPopupSetting.IsOn(SettingsStore.Load(dir), Dev, "0a20"));
    }

    [Fact]
    public void Set_OffForPro2_Persists()
    {
        LidPopupSetting.Set(SettingsStore.Load(dir), Dev, false);
        Assert.False(LidPopupSetting.IsOn(SettingsStore.Load(dir), Dev, "1420"));
    }

    [Theory]
    [InlineData("1420", true)]
    [InlineData("1120", true)]    // Beats Studio Buds: lid popup only
    [InlineData("0a20", true)]    // AirPods Max: supported, off by default
    [InlineData("0320", false)]   // Powerbeats3: no lid popup
    [InlineData("ffff", false)]   // not catalogued
    public void Supported(string modelId, bool expected) => Assert.Equal(expected, LidPopupSetting.Supported(modelId));
}
