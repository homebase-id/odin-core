using Microsoft.AspNetCore.Authorization;

namespace Youverse.Hosting.Authentication.App
{
    public static class AppPolicies
    {
        public const string IsAuthorizedApp = "MustOwnThisIdentity";

        public static void Add(AuthorizationOptions policy)
        {
            //TODO
            // policy.AddPolicy(IsAuthorizedApp, pb =>
            // {
            //     pb.RequireClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower());
            //     pb.AuthenticationSchemes.Add(OwnerAuthConstants.DotIdentityOwnerScheme);
            // });
        }
    }
}