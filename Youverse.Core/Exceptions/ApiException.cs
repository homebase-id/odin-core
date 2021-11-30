using System;
using System.Net;

namespace Youverse.Core.Exceptions
{
    public abstract class ApiException : YouverseException
    {
        public HttpStatusCode HttpStatusCode { get; set; }

        protected ApiException(
            HttpStatusCode httpStatusCode,
            string message
            ) : base(message)
        {
            HttpStatusCode = httpStatusCode;
        }

        protected ApiException(
            HttpStatusCode httpStatusCode,
            string message, 
            Exception inner
            ) : base(message, inner)
        {
            HttpStatusCode = httpStatusCode;
        }
    }
}

