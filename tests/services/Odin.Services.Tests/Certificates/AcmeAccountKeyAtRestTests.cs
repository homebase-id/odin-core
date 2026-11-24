using System.Security.Cryptography;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Certificate;

namespace Odin.Services.Tests.Certificates;

#nullable enable

/// <summary>The ACME account key at rest (#1748): sealed under the storage key, a plain PEM from before still read.</summary>
public class AcmeAccountKeyAtRestTests
{
    private static readonly string KeyPem = MakeKeyPem();

    [Test]
    public void ASealedKey_OpensToTheSameKey_AndHoldsNoPem()
    {
        var storageKey = RandomNumberGenerator.GetBytes(32);

        var sealedKey = AcmeAccountKeyAtRest.Seal(KeyPem, storageKey);
        Assert.That(sealedKey, Does.Not.Contain("BEGIN"));
        Assert.That(sealedKey, Is.Not.EqualTo(AcmeAccountKeyAtRest.Seal(KeyPem, storageKey)), "a fresh IV per seal");

        var (opened, wasPlain) = AcmeAccountKeyAtRest.Open(sealedKey, storageKey);
        Assert.That(opened, Is.EqualTo(KeyPem));
        Assert.That(wasPlain, Is.False);
    }

    [Test]
    public void APlainPemFromBefore_IsReadAsItIs_AndSaidToBePlain()
    {
        var (opened, wasPlain) = AcmeAccountKeyAtRest.Open(KeyPem, RandomNumberGenerator.GetBytes(32));
        Assert.That(opened, Is.EqualTo(KeyPem));
        Assert.That(wasPlain, Is.True);
    }

    [Test]
    public void AWrongStorageKey_IsRefused_NotReadAsNoAccount()
    {
        var sealedKey = AcmeAccountKeyAtRest.Seal(KeyPem, RandomNumberGenerator.GetBytes(32));
        Assert.Throws<OdinSystemException>(() => AcmeAccountKeyAtRest.Open(sealedKey, RandomNumberGenerator.GetBytes(32)));
    }

    [Test]
    public void AValueInNoKnownFormat_IsRefused()
    {
        Assert.Throws<OdinSystemException>(() => AcmeAccountKeyAtRest.Open("garbage", RandomNumberGenerator.GetBytes(32)));
        Assert.Throws<OdinSystemException>(() => AcmeAccountKeyAtRest.Open("aes-gcm:zz:zz", RandomNumberGenerator.GetBytes(32)));
    }

    private static string MakeKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportPkcs8PrivateKeyPem();
    }
}
