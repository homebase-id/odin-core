using System;
using System.Runtime.Serialization;
using Odin.Core.Exceptions;

namespace Odin.Core.Services.Admin;

public class AdminException : OdinSystemException
{
    public AdminException(string message) : base(message)
    {
    }

    public AdminException(string message, Exception innerException) : base(message, innerException)
    {
    }

    protected AdminException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}