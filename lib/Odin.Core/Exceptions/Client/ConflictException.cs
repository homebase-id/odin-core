using System;
using System.Net;

namespace Odin.Core.Exceptions.Client;

public class ConflictException : ClientException
{
    public const string DefaultErrorMessage = "Conflict";

    public ConflictException(
        string message = DefaultErrorMessage,
        HttpStatusCode httpStatusCode = HttpStatusCode.Conflict,
        Exception inner = null
        ) : base(
            message,
            httpStatusCode,
            inner
        )
    {
    }
}
