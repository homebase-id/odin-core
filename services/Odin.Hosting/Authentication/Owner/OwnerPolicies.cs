using System;
using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;
using Youverse.Hosting.Authentication.ClientToken;

namespace Youverse.Hosting.Authentication.Owner
{
    public static class OwnerPolicies
    {
        public const string IsDigitalIdentityOwner = "MustOwnThisIdentity";

        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(IsDigitalIdentityOwner, pb =>
            {
                pb.RequireClaim(OdinClaimTypes.IsIdentityOwner, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(OwnerAuthConstants.SchemeName);
            });
            
        }
        
    }
    
}