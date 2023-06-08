using System;
using System.Runtime.Serialization;

namespace Odin.Core.Exceptions;

public class DriveSecurityException : YouverseSecurityException
{
    public DriveSecurityException()
    {
    }

    public DriveSecurityException(string message) : base(message)
    {
    }

    public DriveSecurityException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected DriveSecurityException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}