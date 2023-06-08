using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Exceptions;

public class YouverseException : Exception
{
    public YouverseException()
    {
    }

    public YouverseException(string message) : base(message)
    {
    }

    public YouverseException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected YouverseException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}