using System;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions.Client;

public class RequestedRangeNotSatisfiableException : ClientException
{
    public const string DefaultErrorMessage = "Requested range not satisfiable";

    public RequestedRangeNotSatisfiableException(
        string message = DefaultErrorMessage,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        Exception inner = null,
        HttpStatusCode httpStatusCode = HttpStatusCode.RequestedRangeNotSatisfiable) : base(
        message,
        httpStatusCode,
        odinClientErrorCode,
        inner)
    {
    }
}
