using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Authentication.System
{
    public static class SystemPolicies
    {
        public const string IsSystemProcess = "IsSystemProcess";

        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(IsSystemProcess, pb =>
            {
                pb.RequireClaim(DotYouClaimTypes.IsSystemProcess, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(SystemAuthConstants.SchemeName);
            });
        }
    }
    
}