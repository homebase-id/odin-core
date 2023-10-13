using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Registry;

namespace Odin.Core.Services.Admin.Tenants;

public interface ITenantAdmin
{
    Task<List<TenantModel>> GetTenants();
    Task<TenantModel> GetTenant(string domain);
    Task<bool> TenantExists(string domain);
    Task EnableTenant(string domain);
    Task DisableTenant(string domain);
}

public class TenantAdmin : ITenantAdmin
{
    private readonly ILogger<TenantAdmin> _logger;
    private readonly OdinConfiguration _config;
    private readonly IIdentityRegistry _identityRegistry;

    public TenantAdmin(ILogger<TenantAdmin> logger, OdinConfiguration config, IIdentityRegistry identityRegistry)
    {
        _logger = logger;
        _config = config;
        _identityRegistry = identityRegistry;
    }

    //

    public async Task<List<TenantModel>> GetTenants()
    {
        var identities = await _identityRegistry.GetList();
        var result = identities.Results.Select(Map).ToList();
        return result;
    }

    //

    public async Task<TenantModel> GetTenant(string domain)
    {
        var identity = await _identityRegistry.Get(domain);
        return identity == null ? null : Map(identity);
    }

    //

    public async Task<bool> TenantExists(string domain)
    {
        return await _identityRegistry.IsIdentityRegistered(domain);
    }

    //

    public async Task EnableTenant(string domain)
    {
        await _identityRegistry.ToggleDisabled(domain, false);
    }

    //

    public async Task DisableTenant(string domain)
    {
        await _identityRegistry.ToggleDisabled(domain, true);
    }

    //

    private static TenantModel Map(IdentityRegistration identityRegistration)
    {
        return new TenantModel
        {
            Domain = identityRegistration.PrimaryDomainName,
            Id = identityRegistration.Id
        };
    }

    //


}