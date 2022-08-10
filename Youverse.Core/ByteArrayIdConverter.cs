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

    public override void Write(Utf8JsonWriter writer, ByteArrayId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}