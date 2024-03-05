using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.Tests.Base;

public class SharedSecretEncryptedPayloadTest
{
    [Test]
    public void ItShouldEncryptPlainAndDecryptFromBase64()
    {
        var key = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16));
        const string data = "mydata";

        var encrypted = SharedSecretEncryptedPayload.Encrypt(data.ToUtf8ByteArray(), key);
        var serialized = OdinSystemSerializer.Serialize(encrypted);

        var base64Key = key.GetKey().ToBase64();
        var base64Data = serialized.ToUtf8ByteArray().ToBase64();

        var decrypted = SharedSecretEncryptedPayload.Decrypt(base64Data, base64Key);
        var decryptedString = decrypted.ToStringFromUtf8Bytes();

        Assert.That(decryptedString, Is.EqualTo(data));
    }

    //

    [Test]
    public void ItShouldDecryptFromBase64()
    {
        const string base64Key = "8bUumNIyZpHrVr9B4l0rSQ==";
        const string base64Data = "eyJpdiI6ImYrU3p0dVpWZFE5Smd4VE5UTGw3K1E9PSIsImRhdGEiOiI4a1F3U3EyV1BHRTZFNUV5WHpMeDc3M2VJOWg4UHRueVUwWE1Tamk5TzRjPSJ9";

        var decrypted = SharedSecretEncryptedPayload.Decrypt(base64Data, base64Key);
        var decryptedString = decrypted.ToStringFromUtf8Bytes();

        Assert.That(decryptedString, Is.EqualTo("{\"command\":\"ping\"}"));
    }

    //

}