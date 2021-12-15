using System;
using Youverse.Core.Exceptions;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public class YouAuthException : YouverseException
    {
        public YouAuthException(string message) : base(message)
        {
        }

        public YouAuthException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}