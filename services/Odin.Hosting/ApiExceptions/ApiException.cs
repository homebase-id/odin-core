using System;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions;

public abstract class ApiException : OdinException
{
    public HttpStatusCode HttpStatusCode { get; set; }

    public ApiException(
        HttpStatusCode httpStatusCode,
        string message
    ) : base(message)
    {
        HttpStatusCode = httpStatusCode;
    }

    public ApiException(
        HttpStatusCode httpStatusCode,
        string message, 
        Exception inner
    ) : base(message, inner)
    {
        HttpStatusCode = httpStatusCode;
    }
}
