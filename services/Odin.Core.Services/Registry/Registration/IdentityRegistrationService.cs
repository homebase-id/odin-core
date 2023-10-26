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
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Dns;
using Odin.Core.Services.Email;
using Odin.Core.Util;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Odin.Core.Services.Registry.Registration;

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
    private readonly IEmailSender _emailSender;
    private readonly IDnsLookupService _dnsLookupService;

    public IdentityRegistrationService(
        ILogger<IdentityRegistrationService> logger,
        IIdentityRegistry registry,
        OdinConfiguration configuration,
        IDnsRestClient dnsRestClient,
        IHttpClientFactory httpClientFactory,
        IEmailSender emailSender,
        IDnsLookupService dnsLookupService)
    {
        _logger = logger;
        _configuration = configuration;
        _registry = registry;
        _dnsRestClient = dnsRestClient;
        _httpClientFactory = httpClientFactory;
        _emailSender = emailSender;
        _dnsLookupService = dnsLookupService;

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
            await httpClient.GetAsync($"https://{domain}");
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
        AsciiDomainNameValidator.AssertValidDomain(domain);

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
        AsciiDomainNameValidator.AssertValidDomain(domain);

        // Identity already exists or domain path clash?
        if (false == await _registry.CanAddNewRegistration(domain))
        {
            return false;
        }

        // We can only create new domain if we can find a zone apex
        var zoneApex = await _dnsLookupService.LookupZoneApex(domain);
        return !string.IsNullOrEmpty(zoneApex);
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

            if (_configuration.Mailgun.Enabled)
            {
                // SEB:TODO we should probably queue this on a Quartz worker instead
                // and only send it once we're sure the certificate has been created
                await SendProvisioningCompleteEmail(domain, email, firstRunToken.ToString());
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
        if (!_configuration.Registry.InvitationCodes.Any())
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

    private async Task SendProvisioningCompleteEmail(string domain, string email, string firstRunToken)
    {
        const string subject = "Your new identity is ready";
        var firstRunlink = $"https://{domain}/owner/firstrun?frt={firstRunToken}";

        var envelope = new Envelope
        {
            To = new List<NameAndEmailAddress> { new() { Email = email } },
            Subject = subject,
            TextMessage = RegistrationEmails.ProvisioningCompletedText(email, domain, firstRunlink),
            HtmlMessage = RegistrationEmails.ProvisioningCompletedHtml(email, domain, firstRunlink),
        };

        await _emailSender.SendAsync(envelope);
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

