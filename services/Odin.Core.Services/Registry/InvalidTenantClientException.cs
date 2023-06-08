using System;
using Odin.Core.Exceptions;

namespace Odin.Core.Services.Registry
{
    public class InvalidTenantClientException : YouverseClientException
    {
        public InvalidTenantClientException(string message) : base(message)
        {
        }

        public InvalidTenantClientException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}