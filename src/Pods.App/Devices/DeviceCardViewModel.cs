using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pods.App.Views;
using Pods.Core.Apple;
using Pods.Core.Devices;

namespace Pods.App.Devices;

internal sealed partial class DeviceCardViewModel : ObservableObject
{
    readonly BatteryViewModel left = new(Strings.Get("BatteryLeft"));
    readonly BatteryViewModel right = new(Strings.Get("BatteryRight"));
    readonly BatteryViewModel @case = new(Strings.Get("BatteryCase"));
    readonly BatteryViewModel single = new(Strings.Get("BatterySingle"));
    bool? singleLayout;

    public DeviceCardViewModel(HeadphoneSnapshot snapshot)
    {
        Key = snapshot.Key;
        Update(snapshot);
    }

    public string Key { get; }

    /// <summary>Left, right and case rings; or one ring for single-battery headphones.</summary>
    public ObservableCollection<BatteryViewModel> Batteries { get; } = [];

    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial string StateText { get; private set; } = "";

    /// <summary>Spinning artwork sheet for this model and colour, or null.</summary>
    [ObservableProperty] public partial string? TurntableSheet { get; private set; }

    public void Update(HeadphoneSnapshot snapshot)
    {
        Name = snapshot.Name;
        StateText = Strings.Get("State" + snapshot.State);
        TurntableSheet = Turntable.Resolve(snapshot.ModelId, snapshot.Colour, TurntableView.Available);

        if (singleLayout != snapshot.IsSingleBattery)
        {
            singleLayout = snapshot.IsSingleBattery;
            Batteries.Clear();
            if (snapshot.IsSingleBattery) Batteries.Add(single);
            else
            {
                Batteries.Add(left);
                Batteries.Add(right);
                Batteries.Add(@case);
            }
        }

        var b = snapshot.Battery;
        if (snapshot.IsSingleBattery) single.Update(b?.Left, b?.LeftCharging ?? false);
        else
        {
            left.Update(b?.Left, b?.LeftCharging ?? false);
            right.Update(b?.Right, b?.RightCharging ?? false);
            @case.Update(b?.Case, b?.CaseCharging ?? false);
        }
    }
}
