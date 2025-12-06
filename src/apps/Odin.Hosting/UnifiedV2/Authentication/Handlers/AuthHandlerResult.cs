#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public sealed class AuthHandlerResult
{
    public AuthHandlerStatus Status { get; init; }

    /// <summary>
    /// Only populated when Status == Success.
    /// </summary>
    public List<Claim>? Claims { get; init; }

    public static AuthHandlerResult Fail()
    {
        return new AuthHandlerResult
        {
            Status = AuthHandlerStatus.Fail,
            Claims = null
        };
    }

    public static AuthHandlerResult Fallback()
    {
        return new AuthHandlerResult
        {
            Status = AuthHandlerStatus.Fail,
            Claims = null
        };
    }

    public static AuthHandlerResult Success(IEnumerable<Claim>? claims = null)
    {
        var actualClaims = claims ?? new List<Claim>();
        return new AuthHandlerResult
        {
            Status = AuthHandlerStatus.Success,
            Claims = actualClaims.ToList()
        };
    }
}