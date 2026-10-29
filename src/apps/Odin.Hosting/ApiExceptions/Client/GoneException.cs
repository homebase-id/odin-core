using System;
using System.Net;
using Odin.Core.Exceptions;

namespace Odin.Hosting.ApiExceptions.Client;

public class GoneException : ClientException
{
    public const string DefaultErrorMessage = "Gone";

    public GoneException(
        string message = DefaultErrorMessage,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        Exception inner = null) : base(
            message,
            HttpStatusCode.Gone,
            odinClientErrorCode,
            inner
        )
    {
    }
}
