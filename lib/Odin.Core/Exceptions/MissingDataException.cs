using System;
using System.Runtime.Serialization;

namespace Odin.Core.Exceptions
{
    public class MissingDataException : OdinException
    {

        public MissingDataException(string message) : base(message)
        {
        }

        public MissingDataException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MissingDataException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}