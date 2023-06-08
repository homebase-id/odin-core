using Odin.Core.Exceptions;
using Odin.Core.Util;

namespace Odin.Core.Services.Authorization.Apps;

public static class AppUtil
{
    public static void AssertValidCorsHeader(string corsHostName)
    {
        var parts = corsHostName.Split(':');

        if (parts.Length > 2)
        {
            throw new YouverseClientException("Invalid host name for CORS; can only be [host name]:[port number]", YouverseClientErrorCode.InvalidCorsHostName);
        }

        if (parts.Length == 2)
        {
            PunyDomainNameValidator.AssertValidDomain(parts[0]);
            if (!int.TryParse(parts[1], out var port) || port > 65535)
            {
                throw new YouverseClientException("Invalid host name for CORS; port must be a number", YouverseClientErrorCode.InvalidCorsHostName);
            }
        }

        if (parts.Length == 1)
        {
            PunyDomainNameValidator.AssertValidDomain(corsHostName);
        }
    }
}