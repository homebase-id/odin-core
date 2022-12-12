using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Util
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