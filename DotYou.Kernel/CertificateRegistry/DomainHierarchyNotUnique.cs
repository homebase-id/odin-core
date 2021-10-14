using System;
using System.Runtime.Serialization;

namespace DotYou.IdentityRegistry
{
    [Serializable]
    public class DomainHierarchyNotUnique : Exception
    {
        public DomainHierarchyNotUnique()
        {
        }

        public DomainHierarchyNotUnique(string message) : base(message)
        {
        }

        public DomainHierarchyNotUnique(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainHierarchyNotUnique(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}