using Microsoft.AspNetCore.Authorization;
using Odin.Core.Services.Authorization;

namespace Odin.Hosting.Authentication.System
{
    public static class SystemPolicies
    {
        public const string IsSystemProcess = "IsSystemProcess";

        public static void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(IsSystemProcess, pb =>
            {
                pb.RequireClaim(OdinClaimTypes.IsSystemProcess, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(SystemAuthConstants.SchemeName);
            });
        }
    }
    
}