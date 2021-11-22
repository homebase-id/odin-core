using Microsoft.AspNetCore.Http;

#nullable enable
namespace Youverse.Hosting.Multitenant
{
    public class HostResolutionStrategy : ITenantResolutionStrategy
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        
        //

        public HostResolutionStrategy(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }
        
        //
    
        public string? GetTenantIdentifier()
        {
            return _httpContextAccessor.HttpContext?.Request.Host.Host;
        }
    }
}