using System;
using System.Net;

namespace Youverse.Core.Exceptions.Client
{
    public class NotFoundException : ClientException
    {
        private const string DefaultErrorMessage = "Not Found";

        public NotFoundException(
            string message = DefaultErrorMessage,
            HttpStatusCode httpStatusCode = HttpStatusCode.NotFound,
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
