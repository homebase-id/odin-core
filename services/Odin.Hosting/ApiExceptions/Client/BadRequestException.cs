using System;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions.Client;

public class BadRequestException : ClientException
{
    public const string DefaultErrorMessage = "Bad Request";

    public BadRequestException(
        string message = DefaultErrorMessage,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        Exception inner = null,
        HttpStatusCode httpStatusCode = HttpStatusCode.BadRequest) : base(
            message,
            httpStatusCode,
            odinClientErrorCode,
            inner)
    {
    }
}
