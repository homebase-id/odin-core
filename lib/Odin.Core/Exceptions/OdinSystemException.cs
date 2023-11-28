using System;
using System.Runtime.Serialization;

namespace Odin.Core.Exceptions;

public class OdinSystemException : OdinException
{
    public OdinSystemException(string message) : base(message)
    {
    }

    public OdinSystemException(string message, Exception innerException) : base(message, innerException)
    {
    }
}