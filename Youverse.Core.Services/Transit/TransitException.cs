using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Services.Transit
{
    public class TransitException : Exception
    {
        public TransitException()
        {
        }

        public TransitException(string message) : base(message)
        {
        }

        public TransitException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected TransitException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}