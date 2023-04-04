using Youverse.Core.Exceptions;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Authorization.Apps;

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
            DomainNameValidator.AssertValidDomain(parts[0]);
            if (!short.TryParse(parts[1], out var _))
            {
                throw new YouverseClientException("Invalid host name for CORS; port must be a number", YouverseClientErrorCode.InvalidCorsHostName);
            }
        }

        if (parts.Length == 1)
        {
            DomainNameValidator.AssertValidDomain(corsHostName);
        }        }
}