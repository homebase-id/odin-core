using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Odin.Services.Email.Dkim;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Odin.Services.Tests.Email.Dkim;

public class DkimKeyGeneratorTest
{
    [Test]
    public void ItShouldGenerateTheFixedSelectorSet()
    {
        var keys = DkimKeyGenerator.GenerateKeys();

        Assert.That(keys.Select(k => k.Selector), Is.EqualTo(new[] { "s1", "s2" }));
        Assert.That(keys[0].Algorithm, Is.EqualTo(DkimAlgorithm.Ed25519));
        Assert.That(keys[1].Algorithm, Is.EqualTo(DkimAlgorithm.Rsa));
    }

    [Test]
    public void Ed25519PublicKeyIsTheRawThirtyTwoBytes()
    {
        var key = DkimKeyGenerator.GenerateEd25519Key("s1");

        // RFC 8463: p= is the raw public key, not a SubjectPublicKeyInfo
        Assert.That(key.PublicKey.Length, Is.EqualTo(32));
        Assert.That(key.KTag, Is.EqualTo("ed25519"));
    }

    [Test]
    public void RsaPublicKeyIsSubjectPublicKeyInfoOf2048Bits()
    {
        var key = DkimKeyGenerator.GenerateRsaKey("s2");

        var publicKey = (RsaKeyParameters)PublicKeyFactory.CreateKey(key.PublicKey);
        Assert.That(publicKey.Modulus.BitLength, Is.EqualTo(2048));
        Assert.That(key.KTag, Is.EqualTo("rsa"));
    }

    [Test]
    public void DnsRecordShapeMatchesTheDnsPlan()
    {
        var key = DkimKeyGenerator.GenerateEd25519Key("s1");

        Assert.That(key.DnsRecordName, Is.EqualTo("s1._domainkey"));
        Assert.That(key.DnsRecordValue, Is.EqualTo($"v=DKIM1; k=ed25519; p={Convert.ToBase64String(key.PublicKey)}"));
    }

    [Test]
    public void SignVerifyRoundTripsForBothAlgorithms()
    {
        var data = Encoding.UTF8.GetBytes("dkim test vector");

        foreach (var key in DkimKeyGenerator.GenerateKeys())
        {
            var signature = DkimKeyGenerator.Sign(key, data);
            Assert.That(DkimKeyGenerator.Verify(key.Algorithm, key.PublicKey, data, signature), Is.True,
                $"round trip failed for {key.KTag}");
            Assert.That(DkimKeyGenerator.Verify(key.Algorithm, key.PublicKey, Encoding.UTF8.GetBytes("tampered"), signature), Is.False,
                $"tampered data verified for {key.KTag}");
        }
    }

    [Test]
    public void SignaturesDoNotVerifyAcrossKeyPairs()
    {
        var data = Encoding.UTF8.GetBytes("dkim test vector");
        var first = DkimKeyGenerator.GenerateEd25519Key("s1");
        var second = DkimKeyGenerator.GenerateEd25519Key("s1");

        var signature = DkimKeyGenerator.Sign(first, data);
        Assert.That(DkimKeyGenerator.Verify(second.Algorithm, second.PublicKey, data, signature), Is.False);
    }

    [Test]
    public void AlgorithmFromKTagRejectsUnknownValues()
    {
        Assert.That(DkimKey.AlgorithmFromKTag("ed25519"), Is.EqualTo(DkimAlgorithm.Ed25519));
        Assert.That(DkimKey.AlgorithmFromKTag("rsa"), Is.EqualTo(DkimAlgorithm.Rsa));
        Assert.Throws<ArgumentException>(() => DkimKey.AlgorithmFromKTag("dsa"));
    }
}
