using Microsoft.AspNetCore.Authorization;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Security
{
    public class PolicyConfig : IPolicyConfig
    {
        public void AddPolicies(AuthorizationOptions policy)
        {
            policy.AddPolicy(DotYouPolicyNames.IsDigitalIdentityOwner, 
                pb =>
                {
                    pb.RequireClaim(DotYouClaimTypes.IsIdentityOwner, true.ToString().ToLower());
                    pb.AuthenticationSchemes.Add(DotYouAuthConstants.DotIdentityOwnerScheme);
                });

            policy.AddPolicy(DotYouPolicyNames.MustBeIdentified,
                pb =>
                {
                    pb.RequireClaim(DotYouClaimTypes.IsIdentified, true.ToString().ToLower());
                    //pb.AuthenticationSchemes.Add((DotYouAuthSchemes.DotIdentityOwner));
                    pb.AuthenticationSchemes.Add((DotYouAuthConstants.ExternalDigitalIdentityClientCertificateScheme));
                });
        }
    }
}