namespace Pods.Core.Updates;

/// <summary>A newer release on GitHub with the three assets an update needs, all under this repo's downloads.</summary>
public sealed record UpdateOffer(Version Version, Uri Installer, Uri Manifest, Uri Signature, Uri ReleasePage);
