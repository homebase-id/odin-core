using System;
using Odin.Core.Exceptions;

namespace Odin.Core.Util.Fluff
{
    public class DomainNameDuplicateInsertedException : OdinSystemException
    {
        public DomainNameDuplicateInsertedException(string message) : base(message)
        {
        }

        public DomainNameDuplicateInsertedException(string message, Exception innerException) : base(message,
            innerException)
        {
        }
    }
}