using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Odin.Core;
using Odin.PushNotification.Apns;

namespace Odin.PushNotification.Tests.Apns;

/// <summary>A throwaway P-256 key stands in for Apple's .p8; the token's shape and signature are checkable without Apple.</summary>
public class ApnsProviderTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Token_HasEs256Header_IssuerClaims_AndAVerifiableSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var token = ApnsProviderToken.Create(key, "ABC123DEFG", "TEAM000001", Now);

        var parts = token.Split('.');
        Assert.That(parts, Has.Length.EqualTo(3), $"token was {token}");

        using var header = JsonDocument.Parse(Base64UrlEncoder.Decode(parts[0]));
        Assert.That(header.RootElement.GetProperty("alg").GetString(), Is.EqualTo("ES256"));
        Assert.That(header.RootElement.GetProperty("kid").GetString(), Is.EqualTo("ABC123DEFG"));

        using var claims = JsonDocument.Parse(Base64UrlEncoder.Decode(parts[1]));
        Assert.That(claims.RootElement.GetProperty("iss").GetString(), Is.EqualTo("TEAM000001"));
        Assert.That(claims.RootElement.GetProperty("iat").GetInt64(), Is.EqualTo(Now.ToUnixTimeSeconds()));

        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64UrlEncoder.Decode(parts[2]);
        Assert.That(signature, Has.Length.EqualTo(64), "ES256 JWS signatures are raw r||s, 32 bytes each");
        Assert.That(key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            Is.True, "signature must verify with the key's public half");
    }

    [Test]
    public void LoadKey_ReadsThePkcs8PemAppleHandsOut()
    {
        using var original = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = original.ExportPkcs8PrivateKeyPem();

        using var loaded = ApnsProviderToken.LoadKey(pem);

        var token = ApnsProviderToken.Create(loaded, "kid", "team", Now);
        var parts = token.Split('.');
        var ok = original.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Base64UrlEncoder.Decode(parts[2]),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.That(ok, Is.True, "a token signed with the loaded key must verify with the original");
    }
}
