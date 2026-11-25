#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Refit;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections.Introductions;

/// <summary>
/// Notices to one person queued before the outbox runs (#1824): one of each type stays, and the same type queued
/// again replaces its own.  The fast host never starts the outbox, so what is queued stays until a drain.
/// </summary>
[TestFixture]
public class RecipientNoticeKeyTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    private static readonly int[] NoticeTypes =
    [
        (int)OutboxItemType.SendIntroduction, (int)OutboxItemType.ConnectIntroducee,
        (int)OutboxItemType.BreakConnectionRequest, (int)OutboxItemType.WithdrawConnectionRequest
    ];

    [Test]
    public async Task AnIntroductionAndADisconnectToOnePerson_BothStayQueued()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var sam = await LoginAsOwner(Identities.Sam);
        await PrepareIntroducer(frodo, sam, merry);

        await IntroduceAsync(frodo, "meet merry", sam.Identity, merry.Identity);
        var disconnect = await frodo.RefitFor<IRefitUniversalCircleNetworkConnections>()
            .Disconnect(new OdinIdRequest { OdinId = sam.Identity }, notifyRemote: true);
        Assert.That(disconnect.IsSuccessStatusCode, Is.True);

        Assert.That(await QueuedNoticeTypesAsync(sam.Identity),
            Is.EquivalentTo(new[] { (int)OutboxItemType.SendIntroduction, (int)OutboxItemType.BreakConnectionRequest }),
            "the disconnect must not replace the introduction queued to the same person");
    }

    [Test]
    public async Task TheSameNoticeQueuedAgain_ReplacesItsOwn()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var sam = await LoginAsOwner(Identities.Sam);
        await PrepareIntroducer(frodo, sam, merry);

        await IntroduceAsync(frodo, "first", sam.Identity, merry.Identity);
        await IntroduceAsync(frodo, "second", sam.Identity, merry.Identity);

        Assert.That(await QueuedNoticeTypesAsync(sam.Identity), Is.EqualTo(new[] { (int)OutboxItemType.SendIntroduction }),
            "one pending introduction per person");
    }

    private static async Task IntroduceAsync(OwnerSession introducer, string message, params OdinId[] recipients)
    {
        var response = await Requests(introducer).SendIntroductions(new IntroductionGroup
        {
            Message = message,
            Recipients = recipients.Select(r => r.DomainName).ToList()
        });
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }

    /// <summary>The types of Frodo's queued notices to <paramref name="recipient"/>, one entry per outbox row</summary>
    private async Task<List<int>> QueuedNoticeTypesAsync(OdinId recipient)
    {
        await using var scope = Host.GetTenantScope(Identities.Frodo).BeginLifetimeScope();
        var rows = await scope.Resolve<TableOutbox>()
            .GetByDriveAndRecipientAsync(SystemDriveConstants.TransientTempDrive.Alias, recipient.DomainName);
        return rows.Select(r => r.type).Where(NoticeTypes.Contains).ToList();
    }
}
