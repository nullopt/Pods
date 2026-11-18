using CommunityToolkit.Mvvm.ComponentModel;
using Pods.App.Logging;
using Pods.Core.Settings;
using Serilog;

namespace Pods.App.Settings;

/// <summary>App-wide settings on the back of the flyout: defaults for every device, and the log level.</summary>
internal sealed partial class AppSettingsViewModel : ObservableObject
{
    readonly SettingsStore settings;
    readonly string dataFolder;
    readonly Action settingsChanged;

    /// <param name="settingsChanged">Called after the default threshold is written, so devices on the default follow.</param>
    public AppSettingsViewModel(SettingsStore settings, string dataFolder, Action settingsChanged)
    {
        this.settings = settings;
        this.dataFolder = dataFolder;
        this.settingsChanged = settingsChanged;

        // Kept inside the slider's range so it never pushes a corrected value back as a change.
        threshold = Math.Clamp(settings.Get(SettingKeys.LowBatteryPercent), SettingChoices.LowBatteryMin, SettingChoices.LowBatteryMax);
        ThresholdText = $"{(int)threshold}%";

        earDetection = settings.Get(SettingKeys.EarDetection) == EarDetectionMode.PlayPause;
        pauseOnDisconnect = settings.Get(SettingKeys.PauseOnDisconnect);
        playOnConnect = settings.Get(SettingKeys.PlayOnConnect);
        autoConnect = settings.Get(SettingKeys.AutoConnect);

        var level = settings.Get(SettingKeys.LogLevel);
        var index = SettingChoices.LogLevels.ToList().FindIndex(l => l.Equals(level, StringComparison.OrdinalIgnoreCase));
        // An unknown name logs at Warning (see LogSetup), so show that.
        selectedLogLevelIndex = index >= 0 ? index : SettingChoices.LogLevels.ToList().IndexOf("Warning");
    }

    public IReadOnlyList<string> LogLevels => SettingChoices.LogLevels;
    public string Version { get; } = string.Format(Strings.Get("AppVersion"), typeof(App).Assembly.GetName().Version?.ToString(3));

    double threshold;
    /// <summary>The default low battery warning, 5-50%.</summary>
    public double Threshold
    {
        get => threshold;
        set
        {
            if (!SetProperty(ref threshold, value)) return;
            var percent = (int)Math.Round(value);
            Save(() => settings.SetGlobal(SettingKeys.LowBatteryPercent, percent));
            ThresholdText = $"{percent}%";
            settingsChanged();
        }
    }

    string thresholdText = "";
    public string ThresholdText { get => thresholdText; private set => SetProperty(ref thresholdText, value); }

    bool earDetection, pauseOnDisconnect, playOnConnect, autoConnect;

    public bool EarDetection
    {
        get => earDetection;
        set => SetDefault(ref earDetection, value, () =>
            settings.SetGlobal(SettingKeys.EarDetection, value ? EarDetectionMode.PlayPause : EarDetectionMode.Off));
    }

    public bool PauseOnDisconnect
    {
        get => pauseOnDisconnect;
        set => SetDefault(ref pauseOnDisconnect, value, () => settings.SetGlobal(SettingKeys.PauseOnDisconnect, value));
    }

    public bool PlayOnConnect
    {
        get => playOnConnect;
        set => SetDefault(ref playOnConnect, value, () => settings.SetGlobal(SettingKeys.PlayOnConnect, value));
    }

    public bool AutoConnect
    {
        get => autoConnect;
        set => SetDefault(ref autoConnect, value, () => settings.SetGlobal(SettingKeys.AutoConnect, value));
    }

    /// <summary>Writes a default, then lets devices that follow it show the new value.</summary>
    void SetDefault(ref bool field, bool value, Action write, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name)) return;
        Save(write);
        settingsChanged();
    }

    int selectedLogLevelIndex;
    public int SelectedLogLevelIndex
    {
        get => selectedLogLevelIndex;
        set
        {
            if (value < 0 || !SetProperty(ref selectedLogLevelIndex, value)) return;
            var level = SettingChoices.LogLevels[value];
            Save(() => settings.SetGlobal(SettingKeys.LogLevel, level));
            LogSetup.Reconfigure(dataFolder, level);
        }
    }

    static void Save(Action write)
    {
        try { write(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning(e, "Saving a setting failed");
        }
    }
}
