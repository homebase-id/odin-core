using System;
using System.IO;
using System.Threading.Tasks;
using DnsClient;
using HttpClientFactoryLite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Core.X509;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;
using Odin.Test.Helpers.Logging;

namespace Odin.Services.Tests.Certificates;

public class CertificateServiceTests
{
    private readonly HttpClientFactory _httpClientFactory = new();
    private readonly LookupClient _lookupClient = new();
    private string _tempDir = null!;
    private string _tempSslDir = null!;
    private CertificateService _certificateService = null!;
    private WebApplication _webServer = null!;

    [SetUp]
    public async Task Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _tempSslDir = Path.Combine(_tempDir, "ssl");
        Directory.CreateDirectory(_tempSslDir);

        //
        // Certificate service setup
        //

        var config = new OdinConfiguration
        {
            CertificateRenewal = new OdinConfiguration.CertificateRenewalSection
            {
                CertificateAuthorityAssociatedEmail = "certtest@homebase.id",

            },
            Registry = new OdinConfiguration.RegistrySection
            {
                DnsConfigurationSet = new DnsConfigurationSet("131.164.170.62", "regression-test-apex-a.dominion.id"),
            }
        };

        var authorativeDnsLookup = new AuthoritativeDnsLookup(
            TestLogFactory.CreateConsoleLogger<AuthoritativeDnsLookup>(),
            new LookupClient());

        var dnsLookupService = new DnsLookupService(
            TestLogFactory.CreateConsoleLogger<DnsLookupService>(),
             config,
             _lookupClient,
             authorativeDnsLookup);

        var acmeAccountConfig = new AcmeAccountConfig
        {
            AcmeContactEmail = config.CertificateRenewal.CertificateAuthorityAssociatedEmail,
            AcmeAccountFolder = _tempDir
        };

        var acmeHttp01TokenCache = new AcmeHttp01TokenCache();
        var certesAcme = new CertesAcme(
            TestLogFactory.CreateConsoleLogger<CertesAcme>(),
            acmeHttp01TokenCache,
            _httpClientFactory,
            isProduction: false);

        var certificateCache = new CertificateCache();

        var certificateServiceFactory = new CertificateServiceFactory(
            TestLogFactory.CreateConsoleLogger<CertificateService>(),
            certificateCache,
            certesAcme,
            dnsLookupService,
            acmeAccountConfig);

        var sslRootPath = Path.Combine(_tempDir, "ssl");
        _certificateService = certificateServiceFactory.Create(sslRootPath);

        //
        // Web server setup
        //

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Information);
        builder.WebHost.UseUrls("http://0.0.0.0:80");
        builder.Services.AddSingleton<IAcmeHttp01TokenCache>(acmeHttp01TokenCache);
        _webServer = builder.Build();
        _webServer.UseMiddleware<CertesAcmeMiddleware>();
        var lifetime = _webServer.Services.GetRequiredService<IHostApplicationLifetime>();
        var serverStartedTcs = new TaskCompletionSource();
        lifetime.ApplicationStarted.Register(() => serverStartedTcs.SetResult());
        await _webServer.StartAsync();
        await serverStartedTcs.Task;
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        await _webServer.StopAsync();
        Directory.Delete(_tempDir, true);
    }

    //

    [Test, Explicit]
    [TestCase("regression-test-apex-a.dominion.id")]
    [TestCase("regression-test-apex-alias.dominion.id")]
    public async Task CreateCertificateAsync_ShouldCreateCertificate_WithoutSans(string domain)
    {
        var certificate = await _certificateService.CreateCertificateAsync(domain)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.That(certificate, Is.Not.Null);
        Assert.That(certificate.Subject, Is.EqualTo($"CN={domain}"));

        var sanList = certificate.GetSubjectAlternativeNames();
        Assert.That(sanList, Contains.Item(domain));
        Assert.That(sanList.Count, Is.EqualTo(1));
    }

    //

    [Test, Explicit]
    [TestCase("regression-test-apex-a.dominion.id")]
    [TestCase("regression-test-apex-alias.dominion.id")]
    public async Task CreateCertificateAsync_ShouldCreateCertificate_WithSans(string domain)
    {
        var sans = new[] { $"capi.{domain}", $"file.{domain}" };
        var certificate = await _certificateService.CreateCertificateAsync(domain, sans)
            .WaitAsync(TimeSpan.FromSeconds(60));

        Assert.That(certificate, Is.Not.Null);
        Assert.That(certificate.Subject, Is.EqualTo($"CN={domain}"));

        var sanList = certificate.GetSubjectAlternativeNames();
        Assert.That(sanList, Contains.Item(domain));
        Assert.That(sanList, Contains.Item($"capi.{domain}"));
        Assert.That(sanList, Contains.Item($"file.{domain}"));
        Assert.That(sanList.Count, Is.EqualTo(3));
    }


}