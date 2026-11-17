using System.Text.Json.Serialization.Metadata;

namespace Pods.Core.Settings;

/// <summary>A typed settings key. Device-scoped settings may have per-device overrides.</summary>
public sealed class Setting<T>(string key, T defaultValue, JsonTypeInfo<T> typeInfo, bool deviceScoped)
{
    public string Key { get; } = key;
    public T Default { get; } = defaultValue;
    public JsonTypeInfo<T> TypeInfo { get; } = typeInfo;
    public bool DeviceScoped { get; } = deviceScoped;
}
