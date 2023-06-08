using System;
using System.Net;

namespace Odin.Core.Exceptions.Server;

public class InternalServerErrorException : ServerException
{
    public const string DefaultErrorMessage = "Internal Server Error";

    public InternalServerErrorException(
        string message = DefaultErrorMessage,
        HttpStatusCode httpStatusCode = HttpStatusCode.InternalServerError,
        Exception inner = null
    ) : base (
        message,
        httpStatusCode,
        inner
    )
    {
    }
    
}