using System;
using System.Runtime.Serialization;

namespace DotYou.IdentityRegistry
{
    [Serializable]
    public class DuplicateDomainNameInsertedException : Exception
    {
        public DuplicateDomainNameInsertedException()
        {
        }

        public DuplicateDomainNameInsertedException(string message) : base(message)
        {
        }

        public DuplicateDomainNameInsertedException(string message, Exception innerException) : base(message,
            innerException)
        {
        }

        protected DuplicateDomainNameInsertedException(SerializationInfo info, StreamingContext context) : base(info,
            context)
        {
        }
    }
}