using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Tests.Membership.Connections;

[TestFixture]
public class IdentityConnectionRegistrationTests
{
    // Vetted used to mean "connected and a member of the Confirmed Connections system circle".  It now
    // means "connected and reviewed" -- the stamp is recorded rather than inferred, so it can also
    // represent a chat-only review that granted nothing.

    [Test]
    public void Redacted_ReviewedConnection_IsVetted()
    {
        var icr = CreateIcr(SystemCircleConstants.ConfirmedConnectionsCircleId);
        icr.ReviewedAt = UnixTimeUtc.Now();

        Assert.That(icr.Redacted().Vetted, Is.True);
        Assert.That(icr.Redacted().ReviewedAt, Is.Not.Null);
    }

    [Test]
    public void Redacted_ReviewedWithNoCircles_IsVetted()
    {
        // The chat-only outcome: reviewed, holding nothing.  The old membership-derived flag could not
        // represent this state at all.
        var icr = CreateIcr();
        icr.ReviewedAt = UnixTimeUtc.Now();

        Assert.That(icr.Redacted().Vetted, Is.True);
    }

    [Test]
    public void Redacted_UnreviewedConnection_IsNotVetted()
    {
        var icr = CreateIcr(SystemCircleConstants.AutoConnectionsCircleId);

        Assert.That(icr.Redacted().Vetted, Is.False);
        Assert.That(icr.Redacted().ReviewedAt, Is.Null);
    }

    [Test]
    public void Redacted_ReviewedButNotConnected_IsNotVetted()
    {
        var icr = CreateIcr(SystemCircleConstants.ConfirmedConnectionsCircleId);
        icr.ReviewedAt = UnixTimeUtc.Now();
        icr.Status = ConnectionStatus.Blocked;

        Assert.That(icr.Redacted().Vetted, Is.False);
    }

    [Test]
    public void RedactedForThirdParty_DropsTheOwnersJudgments()
    {
        var icr = CreateIcr(SystemCircleConstants.ConfirmedConnectionsCircleId,
            introducer: new OdinId("samwise.dotyou.cloud"));
        icr.ReviewedAt = UnixTimeUtc.Now();

        var redacted = icr.RedactedForThirdParty();

        Assert.That(redacted.OdinId, Is.EqualTo(icr.OdinId));
        Assert.That(redacted.ReviewedAt, Is.Null, "the review stamp is owner-private");
        Assert.That(redacted.Vetted, Is.False, "the legacy flag must not leak the stamp either");
        Assert.That(redacted.IntroducerOdinId, Is.Null);
        Assert.That(redacted.AccessGrant, Is.Null);
        Assert.That(redacted.HasVerificationHash, Is.False);
    }

    private static IdentityConnectionRegistration CreateIcr(Guid? memberCircleId = null, OdinId? introducer = null)
    {
        var circleGrants = new Dictionary<Guid, CircleGrant>();

        if (memberCircleId.HasValue)
        {
            circleGrants[memberCircleId.Value] = new CircleGrant
            {
                CircleId = memberCircleId.Value,
                KeyStoreKeyEncryptedDriveGrants = new()
            };
        }

        var icr = new IdentityConnectionRegistration
        {
            OdinId = new OdinId("frodo.dotyou.cloud"),
            IntroducerOdinId = introducer,
            PeerKeyStore = new PeerKeyStore
            {
                CircleGrants = circleGrants
            }
        };
        icr.Status = ConnectionStatus.Connected;
        return icr;
    }
}
