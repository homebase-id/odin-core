using System;
using System.Runtime.Serialization;

namespace Odin.Core.Exceptions;

public class DriveSecurityException : OdinSecurityException
{
    public DriveSecurityException(string message) : base(message)
    {
    }

    public DriveSecurityException(string message, Exception innerException) : base(message, innerException)
    {
    }
}