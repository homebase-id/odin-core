using System;
using System.Net;

namespace Youverse.Core.Exceptions.Client
{
    public abstract class ClientException : ApiException
    {
        protected ClientException(
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
}
