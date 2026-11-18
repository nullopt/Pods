using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pods.Core.Updates;

/// <summary>
/// Reads GitHub's "latest release" JSON. An offer needs a <c>vX.Y.Z</c> tag newer than the running
/// version and the installer, manifest and signature for exactly that version, each downloaded from
/// this repo's releases over HTTPS. Anything else, including error bodies, is no offer.
/// </summary>
public static partial class ReleaseParser
{
    public const string DownloadPrefix = "https://github.com/nullopt/Pods/releases/download/";
    public const string ReleasePagePrefix = "https://github.com/nullopt/Pods/releases/";

    public static UpdateOffer? Parse(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryString(root, "tag_name", out var tag) || !tag.StartsWith('v')
                || ParseVersion(tag[1..]) is not { } version || version <= Normalize(current)
                || !TryString(root, "html_url", out var page) || !page.StartsWith(ReleasePagePrefix, StringComparison.Ordinal)
                || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            var urls = new Dictionary<string, Uri>(StringComparer.Ordinal);
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind == JsonValueKind.Object && TryString(asset, "name", out var name)
                    && TryString(asset, "browser_download_url", out var url))
                    urls[name] = new Uri(url);
            }

            var file = $"PodsSetup-{version.ToString(3)}";
            if (Asset(urls, file + ".exe") is not { } installer
                || Asset(urls, file + ".json") is not { } manifest
                || Asset(urls, file + ".json.sig") is not { } signature)
                return null;
            return new UpdateOffer(version, installer, manifest, signature, new Uri(page));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Exactly three dot-separated numbers, e.g. "0.1.3"; anything else is null.</summary>
    public static Version? ParseVersion(string text) =>
        ThreePartVersion().IsMatch(text) && Version.TryParse(text, out var v) ? v : null;

    /// <summary>Major.Minor.Build of a version: assembly versions carry a fourth part ("0.1.1.0").</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    static Uri? Asset(Dictionary<string, Uri> urls, string name) =>
        urls.TryGetValue(name, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.AbsoluteUri.StartsWith(DownloadPrefix, StringComparison.Ordinal) ? uri : null;

    static bool TryString(JsonElement element, string property, out string value)
    {
        value = "";
        if (!element.TryGetProperty(property, out var p) || p.ValueKind != JsonValueKind.String) return false;
        value = p.GetString() ?? "";
        return true;
    }

    [GeneratedRegex(@"^\d{1,9}\.\d{1,9}\.\d{1,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex ThreePartVersion();
}
