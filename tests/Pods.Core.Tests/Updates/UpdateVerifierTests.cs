using System.Security.Cryptography;
using System.Text;
using Pods.Core.Updates;

namespace Pods.Core.Tests.Updates;

public sealed class UpdateVerifierTests : IDisposable
{
    const string Prefix = "https://github.com/nullopt/Pods/releases/download/";
    static readonly Version Current = new(0, 1, 2);
    static readonly byte[] Installer = Encoding.UTF8.GetBytes("pretend installer");
    static readonly string InstallerHash = Convert.ToHexStringLower(SHA256.HashData(Installer));

    readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    readonly ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose()
    {
        key.Dispose();
        other.Dispose();
    }

    static UpdateOffer Offer(string version, string? installerName = null)
    {
        var baseUrl = $"{Prefix}v{version}/PodsSetup-{version}";
        return new UpdateOffer(Version.Parse(version), new Uri(installerName is null ? baseUrl + ".exe" : $"{Prefix}v{version}/{installerName}"),
            new Uri(baseUrl + ".json"), new Uri(baseUrl + ".json.sig"), new Uri($"https://github.com/nullopt/Pods/releases/tag/v{version}"));
    }

    static UpdateManifest Manifest(string version) => new(Version.Parse(version), $"PodsSetup-{version}.exe", InstallerHash);

    static string Pem(ECDsa k) => k.ExportSubjectPublicKeyInfoPem();

    static byte[] Sign(ECDsa k, byte[] bytes) => k.SignData(bytes, HashAlgorithmName.SHA256);

    [Fact]
    public void Verify_SignedByTrustedKey_ReturnsManifest()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        Assert.Equal(Manifest("0.1.3"), UpdateVerifier.VerifyManifest(bytes, Sign(key, bytes), Offer("0.1.3"), Current, [Pem(key)]));
    }

    [Fact]
    public void Verify_SecondTrustedKeyAlsoAccepted()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        Assert.NotNull(UpdateVerifier.VerifyManifest(bytes, Sign(other, bytes), Offer("0.1.3"), Current, [Pem(key), Pem(other)]));
    }

    [Fact]
    public void Verify_UntrustedKey_Null()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, Sign(other, bytes), Offer("0.1.3"), Current, [Pem(key)]));
    }

    [Fact]
    public void Verify_EmptyKeyList_Null()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, Sign(key, bytes), Offer("0.1.3"), Current, []));
    }

    [Fact]
    public void Verify_TamperedManifestByte_Null()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        var signature = Sign(key, bytes);
        bytes[^3] ^= 1;
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, signature, Offer("0.1.3"), Current, [Pem(key)]));
    }

    [Fact]
    public void Verify_GarbageSignature_Null()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, new byte[63], Offer("0.1.3"), Current, [Pem(key)]));
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, new byte[64], Offer("0.1.3"), Current, [Pem(key)]));
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, [], Offer("0.1.3"), Current, [Pem(key)]));
    }

    [Fact]
    public void Verify_VersionNotOffered_Null()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, Sign(key, bytes), Offer("0.1.4"), Current, [Pem(key)]));
    }

    /// <summary>A genuinely signed old release offered again must not install.</summary>
    [Fact]
    public void Verify_Downgrade_Null()
    {
        var bytes = Manifest("0.1.1").ToJsonBytes();
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, Sign(key, bytes), Offer("0.1.1"), Current, [Pem(key)]));
        var same = Manifest("0.1.2").ToJsonBytes();
        Assert.Null(UpdateVerifier.VerifyManifest(same, Sign(key, same), Offer("0.1.2"), Current, [Pem(key)]));
    }

    [Fact]
    public void Verify_InstallerNameMismatch_Null()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, Sign(key, bytes), Offer("0.1.3", "Other.exe"), Current, [Pem(key)]));
    }

    [Fact]
    public void Verify_BadHashField_Null()
    {
        var bytes = new UpdateManifest(new Version(0, 1, 3), "PodsSetup-0.1.3.exe", "ABC").ToJsonBytes();
        Assert.Null(UpdateVerifier.VerifyManifest(bytes, Sign(key, bytes), Offer("0.1.3"), Current, [Pem(key)]));
    }

    [Fact]
    public void Verify_MalformedPem_SkippedNotThrown()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        Assert.NotNull(UpdateVerifier.VerifyManifest(bytes, Sign(key, bytes), Offer("0.1.3"), Current, ["not a key", Pem(key)]));
    }

    [Fact]
    public void InstallerMatches_RightAndWrongHash()
    {
        Assert.True(UpdateVerifier.InstallerMatches(new MemoryStream(Installer), Manifest("0.1.3")));
        Assert.False(UpdateVerifier.InstallerMatches(new MemoryStream([.. Installer, 0]), Manifest("0.1.3")));
    }

    [Fact]
    public void ManifestJson_RoundTrips_ExactFormat()
    {
        var bytes = Manifest("0.1.3").ToJsonBytes();
        Assert.Equal($$"""{"version":"0.1.3","installer":"PodsSetup-0.1.3.exe","sha256":"{{InstallerHash}}"}""", Encoding.UTF8.GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]);   // no BOM
        Assert.Equal(Manifest("0.1.3"), UpdateManifest.FromJsonBytes(bytes));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":"0.1","installer":"x","sha256":"y"}""")]
    [InlineData("""{"version":"0.1.3"}""")]
    public void ManifestJson_Malformed_Null(string json) => Assert.Null(UpdateManifest.FromJsonBytes(Encoding.UTF8.GetBytes(json)));
}
