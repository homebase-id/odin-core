using System;

namespace Youverse.Core.Exceptions
{
    public class YouverseClientException : Exception
    {
        public YouverseClientErrorCode ErrorCode { get; set; }

        public YouverseClientException(string message, YouverseClientErrorCode code = YouverseClientErrorCode.Todo) : base(message)
        {
            this.ErrorCode = code;
        }

        public YouverseClientException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}