using System;

namespace Odin.Core.Exceptions;

public class OdinIdentityVerificationException : OdinException
{
    public OdinIdentityVerificationException(string message) : base(message)
    {
    }

    public OdinIdentityVerificationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}