using System.Text.Json;

namespace Pods.Core.Updates;

/// <summary>
/// What a release signs: its version, the installer's file name and the installer's SHA-256.
/// Serialised as compact UTF-8 JSON without a BOM; the signature covers those exact bytes.
/// </summary>
public sealed record UpdateManifest(Version Version, string Installer, string Sha256Hex)
{
    public byte[] ToJsonBytes()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("version", Version.ToString(3));
            writer.WriteString("installer", Installer);
            writer.WriteString("sha256", Sha256Hex);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    public static UpdateManifest? FromJsonBytes(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || String(root, "version") is not { } versionText || ReleaseParser.ParseVersion(versionText) is not { } version
                || String(root, "installer") is not { } installer
                || String(root, "sha256") is not { } sha256)
                return null;
            return new UpdateManifest(version, installer, sha256);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
