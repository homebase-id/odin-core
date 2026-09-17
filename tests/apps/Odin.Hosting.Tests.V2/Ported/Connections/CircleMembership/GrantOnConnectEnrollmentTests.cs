#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// A <see cref="CircleGrantOn.Connect"/> circle promises its members are enrolled when a connection is
/// established.  These pin that promise at connection time, as opposed to the v17 -&gt; v18 backfill
/// (<see cref="CircleBackfillMigrationTests"/>), which only moves connections that already existed.
/// </summary>
/// <remarks>
/// Both halves are checked: the recipient's grant is minted at accept, the sender's at send, and each path
/// had to learn about Connect circles separately.
/// </remarks>
[TestFixture]
public class GrantOnConnectEnrollmentTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    // Both values: the reviewed tier governs content evaluation, not enrolment, so it must not change the outcome.
    [TestCase(true)]
    [TestCase(false)]
    public async Task AnAutoConnectionLandsInTheChatCircleOnBothSides(bool useReviewedSecurityTier)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // The configuration in the report: app-initiated requests auto-accepted.
        foreach (var owner in new[] { frodo, sam })
        {
            await owner.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.UseReviewedSecurityTier,
                useReviewedSecurityTier.ToString().ToLowerInvariant());
            await owner.Admin.UpdateTenantSettingsFlag(TenantConfigFlagNames.DisableAutoAcceptConnectionRequests, "false");
        }

        // Rule out the uninteresting failure: the circle is provisioned and still declares Connect.
        foreach (var owner in new[] { frodo, sam })
        {
            var chat = await Host.GetTenantScope(owner.Identity.DomainName)
                .Resolve<CircleDefinitionService>().GetCircleAsync(BuiltinCircles.ChatCircle.Id);
            Assert.That(chat, Is.Not.Null, $"Chat circle is not provisioned on {owner.Identity}");
            Assert.That(chat!.GrantOn, Is.EqualTo(CircleGrantOn.Connect));
        }

        var response = await frodo.Connections.AutoConnectAsync(new ConnectionRequestHeader
        {
            Recipient = sam.Identity,
            Message = "auto-connect",
            ContactData = new ContactRequestData { Name = "test" },
            CircleIds = new List<GuidId>()
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"auto-connect failed: {response.StatusCode}");
        Assert.That(response.Content!.Outcome, Is.EqualTo(AutoConnectOutcome.Connected), $"detail: {response.Content.Detail}");

        var samsViewOfFrodo = await GetIcrAsync(sam, frodo.Identity);
        var frodosViewOfSam = await GetIcrAsync(frodo, sam.Identity);

        Assert.Multiple(() =>
        {
            // Controls: the connection went through the auto-accept path and minted its system circle.
            Assert.That(samsViewOfFrodo.Status, Is.EqualTo(ConnectionStatus.Connected));
            Assert.That(samsViewOfFrodo.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.IdentityOwnerApp));
            Assert.That(samsViewOfFrodo.PeerKeyStore.CircleGrants.Keys,
                Does.Contain(SystemCircleConstants.AutoConnectionsCircleId.Value),
                "sam's auto-accept did not mint the Auto Connections grant");

            // The claim under test.
            Assert.That(samsViewOfFrodo.PeerKeyStore.CircleGrants.Keys,
                Does.Contain(BuiltinCircles.ChatCircle.Id.Value),
                "sam auto-accepted frodo, but frodo is not in sam's Chat circle (GrantOn = Connect). " +
                $"Granted: [{Describe(samsViewOfFrodo)}]");
            Assert.That(frodosViewOfSam.PeerKeyStore.CircleGrants.Keys,
                Does.Contain(BuiltinCircles.ChatCircle.Id.Value),
                "frodo auto-connected to sam, but sam is not in frodo's Chat circle (GrantOn = Connect). " +
                $"Granted: [{Describe(frodosViewOfSam)}]");
        });
    }

    /// <summary>
    /// The owner's own connection: frodo sends, sam accepts by hand, neither names any circles.
    /// </summary>
    /// <remarks>
    /// A Connect circle marks a circle eligible to be granted without a review, not only ambiently
    /// (docs/connection-defaults.md, "On verify"), and a manual accept is the review.  An owner-approved
    /// contact holding Chat less than an introduced stranger does would be backwards.
    /// </remarks>
    [Test]
    public async Task AManualConnectionLandsInTheChatCircleOnBothSides()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var send = await frodo.Connections.SendConnectionRequest(sam.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"send failed: {send.StatusCode}");

        var accept = await sam.Connections.AcceptConnectionRequest(frodo.Identity);
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"accept failed: {accept.StatusCode}");

        var samsViewOfFrodo = await GetIcrAsync(sam, frodo.Identity);
        var frodosViewOfSam = await GetIcrAsync(frodo, sam.Identity);

        Assert.Multiple(() =>
        {
            // Controls: an owner-to-owner connection, reviewed on both sides, holding its system circle.
            foreach (var (label, icr) in new[] { ("sam's view of frodo", samsViewOfFrodo), ("frodo's view of sam", frodosViewOfSam) })
            {
                Assert.That(icr.Status, Is.EqualTo(ConnectionStatus.Connected), label);
                Assert.That(icr.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.IdentityOwner), label);
                Assert.That(icr.ReviewedAt, Is.Not.Null, $"{label}: a manual connection is reviewed");
                Assert.That(icr.PeerKeyStore.CircleGrants.Keys,
                    Does.Contain(SystemCircleConstants.ConfirmedConnectionsCircleId.Value),
                    $"{label}: missing the Confirmed Connections grant");
            }

            // The claim under test.
            Assert.That(samsViewOfFrodo.PeerKeyStore.CircleGrants.Keys,
                Does.Contain(BuiltinCircles.ChatCircle.Id.Value),
                "sam accepted frodo by hand, but frodo is not in sam's Chat circle (GrantOn = Connect). " +
                $"Granted: [{Describe(samsViewOfFrodo)}]");
            Assert.That(frodosViewOfSam.PeerKeyStore.CircleGrants.Keys,
                Does.Contain(BuiltinCircles.ChatCircle.Id.Value),
                "frodo's request to sam was accepted, but sam is not in frodo's Chat circle (GrantOn = Connect). " +
                $"Granted: [{Describe(frodosViewOfSam)}]");
        });
    }

    /// <summary>
    /// The real introduction flow: sam sends merry the request an introduction produces, naming frodo as
    /// the introducer, and merry accepts it.
    /// </summary>
    /// <remarks>
    /// The origin is what distinguishes this from the other two -- an <see cref="ConnectionRequestOrigin.Introduction"/>
    /// request is the one nobody reviewed, and it was the origin the enrolment originally keyed on.  Driven
    /// through the introduction service rather than an endpoint because the V2 client exposes only the
    /// introduction preflight, not the send.
    /// </remarks>
    [Test]
    public async Task AnIntroducedConnectionLandsInTheChatCircleOnBothSides()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        var samScope = Host.GetTenantScope(sam.Identity.DomainName);
        var introductions = samScope.Resolve<CircleNetworkIntroductionService>();

        await introductions.SendAutoConnectIntroduceeRequest(new IdentityIntroduction
        {
            Identity = merry.Identity,
            IntroducerOdinId = frodo.Identity,
            Message = "you two should meet"
        }, CancellationToken.None, await OwnerContextAsync(samScope, sam));

        var incoming = await merry.Connections.GetIncomingRequestFrom(sam.Identity);
        Assert.That(incoming.IsSuccessStatusCode, Is.True, $"no introduced request arrived: {incoming.StatusCode}");
        Assert.That(incoming.Content!.ConnectionRequestOrigin, Is.EqualTo(ConnectionRequestOrigin.Introduction),
            "precondition: the request must be introduction-origin");

        var accept = await merry.Connections.AcceptConnectionRequest(sam.Identity);
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"accept failed: {accept.StatusCode}");

        var merrysViewOfSam = await GetIcrAsync(merry, sam.Identity);
        var samsViewOfMerry = await GetIcrAsync(sam, merry.Identity);

        Assert.Multiple(() =>
        {
            Assert.That(merrysViewOfSam.Status, Is.EqualTo(ConnectionStatus.Connected));
            Assert.That(samsViewOfMerry.Status, Is.EqualTo(ConnectionStatus.Connected));

            Assert.That(merrysViewOfSam.PeerKeyStore.CircleGrants.Keys,
                Does.Contain(BuiltinCircles.ChatCircle.Id.Value),
                "merry accepted an introduced request, but sam is not in merry's Chat circle. " +
                $"Granted: [{Describe(merrysViewOfSam)}]");
            Assert.That(samsViewOfMerry.PeerKeyStore.CircleGrants.Keys,
                Does.Contain(BuiltinCircles.ChatCircle.Id.Value),
                "sam's introduced request completed, but merry is not in sam's Chat circle. " +
                $"Granted: [{Describe(samsViewOfMerry)}]");
        });
    }

    /// <summary>
    /// The grant has to actually open the drive: sam sends frodo a file on the chat drive, using nothing
    /// but what connecting gave them.
    /// </summary>
    /// <remarks>
    /// Membership proves the enrolment ran; only a write proves the enrolment is worth anything.  No circle
    /// is granted by hand here -- that is the point.
    /// <para>
    /// The Confirmed Connections system circle is revoked first, because it grants ChatDrive Write|React
    /// too (<see cref="SystemCircleConstants.ConfirmedConnectionsDefinition"/>): leaving it in place would
    /// let this pass on the system circle alone and say nothing about the Chat grant.  Revoked before any
    /// peer traffic between the two, so there is no cached peer context still holding the old permissions.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AConnectedIdentityCanWriteToTheChatDriveUsingOnlyTheChatGrant()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var send = await frodo.Connections.SendConnectionRequest(sam.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"send failed: {send.StatusCode}");
        var accept = await sam.Connections.AcceptConnectionRequest(frodo.Identity);
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"accept failed: {accept.StatusCode}");

        // Frodo minted sam's Chat grant; it is frodo's chat drive that grant opens.
        Assert.That((await GetIcrAsync(frodo, sam.Identity)).PeerKeyStore.CircleGrants.Keys,
            Does.Contain(BuiltinCircles.ChatCircle.Id.Value), "precondition: sam holds frodo's Chat circle");

        // Strip the system circle, so the Chat grant is the only thing left that opens the chat drive.
        var revoked = await frodo.Connections.RevokeCircle(
            SystemCircleConstants.ConfirmedConnectionsCircleId.Value, sam.Identity);
        Assert.That(revoked.IsSuccessStatusCode, Is.True, $"revoking the system circle failed: {revoked.StatusCode}");

        // Exactly one, not merely "Chat is in there": any other surviving grant could be what opens the
        // drive, and then this would be telling us nothing about Chat.
        var granted = (await GetIcrAsync(frodo, sam.Identity)).PeerKeyStore.CircleGrants.Keys.ToList();
        Assert.That(granted, Is.EquivalentTo(new[] { BuiltinCircles.ChatCircle.Id.Value }),
            $"precondition: Chat must be the only grant left. Granted: [{string.Join(", ", granted.Select(g => g.ToString("N")))}]");

        var metadata = SampleMetadataData.Create(fileType: 4242, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;

        var upload = await sam.Drives.Writer.UploadNewMetadata(
            WellKnownAppDrives.ChatDrive.Alias,
            metadata,
            transitOptions: new TransitOptions { Recipients = [frodo.Identity.DomainName] });

        Assert.That(upload.IsSuccessStatusCode, Is.True, $"sam could not upload to their own chat drive: {upload.StatusCode}");
        var globalTransitId = upload.Content!.GlobalTransitId;
        Assert.That(globalTransitId, Is.Not.Null);

        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(WellKnownAppDrives.ChatDrive);

        var query = await frodo.Drives.Reader.GetBatchAsync(WellKnownAppDrives.ChatDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = [globalTransitId!.Value] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });

        Assert.That(query.IsSuccessStatusCode, Is.True, $"frodo query failed: {query.StatusCode}");
        Assert.That(query.Content!.SearchResults.Count(), Is.EqualTo(1),
            "the Chat grant did not open frodo's chat drive: nothing arrived");
    }

    /// <summary>An owner context with the master key, built the way the version ladder builds one.</summary>
    private static async Task<IOdinContext> OwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<OwnerAuthenticationService>();
        var odinContext = new OdinContext { Tenant = default, AuthTokenCreated = null, Caller = null };
        await authService.UpdateOdinContextAsync(owner.Token, new OdinClientContext
        {
            CorsHostName = null,
            AccessRegistrationId = null,
            DevicePushNotificationKey = null,
            ClientIdOrDomain = null
        }, odinContext);

        odinContext.Caller!.AssertHasMasterKey();
        return odinContext;
    }

    private async Task<IdentityConnectionRegistration> GetIcrAsync(OwnerSession owner, OdinId target)
    {
        var icr = await Host.GetTenantScope(owner.Identity.DomainName)
            .Resolve<CircleNetworkStorage>().GetAsync(target);
        Assert.That(icr, Is.Not.Null, $"no connection record for {target}");
        return icr!;
    }

    private static string Describe(IdentityConnectionRegistration icr) =>
        string.Join(", ", icr.PeerKeyStore.CircleGrants.Keys.Select(k => k.ToString("N")));
}
