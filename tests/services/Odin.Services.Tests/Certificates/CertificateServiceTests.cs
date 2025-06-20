using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using DnsClient;
using HttpClientFactoryLite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Dns;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;
using Odin.Core.X509;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;
using Testcontainers.PostgreSql;

namespace Odin.Services.Tests.Certificates;

public class CertificateServiceTests
{
    private string _tempDir = null!;
    private PostgreSqlContainer? _postgresContainer;
    private WebApplication _webServer = null!;
    private ICertificateService _certificateService = null!;
    private ILifetimeScope _autofacContainer = null!;

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
        await _webServer.DisposeAsync();

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

        builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
        builder.Host.ConfigureContainer<ContainerBuilder>(cb =>
        {
            cb.RegisterInstance<OdinConfiguration>(config);
            cb.RegisterInstance<IHttpClientFactory>(new HttpClientFactory()); // this is HttpClientFactoryLite
            cb.RegisterInstance<ILookupClient>(new LookupClient());
            cb.RegisterType<AuthoritativeDnsLookup>().As<IAuthoritativeDnsLookup>().SingleInstance();
            cb.RegisterType<DnsLookupService>().As<IDnsLookupService>().SingleInstance();
            cb.RegisterType<AcmeHttp01TokenCache>().As<IAcmeHttp01TokenCache>().SingleInstance();
            cb.RegisterInstance<AcmeAccountConfig>(new AcmeAccountConfig
            {
                AcmeContactEmail = config.CertificateRenewal.CertificateAuthorityAssociatedEmail,
            });
            cb.Register(c =>
                new CertesAcme(
                    c.Resolve<ILogger<CertesAcme>>(), c.Resolve<IAcmeHttp01TokenCache>(),
                    c.Resolve<IHttpClientFactory>(),
                    isProduction: false))
                .As<ICertesAcme>().SingleInstance();
            cb.RegisterType<CertificateStore>().As<ICertificateStore>().SingleInstance();
            cb.RegisterType<CertificateService>().As<ICertificateService>().SingleInstance();

            cb.AddDatabaseCacheServices();
            cb.AddDatabaseCounterServices();

            switch (databaseType)
            {
                case DatabaseType.Sqlite:
                    cb.AddSqliteSystemDatabaseServices(Path.Combine(_tempDir, "system-test.db"));
                    break;
                case DatabaseType.Postgres:
                    cb.AddPgsqlSystemDatabaseServices(_postgresContainer!.GetConnectionString());
                    break;
                default:
                    throw new Exception("Unsupported database type");
            }
        });

        //
        // Start web server
        //

        builder.Logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.WebHost.UseUrls("http://0.0.0.0:80");
        _webServer = builder.Build();
        _webServer.UseMiddleware<CertesAcmeMiddleware>();

        _autofacContainer = _webServer.Services.GetAutofacRoot();
        var systemDatabase = _autofacContainer.Resolve<SystemDatabase>();
        await systemDatabase.CreateDatabaseAsync(true);

        var lifetime = _autofacContainer.Resolve<IHostApplicationLifetime>();
        var serverStartedTcs = new TaskCompletionSource();
        lifetime.ApplicationStarted.Register(() => serverStartedTcs.SetResult());
        await _webServer.StartAsync();
        await serverStartedTcs.Task;

        _certificateService = _autofacContainer.Resolve<ICertificateService>();
    }

    //

    // NOTE: for these tests to work:
    // - The DNS records must be set up correctly in the DNS server
    // - Port 80 must be open and accessible for HTTP-01 challenges
    [Test, Explicit]
    [TestCase(DatabaseType.Sqlite, "regression-test-apex-a.dominion.id")]
    [TestCase(DatabaseType.Sqlite, "regression-test-apex-alias.dominion.id")]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, "regression-test-apex-a.dominion.id")]
    [TestCase(DatabaseType.Postgres, "regression-test-apex-alias.dominion.id")]
#endif
    public async Task CreateCertificateAsync_ShouldCreateCertificate_WithoutSans(DatabaseType databaseType, string domain)
    {
        await StartWebServerAsync(databaseType);

        var certificate = await _certificateService.CreateCertificateAsync(domain)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.That(certificate, Is.Not.Null);
        Assert.That(certificate!.Subject, Is.EqualTo($"CN={domain}"));

        var sanList = certificate.GetSubjectAlternativeNames();
        Assert.That(sanList, Contains.Item(domain));
        Assert.That(sanList.Count, Is.EqualTo(1));
    }

    //

    // NOTE: for these tests to work:
    // - The DNS records must be set up correctly in the DNS server
    // - Port 80 must be open and accessible for HTTP-01 challenges
    [Test, Explicit]
    [TestCase(DatabaseType.Sqlite, "regression-test-apex-a.dominion.id")]
    [TestCase(DatabaseType.Sqlite, "regression-test-apex-alias.dominion.id")]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, "regression-test-apex-a.dominion.id")]
    [TestCase(DatabaseType.Postgres, "regression-test-apex-alias.dominion.id")]
#endif
    public async Task CreateCertificateAsync_ShouldCreateCertificate_WithSans(DatabaseType databaseType, string domain)
    {
        await StartWebServerAsync(databaseType);

        var sans = new[] { $"capi.{domain}", $"file.{domain}" };
        var certificate = await _certificateService.CreateCertificateAsync(domain, sans)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.That(certificate, Is.Not.Null);
        Assert.That(certificate!.Subject, Is.EqualTo($"CN={domain}"));

        var sanList = certificate.GetSubjectAlternativeNames();
        Assert.That(sanList, Contains.Item(domain));
        Assert.That(sanList, Contains.Item($"capi.{domain}"));
        Assert.That(sanList, Contains.Item($"file.{domain}"));
        Assert.That(sanList.Count, Is.EqualTo(3));
    }

    // NOTE: for these tests to work:
    // - The DNS records must be set up correctly in the DNS server
    // - Port 80 must be open and accessible for HTTP-01 challenges
    [Test, Explicit]
    [TestCase(DatabaseType.Sqlite, "regression-test-apex-a.dominion.id")]
    [TestCase(DatabaseType.Sqlite, "regression-test-apex-alias.dominion.id")]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, "regression-test-apex-a.dominion.id")]
    [TestCase(DatabaseType.Postgres, "regression-test-apex-alias.dominion.id")]
#endif
    public async Task Renew_ShouldRenewCertAboutToExpire(DatabaseType databaseType, string domain)
    {
        await StartWebServerAsync(databaseType);

        var oldX509 = X509Extensions.CreateSelfSignedEcDsaCertificate(
            domain,
            DateTimeOffset.Now - TimeSpan.FromDays(1),
            DateTimeOffset.Now + TimeSpan.FromDays(1));
        var (pemKey, pemCertificate) = oldX509.ExtractEcDsaPemData();

        await _certificateService.PutCertificateAsync(
            domain,
            new KeysAndCertificates
            {
                PrivateKeyPem = pemKey, CertificatesPem = pemCertificate
            });

        var oldCertificate = await _certificateService.GetCertificateAsync(domain);
        Assert.That(oldCertificate, Is.Not.Null);
        Assert.That(oldCertificate!.Subject, Is.EqualTo($"CN={domain}"));

        var sans = new[] { $"capi.{domain}", $"file.{domain}" };
        var result = await _certificateService.RenewIfAboutToExpireAsync(domain, sans);
            // .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.That(result, Is.True);

        var newCertificate = await _certificateService.GetCertificateAsync(domain);
        Assert.That(newCertificate, Is.Not.Null);
        Assert.That(newCertificate!.Subject, Is.EqualTo($"CN={domain}"));
        Assert.That(newCertificate.NotAfter, Is.GreaterThanOrEqualTo(DateTime.Now + TimeSpan.FromDays(30)));
    }

    //
}








