using System.Text.Json.Serialization;

namespace Pods.Core.Settings;

[JsonSourceGenerationOptions(UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(EarDetectionMode))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
