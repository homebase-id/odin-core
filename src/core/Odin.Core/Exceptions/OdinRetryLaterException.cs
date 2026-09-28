using System;
using System.Net;

namespace Odin.Core.Exceptions
{
    /// <summary>
    /// The request was refused for a reason the caller can wait out (e.g. the identity is over its
    /// storage quota). Answered with <see cref="StatusCode"/> and a Retry-After header, so peers
    /// defer the work instead of dropping it.
    /// </summary>
    public class OdinRetryLaterException : OdinException
    {
        public HttpStatusCode StatusCode { get; }

        public TimeSpan RetryAfter { get; }

        public OdinRetryLaterException(string message, HttpStatusCode statusCode, TimeSpan retryAfter) : base(message)
        {
            StatusCode = statusCode;
            RetryAfter = retryAfter;
        }
    }
}
