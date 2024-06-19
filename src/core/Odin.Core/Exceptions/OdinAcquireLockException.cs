using System;

namespace Odin.Core.Exceptions;

public class OdinAcquireLockException : OdinException
{
    public OdinAcquireLockException(string message) : base(message)
    {
    }

    public OdinAcquireLockException(string message, Exception innerException) : base(message, innerException)
    {
    }
}