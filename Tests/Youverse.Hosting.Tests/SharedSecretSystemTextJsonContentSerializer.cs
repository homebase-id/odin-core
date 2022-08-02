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
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Serialization;

namespace Youverse.Hosting.Tests;

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
        this.jsonSerializerOptions = new JsonSerializerOptions(SerializationConfiguration.JsonSerializerOptions);
        jsonSerializerOptions.Converters.Add(new ObjectToInferredTypesConverter());
    }

    /// <inheritdoc/>
    public HttpContent ToHttpContent<T>(T item)
    {
        var content = JsonContent.Create(item, options: jsonSerializerOptions);
        var contentBytes = content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        
        var iv = ByteArrayUtil.GetRndByteArray(16);
        var key = _sharedSecret; //#wierd
        var encryptedBytes = AesCbc.Encrypt(contentBytes, ref key, iv);

        var payload = new SharedSecretEncryptedPayload()
        {
            Iv = iv,
            Data = encryptedBytes.ToBase64()
        };

        return JsonContent.Create(payload, payload.GetType(), MediaTypeHeaderValue.Parse("application/json"), SerializationConfiguration.JsonSerializerOptions);
    }

    /// <inheritdoc/>
    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, CancellationToken cancellationToken = default)
    {
        var item = await content.ReadFromJsonAsync<T>(jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return item;
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