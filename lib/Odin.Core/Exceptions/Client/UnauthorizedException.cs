using System;
using System.Net;

namespace Odin.Core.Exceptions.Client;

public class UnauthorizedException : ClientException
{
    public const string DefaultErrorMessage = "Unauthorized";

    public UnauthorizedException(
        string message = DefaultErrorMessage,
        HttpStatusCode httpStatusCode = HttpStatusCode.Unauthorized,
        Exception inner = null
        ) : base(
            message,
            httpStatusCode,
            inner
        )
    {
    }
}

