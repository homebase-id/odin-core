using Microsoft.AspNetCore.Authorization;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization;

namespace Odin.Hosting.Authentication.Owner
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