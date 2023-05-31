using System;
using System.Net;

namespace Youverse.Core.Exceptions.Client;

public class ForbiddenException : ClientException
{
    public const string DefaultErrorMessage = "Forbidden";

    public ForbiddenException(
        string message = DefaultErrorMessage,
        HttpStatusCode httpStatusCode = HttpStatusCode.Forbidden,
        Exception inner = null
        ) : base(
            message,
            httpStatusCode,
            inner
        )
    {
    }
}

