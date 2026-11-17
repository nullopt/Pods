using Microsoft.Windows.ApplicationModel.Resources;

namespace Pods.App;

/// <summary>UI strings from Strings/en-US/Resources.resw (SPEC §10).</summary>
internal static class Strings
{
    static readonly ResourceLoader Loader = new();

    public static string Get(string key) => Loader.GetString(key);
}
