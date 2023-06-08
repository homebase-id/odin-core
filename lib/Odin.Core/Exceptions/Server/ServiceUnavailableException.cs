using System;
using System.Net;

namespace Youverse.Core.Exceptions.Server;

public class ServiceUnavailableException : ServerException
{
    public const string DefaultErrorMessage = "Service Unavailable";

    public ServiceUnavailableException(
        string message = DefaultErrorMessage,
        HttpStatusCode httpStatusCode = HttpStatusCode.ServiceUnavailable,
        Exception inner = null
    ) : base (
        message,
        httpStatusCode,
        inner
    )
    {
    }
}