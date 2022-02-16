using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Authentication.App
{
    public static class AppPolicies
    {
        public const string IsAuthorizedApp = "IsAuthorizedApp";

        public static void AddPolicies(AuthorizationOptions policy)
        {
            //TODO
            policy.AddPolicy(IsAuthorizedApp, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsAuthorizedApp, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(AppAuthConstants.SchemeName);
            });
            
        }
    }
}