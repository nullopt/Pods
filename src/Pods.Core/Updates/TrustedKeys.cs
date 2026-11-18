namespace Pods.Core.Updates;

/// <summary>
/// Public keys (SPKI PEM, ECDSA P-256) whose signatures Pods accepts on updates. A list, so another
/// maintainer's key can be added (or one retired) by a release signed with a key already here.
/// The private keys never leave their owners' machines; see tools/Pods.ReleaseSigner.
/// </summary>
public static class TrustedKeys
{
    public static IReadOnlyList<string> All { get; } =
    [
        // nullopt, created 2026-11-18
        """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEuYMG+LlTrdMG4OdUsB/97mGNp6g9
        bKv1UT7StU15tlIfbSm1fMfKM6ZKaZgPucB5LMmmoTLsJZBhQqZv8a1REQ==
        -----END PUBLIC KEY-----
        """,
    ];
}
