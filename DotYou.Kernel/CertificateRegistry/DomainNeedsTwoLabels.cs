using System;
using System.Runtime.Serialization;

namespace DotYou.IdentityRegistry
{
    [Serializable]
    internal class DomainNeedsTwoLabels : Exception
    {
        public DomainNeedsTwoLabels()
        {
        }

        public DomainNeedsTwoLabels(string message) : base(message)
        {
        }

        public DomainNeedsTwoLabels(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainNeedsTwoLabels(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}