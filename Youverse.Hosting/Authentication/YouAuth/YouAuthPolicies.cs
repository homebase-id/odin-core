using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;
using Youverse.Hosting.Authentication.TransitPerimeter;

namespace Youverse.Hosting.Authentication.YouAuth
{
    public static class YouAuthPolicies
    {
        
        public const string IsIdentified = "MustOwnThisIdentity";

        
        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(IsIdentified, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(YouAuthConstants.Scheme);

            });
        }
    }
}