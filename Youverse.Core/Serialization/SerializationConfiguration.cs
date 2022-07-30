#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Youverse.Core.Serialization;

public static class SerializationConfiguration
{
    public static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new ByteArrayConverter() }
    };

}