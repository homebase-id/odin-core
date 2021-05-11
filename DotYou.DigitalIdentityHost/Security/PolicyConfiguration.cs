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
            policy.AddPolicy(DotYouPolicyNames.MustOwnThisIdentity,
                pb => pb.RequireClaim(DotYouClaimTypes.IsIdentityOwner,
                    true.ToString().ToLower()));

            policy.AddPolicy(DotYouPolicyNames.MustBeIdentified,
                pb => pb.RequireClaim(DotYouClaimTypes.IsIdentified,
                    true.ToString().ToLower()));
        }
    }

    public static class AuthSchemes
    {
        /// <summary>
        /// Scheme for authenticating an individual to the Digital Identity they own
        /// </summary>
        public static string DotIdentityOwner = "digital-identity-owner";
        
        /// <summary>
        /// Scheme for authenticating external Digital Identity hosts. 
        /// </summary>
        //TODO: determine why I cannot use my own name here.  I must use 'certificate'
        public static string ExternalDigitialIdentityClientCertificate = "Certificate";
    }
}