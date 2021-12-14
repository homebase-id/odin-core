using System;
using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Authentication.Owner
{
    public static class OwnerPolicies
    {
        public const string IsDigitalIdentityOwnerPolicyName = "MustOwnThisIdentity";
        public static readonly Action<AuthorizationPolicyBuilder> IsDigitalIdentityOwnerPolicy = pb =>
        {
            pb.RequireClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower());
            pb.AuthenticationSchemes.Add(OwnerAuthConstants.DotIdentityOwnerScheme);
        };

    }
}