using System;
using System.Net;

namespace Odin.Core.Exceptions.Client;

public abstract class ClientException : OdinApiException
{
    public ClientException(
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
