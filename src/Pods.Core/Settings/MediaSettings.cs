using Pods.Core.Apple;

namespace Pods.Core.Settings;

/// <summary>
/// A device's effective media settings: its own value where it has one, otherwise the app default.
/// Auto connect is stored alongside them but has no behaviour yet.
/// </summary>
public sealed record MediaSettings(bool EarDetection, bool PauseOnDisconnect, bool PlayOnConnect)
{
    public static MediaSettings For(SettingsStore settings, string deviceId) => new(
        settings.GetForDevice(SettingKeys.EarDetection, deviceId) == EarDetectionMode.PlayPause,
        settings.GetForDevice(SettingKeys.PauseOnDisconnect, deviceId),
        settings.GetForDevice(SettingKeys.PlayOnConnect, deviceId));

    public static bool SupportsEarDetection(string modelId) => Has(modelId, AppleCapabilities.EarDetection);

    public static bool SupportsAutoConnect(string modelId) => Has(modelId, AppleCapabilities.AutoConnect);

    /// <summary>Whether the device has its own value for any of the four toggles.</summary>
    public static bool AnyToggleOverridden(SettingsStore settings, string deviceId) =>
        settings.IsOverridden(SettingKeys.EarDetection, deviceId)
        || settings.IsOverridden(SettingKeys.PauseOnDisconnect, deviceId)
        || settings.IsOverridden(SettingKeys.PlayOnConnect, deviceId)
        || settings.IsOverridden(SettingKeys.AutoConnect, deviceId);

    /// <summary>The four toggles follow the app defaults again; other settings are left alone.</summary>
    public static void ResetToggles(SettingsStore settings, string deviceId)
    {
        settings.ResetToGlobal(SettingKeys.EarDetection, deviceId);
        settings.ResetToGlobal(SettingKeys.PauseOnDisconnect, deviceId);
        settings.ResetToGlobal(SettingKeys.PlayOnConnect, deviceId);
        settings.ResetToGlobal(SettingKeys.AutoConnect, deviceId);
    }

    static bool Has(string modelId, AppleCapabilities capability) =>
        ModelCatalog.Find(modelId) is { } model && model.Capabilities.HasFlag(capability);
}
