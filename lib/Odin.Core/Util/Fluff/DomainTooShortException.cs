using System;
using System.Runtime.Serialization;
using Odin.Core.Exceptions;

namespace Odin.Core.Util.Fluff
{
    public class DomainTooShortException : OdinSystemException
    {
        public DomainTooShortException(string message) : base(message)
        {
        }

        public DomainTooShortException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}