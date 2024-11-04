#nullable enable

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Refit;

namespace Odin.Hosting.Tests;

public sealed class SharedSecretSystemTextJsonContentSerializer : IHttpContentSerializer
{
    /// <summary>
    /// The JSON serialization options to use
    /// </summary>
    readonly JsonSerializerOptions jsonSerializerOptions;

    private readonly SensitiveByteArray _sharedSecret;

    /// <summary>
    /// Creates a new <see cref="SystemTextJsonContentSerializer"/> instance
    /// </summary>
    public SharedSecretSystemTextJsonContentSerializer(SensitiveByteArray sharedSecret)
    {
        _sharedSecret = sharedSecret;
        this.jsonSerializerOptions = new JsonSerializerOptions(OdinSystemSerializer.JsonSerializerOptions!);
        jsonSerializerOptions.Converters.Add(new ObjectToInferredTypesConverter());
    }

    /// <inheritdoc/>
    public HttpContent ToHttpContent<T>(T item)
    {
        var content = JsonContent.Create(item, options: jsonSerializerOptions);
        var contentBytes = content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

        var iv = ByteArrayUtil.GetRndByteArray(16);
        var key = _sharedSecret; //#wierd
        var encryptedBytes = AesCbc.Encrypt(contentBytes, key, iv);

        var payload = new SharedSecretEncryptedPayload()
        {
            Iv = iv,
            Data = encryptedBytes.ToBase64()
        };

        return JsonContent.Create(payload, payload.GetType(), MediaTypeHeaderValue.Parse("application/json"), OdinSystemSerializer.JsonSerializerOptions);
    }

    /// <inheritdoc/>
    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, CancellationToken cancellationToken = default)
    {
        //TOOD: need to see if the content is empty
        //I cannot check the headers :( so i will assume that if the content is empty, we had a 204
        var json = await content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrEmpty(json))
        {
            return default;
        }

        var payload = OdinSystemSerializer.Deserialize<SharedSecretEncryptedPayload>(json);

        if (payload == null)
        {
            throw new Exception($"Response was not type of {nameof(SharedSecretEncryptedPayload)}");
        }

        var key = _sharedSecret;
        var decryptedBytes = AesCbc.Decrypt(Convert.FromBase64String(payload.Data), key, payload.Iv);
        if(decryptedBytes.Length>0)
        {
            var c = await (new ByteArrayContent(decryptedBytes)).ReadFromJsonAsync<T>(jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
            return c;
        }

        return (T)default!;
    }

    public string? GetFieldNameForProperty(PropertyInfo propertyInfo)
    {
        if (propertyInfo is null)
            throw new ArgumentNullException(nameof(propertyInfo));

        return propertyInfo.GetCustomAttributes<JsonPropertyNameAttribute>(true)
            .Select(a => a.Name)
            .FirstOrDefault();
    }
}

public sealed class SharedSecretUrlParameterFormatter : IUrlParameterFormatter
{
    private readonly SensitiveByteArray _sharedSecret;

    public SharedSecretUrlParameterFormatter(SensitiveByteArray sharedSecret)
    {
        _sharedSecret = sharedSecret;
    }

    public string Format(object? value, ICustomAttributeProvider attributeProvider, Type type)
    {
        var name = ((ParameterInfo)attributeProvider).Name;
        string qs = $"{name}={value}";

        var iv = ByteArrayUtil.GetRndByteArray(16);
        var key = _sharedSecret; //#wierd
        var encryptedBytes = AesCbc.Encrypt(qs.ToUtf8ByteArray(), key, iv);

        var payload = new SharedSecretEncryptedPayload()
        {
            Iv = iv,
            Data = encryptedBytes.ToBase64()
        };

        return OdinSystemSerializer.Serialize(payload);
    }
}