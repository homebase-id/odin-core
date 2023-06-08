using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odin.Core;

public class GuidIdConverter : JsonConverter<GuidId>
{   
    public override GuidId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        
        if (Guid.TryParse(value, out Guid g))
        {
            return new GuidId(g.ToByteArray());
        }

        return new GuidId(value);
    }

    public override void Write(Utf8JsonWriter writer, GuidId id, JsonSerializerOptions options)
    {
        string jsonValue = id?.ToString() ?? "";
        writer.WriteStringValue(jsonValue);
    }
}