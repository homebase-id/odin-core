using System;
using Odin.Core.Exceptions;

namespace Odin.Core.Util.Fluff
{
    public class DomainTooLongException : OdinSystemException
    {
        public DomainTooLongException(string message) : base(message)
        {
        }

        public DomainTooLongException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}