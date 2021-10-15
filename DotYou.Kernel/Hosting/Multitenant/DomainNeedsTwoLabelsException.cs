using System;
using System.Runtime.Serialization;

namespace DotYou.IdentityRegistry
{
    [Serializable]
    internal class DomainNeedsTwoLabelsException : Exception
    {
        public DomainNeedsTwoLabelsException()
        {
        }

        public DomainNeedsTwoLabelsException(string message) : base(message)
        {
        }

        public DomainNeedsTwoLabelsException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainNeedsTwoLabelsException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}