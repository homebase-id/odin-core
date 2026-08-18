using System;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Registry.Registration;
using Odin.Core.Util;
using Odin.Hosting.ApiExceptions.Client;

namespace Odin.Hosting.Controllers.Registration;

[ApiController]
[Route("/api/registration/v1/registration")]
[ServiceFilter(typeof(RegistrationRestrictedAttribute))]
public class RegistrationController : ControllerBase
{
    private readonly IIdentityRegistrationService _regService;

    public RegistrationController(IIdentityRegistrationService regService)
    {
        _regService = regService;
    }

    /// <summary>
    /// Validate domain name
    /// </summary>
    /// <returns></returns>
    [HttpGet("is-valid-domain/{domain}")]
    public IActionResult IsValidDomain(string domain)
    {
        domain = domain.Trim();
        return new JsonResult(AsciiDomainNameValidator.TryValidateDomain(domain));
    }

    /// <summary>
    /// Gets zone apex of a domain (i.e. the nearest domain with a SOA and NS record)
    /// </summary>
    /// <returns></returns>
    [HttpGet("lookup-zone-apex/{domain}")]
    public async Task<IActionResult> LookupZoneApex(string domain)
    {
        var zoneApex = await _regService.LookupZoneApexAsync(ParseDomain(domain), HttpContext.RequestAborted);
        return new JsonResult(zoneApex);
    }

    /// <summary>
    /// Gets the DNS configuration required for a custom domain
    /// </summary>
    /// <returns></returns>
    [HttpGet("dns-config/{domain}")]
    public async Task<IActionResult> GetDnsConfiguration(string domain, [FromQuery] bool includeAlias = false)
    {
        var dnsConfig = await _regService.GetDnsConfiguration(ParseDomain(domain));
        if (!includeAlias)
        {
            dnsConfig = dnsConfig.Where(x => x.Type != "ALIAS").ToList();
        }
        return new JsonResult(dnsConfig);
    }

    /// <summary>
    /// Test if dns records have propageted to selection of major dns resolvers
    /// </summary>
    /// <returns></returns>
    [HttpGet("did-dns-records-propagate/{domain}")]
    public async Task<IActionResult> DidDnsRecordsPropagate(string domain)
    {
        var (resolved, _) = await _regService.GetExternalDomainDnsStatus(ParseDomain(domain), HttpContext.RequestAborted);
        return new JsonResult(resolved);
    }

    /// <summary>
    /// Test domain connectivity
    /// </summary>
    /// <returns></returns>
    [HttpGet("can-connect-to/{domain}/{port}")]
    public async Task<IActionResult> CanConnectToHostAndPort(string domain, int port)
    {
        var result = await _regService.CanConnectToHostAndPort(ParseDomain(domain), port);
        return new JsonResult(result);
    }

    /// <summary>
    /// Test domain certificate
    /// </summary>
    /// <returns></returns>
    [HttpGet("has-valid-certificate/{domain}")]
    public async Task<IActionResult> HasValidCertificate(string domain)
    {
        var result = await _regService.HasValidCertificate(ParseDomain(domain));
        return new JsonResult(result);
    }

    /// <summary>
    /// Gets the list of domains managed apexes by this identity
    /// </summary>
    /// <returns></returns>
    [HttpGet("managed-domain-apexes")]
    public async Task<IActionResult> GetManagedDomainApexes()
    {
        var domains = await _regService.GetManagedDomainApexes();
        return new JsonResult(domains);
    }

    /// <summary>
    /// Checks availability
    /// </summary>
    /// <param name="apex"></param>
    /// <param name="prefix"></param>
    /// <returns></returns>
    [HttpGet("is-managed-domain-available/{apex}/{prefix}")]
    public async Task<IActionResult> IsManagedDomainAvailable(string prefix, string apex)
    {
        prefix = prefix.Trim();
        apex = apex.Trim();
        try
        {
            var result = await _regService.IsManagedDomainAvailable(prefix, apex, HttpContext.RequestAborted);
            return new JsonResult(result);
        }
        catch (Exception)
        {
            return new JsonResult(false);
        }
    }

    /// <summary>
    /// Create managed domain
    /// </summary>
    /// <param name="apex"></param>
    /// <param name="prefix"></param>
    /// <param name="invitationCode"></param>
    /// <returns></returns>
    [HttpPost("create-managed-domain/{apex}/{prefix}")]
    public async Task<IActionResult> CreateManagedDomain(
        string prefix,
        string apex,
        [FromQuery(Name = "invitation-code")] string invitationCode)
    {
        prefix = prefix.Trim();
        apex = apex.Trim();

        if (!await _regService.IsValidInvitationCode(invitationCode))
        {
            throw new BadRequestException(message: "Invalid or expired Invitation Code");
        }

        var available = await _regService.IsManagedDomainAvailable(prefix, apex, HttpContext.RequestAborted);
        if (!available)
        {
            return Problem(
                statusCode: StatusCodes.Status412PreconditionFailed,
                title: "Domain name not available"
            );
        }

        await _regService.CreateManagedDomain(prefix, apex);
        return NoContent();
    }

#if DEBUG
    /// <summary>
    /// Delete managed domain
    /// </summary>
    /// <param name="apex"></param>
    /// <param name="prefix"></param>
    /// <returns></returns>
    // curl -X DELETE https://provisioning.dotyou.cloud/api/registration/v1/registration/delete-managed-domain/demo.rocks/foo.bar
    [HttpDelete("delete-managed-domain/{apex}/{prefix}")]
    public async Task<IActionResult> DeleteManagedDomain(string prefix, string apex)
    {
        prefix = prefix.Trim();
        apex = apex.Trim();

        await _regService.DeleteManagedDomain(prefix, apex);
        return NoContent();
    }
#endif

    /// <summary>
    /// Check if own domain is available
    /// </summary>
    /// <returns></returns>
    [HttpGet("is-own-domain-available/{domain}")]
    public async Task<IActionResult> IsOwnDomainAvailable(string domain)
    {
        try
        {
            // Inside the try: an invalid domain is "not available", not an error
            var result = await _regService.IsOwnDomainAvailable(ParseDomain(domain));
            return new JsonResult(result);
        }
        catch (Exception)
        {
            return new JsonResult(false);
        }
    }

    /// <summary>
    /// Pre-provisions the DNS zone for an own-domain on our nameservers so the user can
    /// delegate to us (via NS records or a registrar nameserver change) instead of creating
    /// individual DNS records. The zone is only created once control of the domain is
    /// proven (delegation to our nameservers at the parent, or valid manual records) -
    /// call it before each DNS status poll; it is idempotent and returns created=false
    /// until the proof appears. No-op when zone hosting is not configured.
    /// </summary>
    /// <returns></returns>
    [HttpPost("create-own-domain-zone/{domain}")]
    public async Task<IActionResult> CreateOwnDomainZone(
        string domain,
        [FromQuery(Name = "invitation-code")] string invitationCode)
    {
        var asciiDomain = ParseDomain(domain);

        if (!await _regService.IsValidInvitationCode(invitationCode))
        {
            throw new BadRequestException(message: "Invalid or expired Invitation Code");
        }

        var available = await _regService.IsOwnDomainAvailable(asciiDomain);
        if (!available)
        {
            return Problem(
                statusCode: StatusCodes.Status412PreconditionFailed,
                title: "Domain name not available"
            );
        }

        var result = await _regService.CreateOwnDomainZone(asciiDomain, HttpContext.RequestAborted);
        return new JsonResult(new
        {
            created = result == CreateOwnDomainZoneResult.Created,
            // camelCase reason so the frontend can distinguish transient (controlNotProven)
            // from permanent (shadowsHostedZone, notConfigured) refusals
            reason = char.ToLowerInvariant(result.ToString()[0]) + result.ToString()[1..],
        });
    }

    /// <summary>
    /// DNSSEC state of an own-domain's hosted zone: whether it is signed, whether the
    /// parent zone can carry a DS record, and whether the DS published at the parent
    /// matches our keys - plus the DS values the user would enter at their registrar
    /// (apex) or DNS host (subdomain). Read-only; everything returned is public DNS
    /// material. See docs/byod-dnssec-plan.md.
    /// </summary>
    [HttpGet("dnssec-status/{domain}")]
    public async Task<IActionResult> GetDnssecStatus(string domain)
    {
        var result = await _regService.GetDnssecStatusAsync(ParseDomain(domain), HttpContext.RequestAborted);
        return new JsonResult(new
        {
            // camelCase status so the frontend switches on stable string values
            status = char.ToLowerInvariant(result.Status.ToString()[0]) + result.Status.ToString()[1..],
            ourDsRecords = result.OurDsRecords,
            parentDsRecords = result.ParentDsRecords,
            parentZoneSigned = result.ParentZoneSigned,
        });
    }

    /// <summary>
    /// Gets the status for the ongoing own domain registration
    /// </summary>
    /// <returns></returns>
    [HttpGet("own-domain-dns-status/{domain}")]
    public async Task<IActionResult> GetOwnDomainDnsStatus(string domain, [FromQuery] bool includeAlias = false)
    {
        var (success, dnsConfig) = await _regService.GetAuthoritativeDomainDnsStatus(ParseDomain(domain), HttpContext.RequestAborted);
        if (!includeAlias)
        {
            dnsConfig = dnsConfig.Where(x => x.Type != "ALIAS").ToList();
        }
        return new JsonResult(dnsConfig)
        {
            StatusCode = success ? StatusCodes.Status200OK : StatusCodes.Status202Accepted
        };
    }

#if DEBUG
    /// <summary>
    /// Delete own domain
    /// </summary>
    /// <param name="domain"></param>
    /// <returns></returns>
    // curl -X DELETE https://provisioning.dotyou.cloud/api/registration/v1/registration/delete-own-domain/foo.bar
    [HttpDelete("delete-own-domain/{domain}")]
    public async Task<IActionResult> DeleteOwnDomain(string domain)
    {
        await _regService.DeleteOwnDomain(ParseDomain(domain));
        return NoContent();
    }
#endif

    /// <summary>
    /// Create identity on own or managed domain
    /// </summary>
    /// <param name="domain"></param>
    /// <param name="identity"></param>
    /// <returns></returns>
    [HttpPost("create-identity-on-domain/{domain}")]
    public async Task<IActionResult> CreateIdentityOnDomain(string domain, [FromBody] IdentityModel identity)
    {
        var asciiDomain = ParseDomain(domain);

        if (!MailAddress.TryCreate(identity.Email, out _))
        {
            throw new BadRequestException(message: "Invalid email address");
        }

        if (!await _regService.IsValidInvitationCode(identity.InvitationCode))
        {
            throw new BadRequestException(message: "Invalid or expired Invitation Code");
        }

        //
        // Check that our new domain can be looked up using authoritative nameservers
        //
        var (resolved, _) = await _regService.GetAuthoritativeDomainDnsStatus(asciiDomain, HttpContext.RequestAborted);
        if (!resolved)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "DNS records were not found by all authoritative name servers. Try later."
            );
        }

        var firstRunToken = await _regService.CreateIdentityOnDomainAsync(asciiDomain, identity.Email, identity.PlanId, identity.InvitationCode);
        return new JsonResult(firstRunToken);
    }
    
    /// <summary>
    /// Checks if we need an invitation code
    /// </summary>
    /// <returns></returns>
    [HttpGet("is-invitation-code-needed")]
    public async Task<IActionResult> IsInvitationCodeNeeded()
    {
        return Ok(await _regService.IsInvitationCodeNeeded());
    }

    /// <summary>
    /// Determines if the invitation code is valid
    /// </summary>
    /// <returns></returns>
    [HttpGet("is-valid-invitation-code/{code}")]
    public async Task<IActionResult> IsValidInvitationCode(string code)
    {
        var valid = await _regService.IsValidInvitationCode(code);
        if (valid)
        {
            return Ok();
        }

        return NotFound();
    }

    //

    /// <summary>
    /// The string-to-type boundary: trims, validates and converts a route-supplied domain.
    /// Everything behind the controller works with <see cref="AsciiDomainName"/>.
    /// </summary>
    private static AsciiDomainName ParseDomain(string domain)
    {
        domain = domain.Trim();
        if (!AsciiDomainNameValidator.TryValidateDomain(domain))
        {
            throw new BadRequestException(message: "Invalid domain name");
        }
        return new AsciiDomainName(domain);
    }
}
