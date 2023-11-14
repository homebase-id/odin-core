using System.Web;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Base64 = Org.BouncyCastle.Utilities.Encoders.Base64;

namespace YouAuthClientReferenceImplementation.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public abstract class BaseController : Controller
{
    private readonly ILogger _logger;

    //

    protected BaseController(ILogger logger)
    {
        _logger = logger;
    }

    //

    protected void ShowError(string message)
    {
        TempData["ErrorMessage"] = message;
    }

    //

    protected static string UriWithEncryptedQueryString(string uri, string sharedSecretBase64)
    {
        return UriWithEncryptedQueryString(uri, Base64.Decode(sharedSecretBase64));
    }

    //

    protected static string UriWithEncryptedQueryString(string uri, byte[] sharedSecret)
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
        var encryptedBytes = AesCbc.Encrypt(query.ToUtf8ByteArray(), ref key, iv);

        var payload = new SharedSecretEncryptedPayload()
        {
            Iv = iv,
            Data = encryptedBytes.ToBase64()
        };

        uri = $"{path}?ss={HttpUtility.UrlEncode(OdinSystemSerializer.Serialize(payload))}";

        return uri;
    }

    //

    protected static async Task<T> DecryptContent<T>(HttpResponseMessage response, string sharedSecretBase64)
    {
        return await DecryptContent<T>(response, Base64.Decode(sharedSecretBase64));
    }

    protected static async Task<T> DecryptContent<T>(HttpResponseMessage response, byte[] sharedSecret)
    {
        var cipherJson = await response.Content.ReadAsStringAsync();
        var payload = OdinSystemSerializer.Deserialize<SharedSecretEncryptedPayload>(cipherJson);
        if (payload == null)
        {
            throw new Exception("Error deserializing");
        }

        var key = new SensitiveByteArray(sharedSecret);

        var plainBytes = AesCbc.Decrypt(Convert.FromBase64String(payload.Data), ref key, payload.Iv);
        var plainJson = System.Text.Encoding.UTF8.GetString(plainBytes);

        var result = Deserialize<T>(plainJson);

        return result;
    }

    //

    protected static T Deserialize<T>(string json)
    {
        var result = OdinSystemSerializer.Deserialize<T>(json);
        if (result == null)
        {
            throw new Exception($"Error deserializing {json}");
        }

        return result;
    }



}