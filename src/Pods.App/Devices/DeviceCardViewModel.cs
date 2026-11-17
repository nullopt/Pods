using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    readonly IHeadphoneSource source;
    bool? singleLayout;

    public DeviceCardViewModel(HeadphoneSnapshot snapshot, IHeadphoneSource source, bool adapterAvailable)
    {
        this.source = source;
        Key = snapshot.Key;
        Update(snapshot, adapterAvailable);
    }

    public string Key { get; }

    /// <summary>Left, right and case rings; or one ring for single-battery headphones.</summary>
    public ObservableCollection<BatteryViewModel> Batteries { get; } = [];

    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial string StateText { get; private set; } = "";

    /// <summary>Spinning artwork sheet for this model and colour, or null.</summary>
    [ObservableProperty] public partial string? TurntableSheet { get; private set; }

    /// <summary>Artwork and rings are greyed out unless the device is connected to this PC.</summary>
    [ObservableProperty] public partial double ContentOpacity { get; private set; } = 1;

    [ObservableProperty] public partial bool ShowConnect { get; private set; }
    [ObservableProperty] public partial bool ShowDisconnect { get; private set; }

    /// <summary>False while a request is pending or Bluetooth is off.</summary>
    [ObservableProperty] public partial bool CanAct { get; private set; }

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
