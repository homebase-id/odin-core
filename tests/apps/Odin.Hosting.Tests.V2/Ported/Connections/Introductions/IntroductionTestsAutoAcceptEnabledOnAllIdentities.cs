#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections.Requests;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections.Introductions;

/// <summary>
/// Port of
/// <c>_Universal/Owner/Connections/Introductions/AutoAcceptVariations/IntroductionTestsAutoAcceptEnabledOnAllIdentities</c>.
/// Introductions with every identity on its default auto-accept setting: two strangers connect, a
/// block on either side stops the connection request without telling the introducer, mutual blocks
/// leave nothing at all, an existing owner-made connection is left alone, and a one-sided broken ICR
/// is rebuilt as an auto-connection.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WillAutoAcceptWhenIdentitiesAreNotConnected"/> keeps its <c>[Ignore]</c> verbatim,
/// reason string and all.
/// </para>
/// <para>
/// <c>AwaitIntroductionsProcessing(debugTimeout)</c> — a passive poll of the transient-temp-drive
/// outbox — becomes <c>Sync.DrainOutboxAsync()</c> on the same identity. The fast host registers the
/// outbox background service but never starts it, so the poll would have waited out its timeout and
/// then thrown; draining an introducee's outbox is also what sends the introductory connection
/// request, so the substitution preserves behaviour rather than only timing. <c>DebugTimeout</c> has
/// no analogue and is dropped with the polls.
/// </para>
/// <para>
/// <c>SetupCallerWithOwner</c> ordering was checked: the original built its caller context (drive
/// create + app registration on the introducer) only after <c>PrepareIntroducer</c> and each test's
/// block / connect setup, and here it happens first. It is inert — the app is registered with no
/// authorized circles, so no circle grant fans out to it, and a fresh drive changes no circle
/// definition and therefore no connection grant.
/// </para>
/// <para>
/// The <c>[TearDown]</c> and trailing <c>IntroductionTestUtils.Cleanup(_scaffold)</c> calls were
/// lifecycle for a shared <c>WebScaffold</c> and asserted nothing; per-test reset owns that, and
/// <c>Cleanup</c> is not carried into the moved <see cref="IntroductionTestUtils"/>.
/// </para>
/// <para>
/// Carried defect, behaviour left as found: all five methods declare an expected status code that no
/// body ever reads.
/// </para>
/// </remarks>
[TestFixture]
public class IntroductionTestsAutoAcceptEnabledOnAllIdentities : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    public static IEnumerable<object[]> IntroducerCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.None, PermissionKeys.All), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    [Ignore("stupid unreliable test >:=[")]
    public async Task WillAutoAcceptWhenIdentitiesAreNotConnected(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, introducer) = await SetupCallerWithOwner(spec, Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducer(introducer, sam, merry);

        //
        // Note: Sam and Merry are not connected
        //
        var response = await CallerRequests(caller).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"failed: status code was: {response.StatusCode}");
        await introducer.Sync.DrainOutboxAsync();

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();

        //
        // Validate Sam is connected on merry's identity
        //
        Assert.That(await IsConnectedWithExpectedOrigin(merry, sam.Identity, ConnectionRequestOrigin.Introduction), Is.True);
        Assert.That(await HasIntroductionFromIdentity(merry, sam.Identity), Is.False,
            "there should be no introductions to sam");

        //
        // Validate Merry is connected on Sam's identity
        //
        Assert.That(await IsConnectedWithExpectedOrigin(sam, merry.Identity, ConnectionRequestOrigin.Introduction), Is.True);
        Assert.That(await HasIntroductionFromIdentity(sam, merry.Identity), Is.False,
            "there should be no introductions to merry");
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    public async Task WillNotSendOrReceiveConnectionRequestWhenOneIntroduceeBlocksAnother(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, introducer) = await SetupCallerWithOwner(spec, Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducer(introducer, sam, merry);

        //
        // Setup: sam blocks merry
        //
        await sam.Connections.BlockConnection(merry.Identity);

        var response = await CallerRequests(caller).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"failed: status code was: {response.StatusCode}");
        await introducer.Sync.DrainOutboxAsync();

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();

        // Assert: Sam does not have a connection from Merry
        Assert.That(await HasReceivedIntroducedConnectionRequestFromIntroducee(sam, merry.Identity), Is.False);

        // Assert: Merry does not have a connection from Sam
        Assert.That(await HasReceivedIntroducedConnectionRequestFromIntroducee(merry, sam.Identity), Is.False);

        // Assert: Sam does not have an introduction
        Assert.That(await HasIntroductionFromIdentity(sam, merry.Identity), Is.False);

        // Assert: Merry has an introduction
        Assert.That(await HasIntroductionFromIdentity(merry, sam.Identity), Is.True);
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    public async Task WillHandleWhenIntroduceesAreAlreadyConnectedAndVerifiable(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, introducer) = await SetupCallerWithOwner(spec, Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducer(introducer, sam, merry);

        //
        // Setup: Sam and merry are connected
        //
        await sam.Connections.SendConnectionRequest(merry.Identity);
        await merry.Connections.AcceptConnectionRequest(sam.Identity);

        var response = await CallerRequests(caller).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"failed: status code was: {response.StatusCode}");
        await introducer.Sync.DrainOutboxAsync();

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();

        //
        // Assert: sam should have merry has a normal connection
        //
        Assert.That(await IsConnectedWithExpectedOrigin(sam, merry.Identity, ConnectionRequestOrigin.IdentityOwner), Is.True);

        //
        // Assert: merry should have sam as a normal connection
        //
        Assert.That(await IsConnectedWithExpectedOrigin(merry, sam.Identity, ConnectionRequestOrigin.IdentityOwner), Is.True);

        //
        // Assert: Sam does not have an introduction
        //
        Assert.That(await HasIntroductionFromIdentity(sam, merry.Identity), Is.False);

        //
        // Assert: Merry does not have an introduction
        //
        Assert.That(await HasIntroductionFromIdentity(merry, sam.Identity), Is.False);
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    public async Task WillHandleWithAllIntroduceesBlockEachOther(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, introducer) = await SetupCallerWithOwner(spec, Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducer(introducer, sam, merry);

        //
        // Setup: Sam blocks merry and merry blocks sam
        //
        await sam.Connections.BlockConnection(merry.Identity);
        await merry.Connections.BlockConnection(sam.Identity);

        var response = await CallerRequests(caller).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"failed: status code was: {response.StatusCode}");
        await introducer.Sync.DrainOutboxAsync();

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();

        //
        // Assert there are no introductions
        //
        Assert.That(await HasIntroductionFromIdentity(sam, merry.Identity), Is.False);
        Assert.That(await HasIntroductionFromIdentity(merry, sam.Identity), Is.False);

        //
        // Assert: there are no connection requests
        Assert.That(await HasReceivedIntroducedConnectionRequestFromIntroducee(sam, merry.Identity), Is.False);
        Assert.That(await HasReceivedIntroducedConnectionRequestFromIntroducee(merry, sam.Identity), Is.False);

        //
        // Assert: no one is connected
        //
        Assert.That(await IsConnected(sam, merry.Identity), Is.False);
        Assert.That(await IsConnected(merry, sam.Identity), Is.False);
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    public async Task WillHandleWhenAllWhenConnectionsFailsVerification(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, introducer) = await SetupCallerWithOwner(spec, Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducer(introducer, sam, merry);

        //
        // Setup: merry disconnects from sam, but sam stays connected
        //
        await sam.Connections.SendConnectionRequest(merry.Identity);
        await merry.Connections.AcceptConnectionRequest(sam.Identity);
        await merry.Connections.DisconnectFrom(sam.Identity);

        var response = await CallerRequests(caller).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"failed: status code was: {response.StatusCode}");
        await introducer.Sync.DrainOutboxAsync();

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();

        // both send connection request; identities get connected
        // R3's ICR record on R2's identity must be reset to an auto-connection because we won't
        // have master key and the shared secret get reset.  This means all circles also get reset.  "

        Assert.That(await IsConnectedWithExpectedOrigin(sam, merry.Identity, ConnectionRequestOrigin.Introduction), Is.True);

        Assert.That(await IsConnectedWithExpectedOrigin(merry, sam.Identity, ConnectionRequestOrigin.Introduction), Is.True);
    }

    /// <summary>The V1 connection-requests surface as an arbitrary caller (Owner or App).</summary>
    private static IRefitUniversalCircleNetworkRequests CallerRequests(IV2Caller caller)
    {
        var client = caller.Factory.CreateHttpClient(caller.Identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkRequests>(client, sharedSecret);
    }
}
