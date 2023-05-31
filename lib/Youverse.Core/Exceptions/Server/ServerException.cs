using System;
using System.Net;

namespace Youverse.Core.Exceptions.Server;

public abstract class ServerException : OdinApiException
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

