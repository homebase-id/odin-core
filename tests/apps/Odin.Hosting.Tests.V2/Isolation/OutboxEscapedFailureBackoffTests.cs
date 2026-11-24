#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Tests.V2.Isolation;

/// <summary>
/// A failure an outbox worker lets escape is retried on the attempt back-off, not at once (#1815).  Before, the
/// processor rescheduled it for "now", so all of <c>OutboxOperationMaxAttempts</c> went in well under a second.
/// </summary>
/// <remarks>
/// The item is a push notification whose stored data isn't JSON: its worker throws while reading it, and nothing in
/// the worker catches that.  Drained one pass at a time with no bring-forward, so it runs again only if the outbox
/// itself considers it due.
/// </remarks>
[TestFixture]
public class OutboxEscapedFailureBackoffTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
        ["Unhandled exception occured while processing an outbox"];

    [Test]
    public async Task AnEscapedFailure_IsRetriedOnTheBackoff_NotImmediately()
    {
        var scope = Host.GetTenantScope(Identities.Frodo);
        var (outbox, processor, table) =
            (scope.Resolve<PeerOutbox>(), scope.Resolve<PeerOutboxProcessorBackgroundService>(), scope.Resolve<TableOutbox>());

        var file = new InternalDriveFileId { DriveId = Guid.NewGuid(), FileId = Guid.NewGuid() };
        var frodo = new OdinId(Identities.Frodo);
        await outbox.AddItemAsync(new OutboxFileItem
        {
            Recipient = frodo,
            Priority = 100,
            Type = OutboxItemType.PushNotification,
            File = file,
            State = new OutboxItemState { Data = "{ not json".ToUtf8ByteArray() }
        });

        await processor.DrainAsync(maxRetryPasses: 0);

        var item = await outbox.GetItemAsync(file.DriveId, file.FileId, frodo);
        Assert.That(item, Is.Not.Null, "one failed attempt must not settle the item");
        Assert.That(item!.AttemptCount, Is.EqualTo(1));

        var nextRun = await table.NextScheduledItemAsync(file.DriveId);
        Assert.That(nextRun, Is.Not.Null);
        Assert.That(nextRun!.Value.milliseconds, Is.GreaterThan(UnixTimeUtc.Now().AddSeconds(5).milliseconds),
            "the retry must be parked behind the back-off, not rescheduled for now");

        await processor.DrainAsync(maxRetryPasses: 0);
        Assert.That((await outbox.GetItemAsync(file.DriveId, file.FileId, frodo))!.AttemptCount, Is.EqualTo(1),
            "an item behind its back-off must not be attempted again");
    }
}
