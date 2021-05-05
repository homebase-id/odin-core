using System;
using System.Runtime.Serialization;

namespace DotYou.TenantHost
{
    [Serializable]
    public class DomainIllegalCharacter : Exception
    {
        public DomainIllegalCharacter()
        {
        }

        public DomainIllegalCharacter(string message) : base(message)
        {
        }

        public DomainIllegalCharacter(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainIllegalCharacter(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}