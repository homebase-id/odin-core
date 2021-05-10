using System;
using System.Runtime.Serialization;

namespace DotYou.Kernel.Services.Authentication
{
    internal class InvalidAuthTokenException : Exception
    {
        public InvalidAuthTokenException()
        {
        }

        public InvalidAuthTokenException(string message) : base(message)
        {
        }

        public InvalidAuthTokenException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected InvalidAuthTokenException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}