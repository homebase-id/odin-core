using System;
using Odin.Core.Cryptography.Data;

namespace Odin.Core.Cryptography;

public class YouAuthCrypto
{
    public static (EccFullKeyData keyPair, byte[] randomSalt) CreateEccKeyPair(SensitiveByteArray privateKey)
    {
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);
        var randomSalt = ByteArrayUtil.GetRndByteArray(16);
        return (keyPair, randomSalt);
    }

}