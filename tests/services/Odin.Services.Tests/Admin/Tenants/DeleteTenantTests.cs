using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.Admin.Tenants;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Configuration;
using Odin.Services.Email.Mailbox;
using Odin.Services.Email.Dkim;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.LastSeen;
using Odin.Services.Registry;
using Odin.Services.Registry.PayloadMove;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Tests.Admin.Tenants;

// Deleting a tenant (#1841): only a disabled one, and never its DNS, which in shared PowerDNS may be another host's
public class DeleteTenantTests
{
    private const string Domain = "frodo.dotyou.cloud";

    private string _tempPath = null!;
    private IContainer _container = null!;
    private SystemDatabase _systemDatabase = null!;
    private PayloadMoveSource _source = null!;
    private readonly Guid _identityId = Guid.NewGuid();

    private readonly Mock<IIdentityRegistry> _registry = new();
    private readonly Mock<IMailboxProvider> _mailbox = new();
    private readonly Mock<IDkimStore> _dkimStore = new();
    private readonly Mock<IJobManager> _jobManager = new();

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

        _systemDatabase = _container.Resolve<SystemDatabase>();
        await _systemDatabase.MigrateDatabaseAsync();
        _source = new PayloadMoveSource(_systemDatabase);

        _registry.Reset();
        _mailbox.Reset();
        _dkimStore.Reset();
        _jobManager.Reset();
        _jobManager.Setup(m => m.NewJob<DeleteTenantJob>()).Returns(NewJob);
    }

    [TearDown]
    public void TearDown()
    {
        _container.Dispose();
        Directory.Delete(_tempPath, true);
    }

    //
    // The job
    //

    [Test]
    public async Task ADisabledTenantIsDeletedAndItsDnsIsNotTouched()
    {
        // The job has no DNS dependency at all: PowerDNS is shared, and deleting DNS is a separate command
        Registered(TenantStatus.Disabled, DisabledReason.Moved);
        await CompletedTransferAsync();

        var job = NewJob();
        job.Data.Domain = Domain;
        await job.Run(CancellationToken.None);

        _registry.Verify(r => r.DeleteRegistration(Domain), Times.Once);
        _mailbox.Verify(m => m.DeleteMailboxAsync(Domain), Times.Once);
        _dkimStore.Verify(d => d.DeleteKeysAsync(Domain), Times.Once);
        _registry.Verify(r => r.SetStatusAsync(It.IsAny<string>(), It.IsAny<TenantStatus>(), It.IsAny<DisabledReason?>(), It.IsAny<bool>()),
            Times.Never, "a moved copy keeps saying so until it is gone");
        Assert.That(await _source.LoadAsync(_identityId), Is.Null, "the handoff state goes with it");
    }

    [Test]
    public void TheJobRefusesATenantEnabledSinceItWasQueued()
    {
        Registered(TenantStatus.Active, null);

        var job = NewJob();
        job.Data.Domain = Domain;
        var e = Assert.ThrowsAsync<InvalidOperationException>(() => job.Run(CancellationToken.None));
        Assert.That(e!.Message, Does.Contain("only a disabled tenant is deleted"));
        _registry.Verify(r => r.DeleteRegistration(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ARetryAfterTheRegistrationIsGoneFinishes()
    {
        _registry.Setup(r => r.GetAsync(Domain)).ReturnsAsync((IdentityRegistration)null!);

        var job = NewJob();
        job.Data.Domain = Domain;
        var result = await job.Run(CancellationToken.None);

        Assert.That(result.Result, Is.EqualTo(RunResult.Success));
    }

    //
    // Queuing it
    //

    [TestCase(TenantStatus.Active)]
    [TestCase(TenantStatus.OutOfQuota)]
    [TestCase(TenantStatus.Paused)]
    public void OnlyADisabledTenantCanBeDeleted(TenantStatus status)
    {
        Registered(status, null);

        var e = Assert.ThrowsAsync<OdinClientException>(() => NewTenantAdmin().EnqueueDeleteTenant(Domain));
        Assert.That(e!.Message, Does.Contain("disable it first"));
        NothingScheduled();
    }

    [Test]
    public async Task ADisabledTenantIsQueued()
    {
        Registered(TenantStatus.Disabled, DisabledReason.Admin);
        _jobManager.Setup(m => m.ScheduleJobAsync(It.IsAny<AbstractJob>(), It.IsAny<JobSchedule>())).ReturnsAsync(Guid.NewGuid());

        await NewTenantAdmin().EnqueueDeleteTenant(Domain);

        _jobManager.Verify(m => m.ScheduleJobAsync(It.IsAny<AbstractJob>(), It.IsAny<JobSchedule>()), Times.Once);
    }

    [Test]
    public async Task ADeleteIsRefusedWhileTheTargetStillNeedsThePayloads()
    {
        Registered(TenantStatus.Disabled, DisabledReason.Moved);
        await _source.MintHandoffAsync(_identityId);

        var e = Assert.ThrowsAsync<OdinClientException>(() => NewTenantAdmin().EnqueueDeleteTenant(Domain));
        Assert.That(e!.Message, Does.Contain("has not received all its payloads"));
        NothingScheduled();
    }

    [Test]
    public async Task AMovedCopyWithEmailNeedsDiscardMail()
    {
        Registered(TenantStatus.Disabled, DisabledReason.Moved);
        await CompletedTransferAsync();
        await _systemDatabase.DkimKeys.InsertAsync(new DkimKeysRecord
        {
            domain = new OdinId(Domain), selector = "ed25519", algorithm = "ed25519", publicKey = "pk", privateKey = "sk"
        });
        _jobManager.Setup(m => m.ScheduleJobAsync(It.IsAny<AbstractJob>(), It.IsAny<JobSchedule>())).ReturnsAsync(Guid.NewGuid());

        var e = Assert.ThrowsAsync<OdinClientException>(() => NewTenantAdmin().EnqueueDeleteTenant(Domain));
        Assert.That(e!.Message, Does.Contain("only copy of its mail"));
        NothingScheduled();

        await NewTenantAdmin().EnqueueDeleteTenant(Domain, discardMail: true);
        _jobManager.Verify(m => m.ScheduleJobAsync(It.IsAny<AbstractJob>(), It.IsAny<JobSchedule>()), Times.Once);
    }

    //

    private void Registered(TenantStatus status, DisabledReason? reason)
    {
        _registry.Setup(r => r.GetAsync(Domain)).ReturnsAsync(new IdentityRegistration
        {
            Id = _identityId, PrimaryDomainName = Domain, Status = status, DisabledReason = reason
        });
    }

    // A move whose payloads all arrived: the handoff state stays, no longer pending
    private async Task CompletedTransferAsync()
    {
        var credential = await _source.RedeemAsync(_identityId, await _source.MintHandoffAsync(_identityId));
        Assert.That(await _source.CompleteAsync(_identityId, credential), Is.True);
    }

    private void NothingScheduled() =>
        _jobManager.Verify(m => m.ScheduleJobAsync(It.IsAny<AbstractJob>(), It.IsAny<JobSchedule>()), Times.Never);

    private DeleteTenantJob NewJob() => new(
        new Mock<ILogger<DeleteTenantJob>>().Object, _registry.Object, _mailbox.Object, _dkimStore.Object, _source);

    private TenantAdmin NewTenantAdmin() => new(
        new Mock<ILogger<TenantAdmin>>().Object, LoggerFactory.Create(_ => { }), new OdinConfiguration(), _jobManager.Object,
        _registry.Object, new Mock<IMultiTenantContainer>().Object, new Mock<ILastSeenService>().Object, null!, _source, _systemDatabase);
}
