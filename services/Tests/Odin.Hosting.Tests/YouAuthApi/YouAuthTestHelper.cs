using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.X509;

#nullable enable
namespace Odin.Hosting.Tests.YouAuthApi;

public static class YouAuthTestHelper
{
    public const string Password = "EnSøienØ";
    public const string OwnerCookieName = "DY0810";
    public const string HomeCookieName = "XT32";
    public const string AppAuthTokenHeaderName = "BX0900";
    
    public static readonly Guid PhotosAppId = Guid.Parse("32f0bdbf-017f-4fc0-8004-2d4631182d1e");
    public static readonly Guid PhotosDriveType = Guid.Parse("2af68fe7-2fb8-4896-f39f-97c59d60813a");
    public static readonly Guid PhotosDriveAlias = Guid.Parse("6483b7b1-f71b-d43e-b689-6c86148668cc");
    
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
        return UriWithEncryptedQueryString(uri, Base64.Decode(sharedSecretBase64));
    }

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

        var plainBytes = AesCbc.Decrypt(Convert.FromBase64String(payload.Data), ref key, payload.Iv);
        var plainJson = System.Text.Encoding.UTF8.GetString(plainBytes);

        return plainJson;
    }


    //

    public static HttpContent EncryptContent<T>(T item, string sharedSecretBase64)
    {
        var keyBytes = Base64.Decode(sharedSecretBase64);
        var key = new SensitiveByteArray(keyBytes);
        var serializer = new SharedSecretSystemTextJsonContentSerializer(key);
        return serializer.ToHttpContent(item);
    }
    
    //
    
    // SEB:NOTE this doesn't really work with complex types, use with caution
    public static string GenerateQueryString(object obj)
    {
        // ChatGPT was here...
        var properties = obj.GetType().GetProperties();
        var queryString = string.Empty;

        foreach (var property in properties)
        {
            var key = property.Name;
            var value = property.GetValue(obj);

            if (value != null)
            {
                var encodedValue = HttpUtility.UrlEncode(value.ToString());
                var parameter = $"{key}={encodedValue}";

                if (queryString.Length > 0)
                {
                    queryString += "&";
                }

                queryString += parameter;
            }
        }

        return queryString;
    }
    
    //
    
    public static AsymmetricCipherKeyPair GenerateKeyPair()
    {
        // Generate a new RSA key pair with a key size of 2048 bits
        var generator = new RsaKeyPairGenerator();
        generator.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var keys = generator.GenerateKeyPair();
        return keys;
    }    
    
    //

    public static Dictionary<string, string> ParseQueryString(string uri)
    {
        var result = new Dictionary<string, string>();
        
        var parameters = QueryHelpers.ParseQuery(new Uri(uri).Query);
        foreach (var key in parameters.Keys)
        {
            // Sanity
            if (result.ContainsKey(key))
            {
                throw new Exception("HTTP might allow duplicate keys, but we don't!");
            }

            var values = parameters[key];
            
            // Sanity
            if (values.Count > 1)
            {
                throw new Exception("HTTP might allow multiple values, but we don't!");
            }

            result[key] = values.ToString();
        }

        return result;
    }
    

}

