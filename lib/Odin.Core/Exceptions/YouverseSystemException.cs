using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Exceptions;

public class YouverseSystemException : YouverseException
{
        
    public YouverseSystemException()
    {
    }

    public YouverseSystemException(string message) : base(message)
    {
    }

    public YouverseSystemException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected YouverseSystemException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }

}