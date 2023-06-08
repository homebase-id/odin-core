using System;
using System.Net;

namespace Odin.Core.Exceptions;

public abstract class OdinApiException : OdinException
{
    public HttpStatusCode HttpStatusCode { get; set; }

    public OdinApiException(
        HttpStatusCode httpStatusCode,
        string message
    ) : base(message)
    {
        HttpStatusCode = httpStatusCode;
    }

    public OdinApiException(
        HttpStatusCode httpStatusCode,
        string message, 
        Exception inner
    ) : base(message, inner)
    {
        HttpStatusCode = httpStatusCode;
    }
}
