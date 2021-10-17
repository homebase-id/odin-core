using System;
using System.Runtime.Serialization;

namespace DotYou.IdentityRegistry
{
    [Serializable]
    public class DomainTooShortException : Exception
    {
        public DomainTooShortException()
        {
        }

        public DomainTooShortException(string message) : base(message)
        {
        }

        public DomainTooShortException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainTooShortException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}