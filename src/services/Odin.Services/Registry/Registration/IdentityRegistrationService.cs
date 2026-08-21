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

    public async Task<bool> CanConnectToHostAndPort(AsciiDomainName domain, int port)
    {
        try
        {
            // SEB:TODO will we get a TIME_WAIT problem here?
            using var tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await tcpClient.ConnectAsync(domain.DomainName, port, cts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    //

    public async Task<bool> HasValidCertificate(AsciiDomainName domain)
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

    public Task<string> LookupZoneApexAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
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

    public Task<List<DnsConfig>> GetDnsConfiguration(AsciiDomainName domain)
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
        var domain = new AsciiDomainName(prefix + "." + apex); // ctor validates

        _logger.LogInformation("Creating managed domain {domain}", domain);

        _dnsLookupService.AssertManagedDomainApexAndPrefix(prefix, apex);

        await EnsureManagedDomainRecords(prefix, apex);

        _logger.LogInformation("Created managed domain {domain}", domain);
    }

    //

    /// <summary>
    /// (Re)writes a managed domain's records in the apex zone (REPLACE semantics, so
    /// re-running converges - the CLI backfill uses this to apply new record types to
    /// existing tenants). Deliberately no prefix-label assert: the configured label
    /// count may have changed since the tenant signed up.
    /// </summary>
    public async Task EnsureManagedDomainRecords(string prefix, string apex)
    {
        var domain = new AsciiDomainName(prefix + "." + apex); // ctor validates

        var dnsConfig = _dnsLookupService.GetDnsConfiguration(domain);
        await WriteDnsRecords(apex + ".", dnsConfig, ManagedName(prefix));
    }

    //

    public async Task DeleteManagedDomain(string prefix, string apex)
    {
        var domain = new AsciiDomainName(prefix + "." + apex); // ctor validates
        _dnsLookupService.AssertManagedDomainApexAndPrefix(prefix, apex);

        await _registry.DeleteRegistration(domain.DomainName);

        var dnsConfig = _dnsLookupService.GetDnsConfiguration(domain);
        await DeleteDnsRecords(apex + ".", dnsConfig, ManagedName(prefix));
    }

    //

    /// <summary>
    /// Writes per-tenant on-activation records (e.g. the DKIM TXT set) into wherever
    /// the tenant's DNS records live: prefixed into the shared apex zone for managed
    /// domains, into the tenant's own hosted zone otherwise. Returns false when the
    /// tenant's DNS is not ours to write (manual-records/BYOD, or this host has no
    /// PowerDNS access) - the caller then surfaces the records as instructions.
    /// </summary>
    public async Task<bool> WriteOnActivationRecords(AsciiDomainName domain, List<DnsConfig> records)
    {
        return await DispatchOnActivationRecords(domain, records, WriteDnsRecords);
    }

    /// <summary>
    /// The delete counterpart of <see cref="WriteOnActivationRecords"/> (tenant
    /// deletion / deactivation). Note that own-domain tenant deletion removes the
    /// whole zone anyway; this matters for managed domains, whose on-activation
    /// records would otherwise linger in the shared apex zone.
    /// </summary>
    public async Task<bool> DeleteOnActivationRecords(AsciiDomainName domain, List<DnsConfig> records)
    {
        return await DispatchOnActivationRecords(domain, records, DeleteDnsRecords);
    }

    private async Task<bool> DispatchOnActivationRecords(
        AsciiDomainName domain,
        List<DnsConfig> records,
        Func<string, List<DnsConfig>, Func<DnsConfig, string>, Task> dispatch)
    {
        var domainName = domain.DomainName;

        var apex = FindManagedApex(domainName);
        if (apex != null)
        {
            if (string.IsNullOrEmpty(_configuration.Registry.PowerDnsApiKey))
            {
                return false;
            }

            var prefix = domainName[..^(apex.Length + 1)];
            await dispatch(apex + ".", records, ManagedName(prefix));
            return true;
        }

        // A failed probe (PowerDNS unreachable or placeholder-configured) degrades to
        // the instructions path rather than failing activation - activation is
        // idempotent and the periodic verification flags missing records later
        bool zoneExists;
        try
        {
            zoneExists = await OwnDomainZoneExists(domain);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Cannot probe for a hosted zone for {domain}; treating its records as not writable", domain);
            return false;
        }

        if (zoneExists)
        {
            await dispatch(domainName + ".", records, record => record.Name);
            return true;
        }

        return false;
    }

    //
    // Record dispatch - the single place a DnsConfig record type maps to rrset writes.
    // ALL populate/delete paths (own-domain zones, managed domains, tenant-deletion
    // cleanup, CLI backfills) go through these two methods, so a new record type is
    // added here once. nameOf maps a record to its rrset name in the target zone:
    // own-domain zones use record.Name as-is (empty = apex); managed domains append
    // the tenant prefix (ManagedName).
    //

    private static Func<DnsConfig, string> ManagedName(string prefix)
    {
        return record => record.Name != "" ? record.Name + "." + prefix : prefix;
    }

    private async Task WriteDnsRecords(string zoneId, List<DnsConfig> dnsConfig, Func<DnsConfig, string> nameOf)
    {
        // An MX rrset REPLACE swaps the whole set, so all of a name's MX values must go
        // in ONE call - group first; MX is skipped in the per-record dispatch below
        foreach (var mxGroup in dnsConfig.Where(x => x.Type == "MX").GroupBy(nameOf))
        {
            await _dnsRestClient.CreateMxRecords(zoneId, mxGroup.Key, mxGroup.Select(x => x.Value + "."));
        }

        foreach (var record in dnsConfig)
        {
            var name = nameOf(record);
            if (record.Type == "A")
            {
                await _dnsRestClient.CreateARecords(zoneId, name, new[] { record.Value });
            }
            else if (record.Type == "CNAME")
            {
                await _dnsRestClient.CreateCnameRecords(zoneId, name, record.Value + ".");
            }
            else if (record.Type == "TXT")
            {
                await _dnsRestClient.CreateTxtRecords(zoneId, name, new[] { record.Value });
            }
            else if (record.Type is "ALIAS" or "NS" or "MX")
            {
                // IGNORE - ALIAS is an instruction for third-party DNS hosts only (in our
                // own zones the apex A record is authoritative); NS entries describe
                // delegation of own-domains (created by CreateZone) and never apply to
                // managed domains; MX was grouped and written above
            }
            else
            {
                // Sanity
                throw new OdinSystemException($"Unsupported record: {record.Type}");
            }
        }
    }

    private async Task DeleteDnsRecords(string zoneId, List<DnsConfig> dnsConfig, Func<DnsConfig, string> nameOf)
    {
        foreach (var record in dnsConfig)
        {
            var name = nameOf(record);
            if (record.Type == "A")
            {
                await _dnsRestClient.DeleteARecords(zoneId, name);
            }
            else if (record.Type == "CNAME")
            {
                await _dnsRestClient.DeleteCnameRecords(zoneId, name);
            }
            else if (record.Type == "MX")
            {
                // Deletes the whole rrset; repeating per MX value is a harmless no-op
                await _dnsRestClient.DeleteMxRecords(zoneId, name);
            }
            else if (record.Type == "TXT")
            {
                await _dnsRestClient.DeleteTxtRecords(zoneId, name);
            }
            else if (record.Type is "ALIAS" or "NS")
            {
                // IGNORE - see WriteDnsRecords
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

    public async Task<bool> IsOwnDomainAvailable(AsciiDomainName domain)
    {
        // Managed apexes (and anything under them) are never "own domains": they are
        // provisioned through the managed-domain flow, and an own-domain zone for one
        // would shadow the apex zone we host
        if (IsManagedDomain(domain.DomainName))
        {
            return false;
        }

        // Identity already exists or domain path clash?
        return await _registry.CanAddNewRegistration(domain.DomainName);

        // SEB:NOTE below removed for now since it's taking too big a toll on the system when called for each key press
        // We can only create new domain if we can find a zone apex
        // var zoneApex = await _dnsLookupService.LookupZoneApex(domain);
        // return !string.IsNullOrEmpty(zoneApex);
    }

    //

    public Task<(bool, List<DnsConfig>)> GetAuthoritativeDomainDnsStatus(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        return _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(domain, cancellationToken);
    }

    //

    public Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatus(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        return _dnsLookupService.GetExternalDomainDnsStatusAsync(domain, cancellationToken);
    }

    //

    public async Task DeleteOwnDomain(AsciiDomainName domain)
    {
        await _registry.DeleteRegistration(domain.DomainName);
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
    public async Task<CreateOwnDomainZoneResult> CreateOwnDomainZone(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        var domainName = domain.DomainName;

        if (!CanHostOwnDomainZones)
        {
            _logger.LogDebug("Skipping zone creation for {domain}: PowerDNS or nameservers not configured", domain);
            return CreateOwnDomainZoneResult.NotConfigured;
        }

        if (IsManagedDomain(domainName))
        {
            throw new OdinSystemException($"{domainName} is a managed domain; it has no own zone");
        }

        var dns = _configuration.Registry.DnsConfigurationSet;
        var zoneId = domainName + ".";

        // Domain-control proof first: it is the cheap-to-fail gate on an anonymous endpoint
        var isRegistered = await _registry.GetAsync(domainName) != null;
        if (!isRegistered)
        {
            var delegatedToUs = await _dnsLookupService.IsDomainDelegatedToUsAsync(domain, cancellationToken);
            if (!delegatedToUs)
            {
                var (manualRecordsValid, _) =
                    await _dnsLookupService.GetAuthoritativeDomainDnsStatusAsync(domain, cancellationToken);
                if (!manualRecordsValid)
                {
                    _logger.LogInformation(
                        "Not creating zone {zone} yet: no registered identity, no delegation to us, no valid records",
                        zoneId);
                    return CreateOwnDomainZoneResult.ControlNotProven;
                }
            }
        }

        // Never create a zone inside a zone we already host - it would shadow the parent.
        // Checked via the domain's ancestor suffixes (a handful of ZoneExists lookups)
        // rather than listing every hosted zone.
        for (var ancestor = ParentDomain(domainName); ancestor != null && ancestor.Contains('.'); ancestor = ParentDomain(ancestor))
        {
            if (await _dnsRestClient.ZoneExists(ancestor + "."))
            {
                _logger.LogWarning("Refusing zone {zone}: it would shadow part of hosted zone {existing}",
                    zoneId, ancestor + ".");
                return CreateOwnDomainZoneResult.ShadowsHostedZone;
            }
        }

        if (!await _dnsRestClient.ZoneExists(zoneId))
        {
            _logger.LogInformation("Creating zone {zone}", zoneId);
            await _dnsRestClient.CreateZone(zoneId, dns.NameServers.Select(x => x + ".").ToArray(), dns.SoaAdminEmail);
        }
        else if (!isRegistered)
        {
            // The zone exists but this environment has no registration for the domain. On a
            // DNS server shared between environments this can be another environment's LIVE
            // zone (delegation proof is ambiguous: both environments answer to the same
            // nameserver names), and the REPLACE-populate below would hijack its records.
            // The zone is only ours if its contents already match this environment's values
            // (the normal signup flow: zone created on Validate, identity registered later) -
            // each environment writes its own distinct apex A record, so that is the
            // discriminator. Anything else is refused untouched.
            var zone = await _dnsRestClient.GetZone(zoneId);
            var apexARecords = (zone.rrsets ?? [])
                .Where(x => x.type == "A" && x.name == zoneId)
                .SelectMany(x => x.records)
                .Select(x => x.content)
                .ToList();
            if (!apexARecords.Contains(dns.ApexARecord))
            {
                _logger.LogWarning(
                    "Refusing zone {zone}: it already exists with foreign records (apex A [{a}], ours would be {ours}) " +
                    "and no local registration - it likely belongs to another environment",
                    zoneId, string.Join(',', apexARecords), dns.ApexARecord);
                return CreateOwnDomainZoneResult.ZoneAlreadyHosted;
            }
        }

        // Publish CDS/CDNSKEY (RFC 8078) so parents that scan for them install the DS
        // automatically; harmless elsewhere. Idempotent - re-running (incl. the CLI
        // backfill) converges, exactly like the record REPLACEs below.
        await _dnsRestClient.PublishCdsRecords(zoneId);

        // Populate (REPLACE semantics, so re-running converges on the correct records)
        var dnsConfig = _dnsLookupService.GetDnsConfiguration(domain);
        await WriteDnsRecords(zoneId, dnsConfig, record => record.Name);

        _logger.LogInformation("Created own-domain zone {zone}", zoneId);
        return CreateOwnDomainZoneResult.Created;
    }

    //

    public async Task<bool> OwnDomainZoneExists(AsciiDomainName domain)
    {
        if (!CanHostOwnDomainZones || IsManagedDomain(domain.DomainName))
        {
            return false;
        }
        return await _dnsRestClient.ZoneExists(domain.DomainName + ".");
    }

    //

    /// <summary>
    /// DNSSEC state of an own-domain's hosted zone. The zone-side facts (hosted, our DS
    /// records) come from PowerDNS - this runs on the provisioning host, which operates
    /// the DNS server. The parent-side facts (parent signed, published DS) are generic
    /// public-DNS lookups. Read-only: never creates or repairs anything.
    /// </summary>
    public async Task<DnssecStatusResult> GetDnssecStatusAsync(AsciiDomainName domain, CancellationToken cancellationToken = default)
    {
        if (!await OwnDomainZoneExists(domain))
        {
            return new DnssecStatusResult
            {
                Status = CanHostOwnDomainZones ? DnssecStatus.ZoneNotHosted : DnssecStatus.NotConfigured,
            };
        }

        var ourDsRecords = await _dnsRestClient.GetZoneDsRecords(domain.DomainName + ".");
        var parentZoneSigned = await _dnsLookupService.IsParentZoneSignedAsync(domain, cancellationToken);
        var parentDsRecords = await _dnsLookupService.GetParentDsRecordsAsync(domain, cancellationToken);

        return new DnssecStatusResult
        {
            Status = DnssecStatusResult.ComputeVerdict(ourDsRecords, parentDsRecords, parentZoneSigned),
            OurDsRecords = ourDsRecords,
            ParentDsRecords = parentDsRecords,
            ParentZoneSigned = parentZoneSigned,
        };
    }

    //

    private static string? ParentDomain(string domain)
    {
        var idx = domain.IndexOf('.');
        return idx < 0 ? null : domain[(idx + 1)..];
    }

    //

    /// <summary>
    /// Best-effort DNS cleanup when a tenant is deleted. Managed domains get their
    /// records removed from the shared apex zone; own domains get their zone deleted.
    /// Never throws: a DNS cleanup failure must not block account deletion.
    /// </summary>
    public async Task DeleteDnsRecordsForDomain(AsciiDomainName domain)
    {
        try
        {
            var domainName = domain.DomainName;

            var apex = FindManagedApex(domainName);
            if (apex == null)
            {
                await DeleteOwnDomainZone(domain);
                return;
            }

            // Managed domain: remove its records from the apex zone. Deliberately not
            // DeleteManagedDomain: that re-deletes the registration and asserts the
            // configured prefix label count, which may have changed since signup.
            var prefix = domainName[..^(apex.Length + 1)];
            var dnsConfig = _dnsLookupService.GetDnsConfiguration(domain);
            await DeleteDnsRecords(apex + ".", dnsConfig, ManagedName(prefix));

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
    public async Task DeleteOwnDomainZone(AsciiDomainName domain)
    {
        if (!CanHostOwnDomainZones || IsManagedDomain(domain.DomainName))
        {
            return;
        }

        var zoneId = domain.DomainName + ".";
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
        return FindManagedApex(domain) != null || _configuration.Registry.ManagedDomainApexes
            .Exists(x => domain.Equals(x.Apex, StringComparison.OrdinalIgnoreCase));
    }

    // Longest-suffix match so nested apexes (e.g. id.pub and dev.id.pub) resolve to the
    // most specific zone
    private string? FindManagedApex(string domain)
    {
        return _configuration.Registry.ManagedDomainApexes
            .Where(x => domain.EndsWith("." + x.Apex, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Apex.Length)
            .FirstOrDefault()?.Apex;
    }

    //

    public async Task<Guid> CreateIdentityOnDomainAsync(AsciiDomainName domain, string email, string planId, string invitationCode)
    {
        var identity = await _registry.GetAsync(domain.DomainName);
        if (identity != null)
        {
            throw new OdinSystemException($"Identity {domain} already exists");
        }

        var request = new IdentityRegistrationRequest()
        {
            Id = null, // Sanity
            OdinId = new OdinId(domain),
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
            if (!IsManagedDomain(domain.DomainName))
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
            if (_configuration.Email.IsProviderConfigured)
            {
                var job = _jobManager.NewJob<SendProvisioningCompleteEmailJob>();
                job.Data = new SendProvisioningCompleteEmailJobData
                {
                    Domain = domain.DomainName,
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
            await _registry.DeleteRegistration(domain.DomainName);
            // The zone may have been created above (or earlier during DNS validation);
            // without a registration nothing else reclaims it (there is no prune sweep)
            await DeleteOwnDomainZone(domain);
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
