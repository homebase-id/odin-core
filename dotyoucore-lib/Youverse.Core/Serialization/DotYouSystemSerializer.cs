#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Youverse.Core.Serialization;

/// <summary>
/// Centralizes serialization functions to escape the conversion between NewtonSoft and Microsoft's serialization  >:[
/// </summary>
public static class DotYouSystemSerializer
{
    public static readonly JsonSerializerOptions? JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new ByteArrayConverter(), new NullableGuidConverter(), new GuidConverter() }
    };

    public static async Task Serialize(Stream utf8Json,
        object? value,
        Type inputType,
        CancellationToken cancellationToken = default)
    {
        await JsonSerializer.SerializeAsync(utf8Json, value, inputType, JsonSerializerOptions, cancellationToken);
    }

    public static string Serialize<TValue>(TValue value, Type type)
    {
        var json = JsonSerializer.Serialize(value, type, JsonSerializerOptions);
        return json;
    }
    public static string Serialize<TValue>(TValue value)
    {
        var json = JsonSerializer.Serialize(value, JsonSerializerOptions);
        return json;
    }

    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions);
    }

    public static async Task<T?> Deserialize<T>(Stream utf8Json, CancellationToken cancellationToken = default)
    {
        return await JsonSerializer.DeserializeAsync<T>(utf8Json, JsonSerializerOptions, cancellationToken);
    }
}