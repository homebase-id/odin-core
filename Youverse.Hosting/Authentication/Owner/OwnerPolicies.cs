using System;
using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;
using Youverse.Hosting.Authentication.App;

namespace Youverse.Hosting.Authentication.Owner
{
    public static class OwnerPolicies
    {
        public const string IsDigitalIdentityOwner = "MustOwnThisIdentity";

        public const string IsAuthorizedApp = "IsAuthorizedApp";

        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(IsDigitalIdentityOwner, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(OwnerAuthConstants.SchemeName);
            });
            
            //TODO
            policy.AddPolicy(IsAuthorizedApp, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsAuthorizedApp, true.ToString().ToLower());
                pb.RequireClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower());

                pb.AuthenticationSchemes.Add(AppAuthConstants.SchemeName);
                pb.AuthenticationSchemes.Add(OwnerAuthConstants.SchemeName);

            });
        }
        
    }
    
}