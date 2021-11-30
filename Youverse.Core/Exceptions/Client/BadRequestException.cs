using System;
using System.Net;

namespace Youverse.Core.Exceptions.Client
{
    public class BadRequestException : ClientException
    {
        private const string DefaultErrorMessage = "Bad Request";

        public BadRequestException(
            string message = DefaultErrorMessage,
            HttpStatusCode httpStatusCode = HttpStatusCode.BadRequest,
            Exception inner = null
            ) : base(
                message,
                httpStatusCode,
                inner
            )
        {
        }
    }
}
