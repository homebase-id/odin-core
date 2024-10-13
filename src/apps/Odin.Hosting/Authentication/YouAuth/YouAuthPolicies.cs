using Microsoft.AspNetCore.Authorization;
using Odin.Services.Authorization;

namespace Odin.Hosting.Authentication.YouAuth;

public static class YouAuthPolicies
{
    public const string IsAuthorizedApp = "IsAuthorizedApp";

    public const string IsIdentified = "IsClientTokenIdentified";
        
    public static void AddPolicies(AuthorizationOptions policy)
    {
        policy.AddPolicy(IsIdentified, pb =>
        {
            pb.RequireClaim(OdinClaimTypes.IsAuthenticated, bool.TrueString);
            // pb.RequireRole()
            pb.AuthenticationSchemes.Add(YouAuthConstants.YouAuthScheme);

        });
            
        policy.AddPolicy(IsAuthorizedApp, pb =>
        {
            pb.RequireClaim(OdinClaimTypes.IsAuthenticated, bool.TrueString);
            pb.RequireClaim(OdinClaimTypes.IsAuthorizedApp, bool.TrueString);
            pb.AuthenticationSchemes.Add(YouAuthConstants.YouAuthScheme);
        });
    }
}