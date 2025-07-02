using System;
using System.Collections.Generic;
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
            else if (record.Type == "ALIAS")
            {
                // IGNORE
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
            else if (record.Type == "ALIAS")
            {
                // IGNORE
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
    }

    //

    public async Task<Guid> CreateIdentityOnDomainAsync(string domain, string email, string planId)
    {
        var identity = await _registry.GetAsync(domain);
        if (identity != null)
        {
            throw new OdinSystemException($"Identity {domain} already exists");
        }

        var request = new IdentityRegistrationRequest()
        {
            OdinId = (OdinId)domain,
            Email = email,
            PlanId = planId,
            IsCertificateManaged = false, //TODO
        };

        try
        {
            var firstRunToken = await _registry.AddRegistration(request);

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
        return Task.FromResult(_configuration.Registry.InvitationCodes.Count > 0);
    }
    
    //

    public Task<bool> IsValidInvitationCode(string code)
    {
        if (_configuration.Registry.InvitationCodes.Count == 0)
        {
            return Task.FromResult(true);
        }

        if (string.IsNullOrEmpty(code))
        {
            return Task.FromResult(false);
        }

        var match = _configuration.Registry.InvitationCodes
            .Exists(c => string.Equals(c, code, StringComparison.InvariantCultureIgnoreCase));
        return Task.FromResult(match);
    }
}
