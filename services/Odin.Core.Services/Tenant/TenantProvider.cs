using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Youverse.Core.Services.Registry;
using Youverse.Core.Trie;

#nullable enable
namespace Youverse.Core.Services.Tenant
{
    public class TenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IIdentityRegistry _identityRegistry;
        private readonly ConcurrentDictionary<string, Tenant> _tenants = new();

        //
        
        public TenantProvider(IHttpContextAccessor httpContextAccessor, IIdentityRegistry identityRegistry)
        {
            _httpContextAccessor = httpContextAccessor;
            _identityRegistry = identityRegistry;
        }
        
        //

        public Tenant? GetCurrentTenant()
        {
            var host = _httpContextAccessor.HttpContext?.Request.Host.Host;

            var idReg = _identityRegistry.ResolveIdentityRegistration(host, out _);
            if (idReg == null)
            {
                return null;
            }

            var tenant = _tenants.GetOrAdd(idReg.PrimaryDomainName, domain => new Tenant(domain));
            return tenant;
        }
        
        //
    }
}