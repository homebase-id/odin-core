﻿using System;
using System.Net;

namespace Odin.Core.Exceptions.Client;

public class NotFoundException : ClientException
{
    public const string DefaultErrorMessage = "Not Found";

    public NotFoundException(
        string message = DefaultErrorMessage,
        OdinClientErrorCode odinClientErrorCode = OdinClientErrorCode.NoErrorCode,
        HttpStatusCode httpStatusCode = HttpStatusCode.NotFound,
        Exception inner = null
        ) : base(
            message,
            httpStatusCode,
            odinClientErrorCode,
            inner
        )
    {
    }
}
