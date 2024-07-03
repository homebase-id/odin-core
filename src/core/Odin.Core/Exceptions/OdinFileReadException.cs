using System;

namespace Odin.Core.Exceptions;

public class OdinFileReadException : OdinException
{
    public OdinFileReadException(string message) : base(message)
    {
    }

    public OdinFileReadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}