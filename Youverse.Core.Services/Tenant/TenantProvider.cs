using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

#nullable enable
namespace Youverse.Core.Services.Tenant
{
    public class TenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ConcurrentDictionary<string, Tenant> _tenants = new();

        //
        
        public TenantProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }
        
        //

        public Tenant? GetCurrentTenant()
        {
            var host = _httpContextAccessor.HttpContext?.Request.Host.Host;

            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            if (_tenants.TryGetValue(host, out var tenant))
            {
                return tenant;
            }

            tenant = new Tenant(host);
            _tenants.GetOrAdd(host, tenant);
            
            return tenant;
        }
        
        //
    }
}