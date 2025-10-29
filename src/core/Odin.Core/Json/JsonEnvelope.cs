using System;
using System.Text.Json.Serialization;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;

namespace Odin.Core.Json;

#nullable enable

/// <summary>
/// Wraps a serialized JSON message with its type information, enabling type-safe
/// deserialization without prior knowledge of the concrete type.
/// </summary>
public class JsonEnvelope
{
    public required string MessageTypeName { get; init; }
    public required string? MessageJson { get; init; }

    //

    [JsonIgnore]
    private Type? _cachedMessageType;
    public Type GetMessageType()
    {
        return _cachedMessageType ??=
            Type.GetType(MessageTypeName) ??
            throw new OdinSystemException(
                $"Failed to resolve type '{MessageTypeName}'. " +
                "Ensure the type exists and its assembly is loaded.");
    }

    //

    public object? DeserializeMessage()
    {
        return MessageJson == null ? null : OdinSystemSerializer.DeserializeOrThrow(MessageJson, GetMessageType());
    }

    //

    public T? DeserializeMessage<T>()
    {
        if (MessageJson == null)
        {
            return default;
        }

        var instance = DeserializeMessage();
        if (instance is not T typedInstance)
        {
            throw new OdinSystemException(
                $"Expected type '{typeof(T).FullName}' but envelope contains '{MessageTypeName}'.");
        }

        return typedInstance;
    }

    //

    public static JsonEnvelope Create<T>(T message)
    {
        var messageType = typeof(T).AssemblyQualifiedName;
        if (messageType == null)
        {
            throw new OdinSystemException("Failed to get AssemblyQualifiedName for type");
        }

        var messageJson = message == null ? null : OdinSystemSerializer.Serialize(message);
        return new JsonEnvelope
        {
            MessageTypeName = messageType,
            MessageJson = messageJson
        };
    }
}
