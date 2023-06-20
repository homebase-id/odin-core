using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Org.BouncyCastle.Utilities.Encoders;

#nullable enable
namespace Odin.Hosting.Tests.YouAuthApi;

public static class YouAuthTestHelper
{
    public const string Password = "EnSøienØ";
    public const string OwnerCookieName = "DY0810";
    public const string HomeCookieName = "XT32";
    public const string AppCookieName = "BX0900";
    
    private static readonly JsonSerializerOptions SerializerOptions = new ()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    //

    public static T Deserialize<T>(string json)
    {
        var result = JsonSerializer.Deserialize<T>(json, SerializerOptions);
        if (result == null)
        {
            throw new Exception($"Error deserializing {json}");
        }

        return result;
    }
    
    //
    
    public static T Deserialize<T>(ReadOnlySpan<byte> json)
    {
        var result = JsonSerializer.Deserialize<T>(json, SerializerOptions);
        if (result == null)
        {
            throw new Exception($"Error deserializing");
        }

        return result;
    }

    //
    
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value);
    }
    
    //
    
    public static string? GetHeaderValue(this HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            return values.FirstOrDefault();
        }
        return default;
    }
    
    //

    public static Dictionary<string, string> GetCookies(this HttpResponseMessage response)
    {
        var result = new Dictionary<string, string>();
        
        if (response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            foreach (var cookieHeader in cookieHeaders)
            {
                var name = cookieHeader.Split(';')[0].Split('=')[0];
                var value = cookieHeader.Split(';')[0].Split('=')[1];
                result[name] = value;
            }
        }

        return result;
    }
    
    //

    public static string UriWithEncryptedQueryString(string uri, string sharedSecretBase64)
    {
        var queryIndex = uri.IndexOf('?');
        if (queryIndex == -1 || queryIndex == uri.Length - 1)
        {
            return uri;
        }
        
        var path = uri[..queryIndex];
        var query = uri[(queryIndex + 1)..];
        
        var keyBytes = Base64.Decode(sharedSecretBase64);
        var key = new SensitiveByteArray(keyBytes);

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

    public static async Task<T> DecryptContent<T>(HttpResponseMessage response, string sharedSecretBase64)
    {
        var cipherJson = await response.Content.ReadAsStringAsync();
        var payload = OdinSystemSerializer.Deserialize<SharedSecretEncryptedPayload>(cipherJson);
        if (payload == null)
        {
            throw new Exception("Error deserializing");
        }
        
        var keyBytes = Base64.Decode(sharedSecretBase64);
        var key = new SensitiveByteArray(keyBytes);
        
        var plainJson = AesCbc.Decrypt(Convert.FromBase64String(payload.Data), ref key, payload.Iv);
        
        var result = Deserialize<T>(plainJson);

        return result;
    }
    
    //

    

}

