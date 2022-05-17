using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Authentication.Perimeter
{
    public static class CertificatePerimeterPolicies
    {
        public const string IsInYouverseNetwork = "IsInYouverseNetwork";
        public const string IsInYouverseNetworkWithApp = "IsInYouverseNetworkWithApp";

        public static void AddPolicies(AuthorizationOptions policy, string scheme)
        {
            policy.AddPolicy(IsInYouverseNetwork, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(scheme);
            });
            
            policy.AddPolicy(IsInYouverseNetworkWithApp, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower());
                pb.RequireClaim(DotYouClaimTypes.IsAuthorizedApp, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(scheme);
            });
        }
    }
}