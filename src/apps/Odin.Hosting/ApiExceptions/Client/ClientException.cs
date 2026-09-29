using System;
using System.Collections.Generic;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions.Client;

public abstract class ClientException : ApiException
{
    public OdinClientErrorCode OdinClientErrorCode { get; set; }

    /// <summary>
    /// Carried over from <see cref="OdinClientException.Extensions"/>; written onto the problem details.
    /// </summary>
    public Dictionary<string, object> Extensions { get; init; }

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
