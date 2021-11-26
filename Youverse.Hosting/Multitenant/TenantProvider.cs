using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Tenant;

#nullable enable
namespace Youverse.Hosting.Multitenant
{
    public class TenantProvider : ITenantProvider
    {
        private readonly ITenantResolutionStrategy _tenantResolutionStrategy;
        private readonly ConcurrentDictionary<string, Tenant> _tenants = new();
        //
        
        public TenantProvider(ITenantResolutionStrategy tenantResolutionStrategy)
        {
            _tenantResolutionStrategy = tenantResolutionStrategy;
        }
        
        //

        public Tenant? GetCurrentTenant()
        {
            var tenantIdentifier = _tenantResolutionStrategy.GetTenantIdentifier();

            if (string.IsNullOrWhiteSpace(tenantIdentifier))
            {
                return null;
            }

            if (_tenants.TryGetValue(tenantIdentifier, out var tenant))
            {
                return tenant;
            }

            tenant = new Tenant(tenantIdentifier);
            
            _tenants.GetOrAdd(tenantIdentifier, tenant);
            
            return tenant;
        }
        
        //
    }
}