using System;
using System.Runtime.Serialization;

namespace DotYou.IdentityRegistry
{
    [Serializable]
    public class DomainTooLong : Exception
    {
        public DomainTooLong()
        {
        }

        public DomainTooLong(string message) : base(message)
        {
        }

        public DomainTooLong(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainTooLong(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}