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
            pb.RequireClaim(OdinClaimTypes.IsAuthenticated, true.ToString().ToLower());
            // pb.RequireRole()
            pb.AuthenticationSchemes.Add(YouAuthConstants.YouAuthScheme);

        });
            
        policy.AddPolicy(IsAuthorizedApp, pb =>
        {
            pb.RequireClaim(OdinClaimTypes.IsAuthenticated, true.ToString().ToLower());
            pb.RequireClaim(OdinClaimTypes.IsAuthorizedApp, true.ToString().ToLower());
            pb.AuthenticationSchemes.Add(YouAuthConstants.YouAuthScheme);
        });
    }
}