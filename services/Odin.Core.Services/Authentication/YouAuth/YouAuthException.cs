using System;
using System.Runtime.Serialization;
using Odin.Core.Exceptions;

namespace Odin.Core.Services.Authentication.YouAuth;

public class YouAuthException : OdinSystemException
{
    public YouAuthException(string message) : base(message)
    {
    }

    public YouAuthException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected YouAuthException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

//

// This class serves as a validation layer between service and controller.
// Data in here is meant to be sent to the client. E.g. in a Bad Request response.
public class YouAuthClientException : YouAuthException
{
    public YouAuthClientException(string message) : base(message)
    {
    }

    public YouAuthClientException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected YouAuthClientException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
