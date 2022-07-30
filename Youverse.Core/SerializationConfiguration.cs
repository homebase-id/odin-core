#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Youverse.Core.Serialization;

namespace Youverse.Core;

public static class SerializationConfiguration
{
    public static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new ByteArrayConverter() }
    };

}