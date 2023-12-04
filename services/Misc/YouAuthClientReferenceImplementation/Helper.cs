using System.Web;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Org.BouncyCastle.Utilities.Encoders;

namespace YouAuthClientReferenceImplementation;

public static class Helper
{
    public static string CombineQueryStrings(params string[] queryStrings)
    {
        return string.Join("&", queryStrings.Where(qs => !string.IsNullOrEmpty(qs)));
    }

    //

    public static T Deserialize<T>(string json)
    {
        var result = OdinSystemSerializer.Deserialize<T>(json);
        if (result == null)
        {
            throw new Exception($"Error deserializing {json}");
        }

        return result;
    }

    //

    public static string Serialize<T>(T value)
    {
        return OdinSystemSerializer.Serialize(value);
    }

    //

    public static string UriWithEncryptedQueryString(string uri, string sharedSecretBase64)
    {
        return UriWithEncryptedQueryString(uri, Base64.Decode(sharedSecretBase64));
    }

    //

    public static string UriWithEncryptedQueryString(string uri, byte[] sharedSecret)
    {
        var queryIndex = uri.IndexOf('?');
        if (queryIndex == -1 || queryIndex == uri.Length - 1)
        {
            return uri;
        }

        var path = uri[..queryIndex];
        var query = uri[(queryIndex + 1)..];

        var key = new SensitiveByteArray(sharedSecret);

        var iv = ByteArrayUtil.GetRndByteArray(16);
        var encryptedBytes = AesCbc.Encrypt(query.ToUtf8ByteArray(), key, iv);

        var payload = new SharedSecretEncryptedPayload()
        {
            Iv = iv,
            Data = encryptedBytes.ToBase64()
        };

        uri = $"{path}?ss={HttpUtility.UrlEncode(OdinSystemSerializer.Serialize(payload))}";

        return uri;
    }

    //

    public static async Task<T> DecryptContent<T>(HttpResponseMessage response, string sharedSecretBase64)
    {
        return await DecryptContent<T>(response, Base64.Decode(sharedSecretBase64));
    }

    public static async Task<T> DecryptContent<T>(HttpResponseMessage response, byte[] sharedSecret)
    {
        var cipherJson = await response.Content.ReadAsStringAsync();
        return DecryptContent<T>(cipherJson, sharedSecret);
    }

    public static T DecryptContent<T>(string content, string sharedSecretBase64)
    {
        return DecryptContent<T>(content, Base64.Decode(sharedSecretBase64));
    }

    public static T DecryptContent<T>(string content, byte[] sharedSecret)
    {
        var plainText = DecryptContent(content, sharedSecret);
        return Deserialize<T>(plainText);
    }

    public static string DecryptContent(string content, string sharedSecretBase64)
    {
        return DecryptContent(content, Base64.Decode(sharedSecretBase64));
    }

    public static string DecryptContent(string content, byte[] sharedSecret)
    {
        var payload = OdinSystemSerializer.Deserialize<SharedSecretEncryptedPayload>(content);
        if (payload == null)
        {
            throw new Exception("Error deserializing");
        }

        var key = new SensitiveByteArray(sharedSecret);

        var plainBytes = AesCbc.Decrypt(Convert.FromBase64String(payload.Data), key, payload.Iv);
        var plainJson = System.Text.Encoding.UTF8.GetString(plainBytes);

        return plainJson;
    }

    //


}