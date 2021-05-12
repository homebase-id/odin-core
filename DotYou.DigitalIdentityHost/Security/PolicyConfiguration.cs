using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Identity;
using Microsoft.AspNetCore.Authorization;

namespace DotYou.TenantHost.Security
{
    public interface IPolicyConfig
    {
        void AddPolicies(AuthorizationOptions policy);
    }

    public class PolicyConfig : IPolicyConfig
    {
        public void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(DotYouPolicyNames.IsDigitalIdentityOwner, 
                pb =>
                {
                    pb.RequireClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower());
                    pb.AuthenticationSchemes.Add(DotYouAuthSchemes.DotIdentityOwner);
                });

            policy.AddPolicy(DotYouPolicyNames.MustBeIdentified,
                pb =>
                {
                    pb.RequireClaim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower());
                    //pb.AuthenticationSchemes.Add((DotYouAuthSchemes.DotIdentityOwner));
                    pb.AuthenticationSchemes.Add((DotYouAuthSchemes.ExternalDigitalIdentityClientCertificate));
                });
        }
    }
}