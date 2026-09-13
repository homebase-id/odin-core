using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Services.Email.Dkim;
using Testcontainers.PostgreSql;

namespace Odin.Services.Tests.Email.Dkim;

public class DkimStoreTests
{
    private const string Domain = "frodo.dotyou.cloud";
    private static readonly byte[] StorageKey =
        Convert.FromHexString("BAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00DBAADF00D");

    private string _tempDir = null!;
    private PostgreSqlContainer? _postgresContainer;
    private IDkimStore _dkimStore = null!;
    private ILifetimeScope _autofacContainer = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

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

    private async Task RegisterServicesAsync(DatabaseType databaseType, byte[]? storageKey = null)
    {
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

        cb.RegisterType<DkimStore>().As<IDkimStore>().SingleInstance();
        cb.RegisterInstance(new DkimStorageKey(storageKey ?? StorageKey)).SingleInstance();
        cb.AddDatabaseServices();
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

        _dkimStore = _autofacContainer.Resolve<IDkimStore>();
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task GetKeysAsync_ShouldReturnEmpty_WhenNoKeysExist(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        var keys = await _dkimStore.GetKeysAsync(Domain);
        Assert.That(keys, Is.Empty);
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task SaveAndGet_ShouldRoundTripBothSelectors(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        var generated = DkimKeyGenerator.GenerateKeys();
        await _dkimStore.SaveKeysAsync(Domain, generated);

        var loaded = await _dkimStore.GetKeysAsync(Domain);

        Assert.That(loaded.Count, Is.EqualTo(2));
        foreach (var original in generated)
        {
            var roundTripped = loaded.Single(k => k.Selector == original.Selector);
            Assert.That(roundTripped.Algorithm, Is.EqualTo(original.Algorithm));
            Assert.That(roundTripped.PublicKey, Is.EqualTo(original.PublicKey));
            Assert.That(roundTripped.PrivateKeyPkcs8, Is.EqualTo(original.PrivateKeyPkcs8));
        }
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task PrivateKeyIsEncryptedAtRest_PublicKeyIsCleartext(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        var generated = DkimKeyGenerator.GenerateKeys();
        await _dkimStore.SaveKeysAsync(Domain, generated);

        var table = _autofacContainer.Resolve<TableDkimKeys>();
        var records = await table.GetByDomainAsync(new OdinId(Domain));

        Assert.That(records.Count, Is.EqualTo(2));
        foreach (var record in records)
        {
            var original = generated.Single(k => k.Selector == record.selector);
            Assert.That(record.publicKey, Is.EqualTo(original.PublicKeyBase64));
            Assert.That(record.algorithm, Is.EqualTo(original.KTag));
            // Hex ciphertext, and not just the hex of the plaintext private key
            Assert.That(record.privateKey, Does.Match("^[0-9A-F]+$"));
            Assert.That(record.privateKey, Is.Not.EqualTo(Convert.ToHexString(original.PrivateKeyPkcs8)));
        }
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task SaveAgain_ShouldReplaceKeys_Rotation(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await _dkimStore.SaveKeysAsync(Domain, DkimKeyGenerator.GenerateKeys());
        var second = DkimKeyGenerator.GenerateKeys();
        await _dkimStore.SaveKeysAsync(Domain, second);

        var loaded = await _dkimStore.GetKeysAsync(Domain);

        Assert.That(loaded.Count, Is.EqualTo(2));
        foreach (var rotated in second)
        {
            var current = loaded.Single(k => k.Selector == rotated.Selector);
            Assert.That(current.PublicKey, Is.EqualTo(rotated.PublicKey));
        }
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task DeleteKeysAsync_ShouldRemoveOnlyThatDomain(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        await _dkimStore.SaveKeysAsync(Domain, DkimKeyGenerator.GenerateKeys());
        await _dkimStore.SaveKeysAsync("sam.dotyou.cloud", DkimKeyGenerator.GenerateKeys());

        await _dkimStore.DeleteKeysAsync(Domain);

        Assert.That(await _dkimStore.GetKeysAsync(Domain), Is.Empty);
        Assert.That((await _dkimStore.GetKeysAsync("sam.dotyou.cloud")).Count, Is.EqualTo(2));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task TamperedCiphertext_ShouldFailThePairProof(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);

        var generated = DkimKeyGenerator.GenerateKeys();
        await _dkimStore.SaveKeysAsync(Domain, generated);

        // Swap the s1 ciphertext under the s2 row: each half decrypts under the wrong
        // IV/pairing, which the pair proof must catch (AesCbc has no MAC of its own)
        var table = _autofacContainer.Resolve<TableDkimKeys>();
        var records = await table.GetByDomainAsync(new OdinId(Domain));
        var s1 = records.Single(r => r.selector == "s1");
        var s2 = records.Single(r => r.selector == "s2");
        (s1.privateKey, s2.privateKey) = (s2.privateKey, s1.privateKey);
        await table.UpsertAsync(s1);
        await table.UpsertAsync(s2);

        Assert.CatchAsync<Exception>(() => _dkimStore.GetKeysAsync(Domain));
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task MissingStorageKey_ShouldRefuseLoudly(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, storageKey: []);

        var exception = Assert.ThrowsAsync<OdinSystemException>(() => _dkimStore.SaveKeysAsync(Domain, DkimKeyGenerator.GenerateKeys()));
        Assert.That(exception!.Message, Does.Contain("Email:DkimStorageKey"));
        Assert.ThrowsAsync<OdinSystemException>(() => _dkimStore.GetKeysAsync(Domain));
    }
}
