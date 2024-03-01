using System;

namespace Odin.Core.Exceptions;

/// <summary>
/// Indicates when a file header points to a payload or thumbnail that should exist on disk but is not found.
/// </summary>
public class OdinFileHeaderHasCorruptPayloadException : OdinSystemException
{
    public OdinFileHeaderHasCorruptPayloadException(string message) : base(message)
    {
    }

    public OdinFileHeaderHasCorruptPayloadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}