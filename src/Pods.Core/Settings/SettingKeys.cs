namespace Pods.Core.Settings;

/// <summary>Settings keys and defaults: device-scoped ones can be overridden per device, the rest are global only.</summary>
public static class SettingKeys
{
    static readonly SettingsJsonContext Json = SettingsJsonContext.Default;

    public static readonly Setting<bool> DisableHandsFree = new("disableHandsFree", false, Json.Boolean, true);
    public static readonly Setting<bool> AutoConnect = new("autoConnect", false, Json.Boolean, true);
    public static readonly Setting<EarDetectionMode> EarDetection = new("earDetection", EarDetectionMode.PlayPause, Json.EarDetectionMode, true);
    public static readonly Setting<bool> PauseOnDisconnect = new("pauseOnDisconnect", true, Json.Boolean, true);
    public static readonly Setting<bool> PlayOnConnect = new("playOnConnect", false, Json.Boolean, true);
    public static readonly Setting<bool> SwitchOutputOnConnect = new("switchOutputOnConnect", false, Json.Boolean, true);
    public static readonly Setting<string?> OutOfEarOutputDevice = new("outOfEarOutputDevice", null, Json.String, true);
    public static readonly Setting<int?> ConnectVolume = new("connectVolume", null, Json.NullableInt32, true);
    public static readonly Setting<int> LowBatteryPercent = new("lowBatteryPercent", 20, Json.Int32, true);
    public static readonly Setting<bool> KeepAudioAwake = new("keepAudioAwake", false, Json.Boolean, true);
    public static readonly Setting<bool> WindowsBatteryFallback = new("windowsBatteryFallback", false, Json.Boolean, true);
    public static readonly Setting<bool> SearchByRssi = new("searchByRssi", false, Json.Boolean, true);
    /// <summary>Lid-open popup; null follows the model's default (see <see cref="LidPopupSetting"/>).</summary>
    public static readonly Setting<bool?> LidPopup = new("lidPopup", null, Json.NullableBoolean, true);
    public static readonly Setting<string?> CustomPicture = new("customPicture", null, Json.String, true);

    /// <summary>Daily check of GitHub Releases for a signed update; global only.</summary>
    public static readonly Setting<bool> CheckForUpdates = new("checkForUpdates", true, Json.Boolean, false);

    /// <summary>Serilog minimum level name; global only.</summary>
    public static readonly Setting<string> LogLevel = new("logLevel", "Warning", Json.String, false);
}
