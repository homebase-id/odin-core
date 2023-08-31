using System;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions.Server;

public abstract class ServerException : ApiException
{
    public ServerException(
        string message,
        HttpStatusCode httpStatusCode,
        Exception inner = null
    ) : base(
        httpStatusCode,
        message,
        inner
    )
    {
    }
}

