using System;
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;

namespace Odin.Core.Cryptography.Tests;

/// <summary>
/// A full ECC key as a private JWK, RFC 7518 §6.2.2: the public key's <c>kty</c>, <c>crv</c>,
/// <c>x</c>, <c>y</c> plus <c>d</c>, the private scalar, base64url and zero-padded to the curve
/// size like the coordinates. Small (about 240 characters for P-384) and portable, where the DER
/// form carries the curve parameters spelled out.
/// </summary>
[TestFixture]
public class TestEccPrivateJwk
{
    private static SensitiveByteArray NewPassword() => new(Guid.NewGuid().ToByteArray());

    [TestCase(EccKeySize.P384, 48)]
    [TestCase(EccKeySize.P256, 32)]
    public void ThePrivateJwkIsTheFiveMembersAtCurveSize(EccKeySize size, int coordinateBytes)
    {
        var password = NewPassword();
        var fullKey = new EccFullKeyData(password, size, 1);

        var jwk = fullKey.PrivateKeyJwk(password);
        var members = JsonSerializer.Deserialize<Dictionary<string, string>>(jwk)!;

        Assert.That(members.Keys, Is.EquivalentTo(new[] { "kty", "crv", "x", "y", "d" }), jwk);
        Assert.That(members["kty"], Is.EqualTo("EC"));
        Assert.That(members["crv"], Is.EqualTo(EccPublicKeyData.eccKeyTypeNames[(int)size]));
        Assert.That(Base64UrlEncoder.Decode(members["d"]), Has.Length.EqualTo(coordinateBytes), "d is zero-padded like x and y");
        Assert.That(jwk, Does.Not.Contain("="), "base64url, no padding");
    }

    [Test]
    public void ThePrivateJwkCarriesThePublicJwk()
    {
        var password = NewPassword();
        var fullKey = new EccFullKeyData(password, EccKeySize.P384, 1);

        var priv = JsonSerializer.Deserialize<Dictionary<string, string>>(fullKey.PrivateKeyJwk(password))!;
        var pub = JsonSerializer.Deserialize<Dictionary<string, string>>(fullKey.PublicKeyJwk())!;

        Assert.That(priv["x"], Is.EqualTo(pub["x"]));
        Assert.That(priv["y"], Is.EqualTo(pub["y"]));
    }

    [TestCase(EccKeySize.P384)]
    [TestCase(EccKeySize.P256)]
    public void AKeyRoundTripsThroughItsPrivateJwk(EccKeySize size)
    {
        var password = NewPassword();
        var original = new EccFullKeyData(password, size, 1);

        var restored = EccFullKeyData.FromJwkPrivateKey(password, original.PrivateKeyJwk(password), hours: 1);

        Assert.That(restored.crc32c, Is.EqualTo(original.crc32c), "the CRC that identifies the key survives: the public DER is rebuilt exactly, curve seed included");
        Assert.That(restored.privateDerBase64(password), Is.EqualTo(original.privateDerBase64(password)), "and so is the private DER");
    }

    [Test]
    public void ARestoredKeyDerivesTheSameSharedSecret()
    {
        var pwdFrodo = NewPassword();
        var frodo = new EccFullKeyData(pwdFrodo, EccKeySize.P384, 1);
        var pwdSam = NewPassword();
        var sam = new EccFullKeyData(pwdSam, EccKeySize.P384, 1);
        var salt = ByteArrayUtil.GetRndByteArray(16);

        var frodoRestored = EccFullKeyData.FromJwkPrivateKey(pwdFrodo, frodo.PrivateKeyJwk(pwdFrodo));

        var fromOriginal = frodo.GetEcdhSharedSecret(pwdFrodo, sam, salt).GetKey();
        var fromRestored = frodoRestored.GetEcdhSharedSecret(pwdFrodo, sam, salt).GetKey();
        var samsView = sam.GetEcdhSharedSecret(pwdSam, frodoRestored, salt).GetKey();
        Assert.That(fromRestored, Is.EqualTo(fromOriginal));
        Assert.That(samsView, Is.EqualTo(fromOriginal), "and the other party agrees with the restored key's public half");
    }

    [Test]
    public void ARestoredKeySignsAndItsSignatureVerifies()
    {
        var password = NewPassword();
        var original = new EccFullKeyData(password, EccKeySize.P256, 1);
        var restored = EccFullKeyData.FromJwkPrivateKey(password, original.PrivateKeyJwk(password));
        var message = new byte[] { 1, 2, 3, 4, 5 };

        var signature = restored.Sign(password, message);

        Assert.That(original.VerifySignature(message, signature), Is.True, "the original public key verifies what the restored key signed");
    }

    [Test]
    public void ThePrivateJwkIsSmall()
    {
        var password = NewPassword();
        var fullKey = new EccFullKeyData(password, EccKeySize.P384, 1);

        var jwk = fullKey.PrivateKeyJwk(password);
        var der = fullKey.privateDerBase64(password);

        Assert.That(jwk.Length, Is.LessThan(260), $"P-384 private JWK is {jwk.Length} chars; the DER form is {der.Length} base64 chars");
    }

    [Test]
    public void ThePrivateJwkTravelsAsBase64Url()
    {
        var password = NewPassword();
        var original = new EccFullKeyData(password, EccKeySize.P384, 1);

        var restored = EccFullKeyData.FromJwkBase64UrlPrivateKey(password, original.PrivateKeyJwkBase64Url(password));

        Assert.That(restored.PublicKeyJwk(), Is.EqualTo(original.PublicKeyJwk()));
    }

    [Test]
    public void TheExpirationIsTheImportersChoice()
    {
        var password = NewPassword();
        var original = new EccFullKeyData(password, EccKeySize.P384, 1);

        var restored = EccFullKeyData.FromJwkPrivateKey(password, original.PrivateKeyJwk(password), hours: 3);

        Assert.That(restored.expiration.seconds, Is.GreaterThan(original.expiration.seconds + 3600), "like the public import: the lifetime is not in the JWK");
        Assert.That(restored.IsExpired(), Is.False);
    }

    [TestCase("""{"kty":"RSA","crv":"P-384","x":"AA","y":"AA","d":"AA"}""", typeof(InvalidOperationException))]
    [TestCase("""{"kty":"EC","crv":"P-521","x":"AA","y":"AA","d":"AA"}""", typeof(InvalidOperationException))]
    [TestCase("""{"kty":"EC","crv":"P-384","x":"not base64url!","y":"AA","d":"AA"}""", typeof(OdinClientException))]
    public void AJwkThatIsNotAPrivateEcKeyIsRefused(string jwk, Type refusal)
    {
        Assert.That(() => EccFullKeyData.FromJwkPrivateKey(NewPassword(), jwk), Throws.TypeOf(refusal), "the same refusals as the public import");
    }

    [Test]
    public void APublicJwkIsNotAPrivateKey()
    {
        var password = NewPassword();
        var fullKey = new EccFullKeyData(password, EccKeySize.P384, 1);

        Assert.That(() => EccFullKeyData.FromJwkPrivateKey(password, fullKey.PublicKeyJwk()),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("d"));
    }
}
