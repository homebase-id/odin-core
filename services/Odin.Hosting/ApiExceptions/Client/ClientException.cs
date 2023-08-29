using System;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions.Client;

public abstract class ClientException : ApiException
{
    public OdinClientErrorCode OdinClientErrorCode { get; set; }

    public ClientException(
        string message,
        HttpStatusCode httpStatusCode,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        Exception inner = null) : base(
            httpStatusCode,
            message,
            inner
    )
    {
        OdinClientErrorCode = odinClientErrorCode;
    }
}
