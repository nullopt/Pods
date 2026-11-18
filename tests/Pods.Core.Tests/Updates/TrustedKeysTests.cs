using System.Security.Cryptography;
using Pods.Core.Updates;

namespace Pods.Core.Tests.Updates;

public sealed class TrustedKeysTests
{
    [Fact]
    public void All_HasAtLeastOneValidP256PublicKey()
    {
        Assert.NotEmpty(TrustedKeys.All);
        foreach (var pem in TrustedKeys.All)
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            Assert.Equal(256, key.KeySize);
            Assert.DoesNotContain("PRIVATE", pem);
        }
    }
}
