using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odin.Core.Util;

public class AsciiDomainConverter : JsonConverter<AsciiDomainName>
{
    public override AsciiDomainName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new AsciiDomainName(value);
    }

    public override void Write(Utf8JsonWriter writer, AsciiDomainName value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.DomainName);
    }

}