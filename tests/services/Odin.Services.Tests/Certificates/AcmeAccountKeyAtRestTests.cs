using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.PubSub;
using Odin.Services.Background;
using Odin.Services.Background.BackgroundServices.System;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Tests.Certificates;

#nullable enable

/// <summary>
/// The ACME account key at rest (#1748): saved sealed under the storage key; a plain PEM from before is read and sealed
/// on that first read; a sealed one the storage key can't open is an error, never "no account".
/// </summary>
public class AcmeAccountKeyAtRestTests
{
    private const string SettingsKey = "acme-account-staging-pem"; // ICertesAcme.IsProduction is false on a substitute

    private readonly byte[] _storageKey = RandomNumberGenerator.GetBytes(32);
    private string _tempDir = null!;
    private IContainer _container = null!;

    [SetUp]
    public async Task Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INodeLock, NodeLock>();
        var cb = new ContainerBuilder();
        cb.Populate(services);
        cb.Register(ctx => (IServiceProvider)ctx.Resolve<ILifetimeScope>()).As<IServiceProvider>();
        cb.AddDatabaseServices();
        cb.AddSystemPubSub(redisEnabled: false);
        cb.RegisterModule(new LoggingAutofacModule());
        cb.AddSqliteSystemDatabaseServices(Path.Combine(_tempDir, "system-test.db"));
        _container = cb.Build();
        await _container.Resolve<SystemDatabase>().MigrateDatabaseAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _container.DisposeAsync();
        Directory.Delete(_tempDir, true);
    }

    [Test]
    public async Task APlainPemFromBefore_IsRead_AndSealedOnThatRead()
    {
        var keyPem = MakeKeyPem();
        await WriteSettingAsync(keyPem);

        var account = await Service(_storageKey).LoadAccountAsync();
        Assert.That(account!.AccounKeyPem, Is.EqualTo(keyPem));

        var stored = await ReadSettingAsync();
        Assert.That(stored, Does.Not.Contain("BEGIN"), "sealed on its first read");
        Assert.That(SecretAtRest.Open(stored, _storageKey, SettingsKey), Is.EqualTo(keyPem));

        var again = await Service(_storageKey).LoadAccountAsync();
        Assert.That(again!.AccounKeyPem, Is.EqualTo(keyPem));
    }

    [Test]
    public async Task APlainPemWithLeadingWhitespace_IsStillReadAsPlain()
    {
        var keyPem = "\n" + MakeKeyPem();
        await WriteSettingAsync(keyPem);

        var account = await Service(_storageKey).LoadAccountAsync();
        Assert.That(account!.AccounKeyPem, Is.EqualTo(keyPem));
        Assert.That(SecretAtRest.Open(await ReadSettingAsync(), _storageKey, SettingsKey), Is.EqualTo(keyPem));
    }

    [Test]
    public async Task ASealedKeyTheStorageKeyCantOpen_IsAnError_NotNoAccount()
    {
        await WriteSettingAsync(SecretAtRest.Seal(MakeKeyPem(), RandomNumberGenerator.GetBytes(32), SettingsKey));
        var refused = Assert.ThrowsAsync<AcmeAccountKeyUnreadableException>(() => Service(_storageKey).LoadAccountAsync());
        Assert.That(refused!.Message, Does.Contain(SettingsKey).And.Contain("CertificateRenewal:StorageKey"),
            "the error names the row and the setting to fix");
    }

    [Test]
    public async Task ASavedAccount_IsStoredSealed()
    {
        var keyPem = MakeKeyPem();
        await Service(_storageKey).SaveAccountAsync(new AcmeAccount { AccounKeyPem = keyPem });

        var stored = await ReadSettingAsync();
        Assert.That(stored, Does.Not.Contain("BEGIN"));
        Assert.That(SecretAtRest.Open(stored, _storageKey, SettingsKey), Is.EqualTo(keyPem));
    }

    [Test]
    public async Task NoStoredKey_IsNoAccount()
    {
        Assert.That(await Service(_storageKey).LoadAccountAsync(), Is.Null);
    }

    private CertificateService Service(byte[] storageKey) => new(
        NullLogger<CertificateService>.Instance,
        new NodeLock(),
        Substitute.For<ICertificateStore>(),
        Substitute.For<ICertesAcme>(),
        Substitute.For<IDnsLookupService>(),
        new AcmeAccountConfig(),
        _container.Resolve<IServiceProvider>(),
        new OdinConfiguration(),
        Substitute.For<IBackgroundServiceNotifier<UpdateCertificatesBackgroundService>>(),
        new CertificateStorageKey(storageKey));

    private async Task WriteSettingAsync(string value)
    {
        await using var scope = _container.BeginLifetimeScope();
        await scope.Resolve<TableSettings>().UpsertAsync(new SettingsRecord { key = SettingsKey, value = value });
    }

    private async Task<string> ReadSettingAsync()
    {
        await using var scope = _container.BeginLifetimeScope();
        return (await scope.Resolve<TableSettings>().GetAsync(SettingsKey))!.value;
    }

    private static string MakeKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportPkcs8PrivateKeyPem();
    }
}
