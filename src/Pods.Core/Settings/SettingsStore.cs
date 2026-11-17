using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pods.Core.Settings;

/// <summary>
/// Global settings plus per-device overrides (SPEC §8): <c>settings.json</c> holds global values,
/// <c>devices/&lt;id&gt;.json</c> holds only a device's overrides. Every change is written at once and
/// atomically. A corrupt file is moved aside to <c>.bad</c>; a file from a newer version is read but
/// never written. Thread-safe.
/// </summary>
public sealed partial class SettingsStore
{
    public const int CurrentVersion = 1;
    const string ValuesKey = "values";
    const string OverridesKey = "overrides";

    /// <summary>Entry i migrates a document from version i to i + 1. A missing version is 0.</summary>
    static readonly Func<JsonObject, JsonObject>[] Migrations = [doc => doc];

    readonly string root;
    readonly Lock gate = new();
    readonly SettingsFile global;
    readonly Dictionary<string, SettingsFile> devices = [];
    readonly List<string> warnings = [];

    SettingsStore(string root)
    {
        this.root = root;
        global = Read(Path.Combine(root, "settings.json"), ValuesKey);
    }

    public static SettingsStore Load(string rootFolder) => new(rootFolder);

    /// <summary>True when settings.json comes from a newer version of Pods; changes then stay in memory.</summary>
    public bool IsReadOnly => global.ReadOnly;

    public IReadOnlyList<string> Warnings { get { lock (gate) return [.. warnings]; } }

    public T Get<T>(Setting<T> setting)
    {
        lock (gate) return GlobalValue(setting);
    }

    public T GetForDevice<T>(Setting<T> setting, string deviceId)
    {
        lock (gate)
        {
            var device = Device(deviceId);
            return setting.DeviceScoped && TryConvert(device.Values, setting, out var value)
                ? value
                : GlobalValue(setting);
        }
    }

    public bool IsOverridden<T>(Setting<T> setting, string deviceId)
    {
        lock (gate) return TryConvert(Device(deviceId).Values, setting, out _);
    }

    public void SetGlobal<T>(Setting<T> setting, T value)
    {
        lock (gate)
        {
            global.Values[setting.Key] = JsonSerializer.SerializeToNode(value, setting.TypeInfo);
            Write(global);
        }
    }

    /// <summary>Stores an override, or removes it when <paramref name="value"/> equals the global value.</summary>
    public void SetForDevice<T>(Setting<T> setting, string deviceId, T value)
    {
        if (!setting.DeviceScoped) throw new ArgumentException($"'{setting.Key}' is a global-only setting.", nameof(setting));
        lock (gate)
        {
            var device = Device(deviceId);
            var node = JsonSerializer.SerializeToNode(value, setting.TypeInfo);
            var globalNode = JsonSerializer.SerializeToNode(GlobalValue(setting), setting.TypeInfo);
            if (JsonNode.DeepEquals(node, globalNode)) device.Values.Remove(setting.Key);
            else device.Values[setting.Key] = node;
            Write(device);
        }
    }

    public void ResetToGlobal<T>(Setting<T> setting, string deviceId)
    {
        lock (gate)
        {
            var device = Device(deviceId);
            if (device.Values.Remove(setting.Key)) Write(device);
        }
    }

    T GlobalValue<T>(Setting<T> setting) =>
        TryConvert(global.Values, setting, out var value) ? value : setting.Default;

    /// <summary>
    /// Reads a present key. JSON null counts only for settings whose default is null ("off");
    /// a value of the wrong type counts as absent.
    /// </summary>
    static bool TryConvert<T>(JsonObject values, Setting<T> setting, out T value)
    {
        value = setting.Default;
        if (!values.TryGetPropertyValue(setting.Key, out var node)) return false;
        if (node is null) return setting.Default is null;
        try
        {
            value = node.Deserialize(setting.TypeInfo)!;
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    SettingsFile Device(string deviceId)
    {
        var id = deviceId.ToLowerInvariant();
        if (!DeviceIdPattern().IsMatch(id))
            throw new ArgumentException("Device id must be 12 hexadecimal digits.", nameof(deviceId));
        if (!devices.TryGetValue(id, out var file))
        {
            file = Read(Path.Combine(root, "devices", id + ".json"), OverridesKey);
            devices[id] = file;
        }
        return file;
    }

    SettingsFile Read(string path, string valuesKey)
    {
        if (!File.Exists(path)) return new SettingsFile(path, valuesKey, [], ReadOnly: false);

        JsonObject doc;
        try
        {
            doc = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("Not a JSON object.");
        }
        catch (JsonException e)
        {
            File.Move(path, path + ".bad", overwrite: true);
            warnings.Add($"{Path.GetFileName(path)} was unreadable ({e.Message}); moved to .bad and defaults used.");
            return new SettingsFile(path, valuesKey, [], ReadOnly: false);
        }

        var version = doc["version"] is JsonValue v && v.TryGetValue(out int n) ? n : 0;
        if (version > CurrentVersion)
        {
            warnings.Add($"{Path.GetFileName(path)} is from a newer version ({version}); it will not be changed.");
            return new SettingsFile(path, valuesKey, Values(doc, valuesKey), ReadOnly: true);
        }
        for (var i = version; i < CurrentVersion; i++) doc = Migrations[i](doc);
        return new SettingsFile(path, valuesKey, Values(doc, valuesKey), ReadOnly: false);
    }

    static JsonObject Values(JsonObject doc, string valuesKey) =>
        doc[valuesKey] is JsonObject values ? (JsonObject)values.DeepClone() : [];

    static void Write(SettingsFile file)
    {
        if (file.ReadOnly) return;
        var doc = new JsonObject { ["version"] = CurrentVersion, [file.ValuesKey] = file.Values.DeepClone() };
        Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        var temp = file.Path + ".tmp";
        File.WriteAllText(temp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, file.Path, overwrite: true);
    }

    [GeneratedRegex("^[0-9a-f]{12}$")]
    private static partial Regex DeviceIdPattern();

    sealed record SettingsFile(string Path, string ValuesKey, JsonObject Values, bool ReadOnly);
}
