using System;
using System.Security.Cryptography;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;

namespace Odin.Hosting.Tests.YouAuthApi.IntegrationTests;

public class YouAuthCryptoTest
{
    [Test]
    public void CryptoFlowTest()
    {
        //
        // CLIENT
        //
        var clientPrivateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var clientKeyPair = new EccFullKeyData(clientPrivateKey, 1);

        // Request CLIENT -> HOST
        var queryStringClientPublicKey = clientKeyPair.PublicKeyJwkBase64Url();

        //
        // HOST
        //
        var hostPrivateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var hostKeyPair = new EccFullKeyData(hostPrivateKey, 1);
        var hostSalt = ByteArrayUtil.GetRndByteArray(16);

        var remoteClientPublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(queryStringClientPublicKey);
        var hostSharedSecret = hostKeyPair.GetEcdhSharedSecret(hostPrivateKey, remoteClientPublicKey, hostSalt);
        var hostSharedDigest = SHA256.Create().ComputeHash(hostSharedSecret.GetKey()).ToBase64();

        // Response HOST -> CLIENT
        var responseHostPublicKey = hostKeyPair.PublicKeyJwkBase64Url();
        var responseHostSalt = Convert.ToBase64String(hostSalt);

        //
        // CLIENT
        //
        var remoteHostPublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(responseHostPublicKey);
        var clientSharedSecret = clientKeyPair.GetEcdhSharedSecret(clientPrivateKey, remoteHostPublicKey, Convert.FromBase64String(responseHostSalt));
        var clientSharedDigest = SHA256.Create().ComputeHash(clientSharedSecret.GetKey()).ToBase64();

        Assert.That(Convert.ToBase64String(clientSharedSecret.GetKey()), Is.EqualTo(Convert.ToBase64String(hostSharedSecret.GetKey())));
        Assert.That(clientSharedDigest, Is.EqualTo(hostSharedDigest));
    }
}