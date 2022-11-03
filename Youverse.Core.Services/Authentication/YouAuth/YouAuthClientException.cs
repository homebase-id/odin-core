using System;
using Youverse.Core.Exceptions;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
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