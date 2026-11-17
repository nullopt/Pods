using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pods.App.Views;
using Pods.Core.Apple;
using Pods.Core.Devices;
using Pods.Core.Settings;

namespace Pods.App.Devices;

/// <summary>
/// One device card. Expanded it follows the iPhone case-open card: earbuds and case as separate
/// images (cut from one turntable sheet) with a ring under each; collapsed it shows one row per part,
/// as in the macOS Bluetooth menu. The collapsed choice is remembered per device.
/// </summary>
internal sealed partial class DeviceCardViewModel : ObservableObject
{
    readonly IHeadphoneSource source;
    readonly SettingsStore settings;
    readonly PartRowViewModel budsRow = new(TurntablePart.Left);
    readonly PartRowViewModel caseRow = new(TurntablePart.Right);
    readonly PartRowViewModel wholeRow = new(TurntablePart.Whole);
    bool? singleLayout;

    public DeviceCardViewModel(HeadphoneSnapshot snapshot, IHeadphoneSource source, SettingsStore settings, bool adapterAvailable)
    {
        this.source = source;
        this.settings = settings;
        Key = snapshot.Key;
        IsCollapsed = settings.GetForDevice(SettingKeys.CardCollapsed, Key);
        Update(snapshot, adapterAvailable);
    }

    public string Key { get; }

    public BatteryViewModel Buds { get; } = new(BatteryBadge.None);
    public BatteryViewModel Left { get; } = new(BatteryBadge.Left);
    public BatteryViewModel Right { get; } = new(BatteryBadge.Right);
    public BatteryViewModel Case { get; } = new(BatteryBadge.Case);
    public BatteryViewModel Single { get; } = new(BatteryBadge.None);

    /// <summary>Rows of the collapsed card.</summary>
    public ObservableCollection<PartRowViewModel> Rows { get; } = [];

    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial string StateText { get; private set; } = "";
    [ObservableProperty] public partial string? Sheet { get; private set; }

    /// <summary>The sheet shows earbuds and case side by side, so they are drawn as two images.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWhole))]
    public partial bool IsSplit { get; private set; }

    public bool IsWhole => !IsSplit;

    /// <summary>Spins only while both pods are in the case, as on iPhone.</summary>
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

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsExpanded), nameof(ChevronGlyph), nameof(ChevronTip))]
    public partial bool IsCollapsed { get; private set; }

    public bool IsExpanded => !IsCollapsed;
    public string ChevronGlyph => IsCollapsed ? "" : "";
    public string ChevronTip => Strings.Get(IsCollapsed ? "CardExpand" : "CardCollapse");

    [RelayCommand]
    void Connect() => source.Connect(Key);

    [RelayCommand]
    void Disconnect() => source.Disconnect(Key);

    [RelayCommand]
    void ToggleCollapsed() => SetCollapsed(!IsCollapsed);

    /// <summary>Clicking a collapsed card's rows expands it.</summary>
    public void ExpandFromRows() => SetCollapsed(false);

    void SetCollapsed(bool value)
    {
        if (IsCollapsed == value) return;
        IsCollapsed = value;
        settings.SetForDevice(SettingKeys.CardCollapsed, Key, value);
    }

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
        IsSplit = Sheet is not null && Turntable.Cut(Sheet) is not null;
        IsSpinning = snapshot.IsInCase;
        IsSingleBattery = snapshot.IsSingleBattery;

        var b = snapshot.Battery;
        int? left = b?.Left, right = b?.Right, @case = b?.Case;
        bool leftCharging = b?.LeftCharging ?? false, rightCharging = b?.RightCharging ?? false, caseCharging = b?.CaseCharging ?? false;
        ShowCombinedBuds = left is not null && left == right && leftCharging == rightCharging;
        Buds.Update(left, leftCharging);
        Left.Update(left, leftCharging);
        Right.Update(right, rightCharging);
        Case.Update(@case, caseCharging);
        Single.Update(left, leftCharging);

        UpdateRows(snapshot.Name, left, right, @case, leftCharging, rightCharging, caseCharging);
    }

    void UpdateRows(string name, int? left, int? right, int? @case, bool leftCharging, bool rightCharging, bool caseCharging)
    {
        if (singleLayout != IsSingleBattery)
        {
            singleLayout = IsSingleBattery;
            Rows.Clear();
            if (IsSingleBattery) Rows.Add(wholeRow);
            else
            {
                Rows.Add(budsRow);
                Rows.Add(caseRow);
            }
        }
        if (IsSingleBattery)
        {
            wholeRow.Update(Sheet, name, BatteryText.Format(left), left, leftCharging);
            return;
        }
        // Without a cut the sheet cannot be split, so the rows show no icon rather than the wrong part.
        var partSheet = IsSplit ? Sheet : null;
        var budsText = left == right
            ? BatteryText.Format(left)
            : $"L {BatteryText.Format(left)} · R {BatteryText.Format(right)}";
        int? lowest = left is null ? right : right is null ? left : Math.Min(left.Value, right.Value);
        budsRow.Update(partSheet, name, budsText, lowest, leftCharging && rightCharging);
        Case.BadgeSheet = partSheet;
        caseRow.Update(partSheet, $"{name} {Strings.Get("CardCaseSuffix")}", BatteryText.Format(@case), @case, caseCharging);
    }
}
