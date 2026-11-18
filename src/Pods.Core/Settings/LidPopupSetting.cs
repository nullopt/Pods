using Pods.Core.Apple;

namespace Pods.Core.Settings;

/// <summary>
/// The per-device "show popup when case opens" setting. Unset, it is on, except for the AirPods Max
/// family, whose lid signal also fires when they are taken off the head.
/// </summary>
public static class LidPopupSetting
{
    public static bool Supported(string modelId) =>
        ModelCatalog.Find(modelId) is { } model && model.Capabilities.HasFlag(AppleCapabilities.LidPopup);

    public static bool IsOn(SettingsStore settings, string deviceId, string modelId) =>
        settings.GetForDevice(SettingKeys.LidPopup, deviceId) ?? !ModelCatalog.MaxFamilyIds.Contains(modelId);

    /// <summary>Stores an explicit choice, so it holds even where it matches the model's default.</summary>
    public static void Set(SettingsStore settings, string deviceId, bool on) =>
        settings.SetForDevice(SettingKeys.LidPopup, deviceId, on);
}
