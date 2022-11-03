using System;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Registry
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