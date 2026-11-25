#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
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
/// The backoff test drains one pass at a time with no bring-forward, so an item runs again only if the
/// outbox itself considers it due. The recipient is an identity no host serves, so every send fails.
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
        var tenant = Host.GetTenantScope(Identities.Frodo);
        await EnqueueConnectIntroduceeAsync(OutboxBackoff.Resolve(tenant).Outbox);

        await OutboxBackoff.AssertFailedOnceAndParkedAsync(tenant, SystemDriveConstants.TransientTempDrive.Alias,
            RecipientNoticeKey.For(OutboxItemType.ConnectIntroducee, Unreachable).FileId, Unreachable);

        Assert.That(WarningsContaining("ConnectIntroducee to"), Is.Not.Empty,
            "each failed attempt should say why, at Warning");
    }

    [Test]
    public async Task UnreachableRecipient_SettlesWhenAttemptsRunOut()
    {
        var (outbox, processor, _) = OutboxBackoff.Resolve(Host.GetTenantScope(Identities.Frodo));
        await EnqueueConnectIntroduceeAsync(outbox);

        // Each retry pass brings the backed-off item forward, so this runs it until it settles.
        await processor.DrainAsync(maxRetryPasses: 3 * MaxAttempts);

        Assert.That(await GetItemAsync(outbox), Is.Null, "an exhausted item must leave the outbox");
        Assert.That(WarningsContaining("gave up"), Has.Count.EqualTo(1),
            "giving up on an introduction should be said once, at Warning");
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
            File = RecipientNoticeKey.For(OutboxItemType.ConnectIntroducee, Unreachable),
            State = new OutboxItemState { Data = OdinSystemSerializer.Serialize(iid).ToUtf8ByteArray() }
        }, useUpsert: true);
    }

    private static Task<RedactedOutboxFileItem> GetItemAsync(PeerOutbox outbox) =>
        outbox.GetItemAsync(SystemDriveConstants.TransientTempDrive.Alias,
            RecipientNoticeKey.For(OutboxItemType.ConnectIntroducee, Unreachable).FileId, Unreachable);

    private List<LogEvent> WarningsContaining(string text) =>
        Host.LogStore.GetLogEvents().TryGetValue(LogEventLevel.Warning, out var warnings)
            ? warnings.Where(e => e.RenderMessage().Contains(text)).ToList()
            : [];
}
