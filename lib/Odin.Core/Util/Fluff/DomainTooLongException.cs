using System;
using System.Runtime.Serialization;

namespace Odin.Core.Util.Fluff
{
    [Serializable]
    public class DomainTooLongException : Exception
    {
        public DomainTooLongException()
        {
        }

        public DomainTooLongException(string message) : base(message)
        {
        }

        public DomainTooLongException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainTooLongException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}