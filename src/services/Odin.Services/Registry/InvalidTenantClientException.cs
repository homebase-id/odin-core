using System;
using Odin.Core.Exceptions;

namespace Odin.Services.Registry
{
    public class InvalidTenantClientException : OdinClientException
    {
        public InvalidTenantClientException(string message) : base(message)
        {
        }

        public InvalidTenantClientException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}