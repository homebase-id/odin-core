using System;
using System.Runtime.Serialization;
using System.Text.Json;
using Odin.Core.Exceptions;

namespace Odin.Core.Services.Authentication.YouAuth;

#nullable enable

public sealed class YouAuthClientInfo
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    // ...
    // ...
    // ...
    
    public bool IsValid()
    {
        return
            !string.IsNullOrWhiteSpace(Name) &&
            !string.IsNullOrWhiteSpace(Description);
    }
    
    //

    public static YouAuthClientInfo FromJson(string json)
    {
        var result = JsonSerializer.Deserialize<YouAuthClientInfo>(json, _options);
        if (result == null)
        {
            throw new YouAuthClientInfoExcption($"Error deserializing {json}");
        }
        return result;
    }
    
    //

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, _options);
    }
    
    //
    
    private static JsonSerializerOptions _options = new()
    {
        WriteIndented = false,
    };
}

//

public class YouAuthClientInfoExcption : OdinException
{
    public YouAuthClientInfoExcption(string message) : base(message)
    {
    }

    public YouAuthClientInfoExcption(string message, Exception inner) : base(message, inner)
    {
    }

    public YouAuthClientInfoExcption(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

//