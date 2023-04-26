#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;

namespace Youverse.Core.Services.Base;

public class SharedSecretEncryptedPayload
{
    public byte[] Iv { get; set; } = System.Array.Empty<byte>();
    public string Data { get; set; } = "";

    public static SharedSecretEncryptedPayload Encrypt(byte[] payload, SensitiveByteArray encryptionKey)
    {
        //TODO: Need to encrypt w/o buffering

        var iv = ByteArrayUtil.GetRndByteArray(16);
        var encryptedBytes = AesCbc.Encrypt(payload, ref encryptionKey, iv);

        //TODO: might be better to just put the IV as the first 16 bytes
        return new SharedSecretEncryptedPayload()
        {
            Iv = iv,
            Data = Convert.ToBase64String(encryptedBytes)
        };
    }

    public static async Task<byte[]> Decrypt(Stream stream, SensitiveByteArray key, CancellationToken token = default)
    {
        var ssp = await DotYouSystemSerializer.Deserialize<SharedSecretEncryptedPayload>(stream, token);
        return DecryptInternal(ssp, key);
    }
    
    public static byte[] Decrypt(byte[] buffer, SensitiveByteArray key)
    {
        return Decrypt(buffer.ToStringFromUtf8Bytes(), key);
    }

    public static byte[] Decrypt(string data, SensitiveByteArray key)
    {
        var ssp = DotYouSystemSerializer.Deserialize<SharedSecretEncryptedPayload>(data);
        return DecryptInternal(ssp, key);
    }

    private static byte[] DecryptInternal(SharedSecretEncryptedPayload? ssp, SensitiveByteArray key)
    {
        if (null == ssp)
        {
            throw new YouverseClientException("Failed to deserialize SharedSecretEncryptedRequest, result was null",
                YouverseClientErrorCode.SharedSecretEncryptionIsInvalid);
        }

        var encryptedBytes = Convert.FromBase64String(ssp.Data);
        var decryptedBytes = AesCbc.Decrypt(encryptedBytes, ref key, ssp.Iv);
        return decryptedBytes;
    }
}