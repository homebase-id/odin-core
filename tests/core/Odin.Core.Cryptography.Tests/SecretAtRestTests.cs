using System.Security.Cryptography;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;

namespace Odin.Core.Cryptography.Tests;

#nullable enable

public class SecretAtRestTests
{
    private const string Secret = "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----";

    [Test]
    public void ASealedSecret_OpensToTheSame_AndDoesNotHoldItInTheClear()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        var sealedSecret = SecretAtRest.Seal(Secret, key);
        Assert.That(sealedSecret, Does.Not.Contain("BEGIN"));
        Assert.That(sealedSecret, Is.Not.EqualTo(SecretAtRest.Seal(Secret, key)), "a fresh IV per seal");
        Assert.That(SecretAtRest.Open(sealedSecret, key), Is.EqualTo(Secret));
    }

    [Test]
    public void AWrongKey_IsRefused()
    {
        var sealedSecret = SecretAtRest.Seal(Secret, RandomNumberGenerator.GetBytes(32));
        Assert.Throws<OdinSystemException>(() => SecretAtRest.Open(sealedSecret, RandomNumberGenerator.GetBytes(32)));
    }

    [TestCase("garbage")]
    [TestCase("aes-gcm:zz:zz")]
    [TestCase("aes-gcm:00112233")]
    [TestCase("aes-gcm:00:00")]
    [TestCase("aes-gcm:00112233445566778899AABBCCDDEEFF:")]
    [TestCase(Secret)]
    public void AValueInNoKnownScheme_IsRefused(string stored)
    {
        Assert.Throws<OdinSystemException>(() => SecretAtRest.Open(stored, RandomNumberGenerator.GetBytes(32)));
    }
}
