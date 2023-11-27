using System;
using Odin.Core.Exceptions;

namespace Odin.Core.Util.Fluff
{
    public class DomainHierarchyNotUniqueException : OdinSystemException
    {
        public DomainHierarchyNotUniqueException(string message) : base(message)
        {
        }

        public DomainHierarchyNotUniqueException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}