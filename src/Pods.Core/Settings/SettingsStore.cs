using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pods.Core.Settings;

/// <summary>
/// Global settings plus per-device overrides: <c>settings.json</c> holds global values,
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

    /// <summary>
    /// Raised for each warning after <see cref="Load"/>, e.g. when a device file read on first use is
    /// invalid. Raised while the store is locked: the handler must not call back into the store.
    /// </summary>
    public event Action<string>? WarningAdded;

    /// <summary>Caller holds <see cref="gate"/> (or is the constructor).</summary>
    void Warn(string message)
    {
        warnings.Add(message);
        WarningAdded?.Invoke(message);
    }

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

        var name = Path.GetFileName(path);
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Locked or inaccessible: use defaults in memory and never write over the file.
            Warn($"{name} could not be read ({e.Message}); defaults used and changes will not be saved.");
            return new SettingsFile(path, valuesKey, [], ReadOnly: true);
        }

        var (doc, version, problem) = Parse(text, valuesKey);
        if (problem is not null)
        {
            try
            {
                File.Move(path, path + ".bad", overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Warn($"{name} is invalid ({problem}) and could not be moved aside; defaults used and changes will not be saved.");
                return new SettingsFile(path, valuesKey, [], ReadOnly: true);
            }
            Warn($"{name} was invalid ({problem}); moved to .bad and defaults used.");
            return new SettingsFile(path, valuesKey, [], ReadOnly: false);
        }

        if (version > CurrentVersion)
        {
            Warn($"{Path.GetFileName(path)} is from a newer version ({version}); it will not be changed.");
            return new SettingsFile(path, valuesKey, Values(doc, valuesKey), ReadOnly: true);
        }
        for (var i = version; i < CurrentVersion; i++) doc = Migrations[i](doc);
        return new SettingsFile(path, valuesKey, Values(doc, valuesKey), ReadOnly: false);
    }

    /// <summary>
    /// A valid file is a JSON object with an optional non-negative integer "version" (missing = 0)
    /// and an object under <paramref name="valuesKey"/>. Anything else is reported as a problem.
    /// </summary>
    static (JsonObject Doc, int Version, string? Problem) Parse(string text, string valuesKey)
    {
        JsonObject? doc;
        try
        {
            doc = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException e)
        {
            return ([], 0, e.Message);
        }
        if (doc is null) return ([], 0, "not a JSON object");

        var version = 0;
        if (doc.TryGetPropertyValue("version", out var node))
        {
            if (node is not JsonValue value || !value.TryGetValue(out version) || version < 0)
                return (doc, 0, "\"version\" is not a non-negative integer");
        }
        if (doc[valuesKey] is not JsonObject) return (doc, version, $"\"{valuesKey}\" is missing or not an object");
        return (doc, version, null);
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
