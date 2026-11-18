using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Pods.Core.Updates;

/// <summary>
/// Decides whether a downloaded update may run. The manifest must carry a valid ECDSA P-256 /
/// SHA-256 signature (IEEE P1363) from one of the trusted keys, name exactly the offered version
/// (newer than the running one, so a signed old release cannot be replayed) and installer, and the
/// installer's bytes must hash to the manifest's SHA-256.
/// </summary>
public static partial class UpdateVerifier
{
    public static UpdateManifest? VerifyManifest(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signature, UpdateOffer offer,
        Version current, IReadOnlyList<string> trustedPublicKeysPem)
    {
        if (!SignedByAny(manifest, signature, trustedPublicKeysPem)) return null;
        if (UpdateManifest.FromJsonBytes(manifest) is not { } parsed) return null;
        if (parsed.Version != offer.Version || parsed.Version <= ReleaseParser.Normalize(current)) return null;
        if (parsed.Installer != Path.GetFileName(offer.Installer.AbsolutePath)) return null;
        if (!Sha256Hex().IsMatch(parsed.Sha256Hex)) return null;
        return parsed;
    }

    public static bool InstallerMatches(Stream installer, UpdateManifest manifest)
    {
        if (!Sha256Hex().IsMatch(manifest.Sha256Hex)) return false;
        var actual = SHA256.HashData(installer);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(manifest.Sha256Hex));
    }

    static bool SignedByAny(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, IReadOnlyList<string> keys)
    {
        foreach (var pem in keys)
        {
            using var key = ECDsa.Create();
            try
            {
                key.ImportFromPem(pem);
            }
            catch (Exception e) when (e is ArgumentException or CryptographicException)
            {
                continue;
            }
            if (key.KeySize == 256 && key.VerifyData(data, signature, HashAlgorithmName.SHA256)) return true;
        }
        return false;
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Hex();
}
