#nullable enable
using System.Collections.Generic;
using System.Linq;
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
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections.Introductions;

/// <summary>
/// Notices to one person queued before the outbox runs (#1824).  Each type has its own outbox key, so a notice of
/// one type no longer replaces another's; the same type queued again still replaces its own; and a disconnect or a
/// withdrawn request still drops the introductions queued to that person, as the shared key used to.  The fast host
/// never starts the outbox, so what is queued stays until a drain.
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
    public async Task AWithdrawalThenAnIntroduceeConnect_BothStayQueued()
    {
        var (frodo, sam) = await MerryIntroducesFrodoAndSamLaterAsync();

        await SendAndWithdrawRequestAsync(frodo, sam.Identity);
        await DeliverMerrysIntroductionToFrodoAsync(frodo);

        Assert.That(await QueuedNoticeTypesAsync(sam.Identity),
            Is.EquivalentTo(new[] { (int)OutboxItemType.WithdrawConnectionRequest, (int)OutboxItemType.ConnectIntroducee }),
            "the introducee connect must not replace the withdrawal queued to the same person");
    }

    [Test]
    public async Task AWithdrawal_DropsTheIntroduceeConnectQueuedBeforeIt()
    {
        var (frodo, sam) = await MerryIntroducesFrodoAndSamLaterAsync();
        // Sam receives the introduction too; it must not make Sam accept Frodo's request before Frodo withdraws it
        await sam.Admin.DisableAutoAcceptIntroductions();

        await DeliverMerrysIntroductionToFrodoAsync(frodo);
        Assert.That(await QueuedNoticeTypesAsync(sam.Identity), Is.EqualTo(new[] { (int)OutboxItemType.ConnectIntroducee }));

        await SendAndWithdrawRequestAsync(frodo, sam.Identity);

        Assert.That(await QueuedNoticeTypesAsync(sam.Identity),
            Is.EqualTo(new[] { (int)OutboxItemType.WithdrawConnectionRequest }),
            "a withdrawn request must not be followed by the introduction's connection request");
    }

    [Test]
    public async Task ADisconnect_DropsTheIntroductionQueuedBeforeIt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var sam = await LoginAsOwner(Identities.Sam);
        await PrepareIntroducer(frodo, sam, merry);

        await IntroduceAsync(frodo, "meet merry", sam.Identity, merry.Identity);
        var disconnect = await frodo.RefitFor<IRefitUniversalCircleNetworkConnections>()
            .Disconnect(new OdinIdRequest { OdinId = sam.Identity }, notifyRemote: true);
        Assert.That(disconnect.IsSuccessStatusCode, Is.True);

        Assert.That(await QueuedNoticeTypesAsync(sam.Identity), Is.EqualTo(new[] { (int)OutboxItemType.BreakConnectionRequest }));
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

    /// <summary>Merry is connected to Frodo and Sam, who are not connected to each other</summary>
    private async Task<(OwnerSession Frodo, OwnerSession Sam)> MerryIntroducesFrodoAndSamLaterAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var sam = await LoginAsOwner(Identities.Sam);
        await PrepareIntroducer(merry, frodo, sam);
        _merry = merry;
        return (frodo, sam);
    }

    private OwnerSession? _merry;

    /// <summary>Merry introduces Frodo and Sam; Frodo receives it and queues its connect to Sam</summary>
    private async Task DeliverMerrysIntroductionToFrodoAsync(OwnerSession frodo)
    {
        var merry = _merry!;
        await IntroduceAsync(merry, "meet sam", frodo.Identity, (OdinId)Identities.Sam);
        await merry.Sync.DrainOutboxAsync();
        Assert.That((await Requests(frodo).ProcessIncomingIntroductions()).IsSuccessStatusCode, Is.True);
    }

    private static async Task SendAndWithdrawRequestAsync(OwnerSession sender, OdinId recipient)
    {
        await sender.Connections.SendConnectionRequest(recipient, []);
        var withdraw = await Requests(sender).DeleteSentRequest(new OdinIdRequest { OdinId = recipient }, notifyRemote: true);
        Assert.That(withdraw.IsSuccessStatusCode, Is.True);
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
