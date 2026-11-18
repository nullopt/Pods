using Pods.Core.Settings;

namespace Pods.Core.Tests.Settings;

public sealed class MediaSettingsTests : IDisposable
{
    const string Dev = "a1b2c3d4e5f6";
    readonly string dir = Path.Combine(Path.GetTempPath(), "pods-media-" + Guid.NewGuid().ToString("N"));

    public MediaSettingsTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Defaults_FollowSpec() =>
        Assert.Equal(new MediaSettings(true, true, false), MediaSettings.For(SettingsStore.Load(dir), Dev));

    [Fact]
    public void GlobalChange_FollowedWithoutOverride()
    {
        var store = SettingsStore.Load(dir);
        store.SetGlobal(SettingKeys.PauseOnDisconnect, false);
        Assert.False(MediaSettings.For(store, Dev).PauseOnDisconnect);
    }

    [Fact]
    public void Override_KeptWhenGlobalChanges()
    {
        var store = SettingsStore.Load(dir);
        store.SetForDevice(SettingKeys.PlayOnConnect, Dev, true);
        store.SetGlobal(SettingKeys.PlayOnConnect, false);
        Assert.True(MediaSettings.For(store, Dev).PlayOnConnect);
    }

    [Fact]
    public void EarDetectionOff_ReadsFalse()
    {
        var store = SettingsStore.Load(dir);
        store.SetForDevice(SettingKeys.EarDetection, Dev, EarDetectionMode.Off);
        Assert.False(MediaSettings.For(store, Dev).EarDetection);
    }

    [Fact]
    public void ResetToggles_ClearsAllFour()
    {
        var store = SettingsStore.Load(dir);
        store.SetForDevice(SettingKeys.EarDetection, Dev, EarDetectionMode.Off);
        store.SetForDevice(SettingKeys.PauseOnDisconnect, Dev, false);
        store.SetForDevice(SettingKeys.PlayOnConnect, Dev, true);
        store.SetForDevice(SettingKeys.AutoConnect, Dev, true);
        Assert.True(MediaSettings.AnyToggleOverridden(store, Dev));

        MediaSettings.ResetToggles(store, Dev);

        Assert.False(MediaSettings.AnyToggleOverridden(store, Dev));
        Assert.Equal(new MediaSettings(true, true, false), MediaSettings.For(store, Dev));
        Assert.False(store.GetForDevice(SettingKeys.AutoConnect, Dev));
    }

    [Fact]
    public void AnyToggleOverridden_FalseByDefault() =>
        Assert.False(MediaSettings.AnyToggleOverridden(SettingsStore.Load(dir), Dev));

    [Fact]
    public void ResetToggles_LeavesThreshold()
    {
        var store = SettingsStore.Load(dir);
        store.SetForDevice(SettingKeys.LowBatteryPercent, Dev, 40);
        MediaSettings.ResetToggles(store, Dev);
        Assert.Equal(40, store.GetForDevice(SettingKeys.LowBatteryPercent, Dev));
    }

    [Theory]
    [InlineData("1420", true, true)]     // AirPods Pro 2
    [InlineData("0a20", true, false)]    // AirPods Max: ear detection, no auto-connect
    [InlineData("1120", false, false)]   // Beats Studio Buds: lid popup only
    [InlineData("ffff", false, false)]   // not catalogued
    public void Supports(string modelId, bool ear, bool autoConnect)
    {
        Assert.Equal(ear, MediaSettings.SupportsEarDetection(modelId));
        Assert.Equal(autoConnect, MediaSettings.SupportsAutoConnect(modelId));
    }
}
