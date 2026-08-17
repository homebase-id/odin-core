using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Dns;
using Odin.Services.JobManagement;

namespace Odin.Services.Registry.Registration;

#nullable enable

/// <summary>
/// Handles creating an identity on this host
/// </summary>
public class IdentityRegistrationService : IIdentityRegistrationService
{
    private readonly ILogger<IdentityRegistrationService> _logger;
    private readonly IIdentityRegistry _registry;
    private readonly OdinConfiguration _configuration;
    private readonly IDnsRestClient _dnsRestClient;
    private readonly IDynamicHttpClientFactory _httpClientFactory;
    private readonly IDnsLookupService _dnsLookupService;
    private readonly IJobManager _jobManager;

    public IdentityRegistrationService(
        ILogger<IdentityRegistrationService> logger,
        IIdentityRegistry registry,
        OdinConfiguration configuration,
        IDnsRestClient dnsRestClient,
        IDynamicHttpClientFactory httpClientFactory,
        IDnsLookupService dnsLookupService,
        IJobManager jobManager)
    {
        _logger = logger;
        _configuration = configuration;
        _registry = registry;
        _dnsRestClient = dnsRestClient;
        _httpClientFactory = httpClientFactory;
        _dnsLookupService = dnsLookupService;
        _jobManager = jobManager;
    }

    //

    public async Task<bool> CanConnectToHostAndPort(string domain, int port)
    {
        try
        {
            // SEB:TODO will we get a TIME_WAIT problem here?
            using var tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await tcpClient.ConnectAsync(domain, port, cts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    //

    public async Task<bool> HasValidCertificate(string domain)
    {
        var httpClient = _httpClientFactory.CreateClient($"{nameof(IdentityRegistrationService)}:{domain}", cfg =>
        {
            cfg.HandlerLifetime = TimeSpan.FromSeconds(5); // Short-lived to deal with DNS changes
            cfg.AllowUntrustedServerCertificate =
                _configuration.CertificateRenewal.UseCertificateAuthorityProductionServers == false;
        });
        try
        {
            await httpClient.GetAsync($"https://{domain}:{_configuration.Host.DefaultHttpsPort}");
            return true;
        }
        catch (Exception e)
        {
            var message = e.InnerException?.Message ?? e.Message;
            _logger.LogDebug("IdentityRegistrationService:HasValidCertificate: {message}", message);
            return false;
        }
    }

    //

    public Task<string> LookupZoneApexAsync(string domain, CancellationToken cancellationToken = default)
    {
        return _dnsLookupService.LookupZoneApexAsync(domain, cancellationToken);
    }

    //

    public Task<List<OdinConfiguration.RegistrySection.ManagedDomainApex>> GetManagedDomainApexes()
    {
        // Only return list of managed apexes if we have DNS server config
        var noDnsServerConfig =
            string.IsNullOrEmpty(_configuration.Registry.PowerDnsApiKey) &&
            string.IsNullOrEmpty(_configuration.Registry.PowerDnsHostAddress);

        if (noDnsServerConfig)
        {
            return Task.FromResult(new List<OdinConfiguration.RegistrySection.ManagedDomainApex>());
        }

        return Task.FromResult(_configuration.Registry.ManagedDomainApexes);
    }

    //

    public Task<List<DnsConfig>> GetDnsConfiguration(string domain)
    {
        return Task.FromResult(_dnsLookupService.GetDnsConfiguration(domain));
    }

    //

    //
    // Managed Domain
    //

    public async Task<bool> IsManagedDomainAvailable(string prefix, string apex, CancellationToken cancellationToken = default)
    {
        if (_configuration.Registry.ManagedDomainApexes.Count == 0)
        {
            return false;
        }

        var domain = prefix + "." + apex;

        if (!AsciiDomainNameValidator.TryValidateDomain(domain))
        {
            return false;
        }

        // Identity already exists or domain path clash?
        if (false == await _registry.CanAddNewRegistration(domain))
        {
            return false;
        }

        return await _dnsLookupService.IsManagedDomainAvailableAsync(prefix, apex, cancellationToken);
    }

    //

    public async Task CreateManagedDomain(string prefix, string apex)
    {
        var domain = prefix + "." + apex;

        _logger.LogInformation("Creating managed domain {domain}", domain);

        AsciiDomainNameValidator.AssertValidDomain(domain);
        _dnsLookupService.AssertManagedDomainApexAndPrefix(prefix, apex);

        var dnsConfig = _dnsLookupService.GetDnsConfiguration(domain);

        var zoneId = apex + ".";
        foreach (var record in dnsConfig)
        {
            var name = record.Name != "" ? record.Name + "." + prefix : prefix;
            if (record.Type == "A")
            {
                await _dnsRestClient.CreateARecords(zoneId, name, new[] { record.Value });
            }
            else if (record.Type == "CNAME")
            {
                await _dnsRestClient.CreateCnameRecords(zoneId, name, record.Value + ".");
            }
            else if (record.Type is "ALIAS" or "NS")
            {
                // IGNORE - ALIAS is an instruction for third-party DNS hosts only;
                // NS entries describe delegation of own-domains and never apply to managed domains
            }
            else
            {
                // Sanity
                throw new OdinSystemException($"Unsupported record: {record.Type}");
            }
        }

        _logger.LogInformation("Created managed domain {domain}", domain);
    }

    //

    public async Task DeleteManagedDomain(string prefix, string apex)
    {
        var domain = prefix + "." + apex;
        AsciiDomainNameValidator.AssertValidDomain(domain);
        _dnsLookupService.AssertManagedDomainApexAndPrefix(prefix, apex);

        await _registry.DeleteRegistration(domain);

        var dnsConfig = _dnsLookupService.GetDnsConfiguration(domain);

        var zoneId = apex + ".";
        foreach (var record in dnsConfig)
        {
            var name = record.Name != "" ? record.Name + "." + prefix : prefix;
            if (record.Type == "A")
            {
                await _dnsRestClient.DeleteARecords(zoneId, name);
            }
            else if (record.Type == "CNAME")
            {
                await _dnsRestClient.DeleteCnameRecords(zoneId, name);
            }
            else if (record.Type is "ALIAS" or "NS")
            {
                // IGNORE - see CreateManagedDomain
            }
            else
            {
                // Sanity
                throw new OdinSystemException($"Unsupported record: {record.Type}");
            }
        }
    }

    //
    // Own Domain
    //

    public async Task<bool> IsOwnDomainAvailable(string domain)
    {
        if (!AsciiDomainNameValidator.TryValidateDomain(domain))
        {
            return false;
        }

        // Managed apexes (and anything under them) are never "own domains": they are
        // provisioned through the managed-domain flow, and an own-domain zone for one
        // would shadow the apex zone we host
        if (IsManagedDomain(domain.Trim().ToLower()))
        {
            return false;
        }

        // Identity already exists or domain path clash?
        return await _registry.CanAddNewRegistration(domain);

        // SEB:NOTE below removed for now since it's taking too big a toll on the system when called for each key press
        // We can only create new domain if we can find a zone apex
        // var zoneApex = await _dnsLookupService.LookupZoneApex(domain);
        // return !string.IsNullOrEmpty(zoneApex);
    }

    //

    public Task<(bool, List<DnsConfig>)> GetAuthoritativeDomainDnsStatus(string domain, CancellationToken cancellationToken = default)
    {
        return _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(domain, cancellationToken);
    }

    //

    public Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatus(string domain, CancellationToken cancellationToken = default)
    {
        return _dnsLookupService.GetExternalDomainDnsStatusAsync(domain, cancellationToken);
    }

    //

    public async Task DeleteOwnDomain(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);
        await _registry.DeleteRegistration(domain);
        await DeleteOwnDomainZone(domain);
    }

    //

    /// <summary>
    /// True when we can host DNS zones for own-domains: PowerDNS is configured and
    /// our authoritative nameserver hostnames are known.
    /// </summary>
    public bool CanHostOwnDomainZones =>
        !string.IsNullOrEmpty(_configuration.Registry.PowerDnsApiKey) &&
        _configuration.Registry.DnsConfigurationSet.NameServers.Count > 0;

    //

    /// <summary>
    /// Pre-provisions the DNS zone for an own-domain (apex or subdomain alike) in our PowerDNS,
    /// populated with the same records the user would otherwise create manually. Idempotent.
    ///
    /// Only creates the zone when control of the domain is proven: an identity is already
    /// registered for it, OR the parent zone delegates it to our nameservers, OR its manual
    /// DNS records validate. Without this gate anyone could claim a zone for a domain they
    /// don't own - including subdomains of zones we host ourselves.
    ///
    /// Additionally refuses (defense in depth) any domain that falls inside a zone already
    /// hosted in our PowerDNS: such a child zone would shadow that part of the parent zone
    /// (e.g. a hostile demo.id.pub zone would hijack demo.id.pub away from our id.pub zone).
    /// </summary>
    /// <returns>true when the zone exists (created or already present); false when refused</returns>
    public async Task<bool> CreateOwnDomainZone(string domain)
    {
        domain = domain.Trim().ToLower();
        AsciiDomainNameValidator.AssertValidDomain(domain);

        if (!CanHostOwnDomainZones)
        {
            _logger.LogDebug("Skipping zone creation for {domain}: PowerDNS or nameservers not configured", domain);
            return false;
        }

        if (IsManagedDomain(domain))
        {
            throw new OdinSystemException($"{domain} is a managed domain; it has no own zone");
        }

        var dns = _configuration.Registry.DnsConfigurationSet;
        var zoneId = domain + ".";

        // Never create a zone inside a zone we already host - it would shadow the parent
        var existingZones = await _dnsRestClient.GetZones() ?? [];
        foreach (var zone in existingZones)
        {
            var existingZoneId = zone.name.ToLower();
            if (zoneId != existingZoneId && zoneId.EndsWith("." + existingZoneId, StringComparison.Ordinal))
            {
                _logger.LogWarning("Refusing zone {zone}: it would shadow part of hosted zone {existing}",
                    zoneId, existingZoneId);
                return false;
            }
        }

        // Domain-control proof
        var isRegistered = await _registry.GetAsync(domain) != null;
        if (!isRegistered)
        {
            var delegatedToUs = await _dnsLookupService.IsDomainDelegatedToUsAsync(domain);
            if (!delegatedToUs)
            {
                var (manualRecordsValid, _) = await _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(domain);
                if (!manualRecordsValid)
                {
                    _logger.LogInformation(
                        "Not creating zone {zone} yet: no registered identity, no delegation to us, no valid records",
                        zoneId);
                    return false;
                }
            }
        }

        if (!await _dnsRestClient.ZoneExists(zoneId))
        {
            _logger.LogInformation("Creating zone {zone}", zoneId);
            await _dnsRestClient.CreateZone(zoneId, dns.NameServers.Select(x => x + ".").ToArray(), dns.SoaAdminEmail);
        }

        // Populate (REPLACE semantics, so re-running converges on the correct records)
        var dnsConfig = _dnsLookupService.GetDnsConfiguration(domain);
        foreach (var record in dnsConfig)
        {
            if (record.Type == "A")
            {
                await _dnsRestClient.CreateARecords(zoneId, record.Name, new[] { record.Value });
            }
            else if (record.Type == "CNAME")
            {
                await _dnsRestClient.CreateCnameRecords(zoneId, record.Name, record.Value + ".");
            }
            else if (record.Type is "ALIAS" or "NS")
            {
                // IGNORE - in our own zone the apex A record is authoritative (no ALIAS needed);
                // the zone's NS records were created by CreateZone
            }
            else
            {
                // Sanity
                throw new OdinSystemException($"Unsupported record: {record.Type}");
            }
        }

        _logger.LogInformation("Created own-domain zone {zone}", zoneId);
        return true;
    }

    //

    /// <summary>
    /// Best-effort DNS cleanup when a tenant is deleted. Managed domains get their
    /// records removed from the shared apex zone; own domains get their zone deleted.
    /// Never throws: a DNS cleanup failure must not block account deletion.
    /// </summary>
    public async Task DeleteDnsRecordsForDomain(string domain)
    {
        try
        {
            domain = domain.Trim().ToLower();

            var apex = _configuration.Registry.ManagedDomainApexes
                .Find(x => domain.EndsWith("." + x.Apex, StringComparison.OrdinalIgnoreCase))?.Apex;
            if (apex == null)
            {
                await DeleteOwnDomainZone(domain);
                return;
            }

            // Managed domain: remove its records from the apex zone. Deliberately not
            // DeleteManagedDomain: that re-deletes the registration and asserts the
            // configured prefix label count, which may have changed since signup.
            var prefix = domain[..^(apex.Length + 1)];
            var zoneId = apex + ".";
            var dnsConfig = _dnsLookupService.GetDnsConfiguration(domain);
            foreach (var record in dnsConfig)
            {
                var name = record.Name != "" ? record.Name + "." + prefix : prefix;
                if (record.Type == "A")
                {
                    await _dnsRestClient.DeleteARecords(zoneId, name);
                }
                else if (record.Type == "CNAME")
                {
                    await _dnsRestClient.DeleteCnameRecords(zoneId, name);
                }
                // ALIAS/NS: nothing to delete for managed domains
            }

            _logger.LogInformation("Deleted DNS records for managed domain {domain}", domain);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete DNS records for {domain}; clean up manually", domain);
        }
    }

    //

    /// <summary>
    /// Best-effort removal of an own-domain's zone. Never throws: a DNS cleanup failure
    /// must not block account deletion.
    /// </summary>
    public async Task DeleteOwnDomainZone(string domain)
    {
        if (!CanHostOwnDomainZones || IsManagedDomain(domain))
        {
            return;
        }

        var zoneId = domain + ".";
        try
        {
            if (await _dnsRestClient.ZoneExists(zoneId))
            {
                await _dnsRestClient.DeleteZone(zoneId);
                _logger.LogInformation("Deleted own-domain zone {zone}", zoneId);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete own-domain zone {zone}; delete it manually", zoneId);
        }
    }

    //

    private bool IsManagedDomain(string domain)
    {
        return _configuration.Registry.ManagedDomainApexes.Exists(x =>
            domain.Equals(x.Apex, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith("." + x.Apex, StringComparison.OrdinalIgnoreCase));
    }

    //

    public async Task<Guid> CreateIdentityOnDomainAsync(string domain, string email, string planId, string invitationCode)
    {
        var identity = await _registry.GetAsync(domain);
        if (identity != null)
        {
            throw new OdinSystemException($"Identity {domain} already exists");
        }

        var request = new IdentityRegistrationRequest()
        {
            Id = null, // Sanity
            OdinId = (OdinId)domain,
            Email = email,
            PlanId = planId,
            IsCertificateManaged = false, //TODO
            EnablePublicWebPresence = await CodeGrantsPublicWebPresence(invitationCode),
        };

        try
        {
            var firstRunToken = await _registry.AddRegistration(request);

            // Ensure the domain's zone exists so the owner can switch to NS delegation
            // later (no-op for managed domains and unconfigured hosts). Ownership is
            // proven at this point: the registration exists and DNS validation passed.
            // Best-effort - a DNS host hiccup must not fail the signup.
            if (!IsManagedDomain(domain))
            {
                try
                {
                    await CreateOwnDomainZone(domain);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to create zone for {domain}; run create-own-domain-zones to backfill", domain);
                }
            }

            // Queue background job to send email
            if (_configuration.Mailgun.Enabled)
            {
                var job = _jobManager.NewJob<SendProvisioningCompleteEmailJob>();
                job.Data = new SendProvisioningCompleteEmailJobData
                {
                    Domain = domain,
                    Email = email,
                    FirstRunToken = firstRunToken.ToString(),
                };

                await _jobManager.ScheduleJobAsync(job, new JobSchedule
                {
                    RunAt = DateTimeOffset.Now.AddSeconds(1),
                    MaxAttempts = 20,
                    RetryDelay = TimeSpan.FromMinutes(1),
                    OnSuccessDeleteAfter = TimeSpan.FromMinutes(1),
                    OnFailureDeleteAfter = TimeSpan.FromMinutes(1),
                });
            }

            return firstRunToken;
        }
        catch (Exception)
        {
            await _registry.DeleteRegistration(domain);
            throw;
        }
    }

    //

    public Task<bool> IsInvitationCodeNeeded()
    {
        return Task.FromResult(ConfiguredInvitationCodeCount > 0);
    }

    //

    public Task<bool> IsValidInvitationCode(string code)
    {
        if (ConfiguredInvitationCodeCount == 0)
        {
            return Task.FromResult(true);
        }

        if (string.IsNullOrEmpty(code))
        {
            return Task.FromResult(false);
        }

        var match = MatchesAny(_configuration.Registry.InvitationCodes, code) ||
                    MatchesAny(_configuration.Registry.InvitationCodesWithoutPublicWebPresence, code);
        return Task.FromResult(match);
    }

    //

    public Task<bool> CodeGrantsPublicWebPresence(string code)
    {
        var withoutPresence = !string.IsNullOrEmpty(code) &&
                              MatchesAny(_configuration.Registry.InvitationCodesWithoutPublicWebPresence, code);
        return Task.FromResult(!withoutPresence);
    }

    //

    private int ConfiguredInvitationCodeCount =>
        _configuration.Registry.InvitationCodes.Count +
        _configuration.Registry.InvitationCodesWithoutPublicWebPresence.Count;

    private static bool MatchesAny(List<string> codes, string code)
    {
        return codes.Exists(c => string.Equals(c, code, StringComparison.InvariantCultureIgnoreCase));
    }
}
