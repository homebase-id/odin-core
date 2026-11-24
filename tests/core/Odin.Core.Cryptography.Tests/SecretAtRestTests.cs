using System.Security.Cryptography;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;

namespace Odin.Core.Cryptography.Tests;

#nullable enable

public class SecretAtRestTests
{
    private const string Secret = "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----";
    private const string Context = "acme-account-prod-pem";

    [Test]
    public void ASealedSecret_OpensToTheSame_AndDoesNotHoldItInTheClear()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        var sealedSecret = SecretAtRest.Seal(Secret, key, Context);
        Assert.That(sealedSecret, Does.Not.Contain("BEGIN"));
        Assert.That(sealedSecret, Is.Not.EqualTo(SecretAtRest.Seal(Secret, key, Context)), "a fresh nonce per seal");
        Assert.That(SecretAtRest.Open(sealedSecret, key, Context), Is.EqualTo(Secret));
    }

    [Test]
    public void AWrongKey_IsRefused()
    {
        var sealedSecret = SecretAtRest.Seal(Secret, RandomNumberGenerator.GetBytes(32), Context);
        Assert.Throws<OdinSystemException>(() => SecretAtRest.Open(sealedSecret, RandomNumberGenerator.GetBytes(32), Context));
    }

    [Test]
    public void ASecretSealedForAnotherContext_IsRefused()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var sealedSecret = SecretAtRest.Seal(Secret, key, "acme-account-staging-pem");
        Assert.Throws<OdinSystemException>(() => SecretAtRest.Open(sealedSecret, key, Context));
    }

    [Test]
    public void ANonceOfAnyOtherLength_IsRefused()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var sealedSecret = SecretAtRest.Seal(Secret, key, Context);
        var parts = sealedSecret.Split(':');
        Assert.Throws<OdinSystemException>(() => SecretAtRest.Open($"{parts[0]}:{parts[1]}00:{parts[2]}", key, Context));
    }

    [TestCase("garbage")]
    [TestCase("aes-gcm:zz:zz")]
    [TestCase("aes-gcm:00112233")]
    [TestCase("aes-gcm:00:00")]
    [TestCase("aes-gcm:00112233445566778899AABB:")]
    [TestCase(Secret)]
    public void AValueInNoKnownScheme_IsRefused(string stored)
    {
        Assert.Throws<OdinSystemException>(() => SecretAtRest.Open(stored, RandomNumberGenerator.GetBytes(32), Context));
    }
}
