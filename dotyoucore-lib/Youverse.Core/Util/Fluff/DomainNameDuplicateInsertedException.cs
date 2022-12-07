using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Util
{
    [Serializable]
    public class DomainNameDuplicateInsertedException : Exception
    {
        public DomainNameDuplicateInsertedException()
        {
        }

        public DomainNameDuplicateInsertedException(string message) : base(message)
        {
        }

        public DomainNameDuplicateInsertedException(string message, Exception innerException) : base(message,
            innerException)
        {
        }

        protected DomainNameDuplicateInsertedException(SerializationInfo info, StreamingContext context) : base(info,
            context)
        {
        }
    }
}