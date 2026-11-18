using System.Text.Json.Nodes;
using Pods.Core.Settings;

namespace Pods.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    const string Dev = "a1b2c3d4e5f6";
    readonly string dir = Path.Combine(Path.GetTempPath(), "pods-settings-" + Guid.NewGuid().ToString("N"));
    string SettingsFile => Path.Combine(dir, "settings.json");
    string DeviceFile => Path.Combine(dir, "devices", Dev + ".json");

    public SettingsStoreTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Defaults_WhenNoFiles()
    {
        var s = SettingsStore.Load(dir);
        Assert.Equal(20, s.Get(SettingKeys.LowBatteryPercent));
        Assert.Equal(EarDetectionMode.PlayPause, s.GetForDevice(SettingKeys.EarDetection, Dev));
        Assert.True(s.GetForDevice(SettingKeys.PauseOnDisconnect, Dev));
        Assert.Null(s.GetForDevice(SettingKeys.ConnectVolume, Dev));
        Assert.Equal("Warning", s.Get(SettingKeys.LogLevel));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir));
    }

    [Fact]
    public void CheckForUpdates_DefaultsOn_GlobalOnly()
    {
        var s = SettingsStore.Load(dir);
        Assert.True(s.Get(SettingKeys.CheckForUpdates));
        Assert.Throws<ArgumentException>(() => s.SetForDevice(SettingKeys.CheckForUpdates, Dev, false));
    }

    [Fact]
    public void Animations_DefaultsOn_GlobalOnly()
    {
        var s = SettingsStore.Load(dir);
        Assert.True(s.Get(SettingKeys.Animations));
        Assert.Throws<ArgumentException>(() => s.SetForDevice(SettingKeys.Animations, Dev, false));
    }

    [Fact]
    public void GlobalChange_AppliesToDevicesWithoutOverride()
    {
        var s = SettingsStore.Load(dir);
        s.SetGlobal(SettingKeys.LowBatteryPercent, 20);
        Assert.Equal(20, s.GetForDevice(SettingKeys.LowBatteryPercent, Dev));
        Assert.False(s.IsOverridden(SettingKeys.LowBatteryPercent, Dev));
    }

    [Fact]
    public void Override_SurvivesGlobalChange()
    {
        var s = SettingsStore.Load(dir);
        s.SetForDevice(SettingKeys.AutoConnect, Dev, true);
        s.SetGlobal(SettingKeys.AutoConnect, false);
        Assert.True(s.GetForDevice(SettingKeys.AutoConnect, Dev));
        Assert.True(s.IsOverridden(SettingKeys.AutoConnect, Dev));
    }

    [Fact]
    public void SetForDevice_EqualToGlobal_RemovesOverride()
    {
        var s = SettingsStore.Load(dir);
        s.SetForDevice(SettingKeys.AutoConnect, Dev, true);
        s.SetForDevice(SettingKeys.AutoConnect, Dev, false);
        Assert.False(s.IsOverridden(SettingKeys.AutoConnect, Dev));
        Assert.DoesNotContain("autoConnect", File.ReadAllText(DeviceFile));
    }

    [Fact]
    public void ResetToGlobal_RestoresInheritance()
    {
        var s = SettingsStore.Load(dir);
        s.SetForDevice(SettingKeys.LowBatteryPercent, Dev, 50);
        s.ResetToGlobal(SettingKeys.LowBatteryPercent, Dev);
        s.SetGlobal(SettingKeys.LowBatteryPercent, 15);
        Assert.Equal(15, s.GetForDevice(SettingKeys.LowBatteryPercent, Dev));
        Assert.False(s.IsOverridden(SettingKeys.LowBatteryPercent, Dev));
    }

    [Fact]
    public void NullOverride_BeatsGlobalValue()
    {
        var s = SettingsStore.Load(dir);
        s.SetGlobal(SettingKeys.ConnectVolume, 40);
        s.SetForDevice(SettingKeys.ConnectVolume, Dev, null);
        Assert.Null(s.GetForDevice(SettingKeys.ConnectVolume, Dev));
        Assert.True(s.IsOverridden(SettingKeys.ConnectVolume, Dev));
        Assert.Null(SettingsStore.Load(dir).GetForDevice(SettingKeys.ConnectVolume, Dev));
    }

    [Fact]
    public void Persists_AcrossReload()
    {
        var s = SettingsStore.Load(dir);
        s.SetGlobal(SettingKeys.EarDetection, EarDetectionMode.Off);
        s.SetForDevice(SettingKeys.ConnectVolume, Dev, 40);
        var r = SettingsStore.Load(dir);
        Assert.Equal(EarDetectionMode.Off, r.Get(SettingKeys.EarDetection));
        Assert.Equal(40, r.GetForDevice(SettingKeys.ConnectVolume, Dev));
        Assert.Contains("\"Off\"", File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void MissingVersion_MigratesAndSavesVersion1()
    {
        File.WriteAllText(SettingsFile, """{"values":{"lowBatteryPercent":25}}""");
        var s = SettingsStore.Load(dir);
        Assert.Equal(25, s.Get(SettingKeys.LowBatteryPercent));
        s.SetGlobal(SettingKeys.AutoConnect, true);
        var saved = JsonNode.Parse(File.ReadAllText(SettingsFile))!;
        Assert.Equal(1, saved["version"]!.GetValue<int>());
        Assert.Equal(25, saved["values"]!["lowBatteryPercent"]!.GetValue<int>());
    }

    [Fact]
    public void FutureVersion_IsReadOnlyAndNeverOverwritten()
    {
        const string original = """{"version":99,"values":{"lowBatteryPercent":25}}""";
        File.WriteAllText(SettingsFile, original);
        var s = SettingsStore.Load(dir);
        Assert.True(s.IsReadOnly);
        Assert.Equal(25, s.Get(SettingKeys.LowBatteryPercent));
        s.SetGlobal(SettingKeys.LowBatteryPercent, 10);
        Assert.Equal(10, s.Get(SettingKeys.LowBatteryPercent));
        Assert.Equal(original, File.ReadAllText(SettingsFile));
        Assert.NotEmpty(s.Warnings);
    }

    [Fact]
    public void CorruptFile_BackedUpAndDefaultsUsed()
    {
        File.WriteAllText(SettingsFile, "{not json");
        var s = SettingsStore.Load(dir);
        Assert.Equal(20, s.Get(SettingKeys.LowBatteryPercent));
        Assert.Equal("{not json", File.ReadAllText(SettingsFile + ".bad"));
        Assert.NotEmpty(s.Warnings);
        Assert.False(s.IsReadOnly);
    }

    [Theory]
    [InlineData("""{"logLevel":"Debug"}""")]                    // no "values" wrapper
    [InlineData("""{"version":1,"values":[1,2]}""")]             // values not an object
    [InlineData("""{"version":"2","values":{}}""")]              // version not an integer
    [InlineData("""{"version":2.5,"values":{}}""")]
    [InlineData("""{"version":-1,"values":{}}""")]
    [InlineData("""[1,2,3]""")]
    public void WrongShape_BackedUpAndNeverOverwritten(string content)
    {
        File.WriteAllText(SettingsFile, content);
        var s = SettingsStore.Load(dir);
        Assert.Equal(20, s.Get(SettingKeys.LowBatteryPercent));
        Assert.Equal(content, File.ReadAllText(SettingsFile + ".bad"));
        Assert.NotEmpty(s.Warnings);
    }

    [Fact]
    public void UnreadableFile_IsReadOnlyAndUntouched()
    {
        const string original = """{"version":1,"values":{"lowBatteryPercent":25}}""";
        File.WriteAllText(SettingsFile, original);
        SettingsStore s;
        using (new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
            s = SettingsStore.Load(dir);
        Assert.True(s.IsReadOnly);
        Assert.NotEmpty(s.Warnings);
        s.SetGlobal(SettingKeys.LowBatteryPercent, 10);
        Assert.Equal(original, File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void CorruptDeviceFile_BackedUpAndGlobalUsed()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DeviceFile)!);
        File.WriteAllText(DeviceFile, "[1,");
        var s = SettingsStore.Load(dir);
        Assert.False(s.GetForDevice(SettingKeys.AutoConnect, Dev));
        Assert.Equal("[1,", File.ReadAllText(DeviceFile + ".bad"));
        Assert.NotEmpty(s.Warnings);
    }

    /// <summary>Device files are read on first use, after startup has logged <see cref="SettingsStore.Warnings"/>.</summary>
    [Fact]
    public void CorruptDeviceFile_ReadLater_RaisesWarningAdded()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DeviceFile)!);
        File.WriteAllText(DeviceFile, "[1,");
        var s = SettingsStore.Load(dir);
        List<string> raised = [];
        s.WarningAdded += raised.Add;
        s.GetForDevice(SettingKeys.AutoConnect, Dev);
        var warning = Assert.Single(raised);
        Assert.Contains(Dev + ".json", warning);
        Assert.Equal(s.Warnings[^1], warning);
    }

    [Fact]
    public void WrongValueType_FallsBackToDefault()
    {
        File.WriteAllText(SettingsFile, """{"version":1,"values":{"lowBatteryPercent":"abc","earDetection":"Sideways"}}""");
        var s = SettingsStore.Load(dir);
        Assert.Equal(20, s.Get(SettingKeys.LowBatteryPercent));
        Assert.Equal(EarDetectionMode.PlayPause, s.Get(SettingKeys.EarDetection));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a1b2c3d4e5f")]
    [InlineData("a1b2c3d4e5fg")]
    [InlineData("")]
    public void InvalidDeviceId_Throws(string id) =>
        Assert.Throws<ArgumentException>(() => SettingsStore.Load(dir).GetForDevice(SettingKeys.AutoConnect, id));

    [Fact]
    public void UppercaseDeviceId_IsNormalised()
    {
        var s = SettingsStore.Load(dir);
        s.SetForDevice(SettingKeys.AutoConnect, "A1B2C3D4E5F6", true);
        Assert.True(File.Exists(DeviceFile));
        Assert.True(s.GetForDevice(SettingKeys.AutoConnect, Dev));
    }

    [Fact]
    public void GlobalOnlySetting_RejectsDeviceOverride() =>
        Assert.Throws<ArgumentException>(() => SettingsStore.Load(dir).SetForDevice(SettingKeys.LogLevel, Dev, "Debug"));
}
