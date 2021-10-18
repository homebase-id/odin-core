using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Trie
{
    [Serializable]
    public class DomainHierarchyNotUniqueException : Exception
    {
        public DomainHierarchyNotUniqueException()
        {
        }

        public DomainHierarchyNotUniqueException(string message) : base(message)
        {
        }

        public DomainHierarchyNotUniqueException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainHierarchyNotUniqueException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}