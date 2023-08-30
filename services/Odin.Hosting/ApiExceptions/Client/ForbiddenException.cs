using System;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions.Client;

public class ForbiddenException : ClientException
{
    public const string DefaultErrorMessage = "Forbidden";

    public ForbiddenException(
        string message = DefaultErrorMessage,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        Exception inner = null,
        HttpStatusCode httpStatusCode = HttpStatusCode.Forbidden) : base(
            message,
            httpStatusCode,
            odinClientErrorCode,
            inner
        )
    {
    }
}

