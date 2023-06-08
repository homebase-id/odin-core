using Microsoft.AspNetCore.Authorization;
using Odin.Core.Services.Authorization;

namespace Odin.Hosting.Authentication.Perimeter
{
    public static class CertificatePerimeterPolicies
    {
        public const string IsInYouverseNetwork = "IsInYouverseNetwork";

        public static void AddPolicies(AuthorizationOptions policy, string scheme)
        {
            policy.AddPolicy(IsInYouverseNetwork, pb =>
            {
                pb.RequireClaim(OdinClaimTypes.IsAuthenticated, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(scheme);
            });
            
        }
    }
}