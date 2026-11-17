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
        Sync();
    }

    public ObservableCollection<DeviceCardViewModel> Cards { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; private set; } = true;

    public void Dispose() => source.Changed -= OnChanged;

    /// <summary>Events can arrive out of order from different threads, so each one re-reads the whole list.</summary>
    void OnChanged() => ui.TryEnqueue(Sync);

    /// <summary>Updates, adds and removes cards so they match the source, in its order.</summary>
    void Sync()
    {
        var current = source.Current;
        for (var i = Cards.Count - 1; i >= 0; i--)
            if (!current.Any(s => s.Key == Cards[i].Key)) Cards.RemoveAt(i);
        for (var i = 0; i < current.Count; i++)
        {
            var snapshot = current[i];
            var index = IndexOf(snapshot.Key);
            if (index < 0) Cards.Insert(i, new DeviceCardViewModel(snapshot));
            else
            {
                if (index != i) Cards.Move(index, i);
                Cards[i].Update(snapshot);
            }
        }
        IsEmpty = Cards.Count == 0;
    }

    int IndexOf(string key)
    {
        for (var i = 0; i < Cards.Count; i++) if (Cards[i].Key == key) return i;
        return -1;
    }
}
