using Odin.Core.Exceptions;
using System;

namespace Odin.Core.Storage.Exceptions
{
    public class OdinDatabaseValidationException : OdinSystemException
    {
        public OdinDatabaseValidationException(string message) : base(message)
        {
        }

        public OdinDatabaseValidationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
