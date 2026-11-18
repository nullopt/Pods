using System.Text.Json;
using Pods.Core.Updates;

namespace Pods.Core.Tests.Updates;

public sealed class ReleaseParserTests
{
    const string Prefix = "https://github.com/nullopt/Pods/releases/download/";
    static readonly Version Current = new(0, 1, 2);

    static string Release(string tag, params string[] assetNames) => Release(tag, assetNames.Select(n => (n, $"{Prefix}{tag}/{n}")).ToArray());

    static string Release(string tag, (string Name, string Url)[] assets) => JsonSerializer.Serialize(new
    {
        tag_name = tag,
        html_url = $"https://github.com/nullopt/Pods/releases/tag/{tag}",
        assets = assets.Select(a => new { name = a.Name, browser_download_url = a.Url }),
    });

    static string[] Signed(string v) => [$"PodsSetup-{v}.exe", $"PodsSetup-{v}.json", $"PodsSetup-{v}.json.sig"];

    [Fact]
    public void Parse_NewerSignedRelease_Offers()
    {
        var offer = ReleaseParser.Parse(Release("v0.1.3", Signed("0.1.3")), Current)!;
        Assert.Equal(new Version(0, 1, 3), offer.Version);
        Assert.EndsWith("/v0.1.3/PodsSetup-0.1.3.exe", offer.Installer.AbsoluteUri);
        Assert.EndsWith("/PodsSetup-0.1.3.json", offer.Manifest.AbsoluteUri);
        Assert.EndsWith("/PodsSetup-0.1.3.json.sig", offer.Signature.AbsoluteUri);
        Assert.Equal("https://github.com/nullopt/Pods/releases/tag/v0.1.3", offer.ReleasePage.AbsoluteUri);
    }

    [Fact]
    public void Parse_RunningVersionWithRevision_IsNormalised() =>
        Assert.NotNull(ReleaseParser.Parse(Release("v0.1.3", Signed("0.1.3")), new Version(0, 1, 2, 0)));

    [Theory]
    [InlineData(0, 1, 3)]
    [InlineData(0, 2, 0)]
    public void Parse_SameOrOlder_NoOffer(int major, int minor, int patch) =>
        Assert.Null(ReleaseParser.Parse(Release("v0.1.3", Signed("0.1.3")), new Version(major, minor, patch)));

    [Fact]
    public void Parse_MissingManifestOrSignature_NoOffer()
    {
        Assert.Null(ReleaseParser.Parse(Release("v0.1.3", "PodsSetup-0.1.3.exe"), Current));
        Assert.Null(ReleaseParser.Parse(Release("v0.1.3", "PodsSetup-0.1.3.exe", "PodsSetup-0.1.3.json"), Current));
    }

    [Theory]
    [InlineData("v0.2.0-beta")]
    [InlineData("0.2.0")]
    [InlineData("v0.2")]
    [InlineData("v0.2.0.1")]
    public void Parse_PrereleaseOrOddTag_NoOffer(string tag) =>
        Assert.Null(ReleaseParser.Parse(Release(tag, Signed(tag.TrimStart('v'))), Current));

    [Theory]
    [InlineData("https://evil.example/PodsSetup-0.1.3.exe")]
    [InlineData("https://github.com/someone/Pods/releases/download/v0.1.3/PodsSetup-0.1.3.exe")]
    [InlineData("http://github.com/nullopt/Pods/releases/download/v0.1.3/PodsSetup-0.1.3.exe")]
    public void Parse_AssetOutsideRepo_NoOffer(string installerUrl)
    {
        var json = Release("v0.1.3",
        [
            ("PodsSetup-0.1.3.exe", installerUrl),
            ("PodsSetup-0.1.3.json", Prefix + "v0.1.3/PodsSetup-0.1.3.json"),
            ("PodsSetup-0.1.3.json.sig", Prefix + "v0.1.3/PodsSetup-0.1.3.json.sig"),
        ]);
        Assert.Null(ReleaseParser.Parse(json, Current));
    }

    [Fact]
    public void Parse_AssetNameForOtherVersion_NoOffer() =>
        Assert.Null(ReleaseParser.Parse(Release("v0.1.3", Signed("0.1.4")), Current));

    [Theory]
    [InlineData("""{"message":"API rate limit exceeded for 1.2.3.4."}""")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"tag_name":5}""")]
    public void Parse_ErrorBody_NoOffer(string body) => Assert.Null(ReleaseParser.Parse(body, Current));

    [Fact]
    public void Normalize_DropsRevision() => Assert.Equal(new Version(0, 1, 1), ReleaseParser.Normalize(new Version(0, 1, 1, 0)));

    [Theory]
    [InlineData("0.1.3", true)]
    [InlineData("10.20.30", true)]
    [InlineData("0.1", false)]
    [InlineData("0.1.3-beta", false)]
    [InlineData(" 0.1.3", false)]
    public void ParseVersion_OnlyThreeNumbers(string text, bool valid) => Assert.Equal(valid, ReleaseParser.ParseVersion(text) is not null);
}
