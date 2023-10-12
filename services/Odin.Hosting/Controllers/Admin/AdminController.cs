using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Registry;

namespace Odin.Hosting.Controllers.Admin;

[ApiController]
[Route(AdminApiPathConstants.BasePathV1)]
[ServiceFilter(typeof(AdminApiRestrictedAttribute))]
public class AdminController : ControllerBase
{
    private readonly ILogger<AdminController> _logger;
    private readonly IIdentityRegistry _identityRegistry;

    public AdminController(ILogger<AdminController> logger, IIdentityRegistry identityRegistry)
    {
        _logger = logger;
        _identityRegistry = identityRegistry;
    }

    //

    [HttpGet("ping")]
    public ActionResult<string> Ping()
    {
        return "pong";
    }

    //

    [HttpGet("tenants")]
    public async Task<ActionResult<List<TenantModel>>> GetTenants()
    {
        var identities = await _identityRegistry.GetList();
        var result = identities.Results.Select(Map).ToList();
        return result;
    }

    //

    [HttpGet("tenants/{domain}")]
    public async Task<ActionResult<TenantModel>> GetTenant(string domain)
    {
        var identity = await _identityRegistry.Get(domain);
        if (identity == null)
        {
            return NotFound();
        }
        return Map(identity);
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


}
