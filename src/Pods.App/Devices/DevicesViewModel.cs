using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Pods.Core.Devices;

namespace Pods.App.Devices;

/// <summary>Device cards for one open window; updates arrive on any thread and are marshalled to the UI.</summary>
internal sealed partial class DevicesViewModel : ObservableObject, IDisposable
{
    readonly IHeadphoneSource source;
    readonly DispatcherQueue ui;

    public DevicesViewModel(IHeadphoneSource source, DispatcherQueue ui)
    {
        this.source = source;
        this.ui = ui;
        source.Changed += OnChanged;
        foreach (var snapshot in source.Current) Apply(snapshot);
    }

    public ObservableCollection<DeviceCardViewModel> Cards { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; private set; } = true;

    public void Dispose() => source.Changed -= OnChanged;

    void OnChanged(HeadphoneSnapshot snapshot) => ui.TryEnqueue(() => Apply(snapshot));

    void Apply(HeadphoneSnapshot snapshot)
    {
        var card = Cards.FirstOrDefault(c => c.Key == snapshot.Key);
        if (card is null) Cards.Add(new DeviceCardViewModel(snapshot));
        else card.Update(snapshot);
        IsEmpty = Cards.Count == 0;
    }
}
