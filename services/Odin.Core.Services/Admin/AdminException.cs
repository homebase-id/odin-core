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
}

//

// This class serves as a validation layer between service and controller.
// Data in here is meant to be sent to the client. E.g. in a Bad Request response.
public class AdminValidationException : AdminException
{
    public AdminValidationException(string message) : base(message)
    {
    }

    public AdminValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
