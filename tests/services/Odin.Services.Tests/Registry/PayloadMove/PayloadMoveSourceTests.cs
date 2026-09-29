using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Time;
using Odin.Services.Registry.PayloadMove;

namespace Odin.Services.Tests.Registry.PayloadMove;

public class PayloadMoveSourceTests
{
    private string _tempPath = null!;
    private IContainer _container = null!;
    private PayloadMoveSource _source = null!;
    private readonly Guid _identityId = Guid.NewGuid();

    [SetUp]
    public async Task Setup()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);

        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterInstance(LoggerFactory.Create(_ => { })).As<ILoggerFactory>();
        builder.RegisterType<NodeLock>().As<INodeLock>().SingleInstance();
        builder.AddDatabaseServices();
        builder.AddSqliteSystemDatabaseServices(Path.Combine(_tempPath, "sys.db"));
        _container = builder.Build();

        var systemDatabase = _container.Resolve<SystemDatabase>();
        await systemDatabase.MigrateDatabaseAsync();
        _source = new PayloadMoveSource(systemDatabase);
    }

    [TearDown]
    public void TearDown()
    {
        _container.Dispose();
        Directory.Delete(_tempPath, true);
    }

    [Test]
    public async Task AHandoffTokenIsRedeemedOnceForACredentialThatAuthorizesUntilCompletion()
    {
        var token = await _source.MintHandoffAsync(_identityId);
        Assert.That(await _source.IsTransferPendingAsync(_identityId), Is.True, "an unredeemed export is pending");

        var credential = await _source.RedeemAsync(_identityId, token);
        Assert.That(credential, Is.Not.Null.And.Not.EqualTo(token));
        Assert.That(await _source.RedeemAsync(_identityId, token), Is.Null, "a second import must not get a credential");

        Assert.That(await _source.AuthorizeAsync(_identityId, credential), Is.True);
        Assert.That(await _source.AuthorizeAsync(_identityId, token), Is.False, "the handoff token is not a credential");
        Assert.That(await _source.AuthorizeAsync(Guid.NewGuid(), credential), Is.False, "bound to its identity");
        Assert.That(await _source.AuthorizeAsync(_identityId, null), Is.False);

        Assert.That(await _source.CompleteAsync(_identityId, "wrong"), Is.False);
        Assert.That(await _source.IsTransferPendingAsync(_identityId), Is.True);
        Assert.That(await _source.CompleteAsync(_identityId, credential), Is.True);

        Assert.That(await _source.IsTransferPendingAsync(_identityId), Is.False, "complete releases the source");
        Assert.That(await _source.AuthorizeAsync(_identityId, credential), Is.False, "completion revokes the credential");
    }

    [Test]
    public async Task AWrongOrExpiredHandoffTokenIsRefused()
    {
        var token = await _source.MintHandoffAsync(_identityId);
        Assert.That(await _source.RedeemAsync(_identityId, token + "x"), Is.Null);
        Assert.That(await _source.RedeemAsync(Guid.NewGuid(), token), Is.Null);

        await ExpireHandoffAsync();
        Assert.That(await _source.RedeemAsync(_identityId, token), Is.Null);
        Assert.That(await _source.IsTransferPendingAsync(_identityId), Is.False,
            "an export nobody imported must not block deleting the source forever");
    }

    [Test]
    public async Task ReExportingRevokesTheEarlierTokenAndCredential()
    {
        var first = await _source.MintHandoffAsync(_identityId);
        var credential = await _source.RedeemAsync(_identityId, first);

        var second = await _source.MintHandoffAsync(_identityId);

        Assert.That(await _source.AuthorizeAsync(_identityId, credential), Is.False);
        Assert.That(await _source.RedeemAsync(_identityId, first), Is.Null);
        Assert.That(await _source.RedeemAsync(_identityId, second), Is.Not.Null);
    }

    [Test]
    public async Task OnlyHashesAreStored()
    {
        var token = await _source.MintHandoffAsync(_identityId);
        var credential = await _source.RedeemAsync(_identityId, token);

        var row = await _container.Resolve<SystemDatabase>().Settings.GetAsync($"payload-move-source:{_identityId}");
        Assert.That(row!.value, Does.Not.Contain(token).And.Not.Contain(credential!), row.value);
    }

    private async Task ExpireHandoffAsync()
    {
        var state = (await _source.LoadAsync(_identityId))!;
        state.HandoffExpiresAt = UnixTimeUtc.Now().AddSeconds(-1);
        await _container.Resolve<SystemDatabase>().Settings.UpsertAsync(new SettingsRecord
        {
            key = $"payload-move-source:{_identityId}",
            value = OdinSystemSerializer.Serialize(state)
        });
    }
}
