using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Youverse.Core.Serialization;

public class GuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new Guid(value?? throw new Exception("Invalid Guid Value"));
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}