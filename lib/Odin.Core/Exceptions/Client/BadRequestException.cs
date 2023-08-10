using System;
using System.Net;

namespace Odin.Core.Exceptions.Client;

public class BadRequestException : ClientException
{
    public const string DefaultErrorMessage = "Bad Request";

    public BadRequestException(
        string message = DefaultErrorMessage,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        HttpStatusCode httpStatusCode = HttpStatusCode.BadRequest,
        Exception inner = null
    ) : base(
        message,
        httpStatusCode,
        odinClientErrorCode,
        inner
    )
    {
    }
}
