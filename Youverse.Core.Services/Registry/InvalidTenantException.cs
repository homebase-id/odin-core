using System;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Registry
{
    public class InvalidTenantException : YouverseException
    {
        public InvalidTenantException(string message) : base(message)
        {
        }

        public InvalidTenantException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}