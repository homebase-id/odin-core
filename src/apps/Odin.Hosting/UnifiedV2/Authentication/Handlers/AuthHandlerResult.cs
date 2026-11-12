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

    /// <summary>
    /// Refuse the request (401)
    /// </summary>
    public static AuthHandlerResult Fail()
    {
        return new AuthHandlerResult
        {
            Status = AuthHandlerStatus.Fail,
            Claims = null
        };
    }

    /// <summary>
    /// Serve the request as an anonymous caller, as if it brought no token
    /// </summary>
    public static AuthHandlerResult Anonymous()
    {
        return new AuthHandlerResult
        {
            Status = AuthHandlerStatus.AnonymousFallback,
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