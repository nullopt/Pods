using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pods.App.Views;
using Pods.Core.Apple;
using Pods.Core.Devices;
using Pods.Core.Settings;
using Serilog;

namespace Pods.App.Devices;

/// <summary>
/// One device tile, a condensed iPhone case-open card: earbuds and case as separate images (cut from
/// one turntable sheet) with a ring under each, or the whole headphones over their rings. Its back
/// holds the device's settings.
/// </summary>
internal sealed partial class DeviceCardViewModel : ObservableObject
{
    readonly IHeadphoneSource source;
    readonly SettingsStore settings;
    readonly Action settingsChanged;
    (int Default, int? Override) shownThreshold = (-1, null);
    bool syncingThreshold;

    /// <param name="settingsChanged">Called after a setting is written, so everything showing it can refresh.</param>
    public DeviceCardViewModel(HeadphoneSnapshot snapshot, IHeadphoneSource source, SettingsStore settings, bool adapterAvailable,
        Action settingsChanged)
    {
        this.source = source;
        this.settings = settings;
        this.settingsChanged = settingsChanged;
        Key = snapshot.Key;
        ShowLidPopupSetting = LidPopupSetting.Supported(snapshot.ModelId);
        lidPopup = LidPopupSetting.IsOn(settings, Key, snapshot.ModelId);
        Update(snapshot, adapterAvailable);
    }

    public string Key { get; }

    public BatteryViewModel Buds { get; } = new(BatteryBadge.None);
    public BatteryViewModel Left { get; } = new(BatteryBadge.Left);
    public BatteryViewModel Right { get; } = new(BatteryBadge.Right);
    public BatteryViewModel Case { get; } = new(BatteryBadge.Case);
    public BatteryViewModel Single { get; } = new(BatteryBadge.None);

    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial string StateText { get; private set; } = "";
    [ObservableProperty] public partial string? Sheet { get; private set; }

    /// <summary>The sheet shows earbuds and case side by side, so they are drawn as two images.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWhole))]
    public partial bool IsSplit { get; private set; }

    public bool IsWhole => !IsSplit;

    /// <summary>Earbuds spin only while both pods are in the case, as on iPhone; headphones without a case always spin.</summary>
    [ObservableProperty] public partial bool IsSpinning { get; private set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPodRings))]
    public partial bool IsSingleBattery { get; private set; }

    public bool HasPodRings => !IsSingleBattery;

    /// <summary>One ring for both earbuds when they match; otherwise separate L and R rings.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSeparateBuds))]
    public partial bool ShowCombinedBuds { get; private set; }

    public bool ShowSeparateBuds => !ShowCombinedBuds;

    /// <summary>Artwork and rings are greyed out unless the device is connected to this PC.</summary>
    [ObservableProperty] public partial double ContentOpacity { get; private set; } = 1;

    [ObservableProperty] public partial bool ShowConnect { get; private set; }
    [ObservableProperty] public partial bool ShowDisconnect { get; private set; }

    /// <summary>False while a request is pending or Bluetooth is off.</summary>
    [ObservableProperty] public partial bool CanAct { get; private set; }

    /// <summary>The tile shows its back: this device's settings.</summary>
    [ObservableProperty] public partial bool IsFlipped { get; private set; }

    [RelayCommand]
    void Flip() => IsFlipped = !IsFlipped;

    /// <summary>The low battery warning, 5-50%; equal to the app default means "follow the default", as the store keeps it.</summary>
    [ObservableProperty] public partial double Threshold { get; set; }

    /// <summary>"30%", or "20% (app default)" while the device follows the default.</summary>
    [ObservableProperty] public partial string ThresholdText { get; private set; } = "";

    /// <summary>The device has its own value, so it can go back to the default.</summary>
    [ObservableProperty] public partial bool HasOwnThreshold { get; private set; }

    partial void OnThresholdChanged(double value)
    {
        if (syncingThreshold) return;
        var percent = (int)Math.Round(value);
        Save(() => settings.SetForDevice(SettingKeys.LowBatteryPercent, Key, percent));
        ThresholdText = $"{percent}%";
        settingsChanged();
    }

    /// <summary>The model can show the lid-open popup, so the tile offers the switch.</summary>
    public bool ShowLidPopupSetting { get; }

    bool lidPopup;
    /// <summary>Show the popup when this device's case opens.</summary>
    public bool LidPopup
    {
        get => lidPopup;
        set
        {
            if (!SetProperty(ref lidPopup, value)) return;
            Save(() => LidPopupSetting.Set(settings, Key, value));
        }
    }

    [RelayCommand]
    void UseAppDefault()
    {
        Save(() => settings.ResetToGlobal(SettingKeys.LowBatteryPercent, Key));
        settingsChanged();
    }

    static void Save(Action write)
    {
        try { write(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning(e, "Saving a device setting failed");
        }
    }

    /// <summary>Re-reads the threshold only when the default or the override changed: updates arrive every second.</summary>
    void SyncThreshold()
    {
        var global = settings.Get(SettingKeys.LowBatteryPercent);
        int? overridden = settings.IsOverridden(SettingKeys.LowBatteryPercent, Key)
            ? settings.GetForDevice(SettingKeys.LowBatteryPercent, Key)
            : null;
        if (shownThreshold == (global, overridden)) return;
        shownThreshold = (global, overridden);
        syncingThreshold = true;
        try
        {
            // Kept inside the slider's range so it never pushes a corrected value back as a change.
            Threshold = Math.Clamp(overridden ?? global, SettingChoices.LowBatteryMin, SettingChoices.LowBatteryMax);
            ThresholdText = overridden is { } o ? $"{o}%" : string.Format(Strings.Get("ThresholdDefault"), global);
            HasOwnThreshold = overridden is not null;
        }
        finally
        {
            syncingThreshold = false;
        }
    }

    [RelayCommand]
    void Connect() => source.Connect(Key);

    [RelayCommand]
    void Disconnect() => source.Disconnect(Key);

    public void Update(HeadphoneSnapshot snapshot, bool adapterAvailable)
    {
        var connected = snapshot.State == DeviceState.Connected;
        var pending = snapshot.Request is ConnectionRequest.Connecting or ConnectionRequest.Disconnecting;
        Name = snapshot.Name;
        StateText = Strings.Get(snapshot.Request == ConnectionRequest.None ? "State" + snapshot.State : "Request" + snapshot.Request);
        ContentOpacity = connected ? 1 : 0.4;
        ShowConnect = !connected;
        ShowDisconnect = connected;
        CanAct = adapterAvailable && !pending;

        Sheet = Turntable.Resolve(snapshot.ModelId, snapshot.Colour, TurntableView.Available);
        IsSplit = Sheet is not null && Turntable.Parts(Sheet) is not null;
        IsSpinning = IsSplit ? snapshot.IsInCase : Sheet is not null;
        IsSingleBattery = snapshot.IsSingleBattery;

        var threshold = settings.GetForDevice(SettingKeys.LowBatteryPercent, Key);
        var b = snapshot.Battery;
        int? left = b?.Left, right = b?.Right, @case = b?.Case;
        bool leftCharging = b?.LeftCharging ?? false, rightCharging = b?.RightCharging ?? false, caseCharging = b?.CaseCharging ?? false;
        ShowCombinedBuds = left is not null && left == right && leftCharging == rightCharging;
        Buds.Update(left, leftCharging, threshold);
        Left.Update(left, leftCharging, threshold);
        Right.Update(right, rightCharging, threshold);
        Case.Update(@case, caseCharging, threshold);
        // Single-battery headsets report their level in whichever pod slot their advert uses.
        int? single = left ?? right;
        var singleCharging = left is null ? rightCharging : leftCharging;
        Single.Update(single, singleCharging, threshold);
        SyncThreshold();
    }
}
