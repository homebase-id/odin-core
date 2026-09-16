#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections.Introductions;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/Introductions/AutoAcceptTests</c>. Two introducees who
/// both hold an introduction from the same introducer auto-accept each other's connection request
/// into the Auto-connected circle — unless the recipient has turned introductions off, in which case
/// the introduction never reaches them and neither side connects.
/// </summary>
/// <remarks>
/// <para>
/// Every passive poll is gone. <c>WaitForEmptyOutbox(TransientTempDrive)</c> becomes
/// <c>Sync.DrainOutboxAsync()</c> on the same identity — draining an introducee's outbox is what
/// actually sends the introductory connection request, so this preserves behaviour rather than just
/// timing. The <c>await Task.Delay(1000 * 3)</c> in
/// <see cref="WillNotAutoAcceptWhenRecipientDisablesIntroductions"/> — whose comment explains that
/// the outbox "will never be empty" because Sam's item fails and stays queued — also becomes
/// <c>DrainOutboxAsync()</c>, because on the fast host nothing else moves Frodo's introduction to
/// Merry.
/// </para>
/// <para>
/// <b>Measured, and it does not reproduce the sleep's state.</b> Probing the fixture either side of
/// the drain: the outbox item for Sam is present before it and gone after it.
/// <c>PeerOutboxProcessorBackgroundService.DrainAsync</c> makes three retry passes and then exhausts
/// a permanently-failing item rather than leaving it queued behind a backoff, which is the state the
/// V1 three-second sleep relied on. Nothing observable changes, because the assertion that reads the
/// item is vacuous either way (see the carried defects below) — but its <i>intent</i> is no longer
/// satisfiable here: repairing that assertion would leave it passing on <c>WebScaffold</c> and
/// failing on this framework. Expressing it would need a drain that can be told to stop before
/// exhausting an item.
/// </para>
/// <para>
/// Carried defects, behaviour left exactly as found:
/// <list type="bullet">
/// <item><c>GetOutboxItem</c> returns an <c>ApiResponse</c>, which is never null, so
/// <c>IsNotNull(samOutboxItem)</c> in <see cref="WillNotAutoAcceptWhenRecipientDisablesIntroductions"/>
/// asserts nothing about whether the item exists. Carried as a null check on the response.</item>
/// <item>Both rows of <see cref="CanAutoAcceptIncomingConnectionRequestsWhenIntroductionExists"/>
/// declare an expected status code that the body never reads.</item>
/// <item>That test's commented-out block asserting the outgoing request to Sam is carried as found.</item>
/// </list>
/// </para>
/// <para>
/// <c>SetupCallerWithOwner</c> ordering was checked: the original built its caller context (drive
/// create + app registration on Sam) only after the introductions had been sent, and here it happens
/// first. It is inert — the app is registered with no authorized circles, so nothing fans out to it,
/// and a fresh drive changes no circle definition and so no connection grant.
/// </para>
/// <para>
/// The <c>DisableAllowIntroductions(false)</c> calls at the head of <c>Prepare</c> ("a test that
/// turns introductions off must not leave them off for the next one") and the trailing
/// <c>Cleanup()</c> were both lifecycle for a shared <c>WebScaffold</c>; per-test reset owns that and
/// neither asserted.
/// </para>
/// </remarks>
[TestFixture]
public class AutoAcceptTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    public static IEnumerable<object[]> ProcessIntroductionsCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.None, PermissionKeys.All), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(ProcessIntroductionsCases))]
    public async Task CanAutoAcceptIncomingConnectionRequestsWhenIntroductionExists(CallerSpec spec, HttpStatusCode expected)
    {
        // Note: for your sanity, remember this is a background process that is
        // automatically accepting introductions that are eligible

        var (caller, sam) = await SetupCallerWithOwner(spec, Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);

        await sam.Admin.DisableAutoAcceptIntroductions();
        await merry.Admin.DisableAutoAcceptIntroductions();

        await PrepareIntroducer(frodo, sam, merry);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"failed: status code was: {response.StatusCode}");
        await frodo.Sync.DrainOutboxAsync();

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();

        // Assert: Sam should have a connection request from Merry and visa/versa
        var samClient = CallerRequests(caller);
        var samProcessResponse = await samClient.ProcessIncomingIntroductions();
        Assert.That(samProcessResponse.IsSuccessStatusCode, Is.True);

        // var outgoingRequestToSamResponse = await merryOwnerClient.Connections.GetOutgoingSentRequestTo(sam);
        // var outgoingRequestToSam = outgoingRequestToSamResponse.Content;
        // ClassicAssert.IsNotNull(outgoingRequestToSam);
        // ClassicAssert.IsTrue(outgoingRequestToSam.ConnectionRequestOrigin == ConnectionRequestOrigin.Introduction);
        // ClassicAssert.IsTrue(outgoingRequestToSam.IntroducerOdinId == frodo);

        var outgoingRequestToMerryResponse = await sam.Connections.GetOutgoingSentRequestTo(merry.Identity);
        var outgoingRequestToMerry = outgoingRequestToMerryResponse.Content;
        Assert.That(outgoingRequestToMerry, Is.Not.Null);
        Assert.That(outgoingRequestToMerry!.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.Introduction));
        Assert.That(outgoingRequestToMerry.IntroducerOdinId, Is.EqualTo(frodo.Identity));

        var merryRequestFromSamResponse = await merry.Connections.GetIncomingRequestFrom(sam.Identity);
        var requestFromSam = merryRequestFromSamResponse.Content;
        Assert.That(requestFromSam, Is.Not.Null, "there should be a request from sam since we have not yet processed the inbox");

        // Note: remember there is a background process that is auto-accepting eligible connections so this call might not run the auto-accept code
        var merryProcessResponse = await Requests(merry).ProcessIncomingIntroductions();
        Assert.That(merryProcessResponse.IsSuccessStatusCode, Is.True);

        var merryForceAutoAccept = await Requests(merry).AutoAcceptEligibleIntroductions();
        Assert.That(merryForceAutoAccept.IsSuccessStatusCode, Is.True);

        var getSamConnectionInfoResponse = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(getSamConnectionInfoResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getSamConnectionInfoResponse.Content!.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.Introduction));
        Assert.That(getSamConnectionInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

        Assert.That(getSamConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.That(getSamConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.None.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        var merryIntroductionsResponse = await Requests(merry).GetReceivedIntroductions();
        Assert.That(merryIntroductionsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(merryIntroductionsResponse.Content!.All(intro => intro.Identity != sam.Identity), Is.True,
            "there should be no introductions to sam");

        // Check Sam

        var samForceAutoAccept = await Requests(sam).AutoAcceptEligibleIntroductions();
        Assert.That(samForceAutoAccept.IsSuccessStatusCode, Is.True);

        var getMerryConnectionInfoResponse = await sam.Connections.GetConnectionInfo(merry.Identity);
        Assert.That(getMerryConnectionInfoResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getMerryConnectionInfoResponse.Content!.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.Introduction));
        Assert.That(getMerryConnectionInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

        Assert.That(getMerryConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.That(getMerryConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.None.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        var samIntroductionsResponse = await Requests(sam).GetReceivedIntroductions();
        Assert.That(samIntroductionsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(samIntroductionsResponse.Content!.All(intro => intro.Identity != merry.Identity), Is.True,
            "there should be no introductions to sam");
    }

    [Test]
    public async Task WillNotAutoAcceptWhenRecipientDisablesIntroductions()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducer(frodo, sam, merry);

        // sam turns introductions off, so sam refuses the introduction from frodo
        await sam.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.DisableAllowIntroductions, bool.TrueString);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        var introResult = response.Content!;
        // ClassicAssert.IsFalse(introResult.RecipientStatus[TestIdentities.Samwise.OdinId],
        //     "sam should reject since frodo does not have allow introductions permission");
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        // Note; I have to use a delay because the outbox will never be
        // empty and, currently, there is no way to do an exclusion test on the outbox
        // await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        await frodo.Sync.DrainOutboxAsync();

        var samOutboxItem = await frodo.V1.Drive.GetOutboxItem(SystemDriveConstants.TransientTempDrive,
            sam.Identity.ToHashId(), sam.Identity);
        Assert.That(samOutboxItem, Is.Not.Null,
            "there should be an outbox item for sam since it failed he blocked incoming introductions");

        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();

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

    /// <summary>The V1 connection-requests surface as an arbitrary caller (Owner or App).</summary>
    private static IRefitUniversalCircleNetworkRequests CallerRequests(IV2Caller caller)
    {
        var client = caller.Factory.CreateHttpClient(caller.Identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, sharedSecret);
    }
}
