using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Dns.Health;

namespace Odin.Hosting.Controllers.OwnerToken.Dns;

/// <summary>
/// DNS health for the owner console's Security tab: required-record status, the
/// optional www record, and the DNSSEC chain of trust. Read-only; built entirely on
/// generic public-DNS lookups (never the PowerDNS API - this runs on identity hosts,
/// including self-hosted ones). See docs/owner-console-dnssec-panel-plan.md.
/// </summary>
[ApiController]
[AuthorizeValidOwnerToken]
[Route(OwnerApiPathConstants.DnsV1)]
[ApiExplorerSettings(GroupName = "owner-v1")]
public class OwnerDnsHealthController : OdinControllerBase
{
    private readonly DnsHealthService _dnsHealthService;

    public OwnerDnsHealthController(DnsHealthService dnsHealthService)
    {
        _dnsHealthService = dnsHealthService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var domain = WebOdinContext.Tenant.AsciiDomain;
        var result = await _dnsHealthService.GetDnsHealthAsync(domain, HttpContext.RequestAborted);
        return new JsonResult(result);
    }
}
