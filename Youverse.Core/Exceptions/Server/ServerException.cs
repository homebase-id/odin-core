using System;
using System.Net;

namespace Youverse.Core.Exceptions.Server
{
    public class ServerException : ApiException
    {
        private const string DefaultErrorMessage = "Internal Server Error";

        public ServerException(
            string message = DefaultErrorMessage,
            HttpStatusCode httpStatusCode = HttpStatusCode.InternalServerError,
            Exception inner = null
            ) : base (
                httpStatusCode,
                message,
                inner
            )
        {
        }
    }
}
