using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Youverse.Core;

public class ByteArrayIdConverter : JsonConverter<ByteArrayId>
{
    public override ByteArrayId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return new ByteArrayId(value);
    }

    public override void Write(Utf8JsonWriter writer, ByteArrayId id, JsonSerializerOptions options)
    {
        string jsonValue = id?.ToString() ?? "";
        writer.WriteStringValue(jsonValue);
    }
}