using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Odin.Services.Registry;

#nullable enable
namespace Odin.Services.Tenant;

public class TenantProvider(IHttpContextAccessor httpContextAccessor, IIdentityRegistry identityRegistry)
    : ITenantProvider
{
    private readonly ConcurrentDictionary<string, Tenant> _tenants = new();

    //

    public Tenant? GetCurrentTenant()
    {
        var host = httpContextAccessor.HttpContext?.Request.Host.Host;
        if (host == null)
        {
            return null;
        }

        var idReg = identityRegistry.ResolveIdentityRegistration(host, out _);
        if (idReg == null)
        {
            return null;
        }

        var tenant = _tenants.GetOrAdd(idReg.PrimaryDomainName, domain => new Tenant(domain));
        return tenant;
    }

    //
}
