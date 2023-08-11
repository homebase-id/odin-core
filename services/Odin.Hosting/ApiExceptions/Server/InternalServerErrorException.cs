using System;
using System.Net;

namespace Odin.Hosting.ApiExceptions.Server;

public class InternalServerErrorException : ServerException
{
    public const string DefaultErrorMessage = "Internal Server Error";

    public InternalServerErrorException(
        string message = DefaultErrorMessage,
        Exception inner = null,
        HttpStatusCode httpStatusCode = HttpStatusCode.InternalServerError) : base (
        message,
        httpStatusCode,
        inner
    )
    {
    }
    
}