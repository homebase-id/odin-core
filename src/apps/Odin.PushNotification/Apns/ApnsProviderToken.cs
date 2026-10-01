using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Odin.Core;

namespace Odin.PushNotification.Apns;

/// <summary>
/// APNs provider authentication: a JWT signed ES256 with the .p8 key, sent as
/// "authorization: bearer ...". Apple accepts a token for an hour and rejects one older than that
/// or refreshed more often than every 20 minutes, so the sender caches it and renews after
/// <see cref="Lifetime"/>. Hand-rolled on purpose: the relay references only Odin.Core and the
/// token is three base64url parts.
/// </summary>
public static class ApnsProviderToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(50);

    public static string Create(ECDsa key, string keyId, string teamId, DateTimeOffset now)
    {
        var header = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = keyId }));
        var claims = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new { iss = teamId, iat = now.ToUnixTimeSeconds() }));
        var signingInput = Encoding.ASCII.GetBytes($"{header}.{claims}");

        // JWS wants the raw r||s concatenation, not the DER sequence .NET produces by default.
        var signature = key.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{header}.{claims}.{Base64UrlEncoder.Encode(signature)}";
    }

    /// <summary>Loads the .p8 (a PKCS#8 "PRIVATE KEY" PEM) Apple hands out for an APNs auth key.</summary>
    public static ECDsa LoadKey(string pem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }
}
