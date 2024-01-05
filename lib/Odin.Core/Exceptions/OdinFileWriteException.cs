using System;
using System.Runtime.Serialization;

namespace Odin.Core.Exceptions;

public class OdinFileWriteException : OdinException
{
    public OdinFileWriteException(string message) : base(message)
    {
    }

    public OdinFileWriteException(string message, Exception innerException) : base(message, innerException)
    {
    }
}