using System;
using System.Runtime.Serialization;

namespace Youverse.Hosting.Security.Authentication
{
    [Serializable]
    internal class MissingProfileException : Exception
    {
        public MissingProfileException()
        {
        }

        public MissingProfileException(string message) : base(message)
        {
        }

        public MissingProfileException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MissingProfileException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}