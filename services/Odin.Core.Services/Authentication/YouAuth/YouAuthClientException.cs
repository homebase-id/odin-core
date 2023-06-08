#nullable enable
using System;
using Odin.Core.Exceptions;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public class YouAuthClientException : YouverseClientException
    {
        public YouAuthClientException(string message) : base(message)
        {
        }

        public YouAuthClientException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}