using System;
using Odin.Core.Exceptions;

namespace Odin.Core.Util.Fluff
{
    public class DomainIllegalCharacterException : OdinSystemException
    {
        public DomainIllegalCharacterException(string message) : base(message)
        {
        }

        public DomainIllegalCharacterException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}