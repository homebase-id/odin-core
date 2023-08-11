using System;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions.Client;

public class UnauthorizedException : ClientException
{
    public const string DefaultErrorMessage = "Unauthorized";

    public UnauthorizedException(
        string message = DefaultErrorMessage,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        Exception inner = null,
        HttpStatusCode httpStatusCode = HttpStatusCode.Unauthorized) : base(
            message,
            httpStatusCode,
            odinClientErrorCode,
            inner
        )
    {
    }
}

