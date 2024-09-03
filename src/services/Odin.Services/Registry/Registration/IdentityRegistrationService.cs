using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Dns;
using Odin.Services.JobManagement;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDnsLookupService _dnsLookupService;
    private readonly IJobManager _jobManager;

    public IdentityRegistrationService(
        ILogger<IdentityRegistrationService> logger,
        IIdentityRegistry registry,
        OdinConfiguration configuration,
        IDnsRestClient dnsRestClient,
        IHttpClientFactory httpClientFactory,
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

        RegisterHttpClient();
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
        var httpClient = _httpClientFactory.CreateClient<IdentityRegistrationService>();
        try
        {
            await httpClient.GetAsync($"https://{domain}:{_configuration.Host.DefaultHttpsPort}");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    //

    public Task<string> LookupZoneApex(string domain)
    {
        return _dnsLookupService.LookupZoneApex(domain);
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

    public async Task<bool> IsManagedDomainAvailable(string prefix, string apex)
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

        return await _dnsLookupService.IsManagedDomainAvailable(prefix, apex);
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

    public Task<(bool, List<DnsConfig>)> GetAuthorativeDomainDnsStatus(string domain)
    {
        return _dnsLookupService.GetAuthorativeDomainDnsStatus(domain);
    }

    //

    public Task<(bool, List<DnsConfig>)> GetExternalDomainDnsStatus(string domain)
    {
        return _dnsLookupService.GetExternalDomainDnsStatus(domain);
    }

    //

    public async Task DeleteOwnDomain(string domain)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);
        await _registry.DeleteRegistration(domain);
    }

    //

    public async Task<Guid> CreateIdentityOnDomain(string domain, string email, string planId)
    {
        var identity = await _registry.Get(domain);
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
                    ProvisioningEmailLogoImage = _configuration.Registry.ProvisioningEmailLogoImage,
                    ProvisioningEmailLogoHref = _configuration.Registry.ProvisioningEmailLogoHref            
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

    //

    private void RegisterHttpClient()
    {
        _httpClientFactory.Register<IdentityRegistrationService>(builder => builder
            .ConfigureHttpClient(c =>
            {
                // this is called everytime you request a httpclient
                c.Timeout = TimeSpan.FromSeconds(3);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                // this is called whenever you request a httpclient and handler lifetime has expired
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false, // DO NOT CHANGE!
                };

                // Make sure we accept certifactes from letsencrypt staging servers if not in production
                if (!_configuration.CertificateRenewal.UseCertificateAuthorityProductionServers)
                {
                    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                }

                return handler;
            })
            .SetHandlerLifetime(TimeSpan.FromSeconds(5))); // Shortlived to deal with DNS changes
    }
}

