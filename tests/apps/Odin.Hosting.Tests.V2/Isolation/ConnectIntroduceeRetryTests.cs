#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Serilog.Events;

namespace Odin.Hosting.Tests.V2.Isolation;

/// <summary>
/// A <c>ConnectIntroducee</c> item that cannot reach its recipient must back off between attempts, and
/// settle -- quietly, below Error -- once its attempts run out (#1778).
///
/// The failure was a network error escaping the worker. The processor rescheduled it for "now", so all
/// of <c>OutboxOperationMaxAttempts</c> went in well under a second, each logged at Error, and a
/// recipient that was briefly unreachable lost the introduction for good.
///
/// These drive <see cref="PeerOutboxProcessorBackgroundService.DrainAsync"/> one pass at a time with no
/// bring-forward, so an item runs again only if the outbox itself considers it due. The recipient is an
/// identity no host serves, so every send fails.
/// </summary>
[TestFixture]
public class ConnectIntroduceeRetryTests : V2Fixture
{
    private const int MaxAttempts = 2;

    private static readonly OdinId Unreachable = new("nobody-hosts-this.dotyou.cloud");

    protected override string[] HostIdentities => [Identities.Frodo];

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?> { ["Host:OutboxOperationMaxAttempts"] = MaxAttempts.ToString() };

    [Test]
    public async Task UnreachableRecipient_IsRetriedOnABackoff_NotImmediately()
    {
        var (outbox, processor, tblOutbox) = Resolve();
        await EnqueueConnectIntroduceeAsync(outbox);

        await processor.DrainAsync(maxRetryPasses: 0);

        var item = await GetItemAsync(outbox);
        Assert.That(item, Is.Not.Null, "one failed attempt must not settle the item");
        Assert.That(item!.AttemptCount, Is.EqualTo(1));

        var nextRun = await tblOutbox.NextScheduledItemAsync(SystemDriveConstants.TransientTempDrive.Alias);
        Assert.That(nextRun, Is.Not.Null);
        Assert.That(nextRun!.Value.milliseconds, Is.GreaterThan(UnixTimeUtc.Now().AddSeconds(5).milliseconds),
            "the retry must be parked behind the backoff, not rescheduled for now");

        await processor.DrainAsync(maxRetryPasses: 0);
        Assert.That((await GetItemAsync(outbox))!.AttemptCount, Is.EqualTo(1),
            "an item behind its backoff must not be attempted again");

        Assert.That(WarningsContaining("ConnectIntroducee to"), Is.Not.Empty,
            "each failed attempt should say why, at Warning");
    }

    [Test]
    public async Task UnreachableRecipient_SettlesWhenAttemptsRunOut()
    {
        var (outbox, processor, _) = Resolve();
        await EnqueueConnectIntroduceeAsync(outbox);

        for (var pass = 0; pass < 3 * MaxAttempts && await GetItemAsync(outbox) != null; pass++)
        {
            await processor.DrainAsync(maxRetryPasses: 0);
            await outbox.BringForwardScheduledItemsAsync();
        }

        Assert.That(await GetItemAsync(outbox), Is.Null, "an exhausted item must leave the outbox");
        Assert.That(WarningsContaining("gave up"), Has.Count.EqualTo(1),
            "giving up on an introduction should be said once, at Warning");
    }

    private (PeerOutbox, PeerOutboxProcessorBackgroundService, TableOutbox) Resolve()
    {
        var scope = Host.GetTenantScope(Identities.Frodo);
        return (scope.Resolve<PeerOutbox>(), scope.Resolve<PeerOutboxProcessorBackgroundService>(),
            scope.Resolve<TableOutbox>());
    }

    private static Task EnqueueConnectIntroduceeAsync(PeerOutbox outbox)
    {
        // Shaped as CircleNetworkIntroductionService.SaveAndEnqueueToConnect shapes it.
        var iid = new IdentityIntroduction
        {
            Identity = Unreachable,
            IntroducerOdinId = new OdinId(Identities.Sam),
            Message = "#1778",
            Received = UnixTimeUtc.Now()
        };

        return outbox.AddItemAsync(new OutboxFileItem
        {
            Recipient = Unreachable,
            Priority = 55,
            Type = OutboxItemType.ConnectIntroducee,
            AttemptCount = 0,
            File = new InternalDriveFileId
            {
                DriveId = SystemDriveConstants.TransientTempDrive.Alias,
                FileId = Unreachable.ToHashId()
            },
            DependencyFileId = default,
            State = new OutboxItemState { Data = OdinSystemSerializer.Serialize(iid).ToUtf8ByteArray() }
        }, useUpsert: true);
    }

    private static Task<RedactedOutboxFileItem> GetItemAsync(PeerOutbox outbox) =>
        outbox.GetItemAsync(SystemDriveConstants.TransientTempDrive.Alias, Unreachable.ToHashId(), Unreachable);

    private List<LogEvent> WarningsContaining(string text) =>
        Host.LogStore.GetLogEvents().TryGetValue(LogEventLevel.Warning, out var warnings)
            ? warnings.Where(e => e.RenderMessage().Contains(text)).ToList()
            : [];
}
