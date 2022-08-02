using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Exceptions
{
    public class SharedSecretException : Exception
    {
        public SharedSecretException()
        {
        }

        public SharedSecretException(string message) : base(message)
        {
        }

        public SharedSecretException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected SharedSecretException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}