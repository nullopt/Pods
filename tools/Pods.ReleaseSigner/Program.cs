// Creates the Pods release key and signs or verifies release installers.
//   new-key [--out <path>]                   encrypted private key (PEM) + <path>.pub; prints the public key
//   change-passphrase [--key <path>]         re-encrypts the private key with a new passphrase
//   sign <PodsSetup-X.Y.Z.exe> [--key <path>] writes <name>.json (manifest) and <name>.json.sig next to it
//   verify <PodsSetup-X.Y.Z.exe> [--pub <path>] checks signature and hash; exit 0 when valid
// Passphrases are read from the console (masked) or, when input is redirected, one per line from stdin.
// The private key must never be committed or uploaded anywhere.
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Pods.Core.Updates;

var defaultKey = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pods-release", "release-key.pem");
var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);

try
{
    return args.FirstOrDefault() switch
    {
        "new-key" => NewKey(Option("--out") ?? defaultKey),
        "change-passphrase" => ChangePassphrase(Option("--key") ?? defaultKey),
        "sign" when args.Length > 1 => Sign(args[1], Option("--key") ?? defaultKey),
        "verify" when args.Length > 1 => Verify(args[1], Option("--pub")),
        _ => Usage(),
    };
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

int NewKey(string path)
{
    if (File.Exists(path)) return Fail($"{path} already exists; not overwriting a release key.");
    var passphrase = NewPassphrase();
    if (passphrase is null) return 2;
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, key.ExportEncryptedPkcs8PrivateKeyPem(passphrase, pbe));
    var publicPem = key.ExportSubjectPublicKeyInfoPem();
    File.WriteAllText(path + ".pub", publicPem);
    Console.WriteLine($"Private key: {path} (back it up with its passphrase; never commit it)");
    Console.WriteLine($"Public key:  {path}.pub");
    Console.WriteLine(publicPem);
    return 0;
}

int ChangePassphrase(string path)
{
    using var key = LoadPrivate(path);
    if (key is null) return 2;
    var passphrase = NewPassphrase();
    if (passphrase is null) return 2;
    var temp = path + ".tmp";
    File.WriteAllText(temp, key.ExportEncryptedPkcs8PrivateKeyPem(passphrase, pbe));
    File.Move(temp, path, overwrite: true);
    Console.WriteLine($"Passphrase changed for {path}");
    return 0;
}

int Sign(string installer, string keyPath)
{
    if (InstallerVersion(installer) is not { } version) return Fail("Expected a file named PodsSetup-X.Y.Z.exe.");
    using var key = LoadPrivate(keyPath);
    if (key is null) return 2;
    string hash;
    using (var stream = File.OpenRead(installer)) hash = Convert.ToHexStringLower(SHA256.HashData(stream));
    var manifest = new UpdateManifest(version, Path.GetFileName(installer), hash).ToJsonBytes();
    var signature = key.SignData(manifest, HashAlgorithmName.SHA256);
    File.WriteAllBytes(installer[..^4] + ".json", manifest);
    File.WriteAllBytes(installer[..^4] + ".json.sig", signature);
    Console.WriteLine($"Signed {Path.GetFileName(installer)} ({version.ToString(3)}, sha256 {hash})");
    var publicPem = key.ExportSubjectPublicKeyInfoPem();
    if (!TrustedKeys.All.Any(k => Normalise(k) == Normalise(publicPem)))
        Console.Error.WriteLine("Warning: this key is not in TrustedKeys.All, so installed Pods will refuse this update.");
    return 0;
}

int Verify(string installer, string? publicKeyPath)
{
    if (InstallerVersion(installer) is not { } version) return Fail("Expected a file named PodsSetup-X.Y.Z.exe.");
    var keys = publicKeyPath is null ? TrustedKeys.All : [File.ReadAllText(publicKeyPath)];
    var name = Path.GetFileName(installer);
    var prefix = $"{ReleaseParser.DownloadPrefix}v{version.ToString(3)}/{name[..^4]}";
    var offer = new UpdateOffer(version, new Uri(prefix + ".exe"), new Uri(prefix + ".json"), new Uri(prefix + ".json.sig"),
        new Uri(ReleaseParser.ReleasePagePrefix));
    var manifest = UpdateVerifier.VerifyManifest(File.ReadAllBytes(installer[..^4] + ".json"), File.ReadAllBytes(installer[..^4] + ".json.sig"),
        offer, new Version(0, 0, 0), keys);
    if (manifest is null) return Fail("Manifest signature or contents are not valid for a trusted key.");
    using var stream = File.OpenRead(installer);
    if (!UpdateVerifier.InstallerMatches(stream, manifest)) return Fail("Installer does not match the manifest's SHA-256.");
    Console.WriteLine($"Valid: {name} {version.ToString(3)}");
    return 0;
}

ECDsa? LoadPrivate(string path)
{
    if (!File.Exists(path))
    {
        Fail($"No key at {path}. Create one with: new-key");
        return null;
    }
    var key = ECDsa.Create();
    try
    {
        key.ImportFromEncryptedPem(File.ReadAllText(path), ReadPassphrase("Passphrase: "));
        return key;
    }
    catch (Exception e) when (e is CryptographicException or ArgumentException)
    {
        key.Dispose();
        Fail("Wrong passphrase or not a release key.");
        return null;
    }
}

string? NewPassphrase()
{
    var first = ReadPassphrase("New passphrase (12+ characters): ");
    if (first.Length < 12)
    {
        Fail("The passphrase must have at least 12 characters.");
        return null;
    }
    if (ReadPassphrase("Repeat it: ") != first)
    {
        Fail("The passphrases differ.");
        return null;
    }
    return first;
}

static string ReadPassphrase(string prompt)
{
    // Piped input may start with a byte-order mark (Windows PowerShell adds one).
    if (Console.IsInputRedirected) return (Console.In.ReadLine() ?? "").TrimStart('﻿');
    Console.Write(prompt);
    var text = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (text.Length > 0) text.Length--;
        }
        else if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
    }
    Console.WriteLine();
    return text.ToString();
}

static Version? InstallerVersion(string path)
{
    var match = Regex.Match(Path.GetFileName(path), @"^PodsSetup-(\d+\.\d+\.\d+)\.exe$");
    return match.Success && File.Exists(path) ? ReleaseParser.ParseVersion(match.Groups[1].Value) : null;
}

static string Normalise(string pem) => pem.Replace("\r", "").Trim();

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("Usage: new-key [--out <path>] | change-passphrase [--key <path>] | sign <PodsSetup-X.Y.Z.exe> [--key <path>] | verify <PodsSetup-X.Y.Z.exe> [--pub <path>]");
    return 1;
}
