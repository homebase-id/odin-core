using Microsoft.AspNetCore.Authorization;

namespace Youverse.Hosting.Security
{
    public interface IPolicyConfig
    {
        void AddPolicies(AuthorizationOptions policy);
    }
}