using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Services.Authorization;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Authentication.TransitPerimeter
{
    public static class TransitPerimeterPolicies
    {

        public const string IsInYouverseNetwork = "IsInYouverseNetwork";
        public const string IsInYouverseNetworkWithApp = "IsInYouverseNetworkWithApp";

        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(IsInYouverseNetwork, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(TransitPerimeterAuthConstants.TransitAuthScheme);
            });
            
            policy.AddPolicy(IsInYouverseNetworkWithApp, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower());
                pb.RequireClaim(DotYouClaimTypes.IsAuthorizedApp, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(TransitPerimeterAuthConstants.TransitAuthScheme);
            });
        }
    }
}