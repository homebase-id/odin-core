using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;
using Odin.Core.X509;
using Odin.Services.Certificate;
using Serilog.Events;
using Testcontainers.PostgreSql;

namespace Odin.Services.Tests.Certificates;

public class CertificateStoreTests
{
    private string _tempDir = null!;
    private PostgreSqlContainer? _postgresContainer;
    private ICertificateStore _certificateStore = null!;
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
        _autofacContainer.Dispose();

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
            _postgresContainer = null;
        }

        Directory.Delete(_tempDir, true);
    }

    //

    private async Task RegisterServicesAsync(
        DatabaseType databaseType,
        LogEventLevel logEventLevel = LogEventLevel.Debug)
    {
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

        var services = new ServiceCollection(); // we need this to make IServiceProvider available through Autofac
        services.AddLogging();

        var cb = new ContainerBuilder();
        cb.Populate(services);

        // Register IServiceProvider as the root container (LifetimeScope).
        cb.Register(ctx => (IServiceProvider)ctx.Resolve<ILifetimeScope>()).As<IServiceProvider>();

        cb.RegisterType<CertificateStore>().As<ICertificateStore>().SingleInstance();
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

        _autofacContainer = cb.Build();

        var systemDatabase = _autofacContainer.Resolve<SystemDatabase>();
        await systemDatabase.CreateDatabaseAsync(true);

        _certificateStore = _autofacContainer.Resolve<ICertificateStore>();
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task GetCertificateAsync_ShouldReturnNull_WhenNoCertificateExists(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        var certificate = await _certificateStore.GetCertificateAsync("frodo.dotyou.cloud");
        Assert.That(certificate, Is.Null);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task PutCertificateAsync_ShouldReturnCertificate(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        var x509 = X509Extensions.CreateSelfSignedEcDsaCertificate("frodo.dotyou.cloud");
        var (key, certificate) = x509.ExtractEcDsaPemData();

        var savedCertificate = await _certificateStore.PutCertificateAsync("frodo.dotyou.cloud", key, certificate);
        Assert.That(savedCertificate, Is.Not.Null);
        Assert.That(savedCertificate.Subject, Is.EqualTo("CN=frodo.dotyou.cloud"));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task GetAfterPutCertificateAsync_ShouldReturnCertificate(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        var certificate = await _certificateStore.GetCertificateAsync("frodo.dotyou.cloud");
        Assert.That(certificate, Is.Null);

        var x509 = X509Extensions.CreateSelfSignedEcDsaCertificate("frodo.dotyou.cloud");
        var (pemKey, pemCertificate) = x509.ExtractEcDsaPemData();

        var savedCertificate = await _certificateStore.PutCertificateAsync("frodo.dotyou.cloud", pemKey, pemCertificate);
        Assert.That(savedCertificate, Is.Not.Null);
        Assert.That(savedCertificate.Subject, Is.EqualTo("CN=frodo.dotyou.cloud"));

        certificate = await _certificateStore.GetCertificateAsync("frodo.dotyou.cloud");
        Assert.That(certificate, Is.Not.Null);
        Assert.That(certificate!.Subject, Is.EqualTo("CN=frodo.dotyou.cloud"));
        Assert.That(certificate, Is.SameAs(savedCertificate));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task PutAndGetInvalidCert_ShouldReturnNull_ThenUpdateWithValid(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        {
            var x509 = X509Extensions.CreateSelfSignedEcDsaCertificate(
                "frodo.dotyou.cloud",
                DateTimeOffset.Now - TimeSpan.FromDays(2),
                DateTimeOffset.Now - TimeSpan.FromDays(1));
            var (pemKey, pemCertificate) = x509.ExtractEcDsaPemData();

            var exception = Assert.ThrowsAsync<OdinSystemException>(() => _certificateStore.PutCertificateAsync("frodo.dotyou.cloud", pemKey, pemCertificate));
            Assert.That(exception!.Message, Is.EqualTo($"Certificate for frodo.dotyou.cloud is not valid. Did it expire?"));

            var certificate = await _certificateStore.GetCertificateAsync("frodo.dotyou.cloud");
            Assert.That(certificate, Is.Null);
        }

        {
            var x509 = X509Extensions.CreateSelfSignedEcDsaCertificate("frodo.dotyou.cloud");
            var (pemKey, pemCertificate) = x509.ExtractEcDsaPemData();

            var savedCertificate = await _certificateStore.PutCertificateAsync("frodo.dotyou.cloud", pemKey, pemCertificate);
            Assert.That(savedCertificate, Is.Not.Null);
            Assert.That(savedCertificate.Subject, Is.EqualTo("CN=frodo.dotyou.cloud"));

            var certificate = await _certificateStore.GetCertificateAsync("frodo.dotyou.cloud");
            Assert.That(certificate, Is.Not.Null);
            Assert.That(certificate!.Subject, Is.EqualTo("CN=frodo.dotyou.cloud"));
            Assert.That(certificate, Is.SameAs(savedCertificate));
        }
    }

    //

}