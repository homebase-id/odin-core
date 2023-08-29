using System;
using System.Net;

namespace Odin.Hosting.ApiExceptions.Server;

public class ServiceUnavailableException : ServerException
{
    public const string DefaultErrorMessage = "Service Unavailable";

    public ServiceUnavailableException(
        string message = DefaultErrorMessage,
        Exception inner = null,
        HttpStatusCode httpStatusCode = HttpStatusCode.ServiceUnavailable) : base (
        message,
        httpStatusCode,
        inner
    )
    {
    }
}