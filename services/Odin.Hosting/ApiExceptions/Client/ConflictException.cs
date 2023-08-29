using System;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions.Client;

public class ConflictException : ClientException
{
    public const string DefaultErrorMessage = "Conflict";

    public ConflictException(
        string message = DefaultErrorMessage,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        Exception inner = null,
        HttpStatusCode httpStatusCode = HttpStatusCode.Conflict) : base(
            message,
            httpStatusCode,
            odinClientErrorCode,
            inner
        )
    {
    }
}
