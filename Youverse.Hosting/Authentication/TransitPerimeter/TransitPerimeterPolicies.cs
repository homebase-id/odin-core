using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Services.Authorization;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Authentication.TransitPerimeter
{
    public static class TransitPerimeterPolicies
    {

        public const string MustBeIdentifiedPolicyName = "MustBeIdentified";

        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(MustBeIdentifiedPolicyName, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(TransitPerimeterAuthConstants.TransitAuthScheme);
            });
        }
    }
}