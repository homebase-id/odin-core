#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;

namespace Odin.Core.Serialization;

/// <summary>
/// Centralizes serialization functions to escape the conversion between NewtonSoft and Microsoft's serialization  >:[
/// </summary>
public static class OdinSystemSerializer
{
    public static readonly JsonSerializerOptions? JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new ByteArrayConverter(),
            new NullableGuidConverter(),
            new GuidConverter(),
            // new OdinContextConverter(),
            // new CallerContextConverter(),
            // new PermissionContextConverter(),
            // new PermissionGroupConverter()
        }
    };

    public static void Serialize<TValue>(Utf8JsonWriter writer, TValue value)
    {
        JsonSerializer.Serialize(writer, value, JsonSerializerOptions);
    }

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

    public static string Serialize<TValue>(TValue value, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(value, options);
        return json;
    }

    public static string Serialize(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonSerializerOptions);
        return json;
    }

    public static T? Deserialize<T>(byte[] jsonBytes)
    {
        return JsonSerializer.Deserialize<T>(jsonBytes, JsonSerializerOptions);
    }

    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions);
    }

    public static T DeserializeOrThrow<T>(string json)
    {
        var result = JsonSerializer.Deserialize<T>(json, JsonSerializerOptions);

        if (result == null)
        {
            throw new OdinSystemException("Failed to deserialize data");
        }

        return result;
    }

    public static T? Deserialize<T>(ref Utf8JsonReader reader)
    {
        return JsonSerializer.Deserialize<T>(ref reader, JsonSerializerOptions);
    }

    public static async Task<T?> Deserialize<T>(Stream utf8Json, CancellationToken cancellationToken = default)
    {
        return await JsonSerializer.DeserializeAsync<T>(utf8Json, JsonSerializerOptions, cancellationToken);
    }

    public static T SlowDeepCloneObject<T>(T source)
    {
        var json = Serialize(source);
        return DeserializeOrThrow<T>(json);
    }
}