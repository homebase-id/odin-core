using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Youverse.Core.Serialization;

public class NullableGuidConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return new Guid(value);
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (null != value)
        {
            writer.WriteStringValue(value.GetValueOrDefault().ToString());
        }
        else
        {
            writer.WriteStringValue("");
        }
    }
}