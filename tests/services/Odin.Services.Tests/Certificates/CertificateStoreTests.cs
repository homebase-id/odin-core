using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.PubSub;
using Odin.Core.Time;
using Odin.Core.X509;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
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
        _autofacContainer?.Dispose(); // null when a test needs no database

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
            _postgresContainer = null;
        }

        Directory.Delete(_tempDir, true);
    }

    //

    private async Task RegisterServicesAsync(DatabaseType databaseType)
    {
        var config = new OdinConfiguration
        {
            CertificateRenewal = new OdinConfiguration.CertificateRenewalSection
            {
                StorageKey = Convert.FromHexString("DECAFBADDECAFBADDECAFBADDECAFBADDECAFBADDECAFBADDECAFBADDECAFBAD")
            },
        };

        if (databaseType == DatabaseType.Postgres)
        {
            _postgresContainer = new PostgreSqlBuilder("postgres:latest")
                .WithDatabase("odin")
                .WithUsername("odin")
                .WithPassword("odin")
                .Build();
            await _postgresContainer.StartAsync();
        }

        var services = new ServiceCollection(); // we need this to make IServiceProvider available through Autofac
        services.AddLogging();
        services.AddSingleton<INodeLock, NodeLock>();

        var cb = new ContainerBuilder();
        cb.Populate(services);

        // Register IServiceProvider as the root container (LifetimeScope).
        cb.Register(ctx => (IServiceProvider)ctx.Resolve<ILifetimeScope>()).As<IServiceProvider>();

        cb.RegisterType<CertificateStore>().As<ICertificateStore>().SingleInstance();
        cb.RegisterInstance(new CertificateStorageKey(config.CertificateRenewal.StorageKey)).SingleInstance();
        cb.AddDatabaseServices();
        cb.AddSystemPubSub(redisEnabled: false);
        cb.RegisterModule(new LoggingAutofacModule());


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
        await systemDatabase.MigrateDatabaseAsync();

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

        var certificates = _autofacContainer.Resolve<TableCertificates>();
        var record = await certificates.GetAsync(new OdinId("frodo.dotyou.cloud"));

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.domain.DomainName, Is.EqualTo("frodo.dotyou.cloud"));
        Assert.That(record.privateKey, Is.Not.Empty);
        Assert.That(record.privateKey, Is.Not.EqualTo("error")); // privateKey is encrypted
        Assert.That(record.certificate, Is.EqualTo(certificate));
        Assert.That(record.expiration.milliseconds, Is.EqualTo(UnixTimeUtc.FromDateTime(x509.NotAfter).milliseconds));
        Assert.That(record.lastAttempt.milliseconds, Is.GreaterThan(0));
        Assert.That(record.correlationId, Is.Not.Null.And.Not.Empty);
        Assert.That(record.lastError, Is.Null);
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

        _certificateStore.ClearCache();

        certificate = await _certificateStore.GetCertificateAsync("frodo.dotyou.cloud");
        Assert.That(certificate, Is.Not.Null);
        Assert.That(certificate!.Subject, Is.EqualTo("CN=frodo.dotyou.cloud"));
        Assert.That(certificate, Is.Not.SameAs(savedCertificate));
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

        _certificateStore.ClearCache();

        {
            var certificate = await _certificateStore.GetCertificateAsync("frodo.dotyou.cloud");
            Assert.That(certificate, Is.Not.Null);
            Assert.That(certificate!.Subject, Is.EqualTo("CN=frodo.dotyou.cloud"));
        }
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task StoreFailedCertificate_ShouldInsertNewRecord(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await _certificateStore.StoreFailedCertificateUpdateAsync("frodo.dotyou.cloud", "Test error message");

        var certificates = _autofacContainer.Resolve<TableCertificates>();
        var record = await certificates.GetAsync(new OdinId("frodo.dotyou.cloud"));

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.domain.DomainName, Is.EqualTo("frodo.dotyou.cloud"));
        Assert.That(record.privateKey, Is.EqualTo(""));
        Assert.That(record.certificate, Is.EqualTo(""));
        Assert.That(record.expiration.milliseconds, Is.EqualTo(0));
        Assert.That(record.lastAttempt.milliseconds, Is.GreaterThan(0));
        Assert.That(record.correlationId, Is.Not.Null.And.Not.Empty);
        Assert.That(record.lastError, Is.EqualTo("Test error message"));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task StoreFailedCertificate_ShouldUpdateExistingNewRecord(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        {
            var x509 = X509Extensions.CreateSelfSignedEcDsaCertificate("frodo.dotyou.cloud");
            var (pemKey, pemCertificate) = x509.ExtractEcDsaPemData();
            await _certificateStore.PutCertificateAsync("frodo.dotyou.cloud", pemKey, pemCertificate);

            await _certificateStore.StoreFailedCertificateUpdateAsync("frodo.dotyou.cloud", "Test error message");

            var certificates = _autofacContainer.Resolve<TableCertificates>();
            var record = await certificates.GetAsync(new OdinId("frodo.dotyou.cloud"));

            Assert.That(record, Is.Not.Null);
            Assert.That(record!.domain.DomainName, Is.EqualTo("frodo.dotyou.cloud"));
            Assert.That(record.privateKey, Is.Not.Empty); // privateKey is encrypted
            Assert.That(record.certificate, Is.EqualTo(pemCertificate));
            Assert.That(record.expiration.milliseconds, Is.EqualTo(UnixTimeUtc.FromDateTime(x509.NotAfter).milliseconds));
            Assert.That(record.lastAttempt.milliseconds, Is.GreaterThan(0));
            Assert.That(record.correlationId, Is.Not.Null.And.Not.Empty);
            Assert.That(record.lastError, Is.EqualTo("Test error message"));
        }

        {
            var x509 = X509Extensions.CreateSelfSignedEcDsaCertificate("frodo.dotyou.cloud");
            var (pemKey, pemCertificate) = x509.ExtractEcDsaPemData();
            await _certificateStore.PutCertificateAsync("frodo.dotyou.cloud", pemKey, pemCertificate);

            var certificates = _autofacContainer.Resolve<TableCertificates>();
            var record = await certificates.GetAsync(new OdinId("frodo.dotyou.cloud"));

            Assert.That(record, Is.Not.Null);
            Assert.That(record!.domain.DomainName, Is.EqualTo("frodo.dotyou.cloud"));
            Assert.That(record.privateKey, Is.Not.Empty); // privateKey is encrypted
            Assert.That(record.certificate, Is.EqualTo(pemCertificate));
            Assert.That(record.expiration.milliseconds, Is.EqualTo(UnixTimeUtc.FromDateTime(x509.NotAfter).milliseconds));
            Assert.That(record.lastAttempt.milliseconds, Is.GreaterThan(0));
            Assert.That(record.correlationId, Is.Not.Null.And.Not.Empty);
            Assert.That(record.lastError, Is.Null);
        }
    }

    //

    // A second node: its own CertificateStore, and so its own cache, over the same database and
    // the same pub/sub as _certificateStore.
    private ICertificateStore CreateSecondNode()
    {
        return new CertificateStore(
            _autofacContainer.Resolve<IServiceProvider>(),
            _autofacContainer.Resolve<CertificateStorageKey>());
    }

    //

    private const string Domain = "frodo.dotyou.cloud";

    private static async Task<string> PutNewCertificateAsync(ICertificateStore node)
    {
        var x509 = X509Extensions.CreateSelfSignedEcDsaCertificate(Domain);
        var (pemKey, pemCertificate) = x509.ExtractEcDsaPemData();
        return (await node.PutCertificateAsync(Domain, pemKey, pemCertificate)).Thumbprint;
    }

    //

    // Node B has cached the first certificate; node A then replaces it with the second.
    private async Task<(ICertificateStore nodeA, ICertificateStore nodeB, string first, string second)>
        ArrangeNodeBHoldingReplacedCertificateAsync(DatabaseType databaseType, bool subscribe)
    {
        await RegisterServicesAsync(databaseType);

        var nodeA = _certificateStore;
        var nodeB = CreateSecondNode();
        if (subscribe)
        {
            await nodeA.SubscribeToCertificateChangesAsync();
            await nodeB.SubscribeToCertificateChangesAsync();
        }

        var first = await PutNewCertificateAsync(nodeA);
        Assert.That((await nodeB.GetCertificateAsync(Domain))?.Thumbprint, Is.EqualTo(first),
            "Node B should have loaded (and cached) the first certificate from the database");

        var second = await PutNewCertificateAsync(nodeA);
        Assert.That(second, Is.Not.EqualTo(first));

        return (nodeA, nodeB, first, second);
    }

    //

    // #1747: without the announcement, node B serves what it has in memory until that expires.
    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task PutCertificateAsync_RefreshesTheCertificateCachedOnOtherNodes(DatabaseType databaseType)
    {
        var (nodeA, nodeB, first, second) = await ArrangeNodeBHoldingReplacedCertificateAsync(databaseType, subscribe: true);

        // Delivery is asynchronous, so poll - but only ever through the cache-first read.
        string? served = null;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            served = (await nodeB.GetCertificateAsync(Domain))?.Thumbprint;
            if (served == second)
            {
                break;
            }
            await Task.Delay(20);
        }

        Assert.That(served, Is.EqualTo(second),
            $"Node B is still serving a certificate node A replaced (first was {first})");

        // Node A hears its own announcement too, and must still be serving what it wrote
        Assert.That((await nodeA.GetCertificateAsync(Domain))?.Thumbprint, Is.EqualTo(second));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ReloadCertificateAsync_IgnoresTheCache_AndRefreshesIt(DatabaseType databaseType)
    {
        // Nobody subscribes, so nothing but ReloadCertificateAsync can correct node B's cache
        var (_, nodeB, first, second) = await ArrangeNodeBHoldingReplacedCertificateAsync(databaseType, subscribe: false);

        Assert.That((await nodeB.GetCertificateAsync(Domain))?.Thumbprint, Is.EqualTo(first),
            "The cache-first read is expected to be stale here; that is what the reload is for");
        Assert.That((await nodeB.ReloadCertificateAsync(Domain))?.Thumbprint, Is.EqualTo(second),
            "The reload must answer from the database, not from node B's cache");
        Assert.That((await nodeB.GetCertificateAsync(Domain))?.Thumbprint, Is.EqualTo(second),
            "The reload must leave the fresh certificate in the cache");
    }

    //

    [Test]
    public async Task ReloadCertificateAsync_ShouldReturnNull_WhenNoCertificateExists()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        Assert.That(await _certificateStore.ReloadCertificateAsync("frodo.dotyou.cloud"), Is.Null);
    }

    //

    // Loads finish in any order. On CI a change announcement's reload that read the FIRST
    // certificate completed after the one that read the second, and left node B serving the
    // certificate node A had replaced.
    [Test]
    public void ALoadThatFinishesLate_DoesNotReplaceANewerCachedCertificate()
    {
        var store = new CertificateStore(Substitute.For<IServiceProvider>(), new CertificateStorageKey(new byte[32]));
        using var older = X509Extensions.CreateSelfSignedEcDsaCertificate(Domain);
        using var newer = X509Extensions.CreateSelfSignedEcDsaCertificate(Domain);

        Assert.That(store.CacheUnlessNewerCached(Domain, newer, version: 2).Thumbprint, Is.EqualTo(newer.Thumbprint));

        Assert.That(store.CacheUnlessNewerCached(Domain, older, version: 1).Thumbprint, Is.EqualTo(newer.Thumbprint),
            "A certificate read at an older row version must not replace the cached one");
        Assert.That(store.CacheUnlessNewerCached(Domain, newer, version: 2).Thumbprint, Is.EqualTo(newer.Thumbprint),
            "Re-reading the same version is a no-op, not a rejection");

        using var newest = X509Extensions.CreateSelfSignedEcDsaCertificate(Domain);
        Assert.That(store.CacheUnlessNewerCached(Domain, newest, version: 3).Thumbprint, Is.EqualTo(newest.Thumbprint));
    }
}
