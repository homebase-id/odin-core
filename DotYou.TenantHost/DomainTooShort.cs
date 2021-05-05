using System;
using System.Runtime.Serialization;

namespace DotYou.TenantHost
{
    [Serializable]
    public class DomainTooShort : Exception
    {
        public DomainTooShort()
        {
        }

        public DomainTooShort(string message) : base(message)
        {
        }

        public DomainTooShort(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainTooShort(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}