#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Util;

namespace Odin.Services.Base;

public class SharedSecretEncryptedPayload
{
    public byte[] Iv { get; set; } = Array.Empty<byte>();
    public string Data { get; set; } = "";

    public byte[] Decrypt(SensitiveByteArray key)
    {
        return DecryptInternal(this, key);
    }

    public static SharedSecretEncryptedPayload Encrypt(byte[] payload, SensitiveByteArray encryptionKey)
    {
        //TODO: Need to encrypt w/o buffering

        var iv = ByteArrayUtil.GetRndByteArray(16);
        var encryptedBytes = AesCbc.Encrypt(payload, encryptionKey, iv);

        //TODO: might be better to just put the IV as the first 16 bytes
        return new SharedSecretEncryptedPayload()
        {
            Iv = iv,
            Data = Convert.ToBase64String(encryptedBytes)
        };
    }

    public static async Task<byte[]> Decrypt(Stream stream, SensitiveByteArray key, CancellationToken token = default)
    {
        var ssp = await OdinSystemSerializer.Deserialize<SharedSecretEncryptedPayload>(stream, token);
        OdinValidationUtils.AssertNotNull(ssp, "invalid shared secret payload");
        OdinValidationUtils.AssertNotEmptyByteArray(ssp!.Iv, "missing initialization vector");
        return DecryptInternal(ssp, key);
    }

    public static byte[] Decrypt(byte[] buffer, SensitiveByteArray key)
    {
        return Decrypt(buffer.ToStringFromUtf8Bytes(), key);
    }

    public static byte[] Decrypt(string data, SensitiveByteArray key)
    {
        var ssp = OdinSystemSerializer.Deserialize<SharedSecretEncryptedPayload>(data);
        return DecryptInternal(ssp, key);
    }

    public static byte[] Decrypt(string base64Data, string base64Key)
    {
        var data = Convert.FromBase64String(base64Data);
        var key = new SensitiveByteArray(base64Key);
        return Decrypt(data, key);
    }

    private static byte[] DecryptInternal(SharedSecretEncryptedPayload? ssp, SensitiveByteArray key)
    {
        if (null == ssp)
        {
            throw new OdinClientException("Failed to deserialize SharedSecretEncryptedRequest, result was null",
                OdinClientErrorCode.SharedSecretEncryptionIsInvalid);
        }

        var encryptedBytes = Convert.FromBase64String(ssp.Data);
        var decryptedBytes = AesCbc.Decrypt(encryptedBytes, key, ssp.Iv);
        return decryptedBytes;
    }
}