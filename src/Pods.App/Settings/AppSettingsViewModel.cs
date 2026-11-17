using CommunityToolkit.Mvvm.ComponentModel;
using Pods.App.Logging;
using Pods.Core.Settings;
using Serilog;

namespace Pods.App.Settings;

/// <summary>App-wide settings on the back of the flyout: the default low battery warning and the log level.</summary>
internal sealed partial class AppSettingsViewModel : ObservableObject
{
    readonly SettingsStore settings;
    readonly string dataFolder;
    readonly Action settingsChanged;
    readonly IReadOnlyList<int> percents;

    /// <param name="settingsChanged">Called after the default threshold is written, so devices on the default follow.</param>
    public AppSettingsViewModel(SettingsStore settings, string dataFolder, Action settingsChanged)
    {
        this.settings = settings;
        this.dataFolder = dataFolder;
        this.settingsChanged = settingsChanged;

        var threshold = settings.Get(SettingKeys.LowBatteryPercent);
        percents = SettingChoices.LowBatteryPercents(threshold);
        ThresholdChoices = [.. percents.Select(p => $"{p}%")];
        selectedThresholdIndex = percents.ToList().IndexOf(threshold);

        var level = settings.Get(SettingKeys.LogLevel);
        var index = SettingChoices.LogLevels.ToList().FindIndex(l => l.Equals(level, StringComparison.OrdinalIgnoreCase));
        // An unknown name logs at Warning (see LogSetup), so show that.
        selectedLogLevelIndex = index >= 0 ? index : SettingChoices.LogLevels.ToList().IndexOf("Warning");
    }

    public IReadOnlyList<string> ThresholdChoices { get; }
    public IReadOnlyList<string> LogLevels => SettingChoices.LogLevels;
    public string Version { get; } = string.Format(Strings.Get("AppVersion"), typeof(App).Assembly.GetName().Version?.ToString(3));

    int selectedThresholdIndex;
    public int SelectedThresholdIndex
    {
        get => selectedThresholdIndex;
        set
        {
            if (value < 0 || !SetProperty(ref selectedThresholdIndex, value)) return;
            Save(() => settings.SetGlobal(SettingKeys.LowBatteryPercent, percents[value]));
            settingsChanged();
        }
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
