using Odin.Core.Exceptions;
using Odin.Core.Util;

namespace Odin.Core.Services.Authorization.Apps;

public static class AppUtil
{
    public static void AssertValidCorsHeader(string corsHostName)
    {
        if (!IsValidCorsHeader(corsHostName))
        {
            throw new OdinClientException("Invalid host name for CORS; can only be [host name]:[port number]", OdinClientErrorCode.InvalidCorsHostName);
        }
    }

    public static bool IsValidCorsHeader(string corsHostName)
    {
        var parts = corsHostName.Split(':');
        
        if (parts.Length == 1)
        {
            return AsciiDomainNameValidator.TryValidateDomain(corsHostName);
        }

        if (parts.Length == 2)
        {
            if (!AsciiDomainNameValidator.TryValidateDomain(parts[0]))
            {
                return false;
            }

            if (!int.TryParse(parts[1], out var port) || port > 65535)
            {
                return false;
            }

            return true;
        }
        
        return false;
    }
}