#nullable enable
using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Hosting.Tests.V2.Isolation;

/// <summary>
/// The outbox back-off check the isolation tests share: drained one pass at a time with no bring-forward, a failed item
/// runs again only if the outbox itself considers it due.
/// </summary>
internal static class OutboxBackoff
{
    public static (PeerOutbox Outbox, PeerOutboxProcessorBackgroundService Processor, TableOutbox Table) Resolve(
        ILifetimeScope tenant) =>
        (tenant.Resolve<PeerOutbox>(), tenant.Resolve<PeerOutboxProcessorBackgroundService>(), tenant.Resolve<TableOutbox>());

    /// <summary>One pass fails the item once and parks it behind the back-off; a second pass leaves it alone.</summary>
    public static async Task AssertFailedOnceAndParkedAsync(ILifetimeScope tenant, Guid driveId, Guid fileId, OdinId recipient)
    {
        var (outbox, processor, table) = Resolve(tenant);

        var started = UnixTimeUtc.Now();
        await processor.DrainAsync(maxRetryPasses: 0);

        var item = await outbox.GetItemAsync(driveId, fileId, recipient);
        Assert.That(item, Is.Not.Null, "one failed attempt must not settle the item");
        Assert.That(item!.AttemptCount, Is.EqualTo(1));

        var row = await table.GetAsync(driveId, fileId, recipient.DomainName);
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.nextRunTime.milliseconds, Is.GreaterThanOrEqualTo(started.AddSeconds(9).milliseconds),
            "the retry must be parked behind the back-off, not rescheduled for now");

        await processor.DrainAsync(maxRetryPasses: 0);
        Assert.That((await outbox.GetItemAsync(driveId, fileId, recipient))!.AttemptCount, Is.EqualTo(1),
            "an item behind its back-off must not be attempted again");
    }
}
