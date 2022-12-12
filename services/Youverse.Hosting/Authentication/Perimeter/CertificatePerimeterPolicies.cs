using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Authentication.Perimeter
{
    public static class CertificatePerimeterPolicies
    {
        public const string IsInYouverseNetwork = "IsInYouverseNetwork";

        public static void AddPolicies(AuthorizationOptions policy, string scheme)
        {
            policy.AddPolicy(IsInYouverseNetwork, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsAuthenticated, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(scheme);
            });
            
        }
    }
}