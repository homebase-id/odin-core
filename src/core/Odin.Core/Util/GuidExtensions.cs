using System;
using Odin.Core.Exceptions;

namespace Odin.Core.Util;

#nullable enable

public static class GuidExtensions
{
    public static void AssertGuidNotEmpty(this Guid guid, string message = "Guid is not allowed to be empty")
    {
        if (guid == Guid.Empty)
        {
            throw new OdinSystemException(message);
        }
    }
    
    public static void AssertGuidNotEmpty(this Guid? guid, string message = "Guid is not allowed to be empty")
    {
        if (guid == Guid.Empty)
        {
            throw new OdinSystemException(message);
        }
    }
}
