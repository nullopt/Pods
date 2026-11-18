using System.Globalization;
using Pods.Core.Apple;
using Pods.Core.Devices;
using Serilog;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace Pods.App.Devices;

/// <summary>
/// Paired Apple devices from Windows. Only pairings on the current adapter count (Windows keeps
/// pairings made on other adapters too), and only devices whose hardware ID has Apple's vendor ID
/// and a catalogued model (the product ID byte-swapped is the advert model ID). Each device's
/// <see cref="BluetoothDevice"/> is held so its connection events keep arriving. After
/// <see cref="Start"/>, <see cref="Current"/> keeps the previous list until the new enumeration has
/// finished, so a restart after resume does not flash the cards away. A newly paired device's
/// hardware ID appears only after Windows has installed its drivers, so a pairing without a vendor
/// ID yet is read again on each later update; one that can never qualify (another adapter, vendor
/// or model) is not read again until the watcher restarts.
/// </summary>
internal sealed class WindowsPairedDeviceSource : IPairedDeviceSource, IDisposable
{
    const string ContainerIdKey = "System.Devices.Aep.ContainerId";
    const string HardwareIdsKey = "System.Devices.HardwareIds";

    readonly Lock gate = new();
    readonly List<Entry> entries = [];
    readonly HashSet<string> reading = [];
    readonly HashSet<string> rejected = [];   // pairings that can never qualify on this adapter: not read again
    List<PairedDevice> current = [];
    bool adapterAvailable;
    string? adapterAddress;
    Radio? radio;
    DeviceWatcher? adapterWatcher;
    DeviceWatcher? pairedWatcher;
    bool enumerated;
    int pendingAdds;
    bool started;

    public event Action? Changed;

    public IReadOnlyList<PairedDevice> Current { get { lock (gate) return current; } }

    public bool IsAdapterAvailable { get { lock (gate) return adapterAvailable; } }

    /// <summary>The device's container ID, which its audio interfaces share; null if unknown.</summary>
    internal Guid? ContainerIdOf(string id)
    {
        lock (gate) return entries.FirstOrDefault(e => e.Id == id)?.Container;
    }

    public void Start()
    {
        DeviceWatcher watcher;
        lock (gate)
        {
            if (started) return;
            started = true;
            watcher = adapterWatcher = DeviceInformation.CreateWatcher(BluetoothAdapter.GetDeviceSelector());
        }
        watcher.Added += (_, _) => _ = RefreshAdapterAsync(restartPaired: false);
        watcher.Removed += (_, _) => _ = RefreshAdapterAsync(restartPaired: false);
        watcher.Updated += (_, _) => { };
        watcher.EnumerationCompleted += (_, _) => _ = RefreshAdapterAsync(restartPaired: true);
        watcher.Start();
    }

    public void Stop()
    {
        DeviceWatcher? adapters, paireds;
        lock (gate)
        {
            if (!started) return;
            started = false;
            adapters = adapterWatcher;
            paireds = pairedWatcher;
            adapterWatcher = pairedWatcher = null;
        }
        StopWatcher(adapters);
        StopWatcher(paireds);
    }

    public void Dispose()
    {
        Stop();
        lock (gate)
        {
            foreach (var entry in entries) entry.Device.Dispose();
            entries.Clear();
            if (radio is not null) radio.StateChanged -= OnRadioStateChanged;
            radio = null;
        }
    }

    async Task RefreshAdapterAsync(bool restartPaired)
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            var newRadio = adapter is null ? null : await adapter.GetRadioAsync();
            var address = adapter is null ? null : FormatAddress(adapter.BluetoothAddress);
            bool addressChanged;
            lock (gate)
            {
                if (!started) return;
                if (!ReferenceEquals(radio, newRadio))
                {
                    if (radio is not null) radio.StateChanged -= OnRadioStateChanged;
                    radio = newRadio;
                    if (radio is not null) radio.StateChanged += OnRadioStateChanged;
                }
                adapterAvailable = radio?.State == RadioState.On;
                addressChanged = address != adapterAddress;
                adapterAddress = address;
            }
            if ((restartPaired || addressChanged) && address is not null) RestartPairedWatcher();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading the Bluetooth adapter failed");
        }
    }

    void OnRadioStateChanged(Radio sender, object args)
    {
        lock (gate) adapterAvailable = sender.State == RadioState.On;
        Changed?.Invoke();
    }

    void RestartPairedWatcher()
    {
        DeviceWatcher? old;
        var watcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true), [ContainerIdKey], DeviceInformationKind.AssociationEndpoint);
        lock (gate)
        {
            if (!started) return;
            old = pairedWatcher;
            pairedWatcher = watcher;
            foreach (var entry in entries) entry.Device.Dispose();
            entries.Clear();
            reading.Clear();
            rejected.Clear();
            enumerated = false;
            pendingAdds = 0;
        }
        StopWatcher(old);
        watcher.Added += (sender, info) => _ = AddAsync(sender, info);
        watcher.Removed += (sender, update) => Remove(sender, update.Id);
        watcher.Updated += (sender, update) => _ = RetryAsync(sender, update.Id);
        watcher.EnumerationCompleted += (sender, _) =>
        {
            lock (gate) if (sender == pairedWatcher) enumerated = true;
            Publish();
        };
        watcher.Start();
    }

    /// <summary>
    /// Reads a pairing again that was skipped before, e.g. because its drivers were still installing.
    /// Every paired device's updates arrive here, so ones known never to qualify return at once.
    /// </summary>
    async Task RetryAsync(DeviceWatcher sender, string aepId)
    {
        lock (gate)
        {
            if (sender != pairedWatcher || entries.Any(e => e.AepId == aepId) || reading.Contains(aepId)
                || rejected.Contains(aepId)) return;
        }
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(aepId, [ContainerIdKey], DeviceInformationKind.AssociationEndpoint);
            await AddAsync(sender, info);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Re-reading paired device {Id} failed", aepId);
        }
    }

    async Task AddAsync(DeviceWatcher sender, DeviceInformation info)
    {
        lock (gate)
        {
            if (sender != pairedWatcher || !reading.Add(info.Id)) return;
            pendingAdds++;
        }
        try
        {
            var (entry, never) = await ReadAsync(info);
            if (never)
            {
                lock (gate) if (sender == pairedWatcher) rejected.Add(info.Id);
            }
            else if (entry is not null)
            {
                lock (gate)
                {
                    if (sender != pairedWatcher || entries.Any(e => e.AepId == info.Id))
                    {
                        entry.Device.Dispose();
                        return;
                    }
                    entries.Add(entry);
                }
                entry.Device.ConnectionStatusChanged += (_, _) => Publish();
                entry.Device.NameChanged += (_, _) => Publish();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading paired device {Name} failed", info.Name);
        }
        finally
        {
            lock (gate)
            {
                reading.Remove(info.Id);
                if (sender == pairedWatcher) pendingAdds--;
            }
            Publish();
        }
    }

    /// <summary>
    /// The entry for a qualifying pairing on the current adapter, else null; <c>Never</c> when it can
    /// never qualify on this adapter (paired on another adapter, or another vendor or model).
    /// </summary>
    async Task<(Entry? Entry, bool Never)> ReadAsync(DeviceInformation info)
    {
        string? address;
        lock (gate) address = adapterAddress;
        if (address is null) return (null, false);
        if (!info.Id.StartsWith($"Bluetooth#Bluetooth{address}-", StringComparison.OrdinalIgnoreCase)) return (null, true);
        if (info.Properties.GetValueOrDefault(ContainerIdKey) is not Guid container) return (null, false);

        var nodes = await DeviceInformation.FindAllAsync(
            $"System.Devices.ContainerId:=\"{{{container}}}\"", [HardwareIdsKey], DeviceInformationKind.Device);
        var hardwareIds = nodes.SelectMany(n => n.Properties.GetValueOrDefault(HardwareIdsKey) as string[] ?? []);
        var modelId = HardwareIds.AppleModelId(hardwareIds, out var decided);
        if (modelId is null) return (null, decided);

        var device = await BluetoothDevice.FromIdAsync(info.Id);
        if (device is null) return (null, false);
        return (new Entry(info.Id, device.BluetoothAddress.ToString("x12", CultureInfo.InvariantCulture), modelId, container, device), false);
    }

    void Remove(DeviceWatcher sender, string aepId)
    {
        lock (gate)
        {
            if (sender != pairedWatcher) return;
            var entry = entries.FirstOrDefault(e => e.AepId == aepId);
            if (entry is null) return;
            entries.Remove(entry);
            entry.Device.Dispose();
        }
        Publish();
    }

    void Publish()
    {
        lock (gate)
        {
            if (!enumerated || pendingAdds > 0) return;
            current = [.. entries.Select(e => new PairedDevice(
                e.Id, e.Device.Name, e.ModelId, e.Device.ConnectionStatus == BluetoothConnectionStatus.Connected))];
        }
        Log.Debug("Paired devices: {Devices}", Current);
        Changed?.Invoke();
    }

    static void StopWatcher(DeviceWatcher? watcher)
    {
        if (watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
    }

    static string FormatAddress(ulong address) =>
        string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("x2", CultureInfo.InvariantCulture)));

    sealed record Entry(string AepId, string Id, string ModelId, Guid Container, BluetoothDevice Device);
}
