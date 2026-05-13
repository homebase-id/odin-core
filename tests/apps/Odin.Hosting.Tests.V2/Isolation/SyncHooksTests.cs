using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Isolation;

/// <summary>
/// Sanity sentinels for the test-only drain hooks (<c>ITestSync</c>). All cases exercise the
/// empty-outbox / empty-inbox path — meaningful end-to-end peer-flow tests come once in-process
/// peer routing lands. Verifies the hooks resolve from DI, run without exceptions, and behave
/// like instant no-ops when there's no queued work.
/// </summary>
[TestFixture]
public class SyncHooksTests : V2Fixture
{
    [Test]
    public async Task DrainOutbox_OnEmpty_ReturnsImmediately()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var sw = Stopwatch.StartNew();
        await owner.Sync.DrainOutboxAsync();
        sw.Stop();
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500),
            $"drain on empty outbox should be ~instant; took {sw.ElapsedMilliseconds}ms");
    }

    [Test]
    public async Task ProcessInbox_OnEmpty_ReturnsZero()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Sync sentinel drive");

        var status = await owner.Sync.ProcessInboxAsync(drive);
        Assert.That(status, Is.Not.Null);
        Assert.That(status.TotalItems, Is.EqualTo(0));
        Assert.That(status.PoppedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task IsOutboxEmpty_OnFreshDrive_IsTrue()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Sync sentinel drive");

        Assert.That(await owner.Sync.IsOutboxEmptyAsync(drive), Is.True);
    }

    [Test]
    public async Task AppSync_ProcessInbox_OnEmpty_ReturnsZero()
    {
        // Sanity: AppSession.Sync routes ProcessInbox through the app's V1 inbox endpoint
        // (different path than owner's). This proves the wiring + auth round-trip works.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Sync sentinel drive");

        var app = await AppSession.SetupAsync(owner, drive, DrivePermission.ReadWrite);

        var status = await app.Sync.ProcessInboxAsync(drive);
        Assert.That(status, Is.Not.Null);
        Assert.That(status.TotalItems, Is.EqualTo(0));
        Assert.That(status.PoppedCount, Is.EqualTo(0));
    }

    [Test]
    public async Task WaitForOutboxEmpty_OnFreshDrive_ReturnsQuickly()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Sync sentinel drive");

        using var cts = new CancellationTokenSource(500);
        await owner.Sync.WaitForOutboxEmptyAsync(drive, cancellationToken: cts.Token);
        Assert.That(cts.IsCancellationRequested, Is.False, "should have completed before cancel");
    }
}
