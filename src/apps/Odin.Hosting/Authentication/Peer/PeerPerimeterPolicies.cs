using Microsoft.AspNetCore.Authorization;
using Odin.Services.Authorization;

namespace Odin.Hosting.Authentication.Peer
{
    public static class PeerPerimeterPolicies
    {
        public const string IsInOdinNetwork = "IsInOdinNetwork";

        public static void AddPolicies(AuthorizationOptions policy, string scheme)
        {
            policy.AddPolicy(IsInOdinNetwork, pb =>
            {
                pb.RequireClaim(OdinClaimTypes.IsAuthenticated, true.ToString().ToLower());
                pb.AuthenticationSchemes.Add(scheme);
            });
            
        }
    }
}