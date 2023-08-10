using System;
using System.Net;

namespace Odin.Core.Exceptions.Client;

public abstract class ClientException : OdinApiException
{
    public OdinClientErrorCode OdinClientErrorCode { get; set; }

    public ClientException(
        string message,
        HttpStatusCode httpStatusCode,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        Exception inner = null
    ) : base(
        httpStatusCode,
        message,
        inner
    )
    {
        OdinClientErrorCode = odinClientErrorCode;
    }
}
