using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization;

namespace Odin.Hosting.UnifiedV2.Authentication
{
    public static class UnifiedPolicies
    {
        public const string Owner = "HasValidOwnerToken";
        public const string App = "HasValidAppToken";
        public const string Guest = "HasValidGuestAccessToken";

        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(Owner, pb =>
            {
                pb.RequireAuthenticatedUser();
                pb.RequireClaim(OdinClaimTypes.IsIdentityOwner, bool.TrueString);
                
                pb.RequireClaim(OdinClaimTypes.IsAuthorizedApp, bool.FalseString);
                pb.RequireClaim(OdinClaimTypes.IsAuthorizedGuest, bool.FalseString);
                pb.AuthenticationSchemes.Add(OwnerAuthConstants.SchemeName);
            });

            policy.AddPolicy(App, pb =>
            {
                pb.RequireAuthenticatedUser();
                pb.RequireClaim(OdinClaimTypes.IsIdentityOwner, bool.TrueString);
                pb.RequireClaim(OdinClaimTypes.IsAuthorizedApp, bool.TrueString);
                
                pb.RequireClaim(OdinClaimTypes.IsAuthorizedGuest, bool.FalseString);
                pb.AuthenticationSchemes.Add(YouAuthConstants.AppSchemeName);
            });

            policy.AddPolicy(Guest, pb =>
            {
                pb.RequireAuthenticatedUser();
                pb.RequireClaim(OdinClaimTypes.IsIdentityOwner, bool.FalseString);
                pb.RequireClaim(OdinClaimTypes.IsAuthorizedApp, bool.FalseString);
                
                pb.RequireClaim(OdinClaimTypes.IsAuthorizedGuest, bool.TrueString);
                
                pb.AuthenticationSchemes.Add(YouAuthConstants.YouAuthScheme);
            });
        }
    }
}