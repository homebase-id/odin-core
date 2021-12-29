using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Services.Authentication
{
    internal class YouverseSecurityException : Exception
    {
        public YouverseSecurityException()
        {
        }

        public YouverseSecurityException(string message) : base(message)
        {
        }

        public YouverseSecurityException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected YouverseSecurityException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}