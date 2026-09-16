#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Configuration;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

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
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

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
