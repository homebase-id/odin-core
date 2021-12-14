using System;
using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Authentication.Owner
{
    public static class OwnerPolicies
    {
        public const string IsDigitalIdentityOwnerPolicyName = "MustOwnThisIdentity";

        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(IsDigitalIdentityOwnerPolicyName, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(OwnerAuthConstants.DotIdentityOwnerScheme);
            });
        }
    }
}