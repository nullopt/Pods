namespace Pods.Core.Updates;

/// <summary>
/// Public keys (SPKI PEM, ECDSA P-256) whose signatures Pods accepts on updates. A list, so another
/// maintainer's key can be added (or one retired) by a release signed with a key already here.
/// The private keys never leave their owners' machines; see tools/Pods.ReleaseSigner.
/// </summary>
public static class TrustedKeys
{
    public static IReadOnlyList<string> All { get; } = [];
}
