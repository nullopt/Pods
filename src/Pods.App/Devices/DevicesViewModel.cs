using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Pods.Core.Devices;
using Pods.Core.Settings;

namespace Pods.App.Devices;

/// <summary>Device cards for one open window; updates arrive on any thread and are marshalled to the UI.</summary>
internal sealed partial class DevicesViewModel : ObservableObject, IDisposable
{
    readonly IHeadphoneSource source;
    readonly SettingsStore settings;
    readonly DispatcherQueue ui;

    public DevicesViewModel(IHeadphoneSource source, SettingsStore settings, DispatcherQueue ui)
    {
        this.source = source;
        this.settings = settings;
        this.ui = ui;
        source.Changed += OnChanged;
        Sync();
    }

    public ObservableCollection<DeviceCardViewModel> Cards { get; } = [];

    /// <summary>No paired device to show while Bluetooth works.</summary>
    [ObservableProperty] public partial bool IsEmpty { get; private set; } = true;

    /// <summary>No cards because Bluetooth is off or there is no adapter.</summary>
    [ObservableProperty] public partial bool IsAdapterUnavailable { get; private set; }

    public void Dispose() => source.Changed -= OnChanged;

    /// <summary>Events can arrive out of order from different threads, so each one re-reads the whole list.</summary>
    void OnChanged() => ui.TryEnqueue(Sync);

    /// <summary>Updates, adds and removes cards so they match the source, connected devices first.</summary>
    void Sync()
    {
        var current = DeviceOrder.ConnectedFirst(source.Current);
        var adapterAvailable = source.IsAdapterAvailable;
        for (var i = Cards.Count - 1; i >= 0; i--)
            if (!current.Any(s => s.Key == Cards[i].Key)) Cards.RemoveAt(i);
        for (var i = 0; i < current.Count; i++)
        {
            var snapshot = current[i];
            var index = IndexOf(snapshot.Key);
            if (index < 0) Cards.Insert(i, new DeviceCardViewModel(snapshot, source, settings, adapterAvailable));
            else
            {
                if (index != i) Cards.Move(index, i);
                Cards[i].Update(snapshot, adapterAvailable);
            }
        }
        IsEmpty = Cards.Count == 0 && adapterAvailable;
        IsAdapterUnavailable = Cards.Count == 0 && !adapterAvailable;
    }

    int IndexOf(string key)
    {
        for (var i = 0; i < Cards.Count; i++) if (Cards[i].Key == key) return i;
        return -1;
    }
}
