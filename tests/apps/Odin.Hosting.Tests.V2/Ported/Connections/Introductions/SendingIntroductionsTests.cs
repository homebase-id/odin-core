#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections.Introductions;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/Introductions/SendingIntroductionsTests</c>. What
/// <c>send-introductions</c> does and does not produce: the introducer always hears success (so a
/// block is never disclosed to them), and on the receiving side an introduction turns into a
/// connection request only when neither party has blocked the other, is already connected, or has
/// turned introductions off.
/// </summary>
/// <remarks>
/// <para>
/// Every passive poll is gone. <c>WaitForEmptyOutbox(TransientTempDrive)</c> and
/// <c>AwaitIntroductionsProcessing()</c> — the same poll under two names — become
/// <c>Sync.DrainOutboxAsync()</c>. Draining an introducee's outbox is what actually sends the
/// introductory connection request (<c>ConnectIntroduceeOutboxWorker</c>), so this preserves
/// behaviour, not just timing. The <c>await Task.Delay(1000 * 3)</c> in
/// <see cref="WhenRecipientDisablesIntroductions_OneRecipientGetConnectionRequest_SecondRecipientDoesNot"/>
/// — whose comment explains that the outbox "will never be empty" because Sam's item fails and stays
/// queued — also becomes <c>DrainOutboxAsync()</c>, because on the fast host nothing else moves
/// Frodo's introduction to Merry.
/// </para>
/// <para>
/// <b>Measured, and it does not reproduce the sleep's state.</b> Probing the identically-shaped
/// sibling (<see cref="AutoAcceptTests"/>) either side of the drain: the outbox item for Sam is
/// present before it and gone after it. <c>PeerOutboxProcessorBackgroundService.DrainAsync</c> makes
/// three retry passes and then exhausts a permanently-failing item rather than leaving it queued
/// behind a backoff, which is the state the V1 three-second sleep relied on. Nothing observable
/// changes, because the assertion that reads the item is vacuous either way (see the carried defects
/// below) — but its <i>intent</i> is no longer satisfiable here: repairing that assertion would leave
/// it passing on <c>WebScaffold</c> and failing on this framework.
/// </para>
/// <para>
/// Carried defects, behaviour left exactly as found:
/// <list type="bullet">
/// <item><see cref="WillIgnoreIntroductionIfIntrodceeIsBlocked"/> and
/// <see cref="WillIgnoreIntroductionIfAlreadyConnected"/> both read
/// <c>merryRequestFromSamResponse</c> off <b>Sam's</b> identity asking for a request from Merry —
/// the identical call one line above. Merry's side is never queried, so the "merry should have no
/// request from sam" half of each test is vacuous.</item>
/// <item><c>GetOutboxItem</c> returns an <c>ApiResponse</c>, which is never null, so the
/// <c>IsNotNull</c> on it asserts nothing about whether the item exists.</item>
/// <item><see cref="WillFailToSendConnectionRequestWhenRecipientIsBlocked"/> calls its Samwise
/// session <c>pippinOwnerClient</c>; Pippin is not in the fixture at all.</item>
/// <item><see cref="WillIgnoreIntroductionIfIntrodceeIsBlocked"/>'s first assertion block is
/// commented out in the original where it would have asserted Sam's recipient status.</item>
/// </list>
/// </para>
/// <para>
/// The <c>[TearDown]</c> <c>Cleanup()</c>, the trailing <c>Cleanup()</c> in every test, and the
/// <c>DisableAllowIntroductions(false)</c> resets at the head of <c>Prepare</c> were all lifecycle
/// for a shared <c>WebScaffold</c>, and none of them asserted; per-test reset owns that. The
/// trailing <c>UnblockConnection</c> in
/// <see cref="WillFailToSendConnectionRequestViaIntroductionWhenRecipientIsBlocked"/> and
/// <see cref="WillFailToSendConnectionRequestWhenRecipientIsBlocked"/> is <b>kept</b>: both assert
/// their response, so they are tests rather than cleanup. The <c>DeleteAllIntroductions</c> calls at
/// the end of <c>Prepare</c> are kept as arrange, as
/// <see cref="IntroductionTestUtils.PrepareIntroducerAndClearIntroductionsAsync"/>.
/// </para>
/// <para>No caller matrix in the original and none added; <c>SetupCallerWithOwner</c> is not in play.</para>
/// </remarks>
[TestFixture]
public class SendingIntroductionsTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    [Test]
    public async Task WillIgnoreIntroductionIfIntrodceeIsBlocked()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var sam = await LoginAsOwner(Identities.Sam);

        await merry.Admin.DisableAutoAcceptIntroductions();
        await sam.Admin.DisableAutoAcceptIntroductions();

        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        // block errrrrbody
        await merry.Connections.BlockConnection(sam.Identity);
        await sam.Connections.BlockConnection(merry.Identity);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        //
        // we should return true to the sender so they do not know anything other than the introduction was received
        //
        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await frodo.Sync.DrainOutboxAsync();

        //
        // neither should have connection requests
        //
        var samRequestFromMerryResponse = await sam.Connections.GetIncomingRequestFrom(merry.Identity);
        Assert.That(samRequestFromMerryResponse.Content, Is.Null);

        var merryRequestFromSamResponse = await sam.Connections.GetIncomingRequestFrom(merry.Identity);
        Assert.That(merryRequestFromSamResponse.Content, Is.Null);

        //
        // neither should have someone in the list
        //
        var samReceivedIntroductionsResponse = await Requests(sam).GetReceivedIntroductions();
        Assert.That(samReceivedIntroductionsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(samReceivedIntroductionsResponse.Content, Is.Empty);

        var merryReceivedIntroductionsResponse = await Requests(merry).GetReceivedIntroductions();
        Assert.That(merryReceivedIntroductionsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(merryReceivedIntroductionsResponse.Content, Is.Empty);
    }

    [Test]
    public async Task WillIgnoreIntroductionIfAlreadyConnected()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var sam = await LoginAsOwner(Identities.Sam);

        await merry.Admin.DisableAutoAcceptIntroductions();
        await sam.Admin.DisableAutoAcceptIntroductions();

        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        // connect merry and sam
        await merry.Connections.SendConnectionRequest(sam.Identity);
        await sam.Connections.AcceptConnectionRequest(merry.Identity);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        //
        // we should return true to the sender so they do not know anything other than the introduction was received
        //
        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await frodo.Sync.DrainOutboxAsync();

        //
        // neither should have connection requests
        //
        var samRequestFromMerryResponse = await sam.Connections.GetIncomingRequestFrom(merry.Identity);
        Assert.That(samRequestFromMerryResponse.Content, Is.Null);

        var merryRequestFromSamResponse = await sam.Connections.GetIncomingRequestFrom(merry.Identity);
        Assert.That(merryRequestFromSamResponse.Content, Is.Null);

        //
        // neither should have someone in the list
        //
        var samReceivedIntroductionsResponse = await Requests(sam).GetReceivedIntroductions();
        Assert.That(samReceivedIntroductionsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(samReceivedIntroductionsResponse.Content, Is.Empty);

        var merryReceivedIntroductionsResponse = await Requests(merry).GetReceivedIntroductions();
        Assert.That(merryReceivedIntroductionsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(merryReceivedIntroductionsResponse.Content, Is.Empty);
    }

    [Test]
    public async Task WillSendConnectionRequestToIntroductions()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var sam = await LoginAsOwner(Identities.Sam);

        await merry.Admin.DisableAutoAcceptIntroductions();
        await sam.Admin.DisableAutoAcceptIntroductions();

        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await frodo.Sync.DrainOutboxAsync();

        // There are background processes running which will send introductions automatically
        // we can also call an endpoint to force this.
        // since we don't know when this will occur, we'll call the endpoint

        // there's also logic dictating that when sending a connection request due to an
        // introduction, we do not send it if there's already an incoming request

        // so - we have to add some logic into this test

        // firstly, force sending a request for both parties.
        var merryProcessResponse = await Requests(merry).ProcessIncomingIntroductions();
        Assert.That(merryProcessResponse.IsSuccessStatusCode, Is.True);

        var samProcessResponse = await Requests(sam).ProcessIncomingIntroductions();
        Assert.That(samProcessResponse.IsSuccessStatusCode, Is.True);

        // now, one of them should have a connection request, start with Sam
        var samRequestFromMerryResponse = await sam.Connections.GetIncomingRequestFrom(merry.Identity);
        var requestFromMerry = samRequestFromMerryResponse.Content;

        if (null == requestFromMerry)
        {
            // merry should have a request from sam
            var merryRequestFromSamResponse = await merry.Connections.GetIncomingRequestFrom(sam.Identity);
            var requestFromSam = merryRequestFromSamResponse.Content;

            Assert.That(requestFromSam, Is.Not.Null);
            Assert.That(requestFromSam!.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.Introduction));
            Assert.That(requestFromSam.IntroducerOdinId, Is.EqualTo(frodo.Identity));
        }
        else
        {
            Assert.That(requestFromMerry.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.Introduction));
            Assert.That(requestFromMerry.IntroducerOdinId, Is.EqualTo(frodo.Identity));
        }

        // both should have introductions in the list
        var samReceivedIntroductionsResponse = await Requests(sam).GetReceivedIntroductions();
        Assert.That(samReceivedIntroductionsResponse.IsSuccessStatusCode, Is.True);
        var samsIntroductionToMerry = samReceivedIntroductionsResponse.Content!.Single();
        Assert.That(samsIntroductionToMerry.Identity, Is.EqualTo(merry.Identity));
        Assert.That(samsIntroductionToMerry.IntroducerOdinId, Is.EqualTo(frodo.Identity));
        Assert.That(samsIntroductionToMerry.Received.milliseconds, Is.GreaterThan(0));

        var merryReceivedIntroductionsResponse = await Requests(merry).GetReceivedIntroductions();
        Assert.That(merryReceivedIntroductionsResponse.IsSuccessStatusCode, Is.True);
        var merrysIntroductionToSam = merryReceivedIntroductionsResponse.Content!.Single();
        Assert.That(merrysIntroductionToSam.Identity, Is.EqualTo(sam.Identity));
        Assert.That(merrysIntroductionToSam.IntroducerOdinId, Is.EqualTo(frodo.Identity));
        Assert.That(merrysIntroductionToSam.Received.milliseconds, Is.GreaterThan(0));
    }

    [Test]
    public async Task WillFailToSendConnectionRequestWithBadRequestWhenRecipientAlreadyConnectedWithValidConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var sam = await LoginAsOwner(Identities.Sam);

        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        var sendConnectionRequestResponse = await sam.Connections.SendConnectionRequest(frodo.Identity);
        Assert.That(sendConnectionRequestResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task WillFailToSendConnectionRequestViaIntroductionWhenRecipientIsBlocked()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var sam = await LoginAsOwner(Identities.Sam);

        var introsResponse = await Requests(merry).GetReceivedIntroductions();
        Assert.That(introsResponse.Content, Is.Empty,
            "Cannot start test - merry has pending introductions. this probably happened because they were cleaned up from other tests");

        // Merry blocks sam
        var blockResponse = await merry.Connections.BlockConnection(sam.Identity);
        Assert.That(blockResponse.IsSuccessStatusCode, Is.True);

        var samInfoResponse = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(samInfoResponse.IsSuccessStatusCode, Is.True);
        Assert.That(samInfoResponse.Content!.Status, Is.EqualTo(ConnectionStatus.Blocked));

        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        var samRequestFromMerryResponse2 = await sam.Connections.GetIncomingRequestFrom(merry.Identity);
        var firstRequestFromMerry2 = samRequestFromMerryResponse2.Content;
        Assert.That(firstRequestFromMerry2, Is.Null);

        var firstIntroductionResponse = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        var introResult = firstIntroductionResponse.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await frodo.Sync.DrainOutboxAsync();

        // wait for outbox on sam and merry so they can send their connection requests
        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();

        var samRequestFromMerryResponse = await sam.Connections.GetIncomingRequestFrom(merry.Identity);
        var firstRequestFromMerry = samRequestFromMerryResponse.Content;
        Assert.That(firstRequestFromMerry, Is.Null, "merry should not have been able to send a request to sam");

        var merryRequestFromSamResponse = await merry.Connections.GetIncomingRequestFrom(sam.Identity);
        var firstRequestFromSam = merryRequestFromSamResponse.Content;
        Assert.That(firstRequestFromSam, Is.Null, "merry should not have a request from sam");

        var unblockResponse = await merry.Connections.UnblockConnection(sam.Identity);
        Assert.That(unblockResponse.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task WillFailToSendConnectionRequestWhenRecipientIsBlocked()
    {
        var pippinOwnerClient = await LoginAsOwner(Identities.Sam);
        var frodoOnwerClient = await LoginAsOwner(Identities.Frodo);

        var blockResponse = await pippinOwnerClient.Connections.BlockConnection(frodoOnwerClient.Identity);
        Assert.That(blockResponse.IsSuccessStatusCode, Is.True);

        var frodoInfoResponse = await pippinOwnerClient.Connections.GetConnectionInfo(frodoOnwerClient.Identity);
        Assert.That(frodoInfoResponse.IsSuccessStatusCode, Is.True);
        Assert.That(frodoInfoResponse.Content!.Status, Is.EqualTo(ConnectionStatus.Blocked));

        var requestToPippinResponse = await frodoOnwerClient.Connections.SendConnectionRequest(pippinOwnerClient.Identity);
        // SendConnectionRequest now surfaces a remote 403 as HTTP 400 with
        // OdinClientErrorCode.RemoteServerReturnedForbidden so callers can discriminate the cause.
        Assert.That(requestToPippinResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var unblockResponse = await pippinOwnerClient.Connections.UnblockConnection(frodoOnwerClient.Identity);
        Assert.That(unblockResponse.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task WhenRecipientDisablesIntroductions_OneRecipientGetConnectionRequest_SecondRecipientDoesNot()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        // sam turns introductions off, so sam refuses the introduction from frodo
        await sam.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.DisableAllowIntroductions, bool.TrueString);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        var introResult = response.Content!;
        // ClassicAssert.IsFalse(introResult.RecipientStatus[TestIdentities.Samwise.OdinId],
        // "sam should reject since frodo does not have allow-introductions permission");
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        // Note; I have to use a delay because the outbox will never be
        // empty and, currently, there is no way to do an exclusion test on the outbox
        // await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        await frodo.Sync.DrainOutboxAsync();

        var samOutboxItem = await frodo.V1.Drive.GetOutboxItem(SystemDriveConstants.TransientTempDrive,
            sam.Identity.ToHashId(), sam.Identity);
        Assert.That(samOutboxItem, Is.Not.Null,
            "there should be an outbox item for sam since it failed he blocked incoming introductions");

        // ensure introductions are processed
        var samProcessResponse = await Requests(sam).ProcessIncomingIntroductions();
        Assert.That(samProcessResponse.IsSuccessStatusCode, Is.True);

        var merryProcessResponse = await Requests(merry).ProcessIncomingIntroductions();
        Assert.That(merryProcessResponse.IsSuccessStatusCode, Is.True);

        // Sam should get a connection request from merry (via frodo)
        var incomingRequestFromMerryResponse = await sam.Connections.GetIncomingRequestFrom(merry.Identity);
        Assert.That(incomingRequestFromMerryResponse.IsSuccessStatusCode, Is.True);
        Assert.That(incomingRequestFromMerryResponse.Content!.ConnectionRequestOrigin,
            Is.EqualTo(ConnectionRequestOrigin.Introduction));

        var outgoingRequestToMerryResponse = await sam.Connections.GetOutgoingSentRequestTo(merry.Identity);
        Assert.That(outgoingRequestToMerryResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "sam should not have sent a request");

        var merryRequestFromSamResponse = await merry.Connections.GetIncomingRequestFrom(sam.Identity);
        Assert.That(merryRequestFromSamResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "sam should not have sent a request");

        var getSamConnectionInfoResponse = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(getSamConnectionInfoResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getSamConnectionInfoResponse.Content!.Status, Is.EqualTo(ConnectionStatus.None),
            "sam should not be connected to merry");

        var getMerryConnectionInfoResponse = await sam.Connections.GetConnectionInfo(merry.Identity);
        Assert.That(getMerryConnectionInfoResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getMerryConnectionInfoResponse.Content!.Status, Is.EqualTo(ConnectionStatus.None),
            "merry should not be connected to sam");
    }

    [Test]
    public async Task CanAcceptConnectionRequestManually_AndRelatedIntroductionsAreDeleted()
    {
        // Note: for your sanity, remember this is a background process that is
        // automatically accepting introductions that are eligible
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await merry.Admin.DisableAutoAcceptIntroductions();
        await sam.Admin.DisableAutoAcceptIntroductions();

        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await frodo.Sync.DrainOutboxAsync();

        //
        // validate introductions exist
        //

        var merryIntroductionsResponse = await Requests(merry).GetReceivedIntroductions();
        Assert.That(merryIntroductionsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(merryIntroductionsResponse.Content!.Any(intro => intro.Identity == sam.Identity), Is.True);

        var samIntroductionsResponse = await Requests(sam).GetReceivedIntroductions();
        Assert.That(samIntroductionsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(samIntroductionsResponse.Content!.Any(intro => intro.Identity == merry.Identity), Is.True);

        await merry.Connections.SendConnectionRequest(sam.Identity);
        await sam.Connections.AcceptConnectionRequest(merry.Identity);

        // there should now be no introductions

        var merryIntroductionsResponse2 = await Requests(merry).GetReceivedIntroductions();
        Assert.That(merryIntroductionsResponse2.IsSuccessStatusCode, Is.True);
        Assert.That(merryIntroductionsResponse2.Content!.Any(intro => intro.Identity == sam.Identity), Is.False);

        var samIntroductionsResponse2 = await Requests(sam).GetReceivedIntroductions();
        Assert.That(samIntroductionsResponse2.IsSuccessStatusCode, Is.True);
        Assert.That(samIntroductionsResponse2.Content!.Any(intro => intro.Identity == merry.Identity), Is.False);
    }
}
