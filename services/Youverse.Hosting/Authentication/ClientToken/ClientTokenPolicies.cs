using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Authentication.ClientToken
{
    public static class ClientTokenPolicies
    {
        public const string IsAuthorizedApp = "IsAuthorizedApp";

        public const string IsIdentified = "IsClientTokenIdentified";
        
        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(IsIdentified, pb =>
            {
                pb.RequireClaim(OdinClaimTypes.IsAuthenticated, true.ToString().ToLower());
                // pb.RequireRole()
                pb.AuthenticationSchemes.Add(ClientTokenConstants.YouAuthScheme);

            });
            
            policy.AddPolicy(IsAuthorizedApp, pb =>
            {
                pb.RequireClaim(OdinClaimTypes.IsAuthenticated, true.ToString().ToLower());
                pb.RequireClaim(OdinClaimTypes.IsAuthorizedApp, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(ClientTokenConstants.YouAuthScheme);
            });
        }
    }
}