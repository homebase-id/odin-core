#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
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
/// The original's case source carried an expected status code alongside each caller, and all five
/// method bodies took it and never read it. Every row declared the same code, so the column
/// distinguished nothing; it is dropped rather than carried. The one status the bodies do care
/// about — the introducer's <c>send-introductions</c> always succeeding — is asserted in
/// <see cref="SendIntroductionsAndDrainAsync"/>.
/// </para>
/// </remarks>
[TestFixture]
public class IntroductionTestsAutoAcceptEnabledOnAllIdentities : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    public static IEnumerable<object[]> IntroducerCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon())];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.None, PermissionKeys.All)];
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    [Ignore("stupid unreliable test >:=[")]
    public async Task WillAutoAcceptWhenIdentitiesAreNotConnected(CallerSpec spec)
    {
        var (caller, introducer) = await SetupCallerWithOwner(spec, Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducer(introducer, sam, merry);

        //
        // Note: Sam and Merry are not connected
        //
        await SendIntroductionsAndDrainAsync(caller, introducer, sam, merry);

        //
        // Validate Sam is connected on merry's identity
        //
        Assert.That(await IsConnectedWithExpectedOrigin(merry, sam.Identity, ConnectionRequestOrigin.Introduction), Is.True,
            $"{merry.Identity} must hold {sam.Identity} as an introduced connection");
        Assert.That(await HasIntroductionFromIdentity(merry, sam.Identity), Is.False,
            $"{merry.Identity} must hold no introduction to {sam.Identity}");

        //
        // Validate Merry is connected on Sam's identity
        //
        Assert.That(await IsConnectedWithExpectedOrigin(sam, merry.Identity, ConnectionRequestOrigin.Introduction), Is.True,
            $"{sam.Identity} must hold {merry.Identity} as an introduced connection");
        Assert.That(await HasIntroductionFromIdentity(sam, merry.Identity), Is.False,
            $"{sam.Identity} must hold no introduction to {merry.Identity}");
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    public async Task WillNotSendOrReceiveConnectionRequestWhenOneIntroduceeBlocksAnother(CallerSpec spec)
    {
        var (caller, introducer) = await SetupCallerWithOwner(spec, Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducer(introducer, sam, merry);

        //
        // Setup: sam blocks merry
        //
        await sam.Connections.BlockConnection(merry.Identity);

        await SendIntroductionsAndDrainAsync(caller, introducer, sam, merry);

        // Assert: Sam does not have a connection from Merry
        Assert.That(await HasReceivedIntroducedConnectionRequestFromIntroducee(sam, merry.Identity), Is.False,
            $"{sam.Identity} must hold no introduced request from {merry.Identity}");

        // Assert: Merry does not have a connection from Sam
        Assert.That(await HasReceivedIntroducedConnectionRequestFromIntroducee(merry, sam.Identity), Is.False,
            $"{merry.Identity} must hold no introduced request from {sam.Identity}");

        // Assert: Sam does not have an introduction
        Assert.That(await HasIntroductionFromIdentity(sam, merry.Identity), Is.False,
            $"{sam.Identity} must hold no introduction to {merry.Identity}");

        // Assert: Merry has an introduction
        Assert.That(await HasIntroductionFromIdentity(merry, sam.Identity), Is.True,
            $"{merry.Identity} must hold an introduction to {sam.Identity}");
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    public async Task WillHandleWhenIntroduceesAreAlreadyConnectedAndVerifiable(CallerSpec spec)
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

        await SendIntroductionsAndDrainAsync(caller, introducer, sam, merry);

        //
        // Assert: sam should have merry has a normal connection
        //
        Assert.That(await IsConnectedWithExpectedOrigin(sam, merry.Identity, ConnectionRequestOrigin.IdentityOwner), Is.True,
            $"{sam.Identity} must still hold {merry.Identity} as an owner-made connection");

        //
        // Assert: merry should have sam as a normal connection
        //
        Assert.That(await IsConnectedWithExpectedOrigin(merry, sam.Identity, ConnectionRequestOrigin.IdentityOwner), Is.True,
            $"{merry.Identity} must still hold {sam.Identity} as an owner-made connection");

        //
        // Assert: Sam does not have an introduction
        //
        Assert.That(await HasIntroductionFromIdentity(sam, merry.Identity), Is.False,
            $"{sam.Identity} must hold no introduction to {merry.Identity}");

        //
        // Assert: Merry does not have an introduction
        //
        Assert.That(await HasIntroductionFromIdentity(merry, sam.Identity), Is.False,
            $"{merry.Identity} must hold no introduction to {sam.Identity}");
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    public async Task WillHandleWithAllIntroduceesBlockEachOther(CallerSpec spec)
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

        await SendIntroductionsAndDrainAsync(caller, introducer, sam, merry);

        //
        // Assert there are no introductions
        //
        Assert.That(await HasIntroductionFromIdentity(sam, merry.Identity), Is.False,
            $"{sam.Identity} must hold no introduction to {merry.Identity}");
        Assert.That(await HasIntroductionFromIdentity(merry, sam.Identity), Is.False,
            $"{merry.Identity} must hold no introduction to {sam.Identity}");

        //
        // Assert: there are no connection requests
        Assert.That(await HasReceivedIntroducedConnectionRequestFromIntroducee(sam, merry.Identity), Is.False,
            $"{sam.Identity} must hold no introduced request from {merry.Identity}");
        Assert.That(await HasReceivedIntroducedConnectionRequestFromIntroducee(merry, sam.Identity), Is.False,
            $"{merry.Identity} must hold no introduced request from {sam.Identity}");

        //
        // Assert: no one is connected
        //
        Assert.That(await IsConnected(sam, merry.Identity), Is.False,
            $"{sam.Identity} must not be connected to {merry.Identity}");
        Assert.That(await IsConnected(merry, sam.Identity), Is.False,
            $"{merry.Identity} must not be connected to {sam.Identity}");
    }

    [Test, TestCaseSource(nameof(IntroducerCases))]
    public async Task WillHandleWhenAllWhenConnectionsFailsVerification(CallerSpec spec)
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

        await SendIntroductionsAndDrainAsync(caller, introducer, sam, merry);

        // both send connection request; identities get connected
        // R3's ICR record on R2's identity must be reset to an auto-connection because we won't
        // have master key and the shared secret get reset.  This means all circles also get reset.  "

        Assert.That(await IsConnectedWithExpectedOrigin(sam, merry.Identity, ConnectionRequestOrigin.Introduction), Is.True,
            $"{sam.Identity} must hold {merry.Identity} as an introduced connection");

        Assert.That(await IsConnectedWithExpectedOrigin(merry, sam.Identity, ConnectionRequestOrigin.Introduction), Is.True,
            $"{merry.Identity} must hold {sam.Identity} as an introduced connection");
    }

    /// <summary>
    /// The act every test in this fixture shares: the introducer introduces Sam and Merry to each
    /// other, hears success for both, and all three outboxes are drained — the introducer's to deliver
    /// the introductions, each introducee's to send the connection request it produced.
    /// </summary>
    private static async Task SendIntroductionsAndDrainAsync(
        IV2Caller caller, OwnerSession introducer, OwnerSession sam, OwnerSession merry)
    {
        var response = await caller.RefitFor<IRefitUniversalCircleNetworkRequests>()
            .SendIntroductions(new IntroductionGroup
            {
                Message = "test message from frodo",
                Recipients = [sam.Identity, merry.Identity]
            });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await introducer.Sync.DrainOutboxAsync();

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();
    }
}
