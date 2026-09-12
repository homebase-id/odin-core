using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Services.Membership.Circles;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Tests.Membership.Connections;

/// <summary>
/// The redacted shape carries the review state as <c>ReviewedAt</c>, promoted straight from the
/// <c>Connections.ReviewedAt</c> column rather than derived from Confirmed-circle membership.
/// </summary>
[TestFixture]
public class IdentityConnectionRegistrationTests
{
    [Test]
    public void Redacted_ReviewedConnection_CarriesReviewedAt()
    {
        var icr = CreateIcr(reviewedAt: UnixTimeUtc.Now());

        var redacted = icr.Redacted();

        Assert.That(redacted.ReviewedAt, Is.Not.Null);
    }

    [Test]
    public void Redacted_UnreviewedConnection_HasNullReviewedAt()
    {
        var icr = CreateIcr(reviewedAt: null);

        var redacted = icr.Redacted();

        Assert.That(redacted.ReviewedAt, Is.Null);
    }

    [Test]
    public void Redacted_ReviewedThenBlocked_KeepsReviewedAt()
    {
        // The review stands on its own. Blocking someone does not un-review them -- the owner did
        // once look at this contact and vouch for them -- and Status is what says they are blocked
        // now. Nothing folds connectedness back into the review state.
        var icr = CreateIcr(reviewedAt: UnixTimeUtc.Now());
        icr.Status = ConnectionStatus.Blocked;

        var redacted = icr.Redacted();

        Assert.That(redacted.ReviewedAt, Is.Not.Null);
        Assert.That(redacted.Status, Is.EqualTo(ConnectionStatus.Blocked));
    }

    [Test]
    public void Redacted_ConfirmedConnection_IsVetted()
    {
        var icr = CreateIcr(reviewedAt: null, memberCircleId: SystemCircleConstants.ConfirmedConnectionsCircleId);

        // Vetted still means what it always meant -- Confirmed-circle membership -- so existing clients
        // see exactly what they saw before the review existed. It can disagree with ReviewedAt, which is
        // asking a different question.
        Assert.That(icr.Redacted().Vetted, Is.True);
        Assert.That(icr.Redacted().ReviewedAt, Is.Null);
    }

    [Test]
    public void Redacted_AutoConnectedOnly_IsNotVetted()
    {
        var icr = CreateIcr(reviewedAt: null, memberCircleId: SystemCircleConstants.AutoConnectionsCircleId);

        Assert.That(icr.Redacted().Vetted, Is.False);
    }

    [Test]
    public void Redacted_ConfirmedButNotConnected_IsNotVetted()
    {
        var icr = CreateIcr(reviewedAt: null, memberCircleId: SystemCircleConstants.ConfirmedConnectionsCircleId);
        icr.Status = ConnectionStatus.Blocked;

        Assert.That(icr.Redacted().Vetted, Is.False);
    }

    private static IdentityConnectionRegistration CreateIcr(UnixTimeUtc? reviewedAt)
    {
        var icr = new IdentityConnectionRegistration
        {
            OdinId = new OdinId("frodo.dotyou.cloud"),
            PeerKeyStore = new PeerKeyStore(),
            ReviewedAt = reviewedAt
        };

        icr.Status = ConnectionStatus.Connected;
        return icr;
    }

    private static IdentityConnectionRegistration CreateIcr(UnixTimeUtc? reviewedAt, Guid memberCircleId)
    {
        var icr = new IdentityConnectionRegistration
        {
            OdinId = new OdinId("frodo.dotyou.cloud"),
            ReviewedAt = reviewedAt,
            PeerKeyStore = new PeerKeyStore
            {
                CircleGrants = new Dictionary<Guid, CircleGrant>
                {
                    [memberCircleId] = new CircleGrant
                    {
                        CircleId = memberCircleId,
                        KeyStoreKeyEncryptedDriveGrants = new()
                    }
                }
            }
        };

        icr.Status = ConnectionStatus.Connected;
        return icr;
    }
}
