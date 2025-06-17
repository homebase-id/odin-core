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
using Odin.Core.Storage.Factory;
using Odin.Core.X509;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;
using Odin.Test.Helpers.Logging;
using Testcontainers.PostgreSql;

namespace Odin.Services.Tests.Certificates;

public class CertificateServiceTests
{
    private string _tempDir = null!;
    private PostgreSqlContainer? _postgresContainer;
    private WebApplication _webServer = null!;
    private CertificateService _certificateService = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        await _webServer.StopAsync();

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
            _postgresContainer = null;
        }

        Directory.Delete(_tempDir, true);
    }

    //

    private async Task StartWebServerAsync(DatabaseType databaseType)
    {
        // SEB:TODO delete this when we switch to db
        var sslRootPath = Path.Combine(_tempDir, "ssl");
        Directory.CreateDirectory(sslRootPath);

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

        if (databaseType == DatabaseType.Postgres)
        {
            _postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:latest")
                .WithDatabase("odin")
                .WithUsername("odin")
                .WithPassword("odin")
                .Build();
            await _postgresContainer.StartAsync();
        }

        var builder = WebApplication.CreateBuilder();

        //
        // Register services
        //

        builder.Services.AddSingleton<OdinConfiguration>(config);
        builder.Services.AddSingleton<IHttpClientFactory>(new HttpClientFactory()); // this is HttpClientFactoryLite
        builder.Services.AddSingleton<ILookupClient>(new LookupClient());
        builder.Services.AddSingleton<IAuthoritativeDnsLookup, AuthoritativeDnsLookup>();
        builder.Services.AddSingleton<IDnsLookupService, DnsLookupService>();
        builder.Services.AddSingleton<IAcmeHttp01TokenCache, AcmeHttp01TokenCache>();
        builder.Services.AddSingleton(new AcmeAccountConfig
        {
            AcmeContactEmail = config.CertificateRenewal.CertificateAuthorityAssociatedEmail,
            AcmeAccountFolder = sslRootPath
        });
        builder.Services.AddSingleton<ICertesAcme>(sp => new CertesAcme(
            sp.GetRequiredService<ILogger<CertesAcme>>(),
            sp.GetRequiredService<IAcmeHttp01TokenCache>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            isProduction: false));
        builder.Services.AddSingleton<ICertificateCache, CertificateCache>();
        builder.Services.AddSingleton<ICertificateServiceFactory, CertificateServiceFactory>();

        //
        // Start web server
        //

        builder.Logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.WebHost.UseUrls("http://0.0.0.0:80");
        _webServer = builder.Build();
        _webServer.UseMiddleware<CertesAcmeMiddleware>();
        var lifetime = _webServer.Services.GetRequiredService<IHostApplicationLifetime>();
        var serverStartedTcs = new TaskCompletionSource();
        lifetime.ApplicationStarted.Register(() => serverStartedTcs.SetResult());
        await _webServer.StartAsync();
        await serverStartedTcs.Task;

        var certificateServiceFactory = _webServer.Services.GetRequiredService<ICertificateServiceFactory>();
        _certificateService = certificateServiceFactory.Create(sslRootPath);
    }

    //

    // NOTE: for these tests to work:
    // - The DNS records must be set up correctly in the DNS server
    // - Port 80 must be open and accessible for HTTP-01 challenges

    [Test, Explicit]
    [TestCase("regression-test-apex-a.dominion.id")]
    [TestCase("regression-test-apex-alias.dominion.id")]
    public async Task CreateCertificateAsync_ShouldCreateCertificate_WithoutSans(string domain)
    {
        await StartWebServerAsync(DatabaseType.Sqlite);

        var certificate = await _certificateService.CreateCertificateAsync(domain)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.That(certificate, Is.Not.Null);
        Assert.That(certificate.Subject, Is.EqualTo($"CN={domain}"));

        var sanList = certificate.GetSubjectAlternativeNames();
        Assert.That(sanList, Contains.Item(domain));
        Assert.That(sanList.Count, Is.EqualTo(1));
    }

    //

    // NOTE: for these tests to work:
    // - The DNS records must be set up correctly in the DNS server
    // - Port 80 must be open and accessible for HTTP-01 challenges

    // [Test, Explicit]
    [TestCase("regression-test-apex-a.dominion.id")]
    [TestCase("regression-test-apex-alias.dominion.id")]
    public async Task CreateCertificateAsync_ShouldCreateCertificate_WithSans(string domain)
    {
        await StartWebServerAsync(DatabaseType.Sqlite);

        var sans = new[] { $"capi.{domain}", $"file.{domain}" };
        var certificate = await _certificateService.CreateCertificateAsync(domain, sans)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.That(certificate, Is.Not.Null);
        Assert.That(certificate.Subject, Is.EqualTo($"CN={domain}"));

        var sanList = certificate.GetSubjectAlternativeNames();
        Assert.That(sanList, Contains.Item(domain));
        Assert.That(sanList, Contains.Item($"capi.{domain}"));
        Assert.That(sanList, Contains.Item($"file.{domain}"));
        Assert.That(sanList.Count, Is.EqualTo(3));
    }
}